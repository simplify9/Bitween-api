import type { ApiClient } from "../client";
import type { QueueHealthSnapshot, QueueLane, QueueSeverity, UnattendedQueue } from "../types";
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
  lane: QueueLane;
  title: string;
  workGroupId: number | null;
  informationTypeId: number | null;
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
    const [summary, consumers, retries, deadLetters, alerts, unattended] = await Promise.all([
      get<RawSummary>("/ops/summary"),
      get<RawConsumer[]>("/ops/consumers"),
      get<RawRetryAnalysis[]>("/ops/retries"),
      get<RawDeadLetter[]>("/ops/deadletters"),
      get<RawAlert[]>("/ops/alerts"),
      // The one call that asks the broker for a list rather than for statistics. Newest and
      // least proven thing on an on-call page, so it isn't allowed to take the rest with it.
      get<UnattendedQueue[]>("/ops/unattendedqueues").catch(() => []),
    ]);

    // The retry and dead-letter rows carry `{queueName}.retry` / `.bad` and no lane of
    // their own, so they're matched back to a consumer row by trimming the suffix.
    const titleByQueue = new Map(consumers.map((c) => [c.queueName, c.title]));
    const titleOf = (queueName: string, suffix: string): string =>
      titleByQueue.get(queueName.replace(new RegExp(`\\${suffix}$`), "")) ?? queueName;

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
        lane: c.lane,
        title: c.title,
        workGroupId: c.workGroupId,
        informationTypeId: c.informationTypeId,
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
      unattended,
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
