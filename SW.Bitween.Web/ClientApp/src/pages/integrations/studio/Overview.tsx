import type { ReactNode } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, type IntegrationDetail, type IntegrationRun } from "../../../api";
import { Can } from "../../../auth/guards";
import { Badge, LoadingBlock } from "../../../components/ui/basics";
import { SearchSelect } from "../../../components/ui/SearchSelect";
import { MiniTable } from "../../../components/ui/Table";
import { Panel } from "../../../components/ui/Panel";
import { ExchangesList, HealthBadge, TrailTable } from "../../../components/config/shared";
import { withReturn } from "../../../lib/returnTo";
import { formatDate, formatDateTime, formatDurationMs, timeAgo, timeUntil } from "../../../lib/dates";
import type { Draft, EntryPoint } from "./model";

/** Who can feed this integration. Shared with the Trigger stage, which is the same question. */
export function EntryPointsTable({ rows, empty }: { rows: EntryPoint[]; empty: string }) {
  return (
    <MiniTable
      rows={rows}
      rowKey={(e) => e.key}
      empty={empty}
      columns={[
        {
          header: "Entry point",
          truncate: true,
          cell: (e) => (
            <Link
              to={e.href}
              className="block truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
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
                className="text-[13px] text-ink-600 hover:text-crimson-700 hover:underline"
              >
                {e.partnerName}
              </Link>
            ) : (
              <span className="text-ink-400">—</span>
            ),
        },
        {
          header: "Path",
          align: "right",
          cell: (e) => <code className="font-mono text-xs text-ink-400">{e.detail}</code>,
        },
      ]}
    />
  );
}

/** One labelled cell of the facts strip. */
function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <p className="mb-1 text-[11px] font-medium tracking-wide text-ink-400 uppercase">{label}</p>
      <div className="text-[13px] text-ink-800">{children}</div>
    </div>
  );
}

function LastRunFact({ run }: { run: IntegrationRun | undefined }) {
  if (!run) return <span className="text-ink-400">Never</span>;
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
        <span className="text-ink-400">· {formatDurationMs(run.durationMs)}</span>
      )}
    </span>
  );
}

function RunsTable({ runs, pending }: { runs: IntegrationRun[]; pending: boolean }) {
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
              {r.durationMs === null ? "—" : formatDurationMs(r.durationMs)}
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
              <span className="block truncate font-mono text-[11px] text-danger-700" title={r.error}>
                {r.error}
              </span>
            ) : (
              <span className="text-ink-400">—</span>
            ),
        },
      ]}
    />
  );
}

/**
 * The page with no stage selected: everything about this integration that isn't
 * a step in its pipeline, compressed so it and the pipeline share one screen.
 *
 * The facts an operator triages on are a single strip; the two histories sit
 * below it. Entry points are not repeated here — they are the Trigger node.
 */
