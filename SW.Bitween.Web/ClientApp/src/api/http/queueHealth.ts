import type { ApiClient } from "../client";
import type { InformationTypeRow, QueueLane, QueueHealthSnapshot, QueueSeverity, WorkGroup } from "../types";
import { workGroupMethods } from "./workGroups";
import { documentMethods } from "./documents";
import { get } from "./request";

// ——— backend shapes (camelCase over the wire; severities are PascalCase strings) ———
type RawSeverity = "Info" | "Warning" | "Critical";
interface RawSummary {
  totalConsumers: number;
  unhealthyConsumers: number;
  disconnectedConsumers: number;
  totalQueueDepth: number;
  totalRetryBacklog: number;
  totalDeadLetterBacklog: number;
  totalIncomingRate: number;
  totalAckRate: number;
  lastUpdatedUtc: string;
}
interface RawConsumer {
  name: string;
  messageName: string;
  queueName: string;
  totalNodes: number;
  processingCount: number;
  queueCount: number;
  retryCount: number;
  failedCount: number;
  priority: number;
  prefetch: number;
  incomingRate: number;
  ackRate: number;
  isBackpressured: boolean;
  healthStatus: RawSeverity;
}
interface RawRetryAnalysis {
  consumerName: string;
  queueName: string;
  retryBacklog: number;
  incomingRate: number;
  ackRate: number;
  severity: RawSeverity;
}
interface RawDeadLetter {
  consumerName: string;
  deadLetterQueueName: string;
  deadLetterCount: number;
  lastExceptionType: string | null;
  lastExceptionMessage: string | null;
  lastFailedAt: string | null;
}
interface RawAlert {
  severity: RawSeverity;
  title: string;
  detail: string;
  queueName: string;
  timestampUtc: string;
}

const toSeverity = (s: RawSeverity): QueueSeverity => (s === "Critical" ? "critical" : s === "Warning" ? "warning" : "healthy");

/** Message types `XchangeService` consumes that aren't lanes — bookkeeping. */
const CONTROL: Record<string, { title: string; role: string }> = {
  subscriptionunpausedevent: {
    title: "Resumed integrations",
    role: "releases the exchanges held while an integration was paused",
  },
};

/** Only declared while `Bitween.ConsumeLegacyEventMessages` is on. */
const LEGACY_EVENTS: Record<string, string> = {
  apixchangecreatedevent: "API exchange created",
  internalxchangecreatedevent: "Internal exchange created",
  receivingxchangecreatedevent: "Received exchange created",
  aggregatexchangecreatedevent: "Aggregated exchange created",
  xchangeresultcreatedevent: "Exchange result created",
};

/** `WorkGroup.None` — id 0, so it can never collide with a real group. */
const UNGROUPED = "0ungrouped";
const RESULT_SUFFIX = "-result";

interface Identity {
  lane: QueueLane;
  title: string;
  role: string;
  orphaned: boolean;
  workGroupId: number | null;
  informationTypeId: number | null;
}

/**
 * Queues are named after the C# consumer class and the bus message type —
 * `busservice.shipment_notice`, `xchangeservice.47bitween.bulk-result` — and
 * neither half means anything to the person on call at 3am. This decodes them.
 *
 * The rules come from the two consumers that declare queues:
 * - `BusService`'s message type IS an information type's `busMessageTypeName`,
 *   so each of its queues is the front door for one information type.
 * - `XchangeService` declares two lanes per work group: `{id}{busMessageName}`
 *   runs the integrations, and `…-Result` fans out to the notifiers. Both exist
 *   for `0Ungrouped` too, which is where everything without a work group lands.
 */
function identify(
  consumerName: string,
  messageName: string,
  workGroups: WorkGroup[],
  informationTypes: InformationTypeRow[],
): Identity {
  const message = messageName.toLowerCase();
  const unlinked = { workGroupId: null, informationTypeId: null, orphaned: false };

  if (consumerName === "BusService") {
    const infoType = informationTypes.find((t) => t.busMessageTypeName?.toLowerCase() === message);
    return {
      lane: "front-door",
      title: infoType?.name ?? messageName,
      role: `listens for ${messageName} on the bus`,
      // Bus messages only get a queue while the information type is bus-enabled, so a
      // name that resolves to nothing means the queue outlived it — and queues are
      // never deleted. Worth saying out loud: it may still be holding messages.
      orphaned: !infoType,
      workGroupId: null,
      informationTypeId: infoType?.id ?? null,
    };
  }

  // Tolerate a consumer class this page hasn't been taught: say so rather than
  // guessing it's a work group lane and inventing a name for it.
  if (consumerName !== "XchangeService") {
    return { lane: "control", title: `${consumerName} · ${messageName}`, role: "not a lane this page knows about", ...unlinked };
  }

  const control = CONTROL[message];
  if (control) return { lane: "control", ...control, ...unlinked };

  const legacy = LEGACY_EVENTS[message];
  if (legacy) return { lane: "legacy", title: legacy, role: "old-style event, consumed for compatibility", ...unlinked };

  const isResult = message.endsWith(RESULT_SUFFIX);
  const groupKey = isResult ? message.slice(0, -RESULT_SUFFIX.length) : message;
  const lane: QueueLane = isResult ? "notifications" : "worker";
  const role = isResult
    ? "checks every notifier against the results from this group"
    : "runs the integrations in this group";

  // Not a group, so it doesn't get the group wording — this is the lane every
  // integration without a work group shares, plus exchanges with no integration
  // at all (an unmatched bus message still produces one).
  if (groupKey === UNGROUPED) {
    return {
      lane,
      title: "Ungrouped",
      role: isResult
        ? "checks every notifier against results that belong to no work group"
        : "runs every integration that has no work group",
      ...unlinked,
    };
  }

  const group = workGroups.find((wg) => `${wg.id}${wg.busMessageName}`.toLowerCase() === groupKey);
  return {
    lane,
    title: group?.name ?? messageName,
    role,
    // Deleting a work group doesn't delete its queues, so this row can outlive
    // the group by design. It still shows depth and dead letters.
    orphaned: !group,
    workGroupId: group?.id ?? null,
    informationTypeId: null,
  };
}

