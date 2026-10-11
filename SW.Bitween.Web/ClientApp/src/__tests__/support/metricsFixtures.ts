import { http, HttpResponse } from "msw";
import type { ExchangeSummary, Insights, MetricsOverview } from "../../api/metricsTypes";

// Its own copy of renderApp's helper: renderApp imports this module for its defaults.
const apiPath = (path: string) => `*/api${path}`;

export const summary = (over: Partial<ExchangeSummary> = {}): ExchangeSummary => ({
  total: 0,
  succeeded: 0,
  badResponse: 0,
  failed: 0,
  retries: 0,
  manualRetries: 0,
  successRate: null,
  p50Ms: null,
  p95Ms: null,
  bytes: 0,
  ...over,
});

/** GET /metrics/overview on an instance with nothing yet, with whatever a test sets. */
export const overview = (over: Partial<MetricsOverview> = {}): MetricsOverview => ({
  since: null,
  hourly: [],
  weekAgo: [],
  last24h: summary(),
  previous24h: summary(),
  last7d: summary(),
  daily: [],
  topFailing: [],
  busiestSubscriptions: [],
  busiestPartners: [],
  slowestAdapters: [],
  silent: [],
  failing: 0,
  degraded: 0,
  backlog: [],
  ...over,
});

/** The dashboard's metrics endpoints, answering `overview` and `insights`. */
export const metricsHandlers = (
  o: MetricsOverview = overview(),
  insights: Insights = { since: null, insights: [] },
) => [
  http.get(apiPath("/metrics/overview"), () => HttpResponse.json(o)),
  http.get(apiPath("/metrics/insights"), () => HttpResponse.json(insights)),
];

/**
 * Every metrics endpoint answering "nothing counted yet", which renderApp installs for every test
 * (a test's own handlers come first and win). Pages show metrics beside what they are about, and
 * a test about something else shouldn't have to answer for them.
 */
export const defaultMetrics = () => [
  ...metricsHandlers(),
  http.get(apiPath("/metrics/adapters"), ({ request }) =>
    HttpResponse.json({
      since: null,
      days: Number(new URL(request.url).searchParams.get("days") ?? 7),
      adapters: [],
      quiet: [],
    }),
  ),
  http.get(apiPath("/metrics/adapter"), ({ request }) =>
    HttpResponse.json({
      id: new URL(request.url).searchParams.get("id"),
      days: 7,
      since: null,
      total: {
        calls: 0,
        failures: 0,
        failureRate: null,
        timeouts: 0,
        limitStops: 0,
        crashes: 0,
        p50Ms: null,
        p95Ms: null,
        maxMs: 0,
      },
      versions: [],
      nodes: [],
      roles: [],
      daily: [],
      errors: [],
      subscriptions: [],
    }),
  ),
  http.get(apiPath("/metrics/subscriptions"), () => HttpResponse.json({ since: null, days: 7, subscriptions: [] })),
  http.get(apiPath("/metrics/subscription"), ({ request }) =>
    HttpResponse.json({
      id: Number(new URL(request.url).searchParams.get("id")),
      days: 7,
      since: null,
      status: null,
      lastExchangeHour: null,
      quietHours: null,
      usualGapHours: null,
      total: { ...summary(), failedAtMapper: 0, failedAtHandler: 0 },
      hourly: [],
      daily: [],
      nodes: [],
      errors: [],
    }),
  ),
  http.get(apiPath("/metrics/partners"), () => HttpResponse.json({ since: null, days: 7, partners: [] })),
  http.get(apiPath("/metrics/nodes"), () => HttpResponse.json({ nodes: [] })),
];
