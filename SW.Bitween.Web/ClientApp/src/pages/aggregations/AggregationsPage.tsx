import { useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Layers, Play, Plus, Search } from "lucide-react";
import { api, type SubscriptionRow, type ScheduleHealth } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { Select } from "../../components/ui/forms";
import { Pagination } from "../../components/ui/Pagination";
import { Table } from "../../components/ui/Table";
import { AGGREGATION_TARGET_LABEL } from "../../components/config/AggregationFields";
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

function AggregateNowButton({ job }: { job: SubscriptionRow }) {
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState(false);
  const aggregate = useMutation({
    mutationFn: () => api.aggregateNow(job.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["subscription-rows"] });
      void queryClient.invalidateQueries({ queryKey: ["subscription-rows-search"] });
      void queryClient.invalidateQueries({ queryKey: ["last-runs"] });
    },
  });

  return (
    <>
      <Button
        size="sm"
        disabled={!job.enabled}
        title={job.enabled ? "Roll up everything outstanding right now." : "Enable the aggregation first."}
        onClick={(e) => {
          e.stopPropagation();
          setConfirming(true);
        }}
      >
        <Play className="size-3.5" /> Roll up now
      </Button>
      {confirming && (
        <ConfirmDialog
          title="Roll up now?"
          // Not a dry run: the roll-up exchange goes down its own pipeline, so this can
          // reach a partner. Everything it collects is also marked as collected, so a
          // later scheduled run will not include it again.
          body={`${job.name} collects everything outstanding immediately and runs its delivery — outside the regular schedule.`}
          confirmLabel="Roll up now"
          onConfirm={async () => {
            await aggregate.mutateAsync();
          }}
          onClose={() => setConfirming(false)}
        />
      )}
    </>
  );
}

const STATUS_OPTIONS = [
  { value: "", label: "Any status" },
  { value: "false", label: "Active" },
  { value: "true", label: "Disabled" },
];

/** The scheduler disagreeing with the subscription's own record — see the scheduled-jobs page. */
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
 * Aggregations — the other scheduled type. On a schedule, one collects another
 * subscription's successful exchanges and creates a single exchange whose payload is a
 * JSON list of links to their files.
 *
 * Its own page rather than a row on Scheduled jobs, because the columns that matter are
 * different ones: what it rolls up and which file it collects, in place of the
 * information type every aggregation shares.
 */
const PAGE_SIZE = 25;

