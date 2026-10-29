import type { ReactNode } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, type SubscriptionDetail, type SubscriptionRun } from "../../../api";
import { Can } from "../../../auth/guards";
import { useSessionCan } from "../../../auth/useSessionCan";
import { Badge, LoadingBlock, ProblemText } from "../../../components/ui/basics";
import { MiniTable } from "../../../components/ui/Table";
import { Panel } from "../../../components/ui/Panel";
import { HistoryCard } from "../../../components/config/HistoryCard";
import { ExchangesList, HealthBadge } from "../../../components/config/shared";
import { formatDate, formatDateTime, formatDurationMs, timeAgo, timeUntil } from "../../../lib/dates";
import { ReceiveAttemptsPanel, type AttemptKind } from "./ReceiveAttemptsPanel";
import { RetryBudget } from "./RetryBudget";
import { Fact } from "./Fact";
import { LaneAndRetry } from "./LaneAndRetry";
import { NotificationsPanel } from "./NotificationsPanel";
import type { Draft, EntryPoint } from "./model";
import { keys } from "../../../api/queryKeys";
import { ExceptionBlock, ExceptionLine } from "../../../components/ui/Exception";
import { CategoryField } from "./CategoryField";
import { HealthPanel } from "./HealthPanel";
import { ExpectTrafficPanel } from "./ExpectTrafficPanel";
import { VersionsPanel } from "./VersionsPanel";

/** Who can feed this subscription. Shared with the Trigger stage, which is the same question. */
export function EntryPointsTable({ rows, empty }: { rows: EntryPoint[]; empty: ReactNode }) {
  return (
    <MiniTable
      rows={rows}
      rowKey={(e) => e.key}
      empty={empty}
      columns={[
        {
          header: "Entry point",
          wrap: true,
          cell: (e) => (
            <Link
              to={e.href}
              className="block font-medium text-ink-800 hover:link"
            >
              {e.name}
            </Link>
          ),
        },
        { header: "Kind", cell: (e) => <Badge>{e.kind}</Badge> },
        {
          header: "Partner",
          cell: (e) =>
            e.partnerId !== null ? (
              <Link
                to={`/partners/${e.partnerId}`}
                className="text-[13px] text-ink-600 hover:link"
              >
                {e.partnerName}
              </Link>
            ) : (
              <span className="text-ink-500">-</span>
            ),
        },
        {
          header: "Path",
          align: "right",
          cell: (e) => <code className="font-mono text-xs text-ink-500">{e.detail}</code>,
        },
      ]}
    />
  );
}


function LastRunFact({ run }: { run: SubscriptionRun | undefined }) {
  if (!run) return <span className="text-ink-500">Never</span>;
  return (
    <span className="flex items-center gap-1.5" title={formatDateTime(run.startedOn)}>
      {run.success === null ? (
        <Badge tone="warn">Running</Badge>
      ) : run.success ? (
        <Badge tone="ok">OK</Badge>
      ) : (
        <Badge tone="danger">Failed</Badge>
      )}
      <span>{timeAgo(run.startedOn)}</span>
      {run.durationMs !== null && (
        <span className="text-ink-500">· {formatDurationMs(run.durationMs)}</span>
      )}
    </span>
  );
}

function RunsTable({ runs, pending }: { runs: SubscriptionRun[]; pending: boolean }) {
  if (pending) return <LoadingBlock label="Loading runs…" />;
  return (
    <MiniTable
      rows={runs}
      rowKey={(r) => r.startedOn}
      empty="No runs recorded in the last 30 days."
      columns={[
        {
          header: "Started",
          cell: (r) => (
            <span title={formatDateTime(r.startedOn)} className="text-ink-800">
              {timeAgo(r.startedOn)}
            </span>
          ),
        },
        {
          header: "Outcome",
          cell: (r) =>
            r.success === null ? (
              <Badge tone="warn">Running</Badge>
            ) : r.success ? (
              <Badge tone="ok">Succeeded</Badge>
            ) : (
              <Badge tone="danger">Failed</Badge>
            ),
        },
        {
          header: "Took",
          cell: (r) => (
            <span className="text-ink-600">
              {r.durationMs === null ? "-" : formatDurationMs(r.durationMs)}
            </span>
          ),
        },
        {
          header: "Trigger",
          cell: (r) => <span className="text-ink-500">{r.manual ? "Manual" : "Schedule"}</span>,
        },
        {
          header: "Error",
          truncate: true,
          cell: (r) =>
            r.error ? (
              <ExceptionLine text={r.error} icon className="font-mono text-[11px] text-danger-700" />
            ) : (
              <span className="text-ink-500">-</span>
            ),
        },
      ]}
    />
  );
}

