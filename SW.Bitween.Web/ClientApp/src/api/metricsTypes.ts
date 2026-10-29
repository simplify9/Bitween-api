/*
 * The metrics endpoints' answers (GET /api/metrics/...). Every number is read from hourly rows
 * the server keeps; "since" is when they begin, so a page can say so rather than show an empty
 * past as no traffic.
 */

export interface ExchangeSummary {
  total: number;
  succeeded: number;
  badResponse: number;
  failed: number;
  retries: number;
  manualRetries: number;
  /** Share of finished exchanges that succeeded; null without any. */
  successRate: number | null;
  p50Ms: number | null;
  p95Ms: number | null;
  bytes: number;
}

export type HealthStatus = "healthy" | "degraded" | "failing" | "silent" | "idle" | "paused" | "inactive";

export interface SilentSubscription {
  subscriptionId: number;
  name: string;
  type: string;
  lastExchangeHour: string | null;
  quietHours: number | null;
  usualGapHours: number | null;
}

export interface MetricsOverview {
  since: string | null;
  hourly: { hour: string; total: number; succeeded: number; badResponse: number; failed: number }[];
  weekAgo: { hour: string; total: number }[];
  last24h: ExchangeSummary;
  previous24h: ExchangeSummary;
  last7d: ExchangeSummary;
  daily: { day: string; total: number; succeeded: number; badResponse: number; failed: number; bytes: number }[];
  topFailing: { subscriptionId: number; name: string | null; failed: number; total: number }[];
  busiestSubscriptions: {
    subscriptionId: number;
    name: string | null;
    total: number;
    failed: number;
    badResponse: number;
  }[];
  busiestPartners: { partnerId: number; name: string | null; total: number; failed: number }[];
  slowestAdapters: { adapterId: string; calls: number; p95Ms: number | null; failureRate: number | null }[];
  silent: SilentSubscription[];
  failing: number;
  degraded: number;
  backlog: { at: string; queueDepth: number; retryBacklog: number; deadLetterBacklog: number }[];
}

export interface AdapterUsageTotals {
  calls: number;
  failures: number;
  failureRate: number | null;
  timeouts: number;
  limitStops: number;
  crashes: number;
  p50Ms: number | null;
  p95Ms: number | null;
  maxMs: number;
}

export interface AdapterUsage extends AdapterUsageTotals {
  id: string;
  lastCalledOn: string | null;
  daily: { day: string; calls: number; failures: number }[];
}

export interface AdapterUsageList {
  since: string | null;
  days: number;
  adapters: AdapterUsage[];
  /** Called in the last 30 days but not in the window. */
  quiet: { id: string; lastCalledOn: string }[];
}

export interface ErrorGroup {
  fingerprint: string;
  count: number;
  lastSeenOn: string;
  sample: string | null;
}

export interface AdapterUsageDetail {
  id: string;
  days: number;
  since: string | null;
  total: AdapterUsageTotals;
  versions: { version: string; totals: AdapterUsageTotals }[];
  nodes: { node: string; totals: AdapterUsageTotals }[];
  roles: { role: string; runtime: string; totals: AdapterUsageTotals }[];
  daily: { day: string; calls: number; failures: number; p95Ms: number | null }[];
  errors: ErrorGroup[];
  subscriptions: {
    subscriptionId: number;
    name: string;
    type: string;
    exchanges: number;
    failed: number;
    bytes: number;
  }[];
}

export interface SubscriptionHealthRow {
  subscriptionId: number;
  name: string;
  type: string;
  status: HealthStatus;
  successRate: number | null;
  p95Ms: number | null;
  lastExchangeHour: string | null;
  quietHours: number | null;
  usualGapHours: number | null;
  daily: number[];
  total: number;
  failed: number;
  badResponse: number;
  retries: number;
  manualRetries: number;
  bytes: number;
}

export interface SubscriptionHealthList {
  since: string | null;
  days: number;
  subscriptions: SubscriptionHealthRow[];
}

export interface SubscriptionMetrics {
  id: number;
  days: number;
  since: string | null;
  status: HealthStatus | null;
  lastExchangeHour: string | null;
  quietHours: number | null;
  usualGapHours: number | null;
  total: ExchangeSummary & { failedAtMapper: number; failedAtHandler: number };
  hourly: { hour: string; total: number; failed: number; badResponse: number }[];
  daily: { day: string; total: number; failed: number; p95Ms: number | null }[];
  nodes: { node: string; total: number; failed: number }[];
  errors: ErrorGroup[];
}

export interface PartnerUsage {
  partnerId: number;
  name: string;
  exchanges: number;
  failed: number;
  badResponse: number;
  successRate: number | null;
  bytes: number;
  lastExchangeHour: string | null;
  keyLastUsedOn: string | null;
}

export interface PartnerUsageList {
  since: string | null;
  days: number;
  partners: PartnerUsage[];
}

export interface ResidentInstance {
  /** adapter id, state, pid, memory MB, CPU %, uptime s, restarts, quarantined, in flight, last error. */
  a: string;
  s: string;
  pid: number | null;
  mb: number;
  cpu: number;
  up: number;
  r: number;
  q: boolean;
  f: number;
  e: string | null;
}

export interface NodeMetrics {
  nodes: {
    id: string;
    host: string;
    alive: boolean;
    lastSeenOn: string;
    exchanges: number;
    failed: number;
    successRate: number | null;
    p95Ms: number | null;
    adapterCalls: number;
    adapterFailures: number;
    timeouts: number;
    limitStops: number;
    crashes: number;
    resident: ResidentInstance[];
  }[];
}

export interface Insight {
  kind: string;
  severity: "info" | "warn";
  title: string;
  detail: string;
  count: number;
  items: { label: string; href: string }[];
}

export interface Insights {
  since: string | null;
  insights: Insight[];
}
