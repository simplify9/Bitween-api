import type { ApiClient } from "../client";
import type { ExchangeStatus, LatestFailure, RetrySummary } from "../types";
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

const startedSince = (ms: number) => encodeURIComponent(`StartedOn:6:${new Date(Date.now() - ms).toISOString()}`);

/**
 * What the dashboard shows beside the metrics (which come from /metrics/overview) and the
 * subscription rows, one method per part so each can fill in on its own.
 *
 * Traffic, rates and the busiest used to be counted here, in the browser, from up to 1000
 * exchanges fetched every minute: wrong at any volume above that, and a heavy query for every
 * open dashboard. They come from the server's hourly metrics now.
 */
export const dashboardMethods = {
  /** Two searches over the exchanges of the last two weeks: on a busy instance, seconds. */
  async getLatestFailures(): Promise<LatestFailure[]> {
    const twoWeeks = startedSince(14 * 86_400_000);
    const latest = (status: number) =>
      get<SearchyResponse<RawXchangeForDashboard>>(
        `/xchanges?filter=${twoWeeks}&filter=${encodeURIComponent(`StatusFilter:1:${status}`)}&sort=StartedOn:2&size=${LATEST}`,
      );

    const [failed, badResponses] = await Promise.all([latest(3), latest(2)]);
    return [...failed.result, ...badResponses.result]
      .sort((a, b) => b.startedOn.localeCompare(a.startedOn))
      .slice(0, LATEST)
      .map((r) => ({
        id: r.id,
        status: toStatus(r),
        subscriptionId: r.subscriptionId,
        informationTypeCode: r.documentName,
        on: r.startedOn,
        exception: r.exception,
      }));
  },

  async getPendingRetries(): Promise<number> {
    return (await get<SearchyResponse<unknown>>("/delayedretries?size=1")).totalCount;
  },

  async getQueueAlerts(): Promise<number | null> {
    // Depends on RabbitMQ management being configured on the backend; the "Queue alerts" tile
    // flags it when it isn't.
    const alerts = await get<RawAlert[]>("/ops/alerts").catch(() => null);
    return alerts === null ? null : alerts.filter((a) => a.severity !== "Info").length;
  },

  /**
   * The problems still to deal with, which the tile's list opens. Counted by the server from the
   * failures: the list fetches rows newest first, which for this filter can mean reading back
   * through millions of exchanges first.
   */
  async getFailuresToActOn(): Promise<number> {
    return (await get<{ total: number }>("/dashboard/failurestoacton")).total;
  },

  async getInFlight(): Promise<number> {
    const lastDay = startedSince(86_400_000);
    return (
      await get<SearchyResponse<unknown>>(
        `/xchanges?filter=${lastDay}&filter=${encodeURIComponent("StatusFilter:1:0")}&size=1`,
      )
    ).totalCount;
  },

  async getRetrySummary(): Promise<RetrySummary> {
    const raw = await get<RawRetrySummary>("/dashboard/retrysummary");
    return {
      failingChains: raw.failingChains ?? [],
      retriesLast7Days: raw.retriesLast7Days ?? { finished: 0, succeeded: 0 },
    };
  },
} satisfies Partial<ApiClient>;