export const queueHealthMethods = {
  async getQueueHealth(): Promise<QueueHealthSnapshot> {
    const [summary, consumers, retries, deadLetters, alerts, workGroups, informationTypes] = await Promise.all([
      get<RawSummary>("/ops/summary"),
      get<RawConsumer[]>("/ops/consumers"),
      get<RawRetryAnalysis[]>("/ops/retries"),
      get<RawDeadLetter[]>("/ops/deadletters"),
      get<RawAlert[]>("/ops/alerts"),
      // Both of these exist only to put names on the queues. They're a different
      // permission from monitoring.view, so neither is allowed to take the page
      // down with it — without them the rows fall back to their raw names, which
      // is what this page showed before it could name anything at all.
      workGroupMethods.listWorkGroups().catch(() => []),
      documentMethods.listInformationTypes().catch(() => []),
    ]);

    const identities = new Map(
      consumers.map((c) => [c.queueName, identify(c.name, c.messageName, workGroups, informationTypes)]),
    );
    // The retry and dead-letter rows carry `{queueName}.retry` / `.bad` and no
    // message name, so they're matched back to their lane by trimming the suffix.
    const titleOf = (queueName: string, suffix: string): string =>
      identities.get(queueName.replace(new RegExp(`\\${suffix}$`), ""))?.title ?? queueName;

    return {
      summary: {
        totalConsumers: summary.totalConsumers,
        unhealthyConsumers: summary.unhealthyConsumers,
        disconnectedConsumers: summary.disconnectedConsumers,
        totalQueueDepth: summary.totalQueueDepth,
        totalRetryBacklog: summary.totalRetryBacklog,
        totalDeadLetterBacklog: summary.totalDeadLetterBacklog,
        totalIncomingRate: summary.totalIncomingRate,
        totalAckRate: summary.totalAckRate,
        lastUpdated: summary.lastUpdatedUtc,
      },
      consumers: consumers.map((c) => ({
        name: c.name,
        messageName: c.messageName,
        queueName: c.queueName,
        ...identities.get(c.queueName)!,
        totalNodes: c.totalNodes,
        processingCount: c.processingCount,
        queueCount: c.queueCount,
        retryCount: c.retryCount,
        failedCount: c.failedCount,
        priority: c.priority,
        prefetch: c.prefetch,
        incomingRate: c.incomingRate,
        ackRate: c.ackRate,
        isBackpressured: c.isBackpressured,
        health: toSeverity(c.healthStatus),
      })),
      retryBacklog: retries.map((r) => ({
        consumerName: r.consumerName,
        title: titleOf(r.queueName, ".retry"),
        queueName: r.queueName,
        retryBacklog: r.retryBacklog,
        incomingRate: r.incomingRate,
        ackRate: r.ackRate,
        severity: toSeverity(r.severity),
      })),
      deadLetters: deadLetters.map((d) => ({
        consumerName: d.consumerName,
        title: titleOf(d.deadLetterQueueName, ".bad"),
        queueName: d.deadLetterQueueName,
        count: d.deadLetterCount,
        lastExceptionType: d.lastExceptionType,
        lastExceptionMessage: d.lastExceptionMessage,
        lastFailedAt: d.lastFailedAt,
      })),
      // The frontend's alert severity is narrower ("warning"|"critical") than
      // the backend's ("Info"|"Warning"|"Critical") — informational alerts
      // aren't alerts from the UI's point of view, so drop them.
      alerts: alerts
        .filter((a) => a.severity !== "Info")
        .map((a) => ({
          severity: a.severity === "Critical" ? ("critical" as const) : ("warning" as const),
          title: a.title,
          detail: a.detail,
          queueName: a.queueName,
          on: a.timestampUtc,
        })),
    };
  },
} satisfies Partial<ApiClient>;
