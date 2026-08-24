import type { ReactNode } from "react";
import { Link } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Panel } from "../../components/ui/Panel";
import { timeAgo } from "../../lib/dates";
import { StatusBadge, XchangeId } from "../exchanges/shared";

const CHART_HEIGHT = 140;

const dayLabel = new Intl.DateTimeFormat("en", { day: "numeric", month: "short" });

function StatTile({
  label,
  value,
  sub,
  to,
  accent,
}: {
  label: string;
  value: ReactNode;
  sub?: ReactNode;
  to: string;
  accent?: "danger" | "warn";
}) {
  return (
    <Link
      to={to}
      className={`rounded-xl border bg-white px-4 py-3 transition-colors hover:bg-ink-50 ${
        accent === "danger" ? "border-danger-200" : accent === "warn" ? "border-warn-100" : "border-ink-200"
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

/**
 * The operational front page: today at a glance, two weeks of traffic, and
 * what needs a human — everything links into the page that can act on it.
 */
export function DashboardPage() {
  const { data, isLoading } = useQuery({
    queryKey: ["dashboard"],
    queryFn: () => api.getDashboard(),
    refetchInterval: 60_000,
    placeholderData: keepPreviousData,
  });

  if (isLoading || !data) return <LoadingBlock label="Putting the picture together…" />;

  const delta = data.today.total - data.yesterdayTotal;
  const maxDay = Math.max(1, ...data.trafficByDay.map((d) => d.success + d.failed));
  const needsAttention =
    data.attention.failingIntegrations.length + data.attention.pausedIntegrations.length;

  return (
    <div>
      <PageHeader
        title="Dashboard"
        description="Traffic and health at a glance — every number links to the page where you can act on it."
      />

      {/* — KPI row — */}
      <div className="mb-4 grid grid-cols-2 gap-2.5 sm:grid-cols-3 xl:grid-cols-5">
        <StatTile
          label="Exchanges today"
          value={data.today.total}
          sub={
            <>
              {delta >= 0 ? "+" : ""}
              {delta} vs yesterday
              {data.today.processing > 0 && <> · {data.today.processing} in flight</>}
            </>
          }
          to="/exchanges"
        />
        <StatTile
          label="Success rate (7 days)"
          value={`${data.successRate7d}%`}
          sub="of finished exchanges"
          to="/exchanges?status=success"
        />
        <StatTile
          label="Failed today"
          value={data.today.failed}
          sub={data.today.failed > 0 ? "open the failures" : "nothing failed yet"}
          to="/exchanges?status=failed"
          accent={data.today.failed > 0 ? "danger" : undefined}
        />
        <StatTile
          label="Pending auto-retries"
          value={data.pendingRetries}
          sub={data.pendingRetries > 0 ? "waiting for their slot" : "queue is clear"}
          to="/scheduled-retries"
          accent={data.pendingRetries > 0 ? "warn" : undefined}
        />
        <StatTile
          label="Queue alerts"
          value={data.queueAlerts}
          sub="live consumer health"
          to="/queue-health"
          accent={data.queueAlerts > 0 ? "warn" : undefined}
        />
      </div>

      {/* — 14-day traffic — */}
      <Panel
        title="Traffic, last 14 days"
        description="Exchanges per day."
        className="mb-4"
        action={
          <span className="flex items-center gap-3 text-xs text-ink-500">
            <span className="flex items-center gap-1.5">
              <span className="size-2.5 rounded-[3px] bg-ok-600" aria-hidden /> Succeeded
            </span>
            <span className="flex items-center gap-1.5">
              <span className="size-2.5 rounded-[3px] bg-danger-600" aria-hidden /> Failed or rejected
            </span>
          </span>
        }
      >
        <div className="relative" style={{ height: CHART_HEIGHT + 24 }}>
          {/* gridlines at max and half */}
          {[1, 0.5].map((f) => (
            <div
              key={f}
              className="absolute right-0 left-8 border-t border-ink-100"
              style={{ top: CHART_HEIGHT * (1 - f) }}
            >
              <span className="absolute -top-2 -left-8 w-6 text-right font-mono text-[10px] text-ink-400 tabular-nums">
                {Math.round(maxDay * f)}
              </span>
            </div>
          ))}
          <div className="absolute inset-x-0 bottom-0 left-8 flex items-end gap-1" style={{ height: CHART_HEIGHT + 24 }}>
            {data.trafficByDay.map((d, i) => {
              const okH = Math.round((d.success / maxDay) * CHART_HEIGHT);
              const badH = Math.round((d.failed / maxDay) * CHART_HEIGHT);
              return (
                <div key={d.date} className="group relative flex h-full flex-1 flex-col items-center justify-end">
                  {/* hover tooltip */}
                  <div className="pointer-events-none absolute bottom-full z-10 mb-1 hidden w-max rounded-md bg-ink-950 px-2 py-1 text-[11px] text-ink-100 group-hover:block">
                    <span className="font-medium">{dayLabel.format(new Date(d.date))}</span> — {d.success} ok
                    {d.failed > 0 && <span> · {d.failed} failed</span>}
                  </div>
                  <div className="flex w-full max-w-6 flex-col items-stretch gap-0.5">
                    {d.failed > 0 && (
                      <div className="rounded-t-[4px] bg-danger-600" style={{ height: Math.max(badH, 3) }} />
                    )}
                    {d.success > 0 && (
                      <div
                        className={`bg-ok-600 ${d.failed === 0 ? "rounded-t-[4px]" : ""}`}
                        style={{ height: Math.max(okH, 3) }}
                      />
                    )}
                    {d.success === 0 && d.failed === 0 && <div className="h-px bg-ink-100" />}
                  </div>
                  <span className="mt-1 h-4 text-[10px] whitespace-nowrap text-ink-400">
                    {i % 2 === 1 ? dayLabel.format(new Date(d.date)) : ""}
                  </span>
                </div>
              );
            })}
          </div>
        </div>
        <details className="mt-2">
          <summary className="cursor-pointer text-xs font-medium text-ink-500 hover:text-ink-700">
            View as table
          </summary>
          <table className="mt-2 text-[13px]">
            <thead>
              <tr className="text-left text-[11px] font-medium tracking-wide text-ink-400 uppercase">
                <th className="py-1 pr-6">Day</th>
                <th className="py-1 pr-6 text-right">Succeeded</th>
                <th className="py-1 text-right">Failed or rejected</th>
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {data.trafficByDay.map((d) => (
                <tr key={d.date}>
                  <td className="py-0.5 pr-6 text-ink-600">{dayLabel.format(new Date(d.date))}</td>
                  <td className="py-0.5 pr-6 text-right text-ink-800">{d.success}</td>
                  <td className="py-0.5 text-right text-ink-800">{d.failed}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </details>
      </Panel>

      <div className="mb-4 grid gap-4 lg:grid-cols-2">
        {/* — latest failures — */}
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
                    {f.integrationName && (
                      <Link
                        to={`/subscriptions/${f.integrationId}`}
                        className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                      >
                        {f.integrationName}
                      </Link>
                    )}
                    <span className="ml-auto text-xs text-ink-400">{timeAgo(f.on)}</span>
                  </div>
                  {f.exception && (
                    <p className="mt-0.5 truncate font-mono text-[11px] text-ink-500" title={f.exception}>
                      {f.exception.split("\n")[0]}
                    </p>
                  )}
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

        {/* — integration health — */}
        <Panel title="Integration health" description="Integrations that aren't running clean.">
          {needsAttention === 0 ? (
            <EmptyState title="All integrations healthy">No failures piling up, nothing paused.</EmptyState>
          ) : (
            <ul className="space-y-2">
              {data.attention.failingIntegrations.map((s) => (
                <li key={`f-${s.id}`} className="flex items-center gap-2.5 text-sm">
                  <Link
                    to={`/subscriptions/${s.id}`}
                    className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                  >
                    {s.name}
                  </Link>
                  <Badge tone="danger">
                    {s.consecutiveFailures} consecutive failure{s.consecutiveFailures === 1 ? "" : "s"}
                  </Badge>
                </li>
              ))}
              {data.attention.pausedIntegrations.map((s) => (
                <li key={`p-${s.id}`} className="flex items-center gap-2.5 text-sm">
                  <Link
                    to={`/subscriptions/${s.id}`}
                    className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                  >
                    {s.name}
                  </Link>
                  <Badge tone="warn">Paused</Badge>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>

      {/* — busiest integrations — */}
      <Panel title="Busiest integrations" description="Traffic share over the last 7 days.">
        {data.busiest.length === 0 ? (
          <EmptyState title="No traffic yet">Once exchanges flow, the busiest pipelines rank here.</EmptyState>
        ) : (
          <ul className="space-y-2.5">
            {data.busiest.map((b) => {
              const max = data.busiest[0].count;
              const okPct = ((b.count - b.failed) / max) * 100;
              const badPct = (b.failed / max) * 100;
              return (
                <li key={b.id} className="flex items-center gap-3 text-sm">
                  <Link
                    to={`/subscriptions/${b.id}`}
                    className="w-44 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                    title={b.name}
                  >
                    {b.name}
                  </Link>
                  <span className="flex h-2.5 flex-1 items-stretch gap-0.5">
                    <span className="rounded-l-[4px] bg-ok-600" style={{ width: `${okPct}%` }} />
                    {b.failed > 0 && <span className="rounded-r-[4px] bg-danger-600" style={{ width: `${badPct}%` }} />}
                  </span>
                  <span className="w-16 text-right text-xs text-ink-500 tabular-nums">
                    {b.count}
                    {b.failed > 0 && <span className="text-danger-700"> · {b.failed} ✕</span>}
                  </span>
                </li>
              );
            })}
          </ul>
        )}
      </Panel>
    </div>
  );
}
