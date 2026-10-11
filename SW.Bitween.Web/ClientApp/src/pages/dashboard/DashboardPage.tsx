import { useState, type ReactNode } from "react";
import { Link } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { BellOff, Info, TriangleAlert } from "lucide-react";
import { api } from "../../api";
import type { MetricsOverview } from "../../api/metricsTypes";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Panel } from "../../components/ui/Panel";
import { Lines } from "../../components/charts/Lines";
import { TimeBars, type BarSeries } from "../../components/charts/TimeBars";
import { useRabbitMqManagementConfigured } from "../../lib/appConfig";
import { formatDate, formatDurationMs, timeAgo } from "../../lib/dates";
import { bytes, compact, percent, whole } from "../../lib/numbers";
import { subscriptionPath } from "../../lib/subscriptionPaths";
import { StatusBadge, XchangeId } from "../exchanges/shared";
import { keys } from "../../api/queryKeys";
import { ExceptionLine } from "../../components/ui/Exception";
import { useSessionCan } from "../../auth/useSessionCan";
import { GettingStarted } from "./GettingStarted";

/** Single-line rows, so ten fill about the height of the six two-line "Latest failures" beside them. */
const HEALTH_PAGE_SIZE = 10;

const hourLabel = new Intl.DateTimeFormat("en", { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
const hourTick = new Intl.DateTimeFormat("en", { hour: "2-digit", minute: "2-digit" });
const dayLabel = new Intl.DateTimeFormat("en", { day: "numeric", month: "short" });

/** The three outcomes, in the status colors, always named in the legend and tooltip. */
const OUTCOMES: BarSeries[] = [
  { key: "succeeded", label: "Succeeded", className: "bg-ok-600" },
  { key: "badResponse", label: "Bad response", className: "bg-warn-400" },
  { key: "failed", label: "Failed", className: "bg-danger-600" },
];

/** Categorical slots 1-3, validated together on white; the chart also has a legend and a table. */
const BACKLOG = [
  { key: "queueDepth", label: "Waiting", color: "#2a78d6" },
  { key: "retryBacklog", label: "Retrying", color: "#eb6834" },
  { key: "deadLetterBacklog", label: "Dead letters", color: "#1baf7a" },
];

function StatTile({
  label,
  value,
  sub,
  to,
  accent,
  title,
}: {
  label: string;
  value: ReactNode;
  sub?: ReactNode;
  to: string;
  accent?: "danger" | "warn";
  /** For a number whose meaning its label cannot carry on its own. */
  title?: string;
}) {
  return (
    <Link
      to={to}
      title={title}
      className={`rounded-xl border bg-white px-4 py-3 transition-colors hover:bg-ink-50 ${
        accent === "danger" ? "border-danger-200" : accent === "warn" ? "border-warn-300" : "border-ink-200"
      }`}
    >
      <p className="text-[13px] text-ink-500">{label}</p>
      <p
        className={`mt-0.5 text-2xl font-semibold ${
          accent === "danger" ? "text-danger-700" : accent === "warn" ? "text-warn-700" : "text-ink-900"
        }`}
      >
        {value}
      </p>
      {sub && <p className="mt-0.5 text-xs text-ink-500">{sub}</p>}
    </Link>
  );
}

/** Every hour in the window, the empty ones as zero: a gap in the chart is an hour with nothing. */
function hours(overview: MetricsOverview) {
  const byHour = new Map(overview.hourly.map((h) => [Date.parse(h.hour), h]));
  const weekAgo = new Map(overview.weekAgo.map((h) => [Date.parse(h.hour), h.total]));
  const end = new Date();
  end.setUTCMinutes(0, 0, 0);
  return Array.from({ length: 48 }, (_, i) => {
    const t = end.getTime() - (47 - i) * 3_600_000;
    const h = byHour.get(t);
    return {
      at: new Date(t).toISOString(),
      values: { succeeded: h?.succeeded ?? 0, badResponse: h?.badResponse ?? 0, failed: h?.failed ?? 0 },
      weekAgo: weekAgo.get(t) ?? 0,
    };
  });
}

/**
 * The operational front page: the last day at a glance, two days of traffic against the same
 * hours last week, a month of volume, and what needs a human - every number links to the page
 * that can act on it. The numbers come from the server's hourly metrics; nothing is counted here.
 */
export function DashboardPage() {
  const rabbitMqConfigured = useRabbitMqManagementConfigured();
  const [healthOffset, setHealthOffset] = useState(0);
  const canSeeAdapters = useSessionCan("subscriptions.view");
  const dashboard = useQuery({
    queryKey: keys.dashboard,
    queryFn: () => api.getDashboard(),
    refetchInterval: 60_000,
    placeholderData: keepPreviousData,
  });
  const metrics = useQuery({
    queryKey: keys.metrics.overview,
    queryFn: () => api.getMetricsOverview(),
    refetchInterval: 60_000,
    placeholderData: keepPreviousData,
  });
  const insights = useQuery({ queryKey: keys.metrics.insights, queryFn: () => api.getInsights() });

  const data = dashboard.data;
  const o = metrics.data;
  if ((dashboard.isError && !data) || (metrics.isError && !o))
    return (
      <EmptyState title="Couldn't load the dashboard">
        Something went wrong fetching dashboard data. It'll keep retrying in the background - try refreshing the page.
      </EmptyState>
    );
  if (!data || !o) return <LoadingBlock label="Putting the picture together…" />;

  const queueAlerts = rabbitMqConfigured ? data.queueAlerts : null;
  const queueAlertsSub = !rabbitMqConfigured
    ? "needs RabbitMQ management configured"
    : queueAlerts === null
      ? "unavailable right now"
      : "live consumer health";
  const delta = o.last24h.total - o.previous24h.total;
  const failed24 = o.last24h.failed + o.last24h.badResponse;
  const perHour = hours(o);
  const days = o.daily.map((d) => ({
    at: d.day,
    values: { succeeded: d.succeeded, badResponse: d.badResponse, failed: d.failed },
  }));
  const moved30 = o.daily.reduce((n, d) => n + d.bytes, 0);
  const sinceRecent = o.since && Date.now() - Date.parse(o.since) < 30 * 86_400_000;

  const unhealthy = [
    ...data.attention.failingSubscriptions.map((s) => ({ ...s, paused: false })),
    ...data.attention.pausedSubscriptions.map((s) => ({ ...s, consecutiveFailures: 0, paused: true })),
  ];
  // The list can shrink on a refetch; don't leave the page pointing past its end. Reset the stored
  // offset too, or a later refetch that grows the list again jumps back to the old page.
  const healthStart = healthOffset < unhealthy.length ? healthOffset : 0;
  if (healthStart !== healthOffset) setHealthOffset(0);

  return (
    <div>
      <PageHeader
        title="Dashboard"
        description={`Traffic and health at a glance - every number links to the page where you can act on it.${
          sinceRecent ? ` Metrics go back to ${formatDate(o.since!)}.` : ""
        }`}
      />

      <GettingStarted exchangesSeen={o.daily.some((d) => d.total > 0) || o.last24h.total > 0} />

      <div className="mb-4 grid grid-cols-2 gap-2.5 sm:grid-cols-4">
        <StatTile
          label="Exchanges, last 24 hours"
          value={compact(o.last24h.total)}
          title={whole(o.last24h.total)}
          sub={
            <>
              {delta >= 0 ? "+" : ""}
              {compact(delta)} vs the day before
              {data.inFlight > 0 && <> · {compact(data.inFlight)} in flight</>}
            </>
          }
          to="/exchanges"
        />
        <StatTile
          label="Failed or rejected, last 24 hours"
          value={compact(failed24)}
          sub={failed24 > 0 ? `${compact(o.last24h.badResponse)} bad responses` : "nothing failed"}
          to="/exchanges?status=failed"
          accent={failed24 > 0 ? "danger" : undefined}
        />
        <StatTile
          label="Success rate, 7 days"
          value={percent(o.last7d.successRate)}
          sub={o.last7d.total > 0 ? `of ${compact(o.last7d.total)} exchanges` : "nothing finished yet"}
          to="/exchanges?status=success"
        />
        <StatTile
          label="End-to-end time, last 24 hours"
          value={o.last24h.p95Ms === null ? "-" : formatDurationMs(o.last24h.p95Ms)}
          sub={
            o.last24h.p50Ms === null
              ? "from arriving to done"
              : `95% within; half within ${formatDurationMs(o.last24h.p50Ms)}`
          }
          to="/exchanges"
          title="From an exchange arriving to its being finished, including any wait in the queue."
        />
        {/* "Failed" counts attempts, and a chain retried nine times is nine of them - none of which
            is the current state of anything. This counts the ends of chains: the problems. */}
        <StatTile
          label="Failures to act on"
          // The search stops counting past its cap and reports the cap + 1.
          value={data.failuresToActOn > 10_000 ? "10,000+" : compact(data.failuresToActOn)}
          sub={data.failuresToActOn > 0 ? "newest attempt of each chain" : "every failure was retried"}
          to="/exchanges?status=failed&latest=1"
          accent={data.failuresToActOn > 0 ? "danger" : undefined}
          title="Failures that nothing has been retried from yet. A chain of nine failed attempts counts once - as the attempt at the end of it, which is the one still to deal with."
        />
        <StatTile
          label="Pending auto-retries"
          value={compact(data.pendingRetries)}
          sub={data.pendingRetries > 0 ? "waiting for their slot" : "queue is clear"}
          to="/scheduled-retries"
          accent={data.pendingRetries > 0 ? "warn" : undefined}
        />
        <StatTile
          label="Queue alerts"
          value={queueAlerts ?? "-"}
          sub={queueAlertsSub}
          to="/queue-health"
          accent={queueAlerts !== null && queueAlerts > 0 ? "warn" : undefined}
        />
        <StatTile
          label="Gone quiet"
          value={o.silent.length}
          sub={o.silent.length > 0 ? "usually busy, now silent" : "every busy subscription is busy"}
          to="/subscriptions?health=silent"
          accent={o.silent.length > 0 ? "warn" : undefined}
          title="Subscriptions that normally have traffic and have had none for well over their usual gap. Nothing fails when a partner stops sending, so nothing else would say so."
        />
      </div>

      <div className="mb-4 grid gap-4 xl:grid-cols-2">
        <Panel title="Traffic, last 48 hours" description="Exchanges per hour, with the same hours a week ago.">
          <TimeBars
            points={perHour}
            series={OUTCOMES}
            compare={{ label: "A week ago", values: perHour.map((p) => p.weekAgo) }}
            label={(at) => hourLabel.format(new Date(at))}
            tick={(at) => hourTick.format(new Date(at))}
            every={8}
          />
        </Panel>
        <Panel
          title="Volume, last 30 days"
          description={`Exchanges per day; ${bytes(moved30)} moved in and out over the month.`}
        >
          {days.length === 0 ? (
            <EmptyState title="No traffic yet">Once exchanges flow, each day's volume shows here.</EmptyState>
          ) : (
            <TimeBars
              points={days}
              series={OUTCOMES}
              label={(at) => dayLabel.format(new Date(at))}
              tick={(at) => dayLabel.format(new Date(at))}
              every={5}
            />
          )}
        </Panel>
      </div>

      <div className="mb-4 grid gap-4 lg:grid-cols-2">
        <Panel
          title="Gone quiet"
          description="Subscriptions that usually have traffic and have had none for well over their usual gap."
        >
          {o.silent.length === 0 ? (
            <EmptyState icon={<BellOff />} title="Nothing has gone quiet">
              Every subscription with regular traffic still has it.
            </EmptyState>
          ) : (
            <ul className="space-y-2">
              {o.silent.map((s) => (
                <li key={s.subscriptionId} className="flex items-center gap-2.5 text-sm">
                  <Link
                    to={subscriptionPath(s.subscriptionId)}
                    className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                  >
                    {s.name}
                  </Link>
                  <span className="text-xs text-ink-500">
                    quiet {formatDurationMs((s.quietHours ?? 0) * 3_600_000)}, usually every{" "}
                    {formatDurationMs((s.usualGapHours ?? 0) * 3_600_000)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Panel>

        <Panel title="Subscription health" description="Subscriptions that aren't running clean.">
          {unhealthy.length === 0 ? (
            <EmptyState title="All subscriptions healthy">No failures piling up, nothing paused.</EmptyState>
          ) : (
            <>
              <ul className="space-y-2">
                {unhealthy.slice(healthStart, healthStart + HEALTH_PAGE_SIZE).map((s) => (
                  <li key={`${s.paused ? "p" : "f"}-${s.id}`} className="flex items-center gap-2.5 text-sm">
                    <Link
                      to={subscriptionPath(s.id)}
                      className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                    >
                      {s.name}
                    </Link>
                    {s.paused ? (
                      <Badge tone="warn">Paused</Badge>
                    ) : (
                      <Badge tone="danger">
                        {s.consecutiveFailures} consecutive failure{s.consecutiveFailures === 1 ? "" : "s"}
                      </Badge>
                    )}
                  </li>
                ))}
              </ul>
              {unhealthy.length > HEALTH_PAGE_SIZE && (
                <div className="mt-3 flex items-center justify-between text-[13px]">
                  <span className="text-ink-500 tabular-nums" title="Failing subscriptions first, then paused ones.">
                    {healthStart + 1}-{Math.min(healthStart + HEALTH_PAGE_SIZE, unhealthy.length)} of {unhealthy.length}
                  </span>
                  <span className="flex gap-4 font-medium text-crimson-700">
                    <button
                      type="button"
                      className="hover:underline disabled:text-ink-300 disabled:no-underline"
                      disabled={healthStart === 0}
                      onClick={() => setHealthOffset(Math.max(0, healthStart - HEALTH_PAGE_SIZE))}
                    >
                      ← Previous
                    </button>
                    <button
                      type="button"
                      className="hover:underline disabled:text-ink-300 disabled:no-underline"
                      disabled={healthStart + HEALTH_PAGE_SIZE >= unhealthy.length}
                      onClick={() => setHealthOffset(healthStart + HEALTH_PAGE_SIZE)}
                    >
                      Next →
                    </button>
                  </span>
                </div>
              )}
            </>
          )}
        </Panel>
      </div>

      <div className="mb-4 grid gap-4 lg:grid-cols-2">
        <Panel title="Latest failures" description="The most recent exchanges that need a look.">
          {data.latestFailures.length === 0 ? (
            <EmptyState title="Nothing has failed">When an exchange fails, it shows up here first.</EmptyState>
          ) : (
            <ul className="space-y-2.5">
              {data.latestFailures.map((f) => (
                <li key={f.id} className="text-sm">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <XchangeId id={f.id} />
                    <StatusBadge status={f.status} />
                    {f.subscriptionName && (
                      <Link
                        to={subscriptionPath(f.subscriptionId!)}
                        className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                      >
                        {f.subscriptionName}
                      </Link>
                    )}
                    <span className="ml-auto text-xs text-ink-500">{timeAgo(f.on)}</span>
                  </div>
                  {f.exception && <ExceptionLine text={f.exception} className="mt-0.5 text-[12px] text-ink-600" />}
                </li>
              ))}
            </ul>
          )}
          <Link
            to="/exchanges?status=failed"
            className="mt-3 inline-block text-[13px] font-medium text-crimson-700 hover:underline"
          >
            All failed exchanges →
          </Link>
        </Panel>

        {/* "Latest failures" answers what broke most recently. This answers which of them is not
            getting better however often it is retried - bad luck, or something to go and fix. */}
        <Panel
          title="Chains that keep failing"
          description={
            data.retriesLast7Days.finished === 0
              ? "Retried repeatedly and still failing."
              : `Retried repeatedly and still failing. Of retries in the last 7 days, ${data.retriesLast7Days.succeeded} of ${data.retriesLast7Days.finished} eventually worked.`
          }
        >
          {data.failingChains.length === 0 ? (
            <EmptyState title="No chain is stuck">Nothing has been retried more than once and left failing.</EmptyState>
          ) : (
            <ul className="space-y-2.5">
              {data.failingChains.map((c) => (
                <li key={c.id} className="text-sm">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <Link
                      to={`/exchanges?ids=${encodeURIComponent(c.id)}`}
                      title="Open the newest attempt, where its whole chain is listed"
                      className="shrink-0"
                    >
                      <Badge
                        tone="danger"
                        title={`${c.attempts} attempts have been made, and the newest one still failed`}
                      >
                        {c.attempts} attempts
                      </Badge>
                    </Link>
                    {c.informationTypeCode && <span className="font-medium text-ink-800">{c.informationTypeCode}</span>}
                    {c.subscriptionName && (
                      <Link
                        to={subscriptionPath(c.subscriptionId!)}
                        className="text-ink-600 hover:text-crimson-700 hover:underline"
                      >
                        {c.subscriptionName}
                      </Link>
                    )}
                    <span className="ml-auto text-xs text-ink-500">{timeAgo(c.startedOn)}</span>
                  </div>
                  {c.exception && <ExceptionLine text={c.exception} className="mt-0.5 text-[12px] text-ink-600" />}
                </li>
              ))}
            </ul>
          )}
          <Link
            to="/exchanges?status=failed&latest=1"
            className="mt-3 inline-block text-[13px] font-medium text-crimson-700 hover:underline"
          >
            All failures to act on →
          </Link>
        </Panel>
      </div>

      <div className="mb-4 grid gap-4 lg:grid-cols-3">
        <Panel title="Busiest subscriptions" description="Exchanges over the last 7 days.">
          <RankedBars
            rows={o.busiestSubscriptions.map((s) => ({
              key: s.subscriptionId,
              name: s.name ?? `#${s.subscriptionId}`,
              to: subscriptionPath(s.subscriptionId),
              ok: s.total - s.failed - s.badResponse,
              bad: s.failed + s.badResponse,
            }))}
            empty="Once exchanges flow, the busiest pipelines rank here."
          />
        </Panel>
        <Panel title="Busiest partners" description="Exchanges over the last 24 hours.">
          <RankedBars
            rows={o.busiestPartners.map((p) => ({
              key: p.partnerId,
              name: p.name ?? `#${p.partnerId}`,
              to: `/partners/${p.partnerId}`,
              ok: p.total - p.failed,
              bad: p.failed,
            }))}
            empty="No partner has sent or received anything today."
          />
        </Panel>
        <Panel title="Slowest adapters" description="Time per call, 95th percentile, last 24 hours.">
          {o.slowestAdapters.length === 0 ? (
            <EmptyState title="Nothing to rank yet">Adapters with five or more calls in a day rank here.</EmptyState>
          ) : (
            <ul className="space-y-2 text-sm">
              {o.slowestAdapters.map((a) => (
                <li key={a.adapterId} className="flex items-center gap-2">
                  {canSeeAdapters ? (
                    <Link
                      to={`/adapters?tab=${a.adapterId.startsWith("native") ? "built-in" : "installed"}`}
                      className="min-w-0 flex-1 truncate font-mono text-[12.5px] text-ink-800 hover:text-crimson-700 hover:underline"
                    >
                      {a.adapterId}
                    </Link>
                  ) : (
                    <span className="min-w-0 flex-1 truncate font-mono text-[12.5px] text-ink-800">{a.adapterId}</span>
                  )}
                  <span className="text-xs text-ink-500 tabular-nums">{compact(a.calls)} calls</span>
                  <span className="w-16 text-right font-medium tabular-nums text-ink-900">
                    {a.p95Ms === null ? "-" : formatDurationMs(a.p95Ms)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>

      {o.backlog.length > 1 && (
        <Panel
          title="Broker backlog, last 24 hours"
          description="Messages waiting, being retried and dead-lettered, every five minutes."
          className="mb-4"
        >
          <Lines
            points={o.backlog.map(({ at, ...values }) => ({ at, values }))}
            series={BACKLOG}
            label={(at) => hourLabel.format(new Date(at))}
            tick={(at) => hourTick.format(new Date(at))}
          />
        </Panel>
      )}

      <Panel
        title="Housekeeping"
        description="Configuration that is switched off, unused or out of date - worth a look when tidying up."
      >
        {!insights.data ? (
          <LoadingBlock label="Looking over the configuration…" />
        ) : insights.data.insights.length === 0 ? (
          <EmptyState title="Nothing to tidy">Everything configured is in use.</EmptyState>
        ) : (
          <ul className="divide-y divide-ink-100">
            {insights.data.insights.map((i) => (
              <li key={i.kind} className="flex flex-wrap items-start gap-x-3 gap-y-1 py-2.5">
                {i.severity === "warn" ? (
                  <TriangleAlert className="mt-0.5 size-4 shrink-0 text-warn-700" aria-label="Worth acting on" />
                ) : (
                  <Info className="mt-0.5 size-4 shrink-0 text-ink-500" aria-label="For information" />
                )}
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-medium text-ink-900">
                    {i.title} <span className="font-normal text-ink-500 tabular-nums">· {i.count}</span>
                  </p>
                  <p className="text-[13px] text-ink-500">{i.detail}</p>
                  <p className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-[13px]">
                    {i.items.map((item) => (
                      <Link key={item.href + item.label} to={item.href} className="text-crimson-700 hover:underline">
                        {item.label}
                      </Link>
                    ))}
                    {i.count > i.items.length && (
                      <span className="text-ink-500">and {i.count - i.items.length} more</span>
                    )}
                  </p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  );
}

/** A ranked list with each row's share drawn as a bar: succeeded, then the rest. */
function RankedBars({
  rows,
  empty,
}: {
  rows: { key: number; name: string; to: string; ok: number; bad: number }[];
  empty: string;
}) {
  if (rows.length === 0) return <EmptyState title="No traffic yet">{empty}</EmptyState>;
  const max = Math.max(1, ...rows.map((r) => r.ok + r.bad));
  return (
    <ul className="space-y-2.5">
      {rows.map((r) => (
        <li key={r.key} className="text-sm">
          <div className="flex items-center gap-2">
            <Link
              to={r.to}
              className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
              title={r.name}
            >
              {r.name}
            </Link>
            <span className="text-xs text-ink-500 tabular-nums">
              {compact(r.ok + r.bad)}
              {r.bad > 0 && <span> · {compact(r.bad)} failed</span>}
            </span>
          </div>
          <span className="mt-1 flex h-2 items-stretch gap-0.5" aria-hidden>
            {r.ok > 0 && (
              <span
                className="rounded-l-[4px] bg-ok-600 last:rounded-r-[4px]"
                style={{ width: `${(r.ok / max) * 100}%` }}
              />
            )}
            {r.bad > 0 && (
              <span
                className="rounded-r-[4px] bg-danger-600 first:rounded-l-[4px]"
                style={{ width: `${(r.bad / max) * 100}%` }}
              />
            )}
          </span>
        </li>
      ))}
    </ul>
  );
}
