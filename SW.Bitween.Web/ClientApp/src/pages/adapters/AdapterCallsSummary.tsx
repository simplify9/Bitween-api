import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { LoadingBlock } from "../../components/ui/basics";
import { Sparkline } from "../../components/charts/Sparkline";
import { formatDurationMs, timeAgo } from "../../lib/dates";
import { bytes, compact, percent } from "../../lib/numbers";
import { subscriptionPath } from "../../lib/subscriptionPaths";
import { CALL_DAYS, useAdapterCalls } from "./adapterCalls";
import type { AdapterUsageTotals } from "../../api/metricsTypes";

/** One line under an adapter in the list: its trend, calls, failures and time, or when it last ran. */
export function AdapterCallsLine({ adapterId }: { adapterId: string }) {
  const { calls, lastCalled, loaded } = useAdapterCalls(adapterId);
  if (!loaded) return null;
  if (!calls || calls.calls === 0)
    return (
      <span className="block text-ink-500">
        {lastCalled ? `Last called ${timeAgo(lastCalled)}` : `No calls in ${CALL_DAYS} days`}
      </span>
    );
  return (
    <span className="mt-0.5 flex items-center justify-end gap-2">
      <Sparkline values={calls.daily.map((d) => d.calls)} label={`Calls per day, last ${CALL_DAYS} days`} />
      <span className="tabular-nums">
        {compact(calls.calls)} calls
        {calls.failures > 0 && <span className="text-danger-700"> · {percent(calls.failureRate)} failed</span>}
        {calls.p95Ms !== null && <span title="95 of every 100 calls finished within this time"> · p95 time {formatDurationMs(calls.p95Ms)}</span>}
      </span>
    </span>
  );
}

function Totals({ t }: { t: AdapterUsageTotals }) {
  return (
    <>
      <td className="py-1.5 pr-4 text-right tabular-nums">{compact(t.calls)}</td>
      <td className={`py-1.5 pr-4 text-right tabular-nums ${t.failures > 0 ? "text-danger-700" : ""}`}>
        {t.failures > 0 ? `${compact(t.failures)} (${percent(t.failureRate)})` : "0"}
      </td>
      <td className="py-1.5 pr-4 text-right tabular-nums">{t.p50Ms === null ? "-" : formatDurationMs(t.p50Ms)}</td>
      <td className="py-1.5 text-right tabular-nums">{t.p95Ms === null ? "-" : formatDurationMs(t.p95Ms)}</td>
    </>
  );
}

const HEAD = "py-1 pr-4 text-right font-medium";

/**
 * An adapter's use in detail, fetched when it is opened: the whole of it, by version, by role and
 * runtime, by node; how its failures failed; its most common errors; and the subscriptions it
 * serves with what they moved.
 */
