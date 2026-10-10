import { useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { Badge } from "../../components/ui/basics";
import { ExceptionLine } from "../../components/ui/Exception";
import { Panel } from "../../components/ui/Panel";
import { formatDateTime, timeAgo, timeUntil } from "../../lib/dates";

/** Messages waiting this long for the broker are worth a look: the dispatcher sends within seconds. */
const OUTBOX_SLOW_MS = 5 * 60_000;

/**
 * Bitween's own background work: the system jobs, with when each last ran and runs next, and the
 * outbox of messages waiting for the broker. A job that stopped firing, or an outbox backing up,
 * used to show only as what it caused.
 */
export function BackgroundWorkPanel() {
  const work = useQuery({ queryKey: keys.background, queryFn: () => api.getBackgroundWork(), refetchInterval: 30_000 });
  if (!work.data) return null;
  const { jobs, outbox } = work.data;
  const slow = outbox.oldestPendingOn && Date.now() - new Date(outbox.oldestPendingOn).getTime() > OUTBOX_SLOW_MS;

  return (
    <Panel title="Bitween's own work" description="System jobs that keep Bitween tidy and retrying, and the outbox it sends to the broker from.">
      <ul className="divide-y divide-ink-100">
        {jobs.map((j) => (
          <li key={j.name} className="flex flex-wrap items-start justify-between gap-2 py-2">
            <span className="min-w-0">
              <span className="font-medium text-ink-900">{j.name}</span>
              <span className="block text-[12px] text-ink-500">{j.does}</span>
            </span>
            <span className="text-right text-[12.5px]">
              {j.runningNow ? (
                <Badge tone="ink">Running now</Badge>
              ) : j.state === "Normal" ? (
                <Badge tone="ok">Scheduled</Badge>
              ) : (
                <Badge tone="danger">{j.state === "NotScheduled" ? "Not scheduled" : j.state}</Badge>
              )}
              <span className="block text-ink-600">
                {j.lastRanOn ? <span title={formatDateTime(j.lastRanOn)}>Last ran {timeAgo(j.lastRanOn)}</span> : "Hasn't run yet"}
                {j.nextRunOn && <span title={formatDateTime(j.nextRunOn)}> · next {timeUntil(j.nextRunOn)}</span>}
              </span>
            </span>
          </li>
        ))}
      </ul>
      <div className="mt-3 border-t border-ink-100 pt-3">
        <h4 className="text-[13px] font-semibold text-ink-900">Outbox</h4>
        <p className="mt-0.5 text-[12.5px] text-ink-600">
          {outbox.pending === 0 ? (
            "Nothing waiting for the broker."
          ) : (
            <>
              <Badge tone={slow || outbox.failing > 0 ? "danger" : "neutral"}>{outbox.pending} waiting</Badge>{" "}
              {outbox.oldestPendingOn && <>the oldest since {timeAgo(outbox.oldestPendingOn)}</>}
              {outbox.failing > 0 && <>, {outbox.failing} after a failed attempt</>}
            </>
          )}
          <span className="block text-ink-500">{outbox.publishedLastHour} sent in the last hour.</span>
        </p>
        {outbox.lastError && (
          <p className="mt-1 text-[12px] text-danger-700">
            <ExceptionLine text={outbox.lastError} />
          </p>
        )}
      </div>
    </Panel>
  );
}
