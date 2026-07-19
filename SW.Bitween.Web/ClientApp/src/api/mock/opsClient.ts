import type { ApiClient } from "../client";
import type {
  ConsumerHealth,
  DeadLetterRow,
  QueueAlert,
  QueueHealthSnapshot,
  QueueSeverity,
  RetryBacklogRow,
} from "../types";
import { delay, loadDb } from "./store";

/**
 * A living RabbitMQ simulation. Consumer topology derives from the seeded
 * work groups (plus the built-in Bitween consumers); the numbers drift a
 * little on every poll so the page demonstrably refreshes. State lives in
 * module memory only — a reload starts a fresh but similar picture.
 */

interface SimConsumer {
  name: string;
  messageName: string;
  queueName: string;
  workGroupId: number | null;
  totalNodes: number;
  prefetch: number;
  priority: number;
  // drifting values
  queueCount: number;
  processingCount: number;
  retryCount: number;
  failedCount: number;
  incomingRate: number;
  ackRate: number;
  /** "struggling" consumers keep a retry backlog and fall behind. */
  struggling: boolean;
}

let sim: SimConsumer[] | null = null;
let startedAt = Date.now();

const buildSim = (): SimConsumer[] => {
  const db = loadDb();
  const base: SimConsumer[] = [
    {
      name: "bitween",
      messageName: "Xchange",
      queueName: "bitween.xchange",
      workGroupId: null,
      totalNodes: 2,
      prefetch: 8,
      priority: 5,
      queueCount: 3,
      processingCount: 4,
      retryCount: 0,
      failedCount: 0,
      incomingRate: 2.4,
      ackRate: 2.5,
      struggling: false,
    },
    {
      name: "bitween-notifications",
      messageName: "Notification",
      queueName: "bitween.notifications",
      workGroupId: null,
      totalNodes: 1,
      prefetch: 16,
      priority: 1,
      queueCount: 0,
      processingCount: 1,
      retryCount: 0,
      failedCount: 0,
      incomingRate: 0.3,
      ackRate: 0.3,
      struggling: false,
    },
  ];
  const groups: SimConsumer[] = db.workGroups.map((g, i) => ({
    name: `bitween-${g.busMessageName}`,
    messageName: `Xchange.${g.busMessageName}`,
    queueName: `bitween.xchange.${g.busMessageName}`,
    workGroupId: g.id,
    totalNodes: 1,
    prefetch: g.options.rabbitMqOptions.consumerSettings.prefetch,
    priority: g.options.rabbitMqOptions.consumerSettings.priority,
    queueCount: [1, 14, 0][i] ?? 2,
    processingCount: [2, 6, 0][i] ?? 1,
    retryCount: [0, 9, 0][i] ?? 0,
    failedCount: [0, 3, 0][i] ?? 0,
    incomingRate: [0.8, 5.2, 0.1][i] ?? 0.5,
    ackRate: [0.8, 3.9, 0.1][i] ?? 0.5,
    // "Bulk uploads" runs hot: incoming outpaces acks, retries pile up.
    struggling: g.busMessageName === "bulk-uploads",
  }));
  return [...base, ...groups];
};

/** Drift a value by ±spread, clamped to ≥ floor, one decimal for rates. */
const drift = (value: number, spread: number, floor = 0) =>
  Math.max(floor, value + (Math.random() * 2 - 1) * spread);

const tick = (consumers: SimConsumer[]) => {
  for (const c of consumers) {
    c.incomingRate = Number(drift(c.incomingRate, 0.4).toFixed(1));
    c.ackRate = Number(
      drift(c.struggling ? c.incomingRate * 0.72 : c.incomingRate, 0.3).toFixed(1),
    );
    c.queueCount = Math.round(
      drift(c.queueCount, c.struggling ? 3 : 1.5, 0) + (c.struggling ? 0.6 : 0),
    );
    c.processingCount = Math.min(c.prefetch, Math.round(drift(c.processingCount, 1)));
    if (c.struggling) {
      c.retryCount = Math.round(drift(c.retryCount, 2, 4));
      c.failedCount = Math.round(drift(c.failedCount, 0.6, 1));
    }
  }
};

const healthOf = (c: SimConsumer): QueueSeverity => {
  if (c.failedCount > 0 || c.retryCount > 12) return "critical";
  if (c.retryCount > 0 || c.queueCount > c.prefetch) return "warning";
  return "healthy";
};

export const opsClient: Pick<ApiClient, "getQueueHealth"> = {
  async getQueueHealth() {
    await delay();
    if (!sim) {
      sim = buildSim();
      startedAt = Date.now();
    }
    tick(sim);

    const consumers: ConsumerHealth[] = sim.map((c) => ({
      name: c.name,
      messageName: c.messageName,
      queueName: c.queueName,
      workGroupId: c.workGroupId,
      totalNodes: c.totalNodes,
      processingCount: c.processingCount,
      queueCount: c.queueCount,
      retryCount: c.retryCount,
      failedCount: c.failedCount,
      priority: c.priority,
      prefetch: c.prefetch,
      incomingRate: c.incomingRate,
      ackRate: c.ackRate,
      isBackpressured: c.struggling && c.queueCount > c.prefetch,
      health: healthOf(c),
    }));

    const retryBacklog: RetryBacklogRow[] = sim
      .filter((c) => c.retryCount > 0)
      .map((c) => ({
        consumerName: c.name,
        queueName: `${c.queueName}.retry`,
        retryBacklog: c.retryCount,
        incomingRate: c.incomingRate,
        ackRate: c.ackRate,
        severity: healthOf(c),
      }));

    const deadLetters: DeadLetterRow[] = sim
      .filter((c) => c.failedCount > 0)
      .map((c) => ({
        consumerName: c.name,
        queueName: `${c.queueName}.dlq`,
        count: c.failedCount,
        lastExceptionType: "System.Threading.Tasks.TaskCanceledException",
        lastExceptionMessage:
          "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.",
        lastFailedAt: new Date(Date.now() - 14 * 60_000).toISOString(),
      }));

    const alerts: QueueAlert[] = consumers
      .filter((c) => c.health !== "healthy")
      .map((c) => ({
        severity: c.health === "critical" ? ("critical" as const) : ("warning" as const),
        title:
          c.health === "critical"
            ? `${c.name} has messages in its dead-letter queue`
            : `${c.name} is falling behind`,
        detail:
          c.health === "critical"
            ? `${c.failedCount} message(s) dead-lettered and ${c.retryCount} waiting to retry — the consumer keeps failing.`
            : `Incoming ${c.incomingRate}/s outpaces acknowledgements ${c.ackRate}/s; the queue is growing.`,
        queueName: c.queueName,
        on: new Date(startedAt).toISOString(),
      }));

    const sum = (pick: (c: ConsumerHealth) => number) =>
      consumers.reduce((acc, c) => acc + pick(c), 0);

    const snapshot: QueueHealthSnapshot = {
      summary: {
        totalConsumers: consumers.length,
        unhealthyConsumers: consumers.filter((c) => c.health !== "healthy").length,
        disconnectedConsumers: 0,
        totalQueueDepth: sum((c) => c.queueCount),
        totalRetryBacklog: sum((c) => c.retryCount),
        totalDeadLetterBacklog: sum((c) => c.failedCount),
        totalIncomingRate: Number(sum((c) => c.incomingRate).toFixed(1)),
        totalAckRate: Number(sum((c) => c.ackRate).toFixed(1)),
        lastUpdated: new Date().toISOString(),
      },
      consumers,
      retryBacklog,
      deadLetters,
      alerts,
    };
    return snapshot;
  },
};