export function AdapterCallsDetail({ adapterId }: { adapterId: string }) {
  const detail = useQuery({
    queryKey: keys.metrics.adapter(adapterId.toLowerCase(), CALL_DAYS),
    queryFn: () => api.getAdapterUsageDetail(adapterId, CALL_DAYS),
  });
  if (detail.isPending) return <LoadingBlock label="Reading its use…" />;
  if (detail.isError) return <p className="text-[13px] text-ink-500">Its use couldn't be read just now.</p>;
  const d = detail.data;
  const t = d.total;

  return (
    <div className="space-y-4">
      <div>
        <h3 className="mb-1 text-[12px] font-semibold tracking-wide text-ink-500 uppercase">
          Use, last {CALL_DAYS} days
        </h3>
        {t.calls === 0 ? (
          <p className="text-[13px] text-ink-500">
            No calls{d.since ? ` since counting began, ${timeAgo(d.since)}` : " counted yet"}.
          </p>
        ) : (
          <p className="text-[13px] text-ink-700">
            {compact(t.calls)} calls, {percent(t.failureRate)} failed
            {t.timeouts > 0 && `, ${compact(t.timeouts)} timed out`}
            {t.limitStops > 0 && `, ${compact(t.limitStops)} stopped for going over a limit`}
            {t.crashes > 0 && `, ${compact(t.crashes)} when its process died`}. Half took under{" "}
            {t.p50Ms === null ? "-" : formatDurationMs(t.p50Ms)}, 95% under{" "}
            {t.p95Ms === null ? "-" : formatDurationMs(t.p95Ms)}, the longest {formatDurationMs(t.maxMs)}.
          </p>
        )}
      </div>

      {t.calls > 0 && (
        <div className="grid gap-4 xl:grid-cols-2">
          <table className="w-full text-left text-[12.5px]" aria-label="By version">
            <thead className="text-ink-500">
              <tr>
                <th className="py-1 pr-4 font-medium">Version</th>
                <th className={HEAD}>Calls</th>
                <th className={HEAD}>Failed</th>
                <th className={HEAD} title="Half of the calls finished within this time">p50 time</th>
                <th className="py-1 text-right font-medium" title="95 of every 100 calls finished within this time">p95 time</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-100 text-ink-800">
              {d.versions.map((v) => (
                <tr key={v.version}>
                  <td className="py-1.5 pr-4 font-mono">{v.version ? `v${v.version}` : "built in"}</td>
                  <Totals t={v.totals} />
                </tr>
              ))}
            </tbody>
          </table>
          <table className="w-full text-left text-[12.5px]" aria-label="By node">
            <thead className="text-ink-500">
              <tr>
                <th className="py-1 pr-4 font-medium">Node, as</th>
                <th className={HEAD}>Calls</th>
                <th className={HEAD}>Failed</th>
                <th className={HEAD} title="Half of the calls finished within this time">p50 time</th>
                <th className="py-1 text-right font-medium" title="95 of every 100 calls finished within this time">p95 time</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-100 text-ink-800">
              {d.nodes.map((n) => (
                <tr key={n.node}>
                  <td className="max-w-48 truncate py-1.5 pr-4 font-mono" title={n.node}>
                    {n.node}
                  </td>
                  <Totals t={n.totals} />
                </tr>
              ))}
              {d.roles.map((r) => (
                <tr key={`${r.role}-${r.runtime}`} className="text-ink-600">
                  <td className="py-1.5 pr-4">
                    {r.role} on{" "}
                    {r.runtime === "process"
                      ? "a process per call"
                      : r.runtime === "resident"
                        ? "a resident process"
                        : "Bitween itself"}
                  </td>
                  <Totals t={r.totals} />
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {d.errors.length > 0 && (
        <div>
          <h3 className="mb-1 text-[12px] font-semibold tracking-wide text-ink-500 uppercase">Most common errors</h3>
          <ul className="space-y-1.5">
            {d.errors.map((e) => (
              <li key={e.fingerprint} className="flex items-start gap-3 text-[12.5px]">
                <span className="w-14 shrink-0 text-right font-medium text-danger-700 tabular-nums">
                  {compact(e.count)}×
                </span>
                <span className="min-w-0 flex-1 font-mono break-words text-ink-700">{e.sample}</span>
                <span className="shrink-0 text-ink-500">{timeAgo(e.lastSeenOn)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {d.subscriptions.length > 0 && (
        <div>
          <h3 className="mb-1 text-[12px] font-semibold tracking-wide text-ink-500 uppercase">What it served</h3>
          <table className="w-full max-w-3xl text-left text-[12.5px]">
            <thead className="text-ink-500">
              <tr>
                <th className="py-1 pr-4 font-medium">Subscription</th>
                <th className={HEAD}>Exchanges</th>
                <th className={HEAD}>Failed</th>
                <th className="py-1 text-right font-medium">Moved</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-100 text-ink-800">
              {d.subscriptions.map((s) => (
                <tr key={s.subscriptionId}>
                  <td className="py-1.5 pr-4">
                    <Link
                      to={subscriptionPath(s.subscriptionId)}
                      className="font-medium link"
                    >
                      {s.name}
                    </Link>
                  </td>
                  <td className="py-1.5 pr-4 text-right tabular-nums">{compact(s.exchanges)}</td>
                  <td className={`py-1.5 pr-4 text-right tabular-nums ${s.failed > 0 ? "text-danger-700" : ""}`}>
                    {compact(s.failed)}
                  </td>
                  <td className="py-1.5 text-right tabular-nums">{bytes(s.bytes)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