/** The window the Health panel counts over, so "Fed by" and its Exchanges figure agree. */
const FEED_DAYS = 7;

/**
 * "Fed by" for a direct subscription: the calls it had, plus any gateway also pointing at it.
 * It only reads as a problem when both are none. `calls` is undefined when the viewer may not
 * see traffic, and null while it loads; neither is a reason to call it unfed.
 */
function DirectFeed({ entryPoints, calls }: { entryPoints: number; calls: number | null | undefined }) {
  const gateways = entryPoints > 0 ? `${entryPoints} entry point${entryPoints === 1 ? "" : "s"}` : null;
  if (calls === undefined) return <>{gateways ? `Direct calls, ${gateways}` : "Direct calls"}</>;
  if (calls === null) return <span className="text-ink-500">Direct calls</span>;
  if (calls === 0 && !gateways)
    return <ProblemText plain>Nothing in {FEED_DAYS} days and no gateway points here</ProblemText>;
  return (
    <>
      Direct calls · {calls.toLocaleString()} in {FEED_DAYS} days
      {gateways && `, ${gateways}`}
    </>
  );
}

/**
 * The page with no stage selected: everything about this subscription that isn't
 * a step in its pipeline, compressed so it and the pipeline share one screen.
 *
 * The facts an operator triages on are a single strip; the two histories sit
 * below it. Entry points are not repeated here - they are the Trigger node.
 */
