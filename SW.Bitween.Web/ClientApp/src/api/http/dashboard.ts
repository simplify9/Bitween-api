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

const isBad = (raw: Pick<RawXchangeForDashboard, "status" | "responseBad">) =>
  raw.status === false || (raw.status === true && raw.responseBad === true);
const isProcessing = (raw: Pick<RawXchangeForDashboard, "status">) => raw.status === null;
const toStatus = (raw: Pick<RawXchangeForDashboard, "status" | "responseBad">): ExchangeStatus =>
  raw.status === null ? "processing" : !raw.status ? "failed" : raw.responseBad ? "badResponse" : "success";

export const dashboardMethods = {
  async getDashboard(): Promise<DashboardData> {
    const dayMs = 86_400_000;
    const startOfTodayUtc = new Date(new Date().setUTCHours(0, 0, 0, 0)).getTime();
    // 14 calendar days including today.
    const windowStart = startOfTodayUtc - 13 * dayMs;

    // One bulk fetch covers every stat that's derived from exchange rows
    // (today/yesterday/successRate7d/trafficByDay/busiest/latestFailures) -
    // far fewer round trips than counting each bucket with its own filtered
    // request, and exact rather than approximated. Bounded to a generous page
    // size for what's a modest-scale ops tool; a very high-volume deployment
    // would need real pagination here.
    const [xchangeRes, delayedRes, failuresRes, retrySummary, alertsRaw, subscriptionRows] = await Promise.all([
      get<SearchyResponse<RawXchangeForDashboard>>(
        `/xchanges?filter=${encodeURIComponent(`StartedOn:6:${new Date(windowStart).toISOString()}`)}&size=1000&sort=StartedOn:1`,
      ),
      get<SearchyResponse<unknown>>("/delayedretries?size=1"),
      // Counted by the search rather than from the rows above, for two reasons: those are capped
      // at a page of 1000 and at 14 days, and this number has to agree with the list the tile
      // opens - which carries no date bound. Only the total is wanted, so no rows are fetched.
      get<SearchyResponse<unknown>>(
        `/xchanges?filter=${encodeURIComponent("StatusFilter:1:3")}&filter=${encodeURIComponent("LatestOnly:1:true")}&size=1`,
      ),
      get<RawRetrySummary>("/dashboard/retrysummary"),
      // Depends on RabbitMQ management being configured on the backend - don't let it take the
      // rest of the dashboard down when it isn't; the "Queue alerts" tile flags it instead.
      get<RawAlert[]>("/ops/alerts").catch(() => null),
      subscriptionMethods.listSubscriptionRows(),
    ]);

    const rows = xchangeRes.result;
    const startedAt = (r: RawXchangeForDashboard) => Date.parse(r.startedOn);
    const subscriptionNameById = new Map(subscriptionRows.map((i) => [i.id, i.name]));

    const todayRows = rows.filter((r) => startedAt(r) >= startOfTodayUtc);
    const yesterdayRows = rows.filter((r) => {
      const t = startedAt(r);
      return t >= startOfTodayUtc - dayMs && t < startOfTodayUtc;
    });
    // The last 7 calendar days including today - the same boundary as the
    // final 7 entries of trafficByDay below.
    const sevenDaysAgo = startOfTodayUtc - 6 * dayMs;
    const week = rows.filter((r) => startedAt(r) >= sevenDaysAgo);
    const finished7d = week.filter((r) => !isProcessing(r));
    const successRate7d =
      finished7d.length === 0
        ? 100
        : Math.round((finished7d.filter((r) => !isBad(r)).length / finished7d.length) * 100);

    const trafficByDay = Array.from({ length: 14 }, (_, i) => {
      const dayStart = windowStart + i * dayMs;
      const dayRows = rows.filter((r) => {
        const t = startedAt(r);
        return t >= dayStart && t < dayStart + dayMs;
      });
      return {
        date: new Date(dayStart).toISOString(),
        success: dayRows.filter((r) => !isBad(r)).length,
        failed: dayRows.filter(isBad).length,
      };
    });

    const bySubscription = new Map<number, { count: number; failed: number }>();
    for (const r of week) {
      if (r.subscriptionId === null) continue;
      const entry = bySubscription.get(r.subscriptionId) ?? { count: 0, failed: 0 };
      entry.count++;
      if (isBad(r)) entry.failed++;
      bySubscription.set(r.subscriptionId, entry);
    }
    const busiest = [...bySubscription.entries()]
      .map(([id, v]) => ({ id, name: subscriptionNameById.get(id) ?? `#${id}`, ...v }))
      .sort((a, b) => b.count - a.count)
      .slice(0, 5);

    const latestFailures = rows
      .filter(isBad)
      .sort((a, b) => b.startedOn.localeCompare(a.startedOn))
      .slice(0, 6)
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
      today: {
        total: todayRows.length,
        failed: todayRows.filter(isBad).length,
        processing: todayRows.filter(isProcessing).length,
      },
      yesterdayTotal: yesterdayRows.length,
      failuresToActOn: failuresRes.totalCount,
      failingChains: retrySummary.failingChains ?? [],
      retriesLast7Days: retrySummary.retriesLast7Days ?? { finished: 0, succeeded: 0 },
      successRate7d,
      pendingRetries: delayedRes.totalCount,
      queueAlerts: alertsRaw === null ? null : alertsRaw.filter((a) => a.severity !== "Info").length,
      trafficByDay,
      busiest,
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
