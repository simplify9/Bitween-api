import { useState, type ReactNode } from "react";
import { Link } from "react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Eye, OctagonAlert, RotateCcw, Trash2, Unplug } from "lucide-react";
import {
  api,
  type ConsumerHealth,
  type DeadLetterRow,
  type QueueLane,
  type QueueSeverity,
  type UnattendedDeleteResult,
  type UnattendedQueue,
} from "../../api";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { queueHealthTitle } from "../../components/config/shared";
import { Panel } from "../../components/ui/Panel";
import { ConfirmDialog, Dialog } from "../../components/ui/overlays";
import { useSessionCan } from "../../auth/guards";
import { timeAgo } from "../../lib/dates";
import { keys } from "../../api/queryKeys";
import { useRabbitMqManagementConfigured } from "../../lib/appConfig";
import { ExceptionBlock } from "../../components/ui/Exception";
import { BackgroundWorkPanel } from "./BackgroundWork";

const POLL_MS = 5_000;

/**
 * Sections, in the order a message travels: it arrives at a front door, is run in
 * a work group's lane, and its result is checked against the notifiers. Control and
 * legacy lanes carry no ordinary traffic, so they sit at the bottom.
 */
const LANE_ORDER: QueueLane[] = ["FrontDoor", "Work", "Notifications", "Legacy", "Control"];

/** The wording is this app's; the lane a row is in comes from `Ops/LaneResolver`. */
const LANES: Record<QueueLane, { label: string; blurb: string; role: string }> = {
  FrontDoor: {
    label: "Front doors",
    blurb: "One per information type that listens on the bus. Each message that arrives becomes an exchange.",
    role: "turns arriving messages into exchanges",
  },
  Work: {
    label: "Work",
    blurb: "One per work group. This is where subscriptions actually run — filter, mapping, delivery.",
    role: "runs the subscriptions in this group",
  },
  Notifications: {
    label: "Notifications",
    blurb: "One per work group. Checks every notifier against the results that group produced.",
    role: "checks every notifier against this group's results",
  },
  Legacy: {
    label: "Legacy",
    blurb: "Old-style event messages, consumed only while that setting is on.",
    role: "kept for compatibility",
  },
  Control: {
    label: "Control",
    blurb: "Bookkeeping. No subscription traffic passes through these.",
    role: "internal bookkeeping",
  },
};

/**
 * Ungrouped is a lane, not a group — the one every subscription without a work group
 * shares — so it doesn't get the group wording.
 */
const roleOf = (c: ConsumerHealth): string => {
  if (c.title === "Ungrouped") {
    return c.lane === "Notifications"
      ? "checks every notifier against results that belong to no work group"
      : "runs every subscription that has no work group";
  }
  return LANES[c.lane].role;
};

/** Work groups drill to their group; front doors to the information type they listen for. */
const linkFor = (c: ConsumerHealth): string | null => {
  if (c.workGroupId !== null) return `/work-groups/${c.workGroupId}`;
  if (c.informationTypeId !== null) return `/information-types/${c.informationTypeId}`;
  return null;
};

