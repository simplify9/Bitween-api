import type { ApiClient } from "../client";
import type { QueueHealthSnapshot, QueueSeverity } from "../types";
import { workGroupMethods } from "./workGroups";
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

export const queueHealthMethods = {
  async getQueueHealth(): Promise<QueueHealthSnapshot> {
    const [summary, consumers, retries, deadLetters, alerts, workGroups] = await Promise.all([
      get<RawSummary>("/ops/summary"),
      get<RawConsumer[]>("/ops/consumers"),
      get<RawRetryAnalysis[]>("/ops/retries"),
      get<RawDeadLetter[]>("/ops/deadletters"),
      get<RawAlert[]>("/ops/alerts"),
      workGroupMethods.listWorkGroups(),
    ]);

    // The ops endpoints don't expose workGroupId directly — derive it from the
    // consumer's messageName, which for a grouped consumer is always
    // WorkGroup.GetBusMessageName() == `${id}${busMessageName}`.
    const workGroupIdByMessageName = new Map(
      workGroups.map((wg) => [`${wg.id}${wg.busMessageName}`.toLowerCase(), wg.id]),
    );

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
        workGroupId: workGroupIdByMessageName.get(c.messageName.toLowerCase()) ?? null,
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
        queueName: r.queueName,
        retryBacklog: r.retryBacklog,
        incomingRate: r.incomingRate,
        ackRate: r.ackRate,
        severity: toSeverity(r.severity),
      })),
      deadLetters: deadLetters.map((d) => ({
        consumerName: d.consumerName,
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
