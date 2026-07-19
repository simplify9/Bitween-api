import type { ReactNode } from "react";
import { Link } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, OctagonAlert } from "lucide-react";
import { api, type QueueSeverity } from "../../api";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Panel } from "../../components/ui/Panel";
import { timeAgo } from "../../lib/dates";

const POLL_MS = 5_000;

function HealthBadge({ health }: { health: QueueSeverity }) {
  if (health === "critical") return <Badge tone="crimson">Critical</Badge>;
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

  return (
    <div>
      <PageHeader
        title="Queue health"
        description="Live message-queue throughput, consumer health and backlogs."
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
          label="Consumers"
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
              <span className="font-medium text-crimson-700">need attention</span>
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
                a.severity === "critical" ? "border-crimson-200 bg-crimson-50" : "border-warn-100 bg-warn-100/40"
              }`}
            >
              {a.severity === "critical" ? (
                <OctagonAlert className="mt-0.5 size-4 shrink-0 text-crimson-700" aria-hidden />
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

      {/* — consumers — */}
      <Panel
        title="Consumers"
        description="One row per consumer; work-group consumers link to their group."
        className="mb-4"
      >
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
                <th className="py-2 pr-3">Consumer</th>
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
              {consumers.map((c) => (
                <tr key={c.name} className="border-b border-ink-50 last:border-0">
                  <td className="py-2 pr-3">
                    {c.workGroupId !== null ? (
                      <Link
                        to={`/work-groups/${c.workGroupId}`}
                        className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                      >
                        {c.name}
                      </Link>
                    ) : (
                      <span className="font-medium text-ink-800">{c.name}</span>
                    )}
                    <span className="block text-xs text-ink-400">{c.messageName}</span>
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
                  <td className={`px-3 py-2 text-right ${c.failedCount > 0 ? "font-medium text-crimson-700" : "text-ink-700"}`}>
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
              ))}
            </tbody>
          </table>
        </div>
      </Panel>

      {/* — backlogs — */}
      <div className="grid gap-4 lg:grid-cols-2">
        <Panel title="Retry backlog" description="Messages queued for another attempt.">
          {retryBacklog.length === 0 ? (
            <EmptyState title="No retries pending">Every consumer is keeping up.</EmptyState>
          ) : (
            <ul className="space-y-2">
              {retryBacklog.map((r) => (
                <li key={r.queueName} className="flex items-center gap-2.5 text-sm">
                  <span className="min-w-0 flex-1">
                    <span className="font-medium text-ink-800">{r.consumerName}</span>
                    <code className="block truncate font-mono text-[11px] text-ink-400">{r.queueName}</code>
                  </span>
                  <span className="text-xs text-ink-500">
                    in {r.incomingRate}/s · ack {r.ackRate}/s
                  </span>
                  <Badge tone={r.severity === "critical" ? "crimson" : "warn"}>{r.retryBacklog} waiting</Badge>
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
                      <span className="font-medium text-ink-800">{d.consumerName}</span>
                      <code className="block truncate font-mono text-[11px] text-ink-400">{d.queueName}</code>
                    </span>
                    {d.lastFailedAt && <span className="text-xs text-ink-400">{timeAgo(d.lastFailedAt)}</span>}
                    <Badge tone="crimson">{d.count} dead</Badge>
                  </div>
                  {d.lastExceptionMessage && (
                    <p className="mt-1 rounded-md bg-crimson-50 px-2.5 py-1.5 font-mono text-[11px] leading-relaxed text-crimson-800">
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
