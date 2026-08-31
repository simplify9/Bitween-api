import { useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, DownloadCloud, Plus, Search } from "lucide-react";
import { api, type SubscriptionRow, type ScheduleHealth } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { Select } from "../../components/ui/forms";
import { Pagination } from "../../components/ui/Pagination";
import { Table } from "../../components/ui/Table";
import {
  HealthBadge,
  SubscriptionStatusBadges,
  LinkListCell,
  scheduleFault,
  useSubscriptionsCache,
  useRetryPolicyNames,
  useWorkGroupNames,
} from "../../components/config/shared";
import { formatDateTime, formatDurationMs, timeAgo, timeUntil } from "../../lib/dates";
import { keys } from "../../api/queryKeys";

function ReceiveNowButton({ job }: { job: SubscriptionRow }) {
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState(false);
  const receive = useMutation({
    mutationFn: () => api.receiveNow(job.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
    },
  });

  return (
    <>
      <Button
        size="sm"
        disabled={!job.enabled}
        title={job.enabled ? "Check the source right now." : "Enable the job first."}
        onClick={(e) => {
          e.stopPropagation();
          setConfirming(true);
        }}
      >
        <DownloadCloud className="size-3.5" /> Receive now
      </Button>
      {confirming && (
        <ConfirmDialog
          title="Receive now?"
          body={`${job.name} checks its source immediately — anything found becomes new exchanges, outside the regular schedule.`}
          confirmLabel="Receive now"
          onConfirm={async () => {
            await receive.mutateAsync();
          }}
          onClose={() => setConfirming(false)}
        />
      )}
    </>
  );
}

/**
 * The scheduler disagreeing with the subscription's own record. Everything here
 * means the job is not going to run, while Status still reads "Active" — so it
 * outranks the ordinary badges rather than sitting beside them.
 */
const STATUS_OPTIONS = [
  { value: "", label: "Any status" },
  { value: "false", label: "Active" },
  { value: "true", label: "Disabled" },
];

function ScheduleFault({ health }: { health: ScheduleHealth | undefined }) {
  const fault = scheduleFault(health);
  if (!fault) return null;
  return (
    <Badge tone={fault.tone} title={fault.title}>
      {fault.label}
    </Badge>
  );
}

/**
 * Scheduled jobs — `Receiving` subscriptions, which pull documents in on a
 * schedule. They also appear on the Subscriptions page; this page exists for the
 * columns only a scheduled thing has, and for running one off-schedule.
 *
 * Last run comes from the scheduler's own execution history (kept ~30 days);
 * next run is Bitween's own `ReceiveOn`.
 */
const PAGE_SIZE = 25;

