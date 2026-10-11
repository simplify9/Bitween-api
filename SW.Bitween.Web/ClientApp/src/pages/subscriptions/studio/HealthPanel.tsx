import { useQuery } from "@tanstack/react-query";
import { api } from "../../../api";
import { keys } from "../../../api/queryKeys";
import { TimeBars, type BarSeries } from "../../../components/charts/TimeBars";
import { HealthStatusBadge } from "../../../components/metrics/HealthStatus";
import { Panel } from "../../../components/ui/Panel";
import { formatDurationMs, timeAgo } from "../../../lib/dates";
import { bytes, compact, percent } from "../../../lib/numbers";

const DAYS = 7;
const hourLabel = new Intl.DateTimeFormat("en", { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
const hourTick = new Intl.DateTimeFormat("en", { hour: "2-digit", minute: "2-digit" });

const OUTCOMES: BarSeries[] = [
  { key: "succeeded", label: "Succeeded", className: "bg-ok-600" },
  { key: "badResponse", label: "Bad response", className: "bg-warn-400" },
  { key: "failed", label: "Failed", className: "bg-danger-600" },
];

function Figure({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div>
      <p className="text-[12px] text-ink-500">{label}</p>
      <p className="text-lg font-semibold text-ink-900">{value}</p>
      {sub && <p className="text-[12px] text-ink-500">{sub}</p>}
    </div>
  );
}

/**
 * How this subscription has been doing: its status, a week's numbers, two days hour by hour, where
 * failures came from, and the errors it hits most. From the hourly metrics; nothing counted here.
 */
export function HealthPanel({ subscriptionId }: { subscriptionId: number }) {
  const metrics = useQuery({
    queryKey: keys.metrics.subscription(subscriptionId, DAYS),
    queryFn: () => api.getSubscriptionMetrics(subscriptionId, DAYS),
    refetchInterval: 60_000,
  });
  const m = metrics.data;
  if (!m) return null;
  const t = m.total;

  const end = new Date();
  end.setUTCMinutes(0, 0, 0);
  const byHour = new Map(m.hourly.map((h) => [Date.parse(h.hour), h]));
  const hours = Array.from({ length: 48 }, (_, i) => {
    const at = end.getTime() - (47 - i) * 3_600_000;
    const h = byHour.get(at);
    return {
      at: new Date(at).toISOString(),
      values: {
        succeeded: (h?.total ?? 0) - (h?.failed ?? 0) - (h?.badResponse ?? 0),
        badResponse: h?.badResponse ?? 0,
        failed: h?.failed ?? 0,
      },
    };
  });

  return (
    <Panel
      title="Health, last 7 days"
      description={
        m.since && Date.now() - Date.parse(m.since) < DAYS * 86_400_000
          ? `Counted since ${timeAgo(m.since)}.`
          : "Every exchange that went through it."
      }
      action={m.status ? <HealthStatusBadge status={m.status} /> : undefined}
    >
      {m.status === "silent" && (
        <p className="mb-3 rounded-lg bg-warn-100 px-3 py-2 text-[13px] text-warn-700">
          Nothing has come through for {formatDurationMs((m.quietHours ?? 0) * 3_600_000)}; it usually has traffic about
          every {formatDurationMs((m.usualGapHours ?? 0) * 3_600_000)}. Nothing fails when a partner stops sending, so
          this is the only sign of it.
        </p>
      )}
      <div className="mb-4 grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Figure
          label="Exchanges"
          value={compact(t.total)}
          sub={m.lastExchangeHour ? `latest ${timeAgo(m.lastExchangeHour)}` : undefined}
        />
        <Figure
          label="Succeeded"
          value={percent(t.successRate)}
          sub={t.badResponse > 0 ? `${compact(t.badResponse)} bad responses` : undefined}
        />
        <Figure
          label="End-to-end, 95%"
          value={t.p95Ms === null ? "-" : formatDurationMs(t.p95Ms)}
          sub={t.p50Ms === null ? undefined : `half within ${formatDurationMs(t.p50Ms)}`}
        />
        <Figure
          label="Moved"
          value={bytes(t.bytes)}
          sub={t.retries > 0 ? `${compact(t.retries)} were retries` : undefined}
        />
      </div>
      {t.failed > 0 && (
        <p className="mb-3 text-[13px] text-ink-600">
          {compact(t.failed)} failed: {compact(t.failedAtMapper)} in the mapping, {compact(t.failedAtHandler)} in the
          delivery
          {t.failed - t.failedAtMapper - t.failedAtHandler > 0 &&
            `, ${compact(t.failed - t.failedAtMapper - t.failedAtHandler)} before either`}
          .
        </p>
      )}
      <TimeBars
        points={hours}
        series={OUTCOMES}
        height={110}
        label={(at) => hourLabel.format(new Date(at))}
        tick={(at) => hourTick.format(new Date(at))}
        every={8}
      />
      {m.errors.length > 0 && (
        <div className="mt-4">
          <h3 className="mb-1 text-[12px] font-semibold tracking-wide text-ink-500 uppercase">Most common errors</h3>
          <ul className="space-y-1.5">
            {m.errors.map((e) => (
              <li key={e.fingerprint} className="flex items-start gap-3 text-[12.5px]">
                <span className="w-12 shrink-0 text-right font-medium text-danger-700 tabular-nums">
                  {compact(e.count)}×
                </span>
                <span className="min-w-0 flex-1 font-mono break-words text-ink-700">{e.sample}</span>
                <span className="shrink-0 text-ink-500">{timeAgo(e.lastSeenOn)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
      {m.nodes.length > 1 && (
        <p className="mt-3 text-[12px] text-ink-500">
          Ran on{" "}
          {m.nodes.map((n, i) => (
            <span key={n.node}>
              {i > 0 && ", "}
              <span className="font-mono">{n.node}</span> ({compact(n.total)}
              {n.failed > 0 && `, ${compact(n.failed)} failed`})
            </span>
          ))}
          .
        </p>
      )}
    </Panel>
  );
}