export function AggregationsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const inactiveParam = searchParams.get("inactive");
  const inactive = inactiveParam === "true" ? true : inactiveParam === "false" ? false : null;
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;
  const canOperate = useSessionCan("subscriptions.operate");

  const rows = useQuery({
    queryKey: ["subscription-rows-search", "Aggregation", q, inactive, offset],
    queryFn: () =>
      api.searchSubscriptionRows({ search: q, type: "Aggregation", inactive, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  // The list rows don't carry work group or retry policy; the subscriptions cache does,
  // and it is also where the source subscription's name comes from.
  const setups = useSubscriptionsCache().data ?? [];
  const setupById = useMemo(() => new Map(setups.map((s) => [s.id, s])), [setups]);
  const nameById = useMemo(() => new Map(setups.map((s) => [s.id, s.name])), [setups]);
  const workGroupNames = useWorkGroupNames();
  const retryPolicyNames = useRetryPolicyNames();
  const lastRuns = useQuery({ queryKey: ["last-runs"], queryFn: () => api.listLastRuns() }).data ?? [];
  const lastRunById = useMemo(() => new Map(lastRuns.map((r) => [r.subscriptionId, r])), [lastRuns]);
  const health =
    useQuery({ queryKey: ["schedule-health"], queryFn: () => api.listScheduleHealth() }).data ?? [];
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
        title="Aggregations"
        description="Collect a subscription's exchanges on a schedule into one exchange listing links to their files. The files are not combined — a mapper or delivery does that."
        actions={
          <Can permission="subscriptions.create">
            <Button variant="primary" onClick={() => navigate("/aggregations/new")}>
              <Plus className="size-4" /> New aggregation
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
            placeholder="Search aggregations"
            aria-label="Search aggregations"
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
        <LoadingBlock label="Loading aggregations…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<Layers />} title={q || inactive !== null ? "No aggregations match" : "No aggregations yet"}>
          {q || inactive !== null
            ? "Try a different search or filter."
            : "Create one here, or open the subscription you want summarised and choose “Roll these up”."}
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
              header: "Aggregation",
              headerTitle: "The roll-up job itself. Open it to configure what it delivers.",
              truncate: true,
              cell: (r) => <span className="block truncate font-medium text-ink-900">{r.name}</span>,
            },
            {
              // The whole point of the row: an aggregation with no source name is one
              // whose source was deleted, and it will never produce anything again.
              header: "Rolls up",
              headerTitle: "The subscription whose successful exchanges this collects. Fixed when the aggregation was created.",
              truncate: true,
              cell: (r) => {
                const name = r.aggregationForId === null ? null : nameById.get(r.aggregationForId);
                if (r.aggregationForId === null)
                  return <span className="text-[13px] text-ink-400">Not set</span>;
                return name ? (
                  <Link
                    to={`/subscriptions/${r.aggregationForId}`}
                    onClick={(e) => e.stopPropagation()}
                    className="block truncate text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
                  >
                    {name}
                  </Link>
                ) : (
                  <span className="text-[13px] text-ink-400">—</span>
                );
              },
            },
            {
              header: "Collects",
              headerTitle: "Which file of each collected exchange the roll-up links to. Links only \u2014 the files are not combined.",
              truncate: true,
              cell: (r) => (
                <span className="block truncate text-[13px] text-ink-600">
                  {AGGREGATION_TARGET_LABEL[r.aggregationTarget]}
                </span>
              ),
            },
            {
              header: "Last run",
              headerTitle: "When the roll-up last ran, how long it took, and whether someone triggered it by hand.",
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
              header: "Reliability",
              headerTitle: "How many of the last runs succeeded, from the scheduler\u2019s own history (about 30 days). 3/10 means 3 of the last 10 finished runs worked.",
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
              // The subscription's own AggregateOn, the aggregation counterpart of the
              // ReceiveOn the scheduled-jobs page shows.
              header: "Next run",
              headerTitle: "When the schedule fires next. A dash means no schedule, so it only runs when someone presses Roll up now.",
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
              headerTitle: "Who the roll-up exchange belongs to. Not the partners of the exchanges it collected \u2014 one roll-up can cover many.",
              truncate: true,
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
              header: "Work group",
              headerTitle: "Which queue lane the roll-up runs in \u2014 the difference between idle and queued behind other work.",
              truncate: true,
              cell: (r) => {
                const id = setupById.get(r.id)?.workGroupId ?? null;
                const name = id === null ? null : (workGroupNames.get(id) ?? null);
                if (id === null) return <span className="text-[13px] text-ink-400">Ungrouped</span>;
                return name ? (
                  <Link
                    to={`/work-groups/${id}`}
                    onClick={(e) => e.stopPropagation()}
                    className="block truncate text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
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
              headerTitle: "What happens when the roll-up\u2019s delivery fails. None means a failure is recorded and left alone.",
              truncate: true,
              cell: (r) => {
                const id = setupById.get(r.id)?.retryPolicyId ?? null;
                const name = id === null ? null : (retryPolicyNames.get(id) ?? null);
                if (id === null) return <span className="text-[13px] text-ink-400">None</span>;
                return name ? (
                  <Link
                    to={`/retry-policies/${id}`}
                    onClick={(e) => e.stopPropagation()}
                    className="block truncate text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
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
              headerTitle: "Whether it is turned on, holding work, and executing right now. Hover a badge for what it means.",
              cell: (r) => (
                <span className="inline-flex items-center gap-1">
                  <ScheduleFault health={healthById.get(r.id)} />
                  <SubscriptionStatusBadges enabled={r.enabled} paused={r.paused} />
                  <HealthBadge isRunning={r.isRunning} consecutiveFailures={r.consecutiveFailures} />
                </span>
              ),
            },
            {
              header: "Last error",
              headerTitle: "The most recent failure this aggregation recorded. Open the row for the full run history.",
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
              cell: (r) => canOperate && <AggregateNowButton job={r} />,
            },
          ]}
        />
      )}
    </div>
  );
}