function HealthBadge({ health }: { health: QueueSeverity }) {
  if (health === "critical") return <Badge tone="danger" title={queueHealthTitle("critical")}>Critical</Badge>;
  if (health === "warning") return <Badge tone="warn" title={queueHealthTitle("warning")}>Warning</Badge>;
  return <Badge tone="ok" title={queueHealthTitle("healthy")}>Healthy</Badge>;
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
  const rabbitMqConfigured = useRabbitMqManagementConfigured();
  const { data, isLoading, isError, dataUpdatedAt } = useQuery({
    queryKey: keys.queueHealth,
    queryFn: () => api.getQueueHealth(),
    refetchInterval: POLL_MS,
    placeholderData: keepPreviousData,
    enabled: rabbitMqConfigured,
  });

  if (!rabbitMqConfigured)
    return (
      <div>
        <PageHeader
          title="Queue health"
          description="Live throughput and backlog for every queue this instance consumes."
        />
        <EmptyState icon={<Unplug />} title="RabbitMQ management isn't configured">
          This page reads live queue stats from the RabbitMQ management API, which the backend
          isn't configured to reach. Ask an admin to set the management URL, username and
          password, then reload.
        </EmptyState>
      </div>
    );

  if (isError && !data)
    return (
      <EmptyState title="Couldn't load queue statistics">
        Something went wrong reaching the RabbitMQ management API. It'll keep retrying in the
        background — try refreshing the page.
      </EmptyState>
    );

  if (isLoading || !data) return <LoadingBlock label="Reading queue statistics…" />;

  const { summary, consumers, retryBacklog, deadLetters, unattended, alerts } = data;

  // Same order inside every lane, so Work and Notifications line up row for row and
  // you can read one group's pair across the two sections. Anything that resolved to
  // nothing sinks to the bottom of its lane — Ungrouped, and any unresolved name.
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
                <code className="font-mono text-[11px] text-ink-500">{a.queueName}</code>
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
              <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-500 uppercase">
                <th className="py-2 pr-2">Lane</th>
                <th className="px-2 py-2">Queue</th>
                <th className="px-2 py-2 text-right">Nodes</th>
                <th className="px-2 py-2 text-right">In flight</th>
                <th className="px-2 py-2 text-right">Queued</th>
                <th className="px-2 py-2 text-right">Retrying</th>
                <th className="px-2 py-2 text-right">Dead</th>
                <th className="px-2 py-2 text-right">Prefetch</th>
                <th className="px-2 py-2 text-right">In/s</th>
                <th className="px-2 py-2 text-right">Ack/s</th>
                <th className="px-2 py-2">Health</th>
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {LANE_ORDER.filter((lane) => byLane.get(lane)?.length).flatMap((lane) => [
                <tr key={`h-${lane}`} className="border-b border-ink-100 bg-ink-50/60">
                  <td colSpan={11} className="px-2 py-1.5">
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
                    <td className="py-2 pr-2">
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
                      <span className="block text-xs text-ink-500">{roleOf(c)}</span>
                    </td>
                    <td className="px-2 py-2">
                      <code className="font-mono text-xs wrap-anywhere text-ink-500">{c.queueName}</code>
                    </td>
                    <td className="px-2 py-2 text-right text-ink-700">{c.totalNodes}</td>
                    <td className="px-2 py-2 text-right text-ink-700">{c.processingCount}</td>
                    <td className="px-2 py-2 text-right text-ink-700">{c.queueCount}</td>
                    <td className={`px-2 py-2 text-right ${c.retryCount > 0 ? "font-medium text-warn-700" : "text-ink-700"}`}>
                      {c.retryCount}
                    </td>
                    <td className={`px-2 py-2 text-right ${c.failedCount > 0 ? "font-medium text-danger-700" : "text-ink-700"}`}>
                      {c.failedCount}
                    </td>
                    <td className="px-2 py-2 text-right text-ink-500">{c.prefetch}</td>
                    <td className="px-2 py-2 text-right text-ink-700">{c.incomingRate}</td>
                    <td className="px-2 py-2 text-right text-ink-700">{c.ackRate}</td>
                    <td className="px-2 py-2">
                      <span className="inline-flex items-center gap-1">
                        <HealthBadge health={c.health} />
                        {c.isBackpressured && (
                          <Badge
                            tone="warn"
                            title="Queue depth has passed its configured threshold — messages are arriving faster than they're being consumed."
                          >
                            Backpressure
                          </Badge>
                        )}
                      </span>
                    </td>
                  </tr>
                )),
              ])}
            </tbody>
          </table>
        </div>
      </Panel>

      {/* — queues nothing declares — */}
      {unattended.length > 0 && <UnattendedPanel unattended={unattended} />}

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
                    <code className="block truncate font-mono text-[11px] text-ink-500">{r.queueName}</code>
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
                <DeadLetterItem key={d.queueName} row={d} />
              ))}
            </ul>
          )}
        </Panel>
      </div>

      <div className="mt-5">
        <BackgroundWorkPanel />
      </div>
    </div>
  );
}

type MessageCounts = Pick<UnattendedQueue, "messages" | "retryMessages" | "deadMessages">;

/** "3 queued, 1 retrying and 57 dead", leaving out the zeros. */
const messageBreakdown = (q: MessageCounts): string => {
  const parts = [
    q.messages > 0 && `${q.messages} queued`,
    q.retryMessages > 0 && `${q.retryMessages} retrying`,
    q.deadMessages > 0 && `${q.deadMessages} dead`,
  ].filter((p): p is string => typeof p === "string");
  return parts.length > 1 ? `${parts.slice(0, -1).join(", ")} and ${parts.at(-1)}` : parts[0];
};