export function ScheduledJobsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const inactiveParam = searchParams.get("inactive");
  const inactive = inactiveParam === "true" ? true : inactiveParam === "false" ? false : null;
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;
  const canOperate = useSessionCan("subscriptions.operate");
  const canSeeInfoTypes = useSessionCan("documents.view");

  const rows = useQuery({
    queryKey: keys.subscriptions.rowsSearch({ type: "Receiving", q, inactive, offset }),
    queryFn: () =>
      api.searchSubscriptionRows({ search: q, type: "Receiving", inactive, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  // The list rows don't carry work group or retry policy; the subscriptions
  // cache does, and every page already holds it.
  const setups = useSubscriptionsCache().data ?? [];
  const setupById = useMemo(() => new Map(setups.map((s) => [s.id, s])), [setups]);
  const workGroupNames = useWorkGroupNames();
  const retryPolicyNames = useRetryPolicyNames();
  // One request for the whole list rather than one per row.
  const lastRuns = useQuery({ queryKey: keys.subscriptions.lastRuns, queryFn: () => api.listLastRuns() }).data ?? [];
  const lastRunById = useMemo(() => new Map(lastRuns.map((r) => [r.subscriptionId, r])), [lastRuns]);
  const health =
    useQuery({ queryKey: keys.subscriptions.scheduleHealth, queryFn: () => api.listScheduleHealth() }).data ?? [];
  const healthById = useMemo(() => new Map(health.map((h) => [h.subscriptionId, h])), [health]);

  const setParam = (key: string, value: string | null, resetOffset = true) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        if (resetOffset) next.delete("offset");
        return next;
      },
      { replace: true },
    );

  const filtered = rows.data?.result ?? [];
  const total = rows.data?.total ?? 0;

  return (
    <div>
      <PageHeader
        title="Scheduled jobs"
        description="Subscriptions that pull documents in on a schedule — from an FTP folder, a mailbox, an API."
        actions={
          <Can permission="subscriptions.create">
            <Button variant="primary" onClick={() => navigate("/scheduled-jobs/new")}>
              <Plus className="size-4" /> New scheduled job
            </Button>
          </Can>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative w-full max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
          <input
            type="search"
            value={q}
            onChange={(e) => setParam("q", e.target.value || null)}
            placeholder="Search jobs"
            aria-label="Search scheduled jobs"
            className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
          />
        </div>
        <div className="w-40">
          <Select
            aria-label="Filter by status"
            className="!h-8 text-[13px]"
            value={inactiveParam ?? ""}
            onChange={(e) => setParam("inactive", e.target.value || null)}
            options={STATUS_OPTIONS}
          />
        </div>
      </div>

      {rows.isPending ? (
        <LoadingBlock label="Loading scheduled jobs…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<CalendarClock />} title={q || inactive !== null ? "No jobs match" : "No scheduled jobs yet"}>
          {q || inactive !== null ? "Try a different search or filter." : "Create a job to pull documents in on a schedule."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(r) => r.id}
          minWidth="min-w-270"
          onRowClick={(r) => navigate(`/subscriptions/${r.id}`)}
          footer={
            <Pagination
              offset={offset}
              limit={PAGE_SIZE}
              total={total}
              onOffsetChange={(o) => setParam("offset", String(o), false)}
            />
          }
          columns={[
            {
              header: "Job",
              wrap: true,
              cell: (r) => <span className="block font-medium text-ink-900">{r.name}</span>,
            },
            {
              header: "Pulls in",
              cell: (r) =>
                canSeeInfoTypes ? (
                  <Link
                    to={`/information-types/${r.informationTypeId}`}
                    onClick={(e) => e.stopPropagation()}
                    className="font-mono text-xs text-ink-600 hover:text-crimson-700 hover:underline"
                  >
                    {r.informationTypeCode}
                  </Link>
                ) : (
                  <code className="font-mono text-xs text-ink-600">{r.informationTypeCode}</code>
                ),
            },
            {
              // "Manual" matters here: a job whose only recent run was someone
              // pressing Receive now looks healthy while its schedule is dead.
              header: "Last run",
              className: "whitespace-nowrap",
              cell: (r) => {
                const run = lastRunById.get(r.id);
                if (!run)
                  return (
                    <span className="text-ink-400" title="No run in the last 30 days">
                      —
                    </span>
                  );
                const running = run.success === null;
                return (
                  <>
                    <span className="text-[13px] font-medium text-ink-800">{timeAgo(run.startedOn)}</span>
                    <span
                      className={`block text-xs ${run.success === false ? "text-danger-700" : "text-ink-400"}`}
                    >
                      {running
                        ? "running…"
                        : [
                            run.success === false ? "failed" : null,
                            run.durationMs !== null ? formatDurationMs(run.durationMs) : null,
                            run.manual ? "manual" : null,
                          ]
                            .filter(Boolean)
                            .join(" · ")}
                    </span>
                  </>
                );
              },
            },
            {
              // A single failure is noise; a job that has been failing half its
              // runs is the one to look at, and the health badge can't say that.
              header: "Reliability",
              className: "whitespace-nowrap",
              cell: (r) => {
                const run = lastRunById.get(r.id);
                if (!run || run.recentTotal === 0) return <span className="text-ink-400">—</span>;
                const failed = run.recentTotal - run.recentSucceeded;
                return (
                  <span
                    className={`text-[13px] ${failed > 0 ? "text-danger-700" : "text-ink-600"}`}
                    title={`${run.recentSucceeded} of the last ${run.recentTotal} finished runs succeeded`}
                  >
                    {run.recentSucceeded}/{run.recentTotal}
                  </span>
                );
              },
            },
            {
              // No Schedule column: `Search.cs` can't select Schedules in its
              // joined query, so `scheduleSummary` is always empty here and the
              // cell would read "No schedule" for a job that plainly has one.
              // Next run is real — it's the subscription's own ReceiveOn.
              header: "Next run",
              className: "whitespace-nowrap",
              cell: (r) =>
                r.nextReceiveOn ? (
                  <>
                    <span className="text-[13px] font-medium text-ink-800">{timeUntil(r.nextReceiveOn)}</span>
                    <span className="block text-xs text-ink-400">{formatDateTime(r.nextReceiveOn)}</span>
                  </>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
            {
              header: "Partner",
              wrap: true,
              cell: (r) => (
                <LinkListCell
                  label="partners"
                  items={r.partners.map((p) => ({
                    key: p.id,
                    name: p.name,
                    href: `/partners/${p.id}`,
                  }))}
                />
              ),
            },
            {
              // Which queue lane it runs in — the difference between a job that
              // is merely idle and one that is queued behind everything else.
              header: "Work group",
              wrap: true,
              cell: (r) => {
                const id = setupById.get(r.id)?.workGroupId ?? null;
                const name = id === null ? null : (workGroupNames.get(id) ?? null);
                if (id === null) return <span className="text-[13px] text-ink-400">Ungrouped</span>;
                return name ? (
                  <Link
                    to={`/work-groups/${id}`}
                    onClick={(e) => e.stopPropagation()}
                    className="block text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
                  >
                    {name}
                  </Link>
                ) : (
                  <span className="text-[13px] text-ink-400">—</span>
                );
              },
            },
            {
              header: "Retry policy",
              wrap: true,
              cell: (r) => {
                const id = setupById.get(r.id)?.retryPolicyId ?? null;
                const name = id === null ? null : (retryPolicyNames.get(id) ?? null);
                if (id === null) return <span className="text-[13px] text-ink-400">None</span>;
                return name ? (
                  <Link
                    to={`/retry-policies/${id}`}
                    onClick={(e) => e.stopPropagation()}
                    className="block text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
                  >
                    {name}
                  </Link>
                ) : (
                  <span className="text-[13px] text-ink-400">—</span>
                );
              },
            },
            {
              header: "Status",
              cell: (r) => (
                <span className="flex max-w-20 flex-wrap items-center gap-1">
                  <ScheduleFault health={healthById.get(r.id)} />
                  <SubscriptionStatusBadges enabled={r.enabled} paused={r.paused} />
                  <HealthBadge isRunning={r.isRunning} consecutiveFailures={r.consecutiveFailures} />
                </span>
              ),
            },
            {
              header: "Last error",
              truncate: true,
              cell: (r) =>
                r.lastException ? (
                  <span
                    className="block truncate font-mono text-[11px] text-danger-700"
                    title={r.lastException}
                  >
                    {r.lastException}
                  </span>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
            {
              header: "",
              align: "right",
              cell: (r) => canOperate && <ReceiveNowButton job={r} />,
            },
          ]}
        />
      )}
    </div>
  );
}