export function Overview({
  s,
  draft,
  set,
  canEdit,
  entryPoints,
  scheduled,
  onNewChannel,
  onEditChannel,
}: {
  s: SubscriptionDetail;
  draft: Draft;
  set: <K extends keyof Draft>(key: K, value: Draft[K]) => void;
  canEdit: boolean;
  entryPoints: EntryPoint[];
  /** Receiving and Aggregation run on a schedule, so they have run history. */
  scheduled: boolean;
  /** Leave for a channel's page, keeping the draft - see NotificationsPanel. */
  onNewChannel?: (index: number) => void;
  onEditChannel?: (channelId: number) => void;
}) {
  // Both scheduled types keep their own attempt history and show it in one table
  // (ReceiveAttemptsPanel) instead of the scheduler's run history beside a separate exchange
  // list. The scheduler's history is Quartz vocabulary an operator has no reason to know, it
  // always reports success even when the job's own step failed, and - the reason two tables
  // were worse than one - nothing joins a run to the exchange it produced.
  const receiving = s.type === "Receiving";
  const aggregation = s.type === "Aggregation";
  const attemptKind: AttemptKind | null = receiving ? "receiving" : aggregation ? "aggregation" : null;
  const runs = useQuery({
    queryKey: keys.subscriptions.runs(s.id),
    queryFn: () => api.listSubscriptionRuns(s.id, 20),
    enabled: scheduled && attemptKind === null,
  });
  // Just for the "Last run" fact above - ReceiveAttemptsPanel fetches its own page.
  const latestAttempt = useQuery({
    queryKey: keys.subscriptions.receiveAttempts(s.id, { outcome: null, offset: 0, limit: 1 }),
    queryFn: () => api.searchReceiveAttempts(s.id, { outcome: null, offset: 0, limit: 1 }),
    enabled: attemptKind !== null,
  });
  const lastReceiveRun: SubscriptionRun | undefined = ((): SubscriptionRun | undefined => {
    const a = latestAttempt.data?.result[0];
    if (!a) return undefined;
    return {
      startedOn: a.startedOn,
      endedOn: a.finishedOn,
      durationMs: new Date(a.finishedOn).getTime() - new Date(a.startedOn).getTime(),
      success: a.outcome !== "Failed",
      error: a.errorMessage,
      node: "",
      manual: false,
    };
  })();
  const paused = s.pausedOn !== null;
  // A direct subscription has no gateway, so its entry points say nothing about whether
  // anything feeds it: what does is the calls it actually got. Same query (and cache entry)
  // as the Health panel below, so this costs no extra request.
  const direct = s.type === "Internal" || s.type === "ApiCall";
  const canSeeTraffic = useSessionCan("exchanges.view");
  const directTraffic = useQuery({
    queryKey: keys.metrics.subscription(s.id, FEED_DAYS),
    queryFn: () => api.getSubscriptionMetrics(s.id, FEED_DAYS),
    enabled: direct && canSeeTraffic,
  });

  return (
    <div className="space-y-5">
      {/* No card around this: the pipeline above has to be the loudest thing on
          the page, and a second full-width white panel directly under it was
          pulling the eye straight past the diagram. */}
      <div className="flex flex-wrap items-start gap-x-10 gap-y-4 border-b border-ink-200 px-1 pb-5">
        <Fact label="Health">
          <HealthBadge isRunning={s.isRunning} consecutiveFailures={s.consecutiveFailures} />
        </Fact>
        {scheduled && (
          <>
            <Fact label="Next run">{s.nextReceiveOn ? timeUntil(s.nextReceiveOn) : "-"}</Fact>
            <Fact label="Last run">
              <LastRunFact run={attemptKind !== null ? lastReceiveRun : runs.data?.[0]} />
            </Fact>
          </>
        )}
        <Fact label="Fed by">
          {scheduled ? (
            "Its own schedule"
          ) : direct ? (
            <DirectFeed
              entryPoints={entryPoints.length}
              calls={canSeeTraffic ? (directTraffic.data?.total.total ?? null) : undefined}
            />
          ) : entryPoints.length ? (
            // A response subscription's entry points are the subscriptions feeding it.
            `${entryPoints.length} ${s.type === "Response" ? "subscription" : "entry point"}${entryPoints.length === 1 ? "" : "s"}`
          ) : (
            <ProblemText plain>
              {s.type === "Response" ? "No subscription hands it a response" : "Nothing, no gateway points here"}
            </ProblemText>
          )}
        </Fact>
        <LaneAndRetry
          workGroupId={draft.workGroupId}
          retryPolicyId={draft.retryPolicyId}
          onWorkGroupChange={(id) => set("workGroupId", id)}
          onRetryPolicyChange={(id) => set("retryPolicyId", id)}
          canEdit={canEdit}
          idPrefix="in"
          inlineRetryGroups={s.customRetryGroups ?? null}
          inlineReplaced={draft.retryPolicyId !== s.retryPolicyId}
          subscriptionId={s.id}
        />
        <CategoryField
          categoryId={draft.categoryId}
          onChange={(id) => set("categoryId", id)}
          canEdit={canEdit}
          idPrefix="in"
        />
        <Fact label="Auto-pause">
          <AutoPauseField
            value={draft.autoPauseAfterFailures}
            onChange={(n) => set("autoPauseAfterFailures", n)}
            disabled={!canEdit}
          />
        </Fact>
      </div>

      <RetryBudget subscriptionId={s.id} canEdit={canEdit} />

      <Can permission="exchanges.view">
        <HealthPanel subscriptionId={s.id} />
      </Can>

      {paused && (
        <p className="rounded-xl bg-warn-100 px-4 py-2.5 text-[13px] text-warn-700">
          {s.pausedAutomatically
            ? `Paused automatically on ${formatDate(s.pausedOn!)} after ${s.consecutiveFailures} failed deliveries in a row`
            : `Paused since ${formatDate(s.pausedOn!)}`}{" "}
          - incoming work is being held and will be released on resume.
        </p>
      )}
      {s.quietSince && (
        <p className="rounded-xl bg-warn-100 px-4 py-2.5 text-[13px] text-warn-700">
          Quiet since {formatDateTime(s.quietSince)}
          {s.expectTraffic && `: no exchanges within the expected ${s.expectTraffic.everyMinutes} minutes`}.
        </p>
      )}
      {/* The scheduled types get this per-attempt instead, in ReceiveAttemptsPanel below -
          showing it again here duplicated the same error twice on one page. */}
      {s.lastException && attemptKind === null && (
        <ExceptionBlock text={s.lastException} title="Last error" />
      )}

      {attemptKind !== null && (
        <Can permission="exchanges.view">
          <ReceiveAttemptsPanel subscriptionId={s.id} kind={attemptKind} />
        </Can>
      )}

      <div className="grid gap-5 lg:grid-cols-2">
        <div className="min-w-0 space-y-5">
          <ExpectTrafficPanel
            value={draft.expectTraffic}
            onChange={(v) => set("expectTraffic", v)}
            disabled={!canEdit}
          />

          {scheduled && attemptKind === null && (
            <Panel title="Recent runs" description="From the scheduler's own history, kept about 30 days.">
              <RunsTable runs={runs.data ?? []} pending={runs.isPending} />
            </Panel>
          )}

          <VersionsPanel subscriptionId={s.id} />

          <HistoryCard entityName="Subscription" entityKey={s.id} />
        </div>

        <div className="min-w-0 space-y-5">
          <NotificationsPanel
            value={draft.notifications}
            onChange={(notifications) => set("notifications", notifications)}
            canEdit={canEdit}
            onNewChannel={onNewChannel}
            onEditChannel={onEditChannel}
          />

          {attemptKind === null && (
            <Can permission="exchanges.view">
              <Panel title="Recent exchanges" description="Latest traffic through this subscription.">
                <ExchangesList items={s.recentExchanges} hide={["type", "partner"]} />
              </Panel>
            </Can>
          )}

          {s.watchingNotifiers.length > 0 && (
            <Panel title="Legacy notifiers" description="Notifiers made before channels, still alerting on this subscription's outcomes.">
              <MiniTable
                rows={s.watchingNotifiers}
                rowKey={(n) => n.id}
                empty="Nothing watches this subscription."
                columns={[
                  {
                    header: "Notifier",
                    wrap: true,
                    cell: (n) => (
                      <Link
                        to={`/notifiers/${n.id}`}
                        className="block font-medium text-ink-800 hover:link"
                      >
                        {n.name}
                      </Link>
                    ),
                  },
                ]}
              />
            </Panel>
          )}
        </div>
      </div>
    </div>
  );
}

/**
 * After how many failed deliveries in a row the subscription pauses itself. Empty never pauses -
 * the default, and what every subscription saved before this had.
 */
function AutoPauseField({
  value,
  onChange,
  disabled,
}: {
  value: number | null;
  onChange: (n: number | null) => void;
  disabled: boolean;
}) {
  return (
    <div className="w-52">
      <label className="flex items-center gap-2 text-[13px] text-ink-700">
        <span>After</span>
        <input
          id="in-autopause"
          aria-label="Pause after this many failed deliveries in a row"
          type="number"
          min={1}
          max={1000}
          inputMode="numeric"
          placeholder="Never"
          value={value ?? ""}
          disabled={disabled}
          onChange={(e) => {
            const n = e.target.valueAsNumber;
            onChange(Number.isFinite(n) && n >= 1 ? Math.min(Math.floor(n), 1000) : null);
          }}
          className="w-20 rounded-lg border border-ink-200 px-2 py-1 text-[13px] disabled:bg-ink-50"
        />
        <span>failures in a row</span>
      </label>
    </div>
  );
}