export function Overview({
  s,
  draft,
  set,
  canEdit,
  canCreateWorkGroup,
  here,
  entryPoints,
  scheduled,
}: {
  s: IntegrationDetail;
  draft: Draft;
  set: <K extends keyof Draft>(key: K, value: Draft[K]) => void;
  canEdit: boolean;
  canCreateWorkGroup: boolean;
  /** This page's own URL, for the "+ New work group" detour. */
  here: string;
  entryPoints: EntryPoint[];
  /** Receiving and Aggregation run on a schedule, so they have run history. */
  scheduled: boolean;
}) {
  const workGroups = useQuery({
    queryKey: ["work-groups"],
    queryFn: () => api.listWorkGroups(),
    staleTime: Infinity,
  });
  const retryPolicies = useQuery({ queryKey: ["retry-policies"], queryFn: () => api.listRetryPolicies() });
  const runs = useQuery({
    queryKey: ["integration-runs", s.id],
    queryFn: () => api.listIntegrationRuns(s.id, 20),
    enabled: scheduled,
  });
  const paused = s.pausedOn !== null;

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
            <Fact label="Next run">{s.nextReceiveOn ? timeUntil(s.nextReceiveOn) : "—"}</Fact>
            <Fact label="Last run">
              <LastRunFact run={runs.data?.[0]} />
            </Fact>
          </>
        )}
        <Fact label="Fed by">
          {scheduled ? (
            "Its own schedule"
          ) : entryPoints.length ? (
            `${entryPoints.length} entry point${entryPoints.length === 1 ? "" : "s"}`
          ) : (
            <span className="text-danger-700">Nothing — it never runs</span>
          )}
        </Fact>
        <Fact label="Work group">
          <div className="w-52">
            <SearchSelect
              id="in-wg"
              value={draft.workGroupId === null ? "" : String(draft.workGroupId)}
              disabled={!canEdit}
              onChange={(v) => set("workGroupId", v === "" ? null : Number(v))}
              clearLabel="Ungrouped (default lane)"
              options={(workGroups.data ?? []).map((w) => ({ value: String(w.id), label: w.name }))}
            />
          </div>
          <div className="mt-1 flex items-center gap-3">
            {draft.workGroupId !== null && (
              <Link
                to={`/work-groups/${draft.workGroupId}`}
                className="text-[12px] font-medium text-crimson-700 hover:underline"
              >
                View
              </Link>
            )}
            {canCreateWorkGroup && (
              <Link
                to={withReturn("/work-groups/new", {
                  to: here,
                  label: `Assigning ${s.name} to a work group`,
                })}
                className="text-[12px] font-medium text-crimson-700 hover:underline"
              >
                + New
              </Link>
            )}
          </div>
        </Fact>
        <Fact label="Retry policy">
          <div className="w-52">
            <SearchSelect
              id="in-rp"
              value={draft.retryPolicyId === null ? "" : String(draft.retryPolicyId)}
              disabled={!canEdit}
              onChange={(v) => set("retryPolicyId", v === "" ? null : Number(v))}
              clearLabel="None — failures are not retried"
              options={(retryPolicies.data ?? []).map((p) => ({ value: String(p.id), label: p.name }))}
            />
          </div>
          {draft.retryPolicyId !== null && (
            <Link
              to={`/retry-policies/${draft.retryPolicyId}`}
              className="mt-1 inline-block text-[12px] font-medium text-crimson-700 hover:underline"
            >
              View
            </Link>
          )}
        </Fact>
      </div>

      {paused && (
        <p className="rounded-xl bg-warn-100 px-4 py-2.5 text-[13px] text-warn-700">
          Paused since {formatDate(s.pausedOn!)} — incoming work is being held and will be released
          on resume.
        </p>
      )}
      {s.lastException && (
        <pre className="max-h-40 overflow-auto rounded-xl border border-danger-200 bg-danger-50 px-4 py-3 font-mono text-[11px] leading-relaxed whitespace-pre-wrap text-danger-800">
          {s.lastException}
        </pre>
      )}

      <div className="grid gap-5 lg:grid-cols-2">
        <div className="min-w-0 space-y-5">
          {scheduled && (
            <Panel title="Recent runs" description="From the scheduler's own history, kept about 30 days.">
              <RunsTable runs={runs.data ?? []} pending={runs.isPending} />
            </Panel>
          )}

          {s.trail.length > 0 && (
            <Panel title="History">
              <TrailTable entries={s.trail} />
            </Panel>
          )}
        </div>

        <div className="min-w-0 space-y-5">
          <Can permission="exchanges.view">
            <Panel title="Recent exchanges" description="Latest traffic through this integration.">
              <ExchangesList items={s.recentExchanges} hide={["type", "partner"]} />
            </Panel>
          </Can>

          {s.watchingNotifiers.length > 0 && (
            <Panel title="Watched by" description="Notifiers alerting on this integration's outcomes.">
              <MiniTable
                rows={s.watchingNotifiers}
                rowKey={(n) => n.id}
                empty="Nothing watches this integration."
                columns={[
                  {
                    header: "Notifier",
                    truncate: true,
                    cell: (n) => (
                      <Link
                        to={`/notifiers/${n.id}`}
                        className="block truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
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
