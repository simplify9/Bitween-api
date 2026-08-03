import type { ReactNode } from "react";
import { Link } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, OctagonAlert } from "lucide-react";
import { api, type ConsumerHealth, type QueueLane, type QueueSeverity } from "../../api";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Panel } from "../../components/ui/Panel";
import { timeAgo } from "../../lib/dates";

const POLL_MS = 5_000;

/**
 * Sections, in the order a message travels: it arrives at a front door, is run in
 * a work group's lane, and its result is checked against the notifiers. Control and
 * legacy lanes carry no ordinary traffic, so they sit at the bottom.
 */
const LANE_ORDER: QueueLane[] = ["front-door", "worker", "notifications", "legacy", "control"];

const LANES: Record<QueueLane, { label: string; blurb: string }> = {
  "front-door": {
    label: "Front doors",
    blurb: "One per information type that listens on the bus. Each message that arrives becomes an exchange.",
  },
  worker: {
    label: "Work",
    blurb: "One per work group. This is where integrations actually run — filter, mapping, delivery.",
  },
  notifications: {
    label: "Notifications",
    blurb: "One per work group. Checks every notifier against the results that group produced.",
  },
  legacy: {
    label: "Legacy",
    blurb: "Old-style event messages, consumed only while that setting is on.",
  },
  control: {
    label: "Control",
    blurb: "Bookkeeping. No integration traffic passes through these.",
  },
};

/** Work groups drill to their group; front doors to the information type they listen for. */
const linkFor = (c: ConsumerHealth): string | null => {
  if (c.workGroupId !== null) return `/work-groups/${c.workGroupId}`;
  if (c.informationTypeId !== null) return `/information-types/${c.informationTypeId}`;
  return null;
};

function HealthBadge({ health }: { health: QueueSeverity }) {
  if (health === "critical") return <Badge tone="danger">Critical</Badge>;
  if (health === "warning") return <Badge tone="warn">Warning</Badge>;
  return <Badge tone="ok">Healthy</Badge>;
}

function StatTile({ label, value, sub }: { label: string; value: ReactNode; sub?: ReactNode }) {
  return (
    <div className="rounded-xl border border-ink-200 bg-white px-4 py-3">
      <p className="text-[13px] text-ink-500">{label}</p>
      <p className="mt-0.5 text-2xl font-semibold text-ink-900">{value}</p>
      {sub && <p className="mt-0.5 text-xs text-ink-500">{sub}</p>}
    </div>
  );
}

/**
 * Live RabbitMQ picture: summary, active alerts, per-consumer health, and the
 * retry / dead-letter backlogs. Polls every few seconds; the previous snapshot
 * stays on screen while the next one loads, so nothing flashes.
 */