function UnattendedPanel({ unattended }: { unattended: UnattendedQueue[] }) {
  const queryClient = useQueryClient();
  const canOperate = useSessionCan("monitoring.operate");
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [confirming, setConfirming] = useState(false);
  const [outcome, setOutcome] = useState<UnattendedDeleteResult | null>(null);

  const unattendedMessages = unattended.reduce((n, q) => n + q.messages + q.retryMessages + q.deadMessages, 0);
  const unattendedQueueCount = unattended.reduce((n, q) => n + q.queues, 0);

  // A lane something still reads can't be deleted, so it can't be ticked. Filtering the ticks
  // through the current list also drops any whose lane has gone, or been picked up, since.
  const deletable = unattended.filter((q) => q.consumers === 0);
  const selected = deletable.filter((q) => picked.has(q.queueName));
  const allSelected = deletable.length > 0 && selected.length === deletable.length;
  const totals = selected.reduce(
    (t, q) => ({
      messages: t.messages + q.messages,
      retryMessages: t.retryMessages + q.retryMessages,
      deadMessages: t.deadMessages + q.deadMessages,
      queues: t.queues + q.queues,
    }),
    { messages: 0, retryMessages: 0, deadMessages: 0, queues: 0 },
  );
  const selectedMessages = totals.messages + totals.retryMessages + totals.deadMessages;

  const toggle = (name: string) =>
    setPicked((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });

  return (
    <Panel
      title="Nobody is reading these"
      description="Queues RabbitMQ still has that nothing here consumes: a paused information type's, or ones an older version of Bitween left behind when a work group or information type was deleted or renamed. Every other view on this page is built from what this instance declares, so these appear nowhere else."
      className="mb-4"
    >
      <p className="mb-3 text-[13px] text-ink-600">
        <span className="font-medium text-ink-900">
          {unattended.length} {unattended.length === 1 ? "lane" : "lanes"}
        </span>{" "}
        ({unattendedQueueCount} queues), holding{" "}
        <span className={unattendedMessages > 0 ? "font-medium text-warn-700" : "font-medium text-ink-900"}>
          {unattendedMessages} {unattendedMessages === 1 ? "message" : "messages"}
        </span>
        . Empty ones are only clutter; anything holding messages is stuck where nothing will
        pick it up.
      </p>

      {outcome && (
        <div className="mb-3 flex items-start justify-between gap-3 rounded-lg bg-ink-50 px-3 py-2 text-[13px] text-ink-700">
          <div>
            <p>
              Deleted {outcome.deleted.length} {outcome.deleted.length === 1 ? "lane" : "lanes"}.
              {outcome.skipped.length > 0 && ` Skipped ${outcome.skipped.length}:`}
            </p>
            {outcome.skipped.length > 0 && (
              <ul className="mt-1 space-y-0.5">
                {outcome.skipped.map((s) => (
                  <li key={s.queueName}>
                    <code className="font-mono text-xs">{s.queueName}</code> — {s.reason}
                  </li>
                ))}
              </ul>
            )}
          </div>
          <Button size="sm" variant="ghost" onClick={() => setOutcome(null)}>
            Dismiss
          </Button>
        </div>
      )}

      {canOperate && selected.length > 0 && (
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2 rounded-lg border border-ink-200 px-3 py-2">
          <span className="text-sm text-ink-700">
            <strong className="font-semibold">{selected.length}</strong> selected
            {selectedMessages > 0 && (
              <span className="text-warn-700">
                {" "}
                — holding {selectedMessages} {selectedMessages === 1 ? "message" : "messages"}
              </span>
            )}
          </span>
          <span className="flex gap-2">
            <Button size="sm" variant="ghost" onClick={() => setPicked(new Set())}>
              Clear
            </Button>
            <Button size="sm" variant="danger" onClick={() => setConfirming(true)}>
              <Trash2 className="size-3.5" aria-hidden />
              Delete selected…
            </Button>
          </span>
        </div>
      )}

      {/* Capped height rather than a disclosure: a long list stays scrollable and the
          rows holding messages sort to the top, where they are seen without a click. */}
      <div className="max-h-80 overflow-auto">
        <table className="w-full text-left text-sm">
          <thead className="sticky top-0 bg-white">
            <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-500 uppercase">
              {canOperate && (
                <th className="w-7 py-2">
                  <input
                    type="checkbox"
                    aria-label="Select every lane that can be deleted"
                    title="Select every lane that can be deleted"
                    className="size-3.5 cursor-pointer accent-crimson-600"
                    checked={allSelected}
                    disabled={deletable.length === 0}
                    onChange={() =>
                      setPicked(allSelected ? new Set() : new Set(deletable.map((q) => q.queueName)))
                    }
                  />
                </th>
              )}
              <th className="py-2 pr-2">Queue</th>
              <th className="px-2 py-2 text-right">Queued</th>
              <th className="px-2 py-2 text-right">Retrying</th>
              <th className="px-2 py-2 text-right">Dead</th>
            </tr>
          </thead>
          <tbody className="tabular-nums">
            {/* Toned per cell, matching the lanes table: a lane with 57 dead and nothing
                queued should draw the eye to Dead, not to a highlighted zero. */}
            {unattended.map((q) => (
              <tr key={q.queueName} className="border-b border-ink-50 last:border-0">
                {canOperate && (
                  <td className="py-1.5">
                    {q.consumers === 0 && (
                      <input
                        type="checkbox"
                        aria-label={`Select ${q.queueName}`}
                        className="size-3.5 cursor-pointer accent-crimson-600"
                        checked={picked.has(q.queueName)}
                        onChange={() => toggle(q.queueName)}
                      />
                    )}
                  </td>
                )}
                <td className="py-1.5 pr-3">
                  <code className="font-mono text-xs text-ink-600">{q.queueName}</code>
                  {q.queues > 1 && <span className="ml-1.5 text-[11px] text-ink-500">+{q.queues - 1}</span>}
                  {q.consumers > 0 && (
                    <Badge
                      className="ml-2"
                      title="Something still has a listener on these queues, so they can't be deleted yet."
                    >
                      In use
                    </Badge>
                  )}
                </td>
                <td className={`px-2 py-1.5 text-right ${q.messages > 0 ? "font-medium text-warn-700" : "text-ink-500"}`}>
                  {q.messages}
                </td>
                <td className={`px-2 py-1.5 text-right ${q.retryMessages > 0 ? "font-medium text-warn-700" : "text-ink-500"}`}>
                  {q.retryMessages}
                </td>
                <td className={`px-2 py-1.5 text-right ${q.deadMessages > 0 ? "font-medium text-danger-700" : "text-ink-500"}`}>
                  {q.deadMessages}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {confirming && selected.length > 0 && (
        <ConfirmDialog
          title={selected.length === 1 ? "Delete these queues?" : `Delete ${selected.length} lanes?`}
          body={
            <div className="space-y-2">
              <p>
                {selected.length === 1 ? (
                  <>
                    <code className="font-mono text-xs text-ink-800">{selected[0].queueName}</code>
                    {selected[0].queues > 1 &&
                      ` and its ${selected[0].queues - 1} retry/dead ${selected[0].queues === 2 ? "queue" : "queues"}`}
                  </>
                ) : (
                  `${selected.length} lanes (${totals.queues} queues)`
                )}{" "}
                will be deleted from RabbitMQ.
              </p>
              {selectedMessages > 0 ? (
                <p className="font-medium text-danger-700">
                  {selectedMessages === 1 ? "They still hold 1 message" : `They still hold ${selectedMessages} messages`} (
                  {messageBreakdown(totals)}), which will be deleted with them.
                </p>
              ) : (
                <p>{selected.length === 1 ? "They're empty." : "They're all empty."}</p>
              )}
            </div>
          }
          confirmLabel={selected.length === 1 ? "Delete queues" : `Delete ${selected.length} lanes`}
          onConfirm={async () => {
            setOutcome(await api.deleteUnattendedQueues(selected.map((q) => q.queueName)));
            setPicked(new Set());
            await queryClient.invalidateQueries({ queryKey: keys.queueHealth });
          }}
          onClose={() => setConfirming(false)}
        />
      )}
    </Panel>
  );
}

/**
 * One consumer's dead letters: what the bus last failed on, what is sitting there, and sending it
 * back to be tried again. Reading the messages takes the right to see exchanges as well, since the
 * bodies are the messages themselves.
 */
function DeadLetterItem({ row: d }: { row: DeadLetterRow }) {
  const queryClient = useQueryClient();
  const canMonitor = useSessionCan("monitoring.view");
  const canSeeExchanges = useSessionCan("exchanges.view");
  const canBrowse = canMonitor && canSeeExchanges;
  const canRequeue = useSessionCan("monitoring.operate");
  const [browsing, setBrowsing] = useState(false);
  const [requeueing, setRequeueing] = useState(false);
  const [requeued, setRequeued] = useState<number | null>(null);

  return (
    <li className="text-sm">
      <div className="flex items-center gap-2.5">
        <span className="min-w-0 flex-1">
          <span className="font-medium text-ink-800">{d.title}</span>
          <code className="block truncate font-mono text-[11px] text-ink-500">{d.queueName}</code>
        </span>
        {d.lastFailedAt && <span className="text-xs text-ink-500">{timeAgo(d.lastFailedAt)}</span>}
        <Badge tone="danger">{d.count} dead</Badge>
      </div>
      {d.lastExceptionMessage && (
        <p className="mt-1 rounded-md bg-danger-50 px-2.5 py-1.5 font-mono text-[11px] leading-relaxed text-danger-800">
          {d.lastExceptionType && <span className="font-semibold">{d.lastExceptionType}: </span>}
          {d.lastExceptionMessage}
        </p>
      )}
      {(canBrowse || canRequeue) && d.count > 0 && (
        <div className="mt-1.5 flex items-center gap-2">
          {canBrowse && (
            <Button size="sm" onClick={() => setBrowsing(true)}>
              <Eye className="size-3.5" /> Show messages
            </Button>
          )}
          {canRequeue && (
            <Button size="sm" onClick={() => setRequeueing(true)}>
              <RotateCcw className="size-3.5" /> Send back
            </Button>
          )}
          {requeued !== null && (
            <span role="status" className="text-xs text-ink-600">
              {requeued === 1 ? "1 message sent back." : `${requeued} messages sent back.`}
            </span>
          )}
        </div>
      )}

      {browsing && <DeadLetterMessages row={d} onClose={() => setBrowsing(false)} />}
      {requeueing && (
        <ConfirmDialog
          title={d.count === 1 ? "Send this message back?" : `Send ${d.count} messages back?`}
          body={
            <div className="space-y-2">
              <p>
                {d.count === 1 ? "It goes" : "They go"} back to{" "}
                <span className="font-medium text-ink-900">{d.title}</span>, oldest first, and{" "}
                {d.count === 1 ? "is" : "are"} tried again with a fresh set of retries.
              </p>
              <p>
                If what made {d.count === 1 ? "it" : "them"} fail hasn't been fixed,{" "}
                {d.count === 1 ? "it" : "they"} will end up here again.
              </p>
            </div>
          }
          confirmLabel="Send back"
          confirmVariant="primary"
          onConfirm={async () => {
            setRequeued(await api.requeueDeadLetters(d.queueName));
            await queryClient.invalidateQueries({ queryKey: keys.queueHealth });
          }}
          onClose={() => setRequeueing(false)}
        />
      )}
    </li>
  );
}

function DeadLetterMessages({ row, onClose }: { row: DeadLetterRow; onClose: () => void }) {
  const messages = useQuery({
    queryKey: [...keys.queueHealth, "dead-letters", row.queueName],
    queryFn: () => api.browseDeadLetters(row.queueName),
  });

  return (
    <Dialog title={`Dead letters — ${row.title}`} onClose={onClose} wide>
      {messages.isPending ? (
        <LoadingBlock label="Reading the queue…" />
      ) : messages.isError ? (
        <p role="alert" className="text-[13px] text-danger-700">
          {messages.error.message}
        </p>
      ) : messages.data.length === 0 ? (
        <EmptyState title="Empty">Nothing is waiting in this queue now.</EmptyState>
      ) : (
        <div className="space-y-4">
          <p className="text-[13px] text-ink-600">
            The oldest {messages.data.length} of {row.count}, read without taking them off the queue.
          </p>
          <ol className="space-y-3">
            {messages.data.map((m) => (
              <li key={m.position} className="rounded-xl border border-ink-200 p-3">
                <div className="mb-1.5 flex flex-wrap items-center gap-x-3 text-xs text-ink-500">
                  <span className="font-medium text-ink-800">#{m.position}</span>
                  {m.correlationId && <span>Correlation {m.correlationId}</span>}
                </div>
                {m.lastException && (
                  <div className="mb-2">
                    <ExceptionBlock text={m.lastException} title="Why it was dead-lettered" />
                  </div>
                )}
                <pre className="max-h-64 overflow-auto rounded-md bg-ink-50 p-2.5 font-mono text-[11.5px] text-ink-800">
                  {m.body}
                </pre>
                {m.bodyCut && <p className="mt-1 text-xs text-ink-500">Only the start of a long message is shown.</p>}
              </li>
            ))}
          </ol>
        </div>
      )}
    </Dialog>
  );
}
