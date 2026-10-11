import type { ApiClient } from "../client";
import type { DashboardData, ExchangeStatus } from "../types";
import { subscriptionMethods } from "./subscriptions";
import { get } from "./request";

// --- backend shapes (camelCase over the wire) ---
interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawXchangeForDashboard {
  id: string;
  subscriptionId: number | null;
  documentName: string;
  status: boolean | null;
  responseBad: boolean | null;
  exception: string | null;
  startedOn: string;
}
interface RawAlert {
  severity: "Info" | "Warning" | "Critical";
}
interface RawRetrySummary {
  retriesLast7Days: { finished: number; succeeded: number };
  failingChains: {
    id: string;
    attempts: number;
    subscriptionId: number | null;
    subscriptionName: string | null;
    informationTypeCode: string | null;
    startedOn: string;
    exception: string | null;
  }[];
}

const toStatus = (raw: Pick<RawXchangeForDashboard, "status" | "responseBad">): ExchangeStatus =>
  raw.status === null ? "processing" : !raw.status ? "failed" : raw.responseBad ? "badResponse" : "success";

const LATEST = 6;

export const dashboardMethods = {
  /**
   * What the dashboard shows beside the metrics (which come from /metrics/overview): the latest
   * failures, what is in flight, retries, queue alerts and subscriptions needing attention.
   *
   * Traffic, rates and the busiest used to be counted here, in the browser, from up to 1000
   * exchanges fetched every minute: wrong at any volume above that, and a heavy query for every
   * open dashboard. They come from the server's hourly metrics now; what is left here is small,
   * bounded queries over the last two weeks.
   */
  async getDashboard(): Promise<DashboardData> {
    const now = Date.now();
    const since = (ms: number) => encodeURIComponent(`StartedOn:6:${new Date(now - ms).toISOString()}`);
    const twoWeeks = since(14 * 86_400_000);
    const latest = (status: number) =>
      get<SearchyResponse<RawXchangeForDashboard>>(
        `/xchanges?filter=${twoWeeks}&filter=${encodeURIComponent(`StatusFilter:1:${status}`)}&sort=StartedOn:2&size=${LATEST}`,
      );

    const [failed, badResponses, inFlight, delayedRes, failuresRes, retrySummary, alertsRaw, subscriptionRows] =
      await Promise.all([
        latest(3),
        latest(2),
        get<SearchyResponse<unknown>>(`/xchanges?filter=${since(86_400_000)}&filter=${encodeURIComponent("StatusFilter:1:0")}&size=1`),
        get<SearchyResponse<unknown>>("/delayedretries?size=1"),
        // Only the total is wanted: the problems still to deal with, which the tile's list opens.
        get<SearchyResponse<unknown>>(
          `/xchanges?filter=${encodeURIComponent("StatusFilter:1:3")}&filter=${encodeURIComponent("LatestOnly:1:true")}&size=1`,
        ),
        get<RawRetrySummary>("/dashboard/retrysummary"),
        // Depends on RabbitMQ management being configured on the backend - don't let it take the
        // rest of the dashboard down when it isn't; the "Queue alerts" tile flags it instead.
        get<RawAlert[]>("/ops/alerts").catch(() => null),
        subscriptionMethods.listSubscriptionRows(),
      ]);

    const subscriptionNameById = new Map(subscriptionRows.map((i) => [i.id, i.name]));
    const latestFailures = [...failed.result, ...badResponses.result]
      .sort((a, b) => b.startedOn.localeCompare(a.startedOn))
      .slice(0, LATEST)
      .map((r) => ({
        id: r.id,
        status: toStatus(r),
        subscriptionId: r.subscriptionId,
        subscriptionName: r.subscriptionId !== null ? (subscriptionNameById.get(r.subscriptionId) ?? null) : null,
        informationTypeCode: r.documentName,
        on: r.startedOn,
        exception: r.exception,
      }));

    return {
      inFlight: inFlight.totalCount,
      failuresToActOn: failuresRes.totalCount,
      failingChains: retrySummary.failingChains ?? [],
      retriesLast7Days: retrySummary.retriesLast7Days ?? { finished: 0, succeeded: 0 },
      pendingRetries: delayedRes.totalCount,
      queueAlerts: alertsRaw === null ? null : alertsRaw.filter((a) => a.severity !== "Info").length,
      latestFailures,
      attention: {
        failingSubscriptions: subscriptionRows
          .filter((i) => i.consecutiveFailures > 0)
          .map((i) => ({ id: i.id, name: i.name, consecutiveFailures: i.consecutiveFailures })),
        pausedSubscriptions: subscriptionRows.filter((i) => i.paused).map((i) => ({ id: i.id, name: i.name })),
      },
    };
  },
} satisfies Partial<ApiClient>;