export function QueueHealthPage() {
  const { data, isLoading, dataUpdatedAt } = useQuery({
    queryKey: ["queue-health"],
    queryFn: () => api.getQueueHealth(),
    refetchInterval: POLL_MS,
    placeholderData: keepPreviousData,
  });

  if (isLoading || !data) return <LoadingBlock label="Reading queue statistics…" />;

  const { summary, consumers, retryBacklog, deadLetters, alerts } = data;

  // Same order inside every lane, so Work and Notifications line up row for row and
  // you can read one group's pair across the two sections. Anything that resolved to
  // nothing sinks to the bottom of its lane — Ungrouped, and orphaned queues.
  const sortsLast = (c: ConsumerHealth) => (c.workGroupId === null && c.informationTypeId === null ? 1 : 0);
  const byLane = new Map<QueueLane, ConsumerHealth[]>(
    LANE_ORDER.map((lane) => [
      lane,
      consumers
        .filter((c) => c.lane === lane)
        .sort((a, b) => sortsLast(a) - sortsLast(b) || a.title.localeCompare(b.title)),
    ]),
  );

  return (
    <div>
      <PageHeader
        title="Queue health"
        description="Live throughput and backlog for every queue this instance consumes."
        actions={
          <span className="flex items-center gap-1.5 text-[13px] text-ink-500">
            <span className="inline-block size-1.5 animate-pulse rounded-full bg-ok-600" aria-hidden />
            Live · updated {timeAgo(new Date(dataUpdatedAt).toISOString())}
          </span>
        }
      />

      {/* — summary — */}
      <div className="mb-4 grid grid-cols-2 gap-2.5 sm:grid-cols-3 xl:grid-cols-6">
        <StatTile
          label="Lanes"
          value={summary.totalConsumers}
          sub={
            summary.unhealthyConsumers > 0 ? (
              <span className="font-medium text-warn-700">{summary.unhealthyConsumers} unhealthy</span>
            ) : (
              "all healthy"
            )
          }
        />
        <StatTile label="Queue depth" value={summary.totalQueueDepth} sub="messages waiting" />
        <StatTile
          label="Retry backlog"
          value={summary.totalRetryBacklog}
          sub={summary.totalRetryBacklog > 0 ? <span className="font-medium text-warn-700">waiting to retry</span> : "empty"}
        />
        <StatTile
          label="Dead letters"
          value={summary.totalDeadLetterBacklog}
          sub={
            summary.totalDeadLetterBacklog > 0 ? (
              <span className="font-medium text-danger-700">need attention</span>
            ) : (
              "empty"
            )
          }
        />
        <StatTile label="Incoming" value={`${summary.totalIncomingRate}/s`} sub="across all queues" />
        <StatTile label="Acknowledged" value={`${summary.totalAckRate}/s`} sub="across all queues" />
      </div>

      {/* — active alerts — */}
      {alerts.length > 0 && (
        <div className="mb-4 space-y-1.5">
          {alerts.map((a) => (
            <div
              key={a.queueName + a.title}
              className={`flex items-start gap-2.5 rounded-xl border px-3.5 py-2.5 ${
                a.severity === "critical" ? "border-danger-200 bg-danger-50" : "border-warn-100 bg-warn-100/40"
              }`}
            >
              {a.severity === "critical" ? (
                <OctagonAlert className="mt-0.5 size-4 shrink-0 text-danger-700" aria-hidden />
              ) : (
                <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warn-700" aria-hidden />
              )}
              <div className="min-w-0">
                <p className="text-sm font-medium text-ink-900">{a.title}</p>
                <p className="text-[13px] text-ink-600">{a.detail}</p>
                <code className="font-mono text-[11px] text-ink-400">{a.queueName}</code>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* — lanes — */}
      <Panel
        title="Lanes"
        description="Every queue this instance consumes, grouped by what it is for."
        className="mb-4"
      >
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
                <th className="py-2 pr-3">Lane</th>
                <th className="px-3 py-2">Queue</th>
                <th className="px-3 py-2 text-right">Nodes</th>
                <th className="px-3 py-2 text-right">In flight</th>
                <th className="px-3 py-2 text-right">Queued</th>
                <th className="px-3 py-2 text-right">Retrying</th>
                <th className="px-3 py-2 text-right">Dead</th>
                <th className="px-3 py-2 text-right">Prefetch</th>
                <th className="px-3 py-2 text-right">In/s</th>
                <th className="px-3 py-2 text-right">Ack/s</th>
                <th className="px-3 py-2">Health</th>
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {LANE_ORDER.filter((lane) => byLane.get(lane)?.length).flatMap((lane) => [
                <tr key={`h-${lane}`} className="border-b border-ink-100 bg-ink-50/60">
                  <td colSpan={11} className="px-3 py-1.5">
                    <span className="text-[11px] font-semibold tracking-wide text-ink-500 uppercase">
                      {LANES[lane].label}
                    </span>
                    <span className="ml-2 text-[12px] text-ink-500">{LANES[lane].blurb}</span>
                  </td>
                </tr>,
                ...byLane.get(lane)!.map((c) => (
                  // Keyed on the queue name: the consumer name is the C# class, which
                  // repeats across every work-group lane.
                  <tr key={c.queueName} className="border-b border-ink-50 last:border-0">
                    <td className="py-2 pr-3">
                      {linkFor(c) !== null ? (
                        <Link
                          to={linkFor(c)!}
                          className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                        >
                          {c.title}
                        </Link>
                      ) : (
                        <span className="font-medium text-ink-800">{c.title}</span>
                      )}
                      {c.orphaned && (
                        <Badge tone="warn" className="ml-1.5">
                          Orphaned
                        </Badge>
                      )}
                      <span className="block text-xs text-ink-400">
                        {c.orphaned ? "nothing by this name exists any more — the queue outlived it" : c.role}
                      </span>
                    </td>
                    <td className="px-3 py-2">
                      <code className="font-mono text-xs text-ink-500">{c.queueName}</code>
                    </td>
                    <td className="px-3 py-2 text-right text-ink-700">{c.totalNodes}</td>
                    <td className="px-3 py-2 text-right text-ink-700">{c.processingCount}</td>
                    <td className="px-3 py-2 text-right text-ink-700">{c.queueCount}</td>
                    <td className={`px-3 py-2 text-right ${c.retryCount > 0 ? "font-medium text-warn-700" : "text-ink-700"}`}>
                      {c.retryCount}
                    </td>
                    <td className={`px-3 py-2 text-right ${c.failedCount > 0 ? "font-medium text-danger-700" : "text-ink-700"}`}>
                      {c.failedCount}
                    </td>
                    <td className="px-3 py-2 text-right text-ink-500">{c.prefetch}</td>
                    <td className="px-3 py-2 text-right text-ink-700">{c.incomingRate}</td>
                    <td className="px-3 py-2 text-right text-ink-700">{c.ackRate}</td>
                    <td className="px-3 py-2">
                      <span className="inline-flex items-center gap-1">
                        <HealthBadge health={c.health} />
                        {c.isBackpressured && <Badge tone="warn">Backpressure</Badge>}
                      </span>
                    </td>
                  </tr>
                )),
              ])}
            </tbody>
          </table>
        </div>
      </Panel>

      {/* — backlogs — */}
      <div className="grid gap-4 lg:grid-cols-2">
        <Panel title="Retry backlog" description="Messages queued for another attempt.">
          {retryBacklog.length === 0 ? (
            <EmptyState title="No retries pending">Every lane is keeping up.</EmptyState>
          ) : (
            <ul className="space-y-2">
              {retryBacklog.map((r) => (
                <li key={r.queueName} className="flex items-center gap-2.5 text-sm">
                  <span className="min-w-0 flex-1">
                    <span className="font-medium text-ink-800">{r.title}</span>
                    <code className="block truncate font-mono text-[11px] text-ink-400">{r.queueName}</code>
                  </span>
                  <span className="text-xs text-ink-500">
                    in {r.incomingRate}/s · ack {r.ackRate}/s
                  </span>
                  <Badge tone={r.severity === "critical" ? "danger" : "warn"}>{r.retryBacklog} waiting</Badge>
                </li>
              ))}
            </ul>
          )}
        </Panel>

        <Panel title="Dead letters" description="Messages that exhausted their retries.">
          {deadLetters.length === 0 ? (
            <EmptyState title="No dead letters">Nothing has been abandoned.</EmptyState>
          ) : (
            <ul className="space-y-3">
              {deadLetters.map((d) => (
                <li key={d.queueName} className="text-sm">
                  <div className="flex items-center gap-2.5">
                    <span className="min-w-0 flex-1">
                      <span className="font-medium text-ink-800">{d.title}</span>
                      <code className="block truncate font-mono text-[11px] text-ink-400">{d.queueName}</code>
                    </span>
                    {d.lastFailedAt && <span className="text-xs text-ink-400">{timeAgo(d.lastFailedAt)}</span>}
                    <Badge tone="danger">{d.count} dead</Badge>
                  </div>
                  {d.lastExceptionMessage && (
                    <p className="mt-1 rounded-md bg-danger-50 px-2.5 py-1.5 font-mono text-[11px] leading-relaxed text-danger-800">
                      {d.lastExceptionType && <span className="font-semibold">{d.lastExceptionType}: </span>}
                      {d.lastExceptionMessage}
                    </p>
                  )}
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>
    </div>
  );
}
