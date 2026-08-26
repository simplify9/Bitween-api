import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ArrowUpRight, Layers, Plus, Search } from "lucide-react";
import { api, type QueueHealthSnapshot, type WorkGroupRow } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { WorkGroupDialog } from "../../components/config/WorkGroupDialog";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Pagination } from "../../components/ui/Pagination";
import { Table, type Column } from "../../components/ui/Table";
import { UsedByCell, queueHealthTitle, useSubscriptionsCache } from "../../components/config/shared";

/**
 * The live RabbitMQ numbers, as columns rather than a per-row drill-down.
 * One shared `queue-health` query feeds every row — the same cache entry the
 * Queue health page uses, so this costs one poll, not one per group.
 */
function liveColumns(snapshot: QueueHealthSnapshot | undefined): Column<WorkGroupRow>[] {
  const consumerFor = (g: WorkGroupRow) => snapshot?.consumers.find((c) => c.workGroupId === g.id);
  const num = (get: (g: WorkGroupRow) => number | undefined) => (g: WorkGroupRow) => {
    const v = get(g);
    return <span className="tabular-nums text-ink-700">{v ?? "—"}</span>;
  };
  return [
    {
      header: "Health",
      cell: (g) => {
        const c = consumerFor(g);
        if (!c) return <span className="text-ink-400">—</span>;
        return c.health === "critical" ? (
          <Badge tone="danger" title={queueHealthTitle("critical")}>Critical</Badge>
        ) : c.health === "warning" ? (
          <Badge tone="warn" title={queueHealthTitle("warning")}>Warning</Badge>
        ) : (
          <Badge tone="ok" title={queueHealthTitle("healthy")}>Healthy</Badge>
        );
      },
    },
    { header: "Nodes", align: "right", cell: num((g) => consumerFor(g)?.totalNodes) },
    { header: "In flight", align: "right", cell: num((g) => consumerFor(g)?.processingCount) },
    { header: "Queued", align: "right", cell: num((g) => consumerFor(g)?.queueCount) },
    { header: "Retrying", align: "right", cell: num((g) => consumerFor(g)?.retryCount) },
    { header: "Dead", align: "right", cell: num((g) => consumerFor(g)?.failedCount) },
    { header: "Prefetch", align: "right", cell: num((g) => consumerFor(g)?.prefetch) },
    { header: "Priority", align: "right", cell: num((g) => consumerFor(g)?.priority) },
  ];
}

const PAGE_SIZE = 25;

export function WorkGroupsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const q = searchParams.get("q") ?? "";
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;
  const canMonitor = useSessionCan("monitoring.view");

  const groups = useQuery({
    queryKey: ["work-groups-search", q, offset],
    queryFn: () => api.searchWorkGroups({ search: q, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const subscriptions = useSubscriptionsCache().data ?? [];
  const live = useQuery({
    queryKey: ["queue-health"],
    queryFn: () => api.getQueueHealth(),
    refetchInterval: 5_000,
    placeholderData: keepPreviousData,
    enabled: canMonitor,
  });

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

  const filtered = groups.data?.result ?? [];
  const total = groups.data?.total ?? 0;

  return (
    <div>
      <PageHeader
        title="Work groups"
        description="Give a set of subscriptions their own queue, priority and prefetch, separate from the default lane."
        help={{
          title: "How work groups work",
          body: (
            <>
              <p>
                Every subscription runs in the default (ungrouped) lane unless assigned to a work
                group. Groups get their own RabbitMQ queue — <strong>prefetch</strong> controls how
                many messages a consumer pulls at once, <strong>priority</strong> decides which
                group's queue is drained first when several are busy.
              </p>
              <p>Changes to a group's settings apply live — no restart needed.</p>
            </>
          ),
        }}
        actions={
          <Can permission="workgroups.create">
            <Button variant="primary" onClick={() => setCreating(true)}>
              <Plus className="size-4" /> New work group
            </Button>
          </Can>
        }
      />

      <div className="relative mb-4 max-w-xs">
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
        <input
          type="search"
          value={q}
          onChange={(e) => setParam("q", e.target.value || null)}
          placeholder="Search work groups"
          aria-label="Search work groups"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {groups.isPending ? (
        <LoadingBlock label="Loading work groups…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<Layers />} title={q ? "No work groups match" : "No work groups yet"}>
          {q ? "Try a different search." : "Create one to give a set of subscriptions their own queue."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(g) => g.id}
          minWidth="min-w-220"
          onRowClick={(g) => navigate(`/work-groups/${g.id}`)}
          footer={
            <Pagination
              offset={offset}
              limit={PAGE_SIZE}
              total={total}
              onOffsetChange={(o) => setParam("offset", String(o), false)}
            />
          }
          columns={[
            { header: "Name", cell: (g) => <span className="font-medium text-ink-900">{g.name}</span> },
            {
              header: "Bus message name",
              cell: (g) => <code className="font-mono text-xs text-ink-600">{g.busMessageName}</code>,
            },
            {
              header: "Used by",
              truncate: true,
              cell: (g) => <UsedByCell items={subscriptions.filter((s) => s.workGroupId === g.id)} />,
            },
            ...(canMonitor ? liveColumns(live.data) : []),
            {
              header: "",
              align: "right",
              className: "w-10",
              cell: (g) => (
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    navigate(`/work-groups/${g.id}`);
                  }}
                  aria-label={`Open ${g.name}`}
                  title="Open"
                  className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                >
                  <ArrowUpRight className="size-4" />
                </button>
              ),
            },
          ]}
        />
      )}

      {creating && (
        <WorkGroupDialog
          groupId={null}
          onClose={() => setCreating(false)}
          onSaved={(id) => navigate(`/work-groups/${id}`)}
        />
      )}
    </div>
  );
}
