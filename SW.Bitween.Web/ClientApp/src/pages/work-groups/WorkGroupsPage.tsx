import { useState } from "react";
import { useNavigate } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ArrowUpRight, Layers, Plus } from "lucide-react";
import { api, type QueueHealthSnapshot, type WorkGroupRow } from "../../api";
import { Can } from "../../auth/guards";
import { useSessionCan } from "../../auth/useSessionCan";
import { WorkGroupDialog } from "../../components/config/WorkGroupDialog";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, InlineNotice, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Pagination } from "../../components/ui/Pagination";
import { Table, type Column } from "../../components/ui/Table";
import { UsedByCell } from "../../components/config/shared";
import { queueHealthTitle } from "../../components/config/subscriptionLabels";
import { useSubscriptionsCache } from "../../components/config/lookups";
import { keys } from "../../api/queryKeys";
import { useRabbitMqManagementConfigured } from "../../lib/appConfig";
import { workGroupQueueName } from "../../lib/busMessageName";
import { useListParams } from "../../lib/listParams";
import { SearchBox } from "../../components/ui/SearchBox";

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
        if (!c) return <span className="text-ink-500">—</span>;
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
  const { params: searchParams, set: setParam, searchText, setSearchText } = useListParams();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const q = searchParams.get("q") ?? "";
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;
  const canMonitor = useSessionCan("monitoring.view");
  const rabbitMqConfigured = useRabbitMqManagementConfigured();

  const groups = useQuery({
    queryKey: keys.workGroups.search({ q, offset }),
    queryFn: () => api.searchWorkGroups({ search: q, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const subscriptions = useSubscriptionsCache().data ?? [];
  const live = useQuery({
    queryKey: keys.queueHealth,
    queryFn: () => api.getQueueHealth(),
    refetchInterval: 5_000,
    placeholderData: keepPreviousData,
    enabled: canMonitor && rabbitMqConfigured,
  });


  const filtered = groups.data?.result ?? [];
  const total = groups.data?.total ?? 0;

  // In the header, and in the empty list where there is nothing else to do.
  const createAction = (
    <Can permission="workgroups.create">
      <Button variant="primary" onClick={() => setCreating(true)}>
        <Plus className="size-4" /> New work group
      </Button>
    </Can>
  );

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
        actions={createAction}
      />

      <SearchBox value={searchText} onChange={setSearchText} label="Search work groups" className="mb-4 max-w-xs" />

      {canMonitor && !rabbitMqConfigured && (
        <InlineNotice>
          Live queue stats (Health, Nodes, Queued, …) need RabbitMQ management configured on the
          backend — those columns will stay blank until then.
        </InlineNotice>
      )}

      {groups.isPending ? (
        <LoadingBlock label="Loading work groups…" />
      ) : groups.isError ? (
        <LoadError error={groups.error} what="work groups" onRetry={() => void groups.refetch()} />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<Layers />} title={q ? "No work groups match" : "No work groups yet"} action={q ? undefined : createAction}>
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
              onOffsetChange={(o) => setParam("offset", String(o))}
            />
          }
          columns={[
            { header: "Name", cell: (g) => <span className="font-medium text-ink-900">{g.name}</span> },
            {
              // The queue rather than the bare bus message name: nothing stops two groups
              // sharing both a name and a bus message name, and then neither column tells
              // them apart. The queue carries the id, so it always does — and it is what
              // these rows are called in RabbitMQ.
              header: "Queue",
              headerTitle: "The group's queue in RabbitMQ — its id followed by its bus message name.",
              cell: (g) => <code className="font-mono text-xs text-ink-600">{workGroupQueueName(g)}</code>,
            },
            {
              header: "Used by",
              wrap: true,
              cell: (g) => <UsedByCell items={subscriptions.filter((s) => s.workGroupId === g.id)} />,
            },
            // `enabled: false` on the `live` query only stops it refetching — it doesn't clear a
            // result already cached from before RabbitMQ management was disabled. Pass undefined
            // explicitly so the columns actually go blank rather than showing stale numbers.
            ...(canMonitor ? liveColumns(rabbitMqConfigured ? live.data : undefined) : []),
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
                  className="rounded-md p-1.5 text-ink-500 hover:bg-ink-100 hover:text-ink-700"
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
