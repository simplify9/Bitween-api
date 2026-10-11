import { useQuery } from "@tanstack/react-query";
import { Server } from "lucide-react";
import { Link } from "react-router";
import { api, type ClusterNodes } from "../../api";
import { keys } from "../../api/queryKeys";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, EmptyState, InlineNotice, LoadError, LoadingBlock } from "../../components/ui/basics";
import { MiniTable, Table } from "../../components/ui/Table";
import { formatDateTime, timeAgo, formatDurationMs } from "../../lib/dates";
import { compact } from "../../lib/numbers";
import { Panel } from "../../components/ui/Panel";

type Node = ClusterNodes["nodes"][number];

const RUNTIME_LABEL: Record<string, string> = { dotnet: ".NET", python: "Python", node: "Node.js" };

/**
 * The nodes of this Bitween, as each last described itself: which are there, which run data
 * sources, what each can run, and what each holds. A cluster used to be visible only as a data
 * source saying it ran "on no node", with nothing to say why.
 */
export function NodesPage() {
  const nodes = useQuery({ queryKey: keys.settings.nodes, queryFn: () => api.getClusterNodes(), refetchInterval: 15_000 });
  // What each node did in the last day, and its resident adapter instances, from the metrics.
  const metrics = useQuery({ queryKey: keys.metrics.nodes, queryFn: () => api.getNodeMetrics(), refetchInterval: 60_000 });

  const header = (
    <PageHeader
      title="Nodes"
      description="Every Bitween node writes in every 30 seconds. One that stops is shown as gone after 90 seconds, and forgotten after a week."
    />
  );
  if (nodes.isPending) return <div>{header}<LoadingBlock label="Asking the nodes…" /></div>;
  if (nodes.isError)
    return (
      <div>
        {header}
        <LoadError error={nodes.error} what="the nodes" onRetry={() => void nodes.refetch()} />
      </div>
    );

  const all = nodes.data.nodes;
  const online = all.filter((n) => n.online);
  const versions = [...new Set(online.map((n) => n.version))];
  const runsDataSources = online.some((n) => n.dataSources);
  const byNode = new Map((metrics.data?.nodes ?? []).map((m) => [m.id, m]));
  // A node that fails clearly more than the other nodes do is the one to look at first. Measured
  // against the others, not the whole cluster: with two nodes, the bad one lifts the average.
  const all24 = metrics.data?.nodes ?? [];
  const standsOut = (m: { id: string; exchanges: number; failed: number } | undefined) => {
    if (!m || all24.length < 2 || m.exchanges < 20) return false;
    const others = all24.filter((o) => o.id !== m.id);
    const total = others.reduce((n, o) => n + o.exchanges, 0);
    const otherRate = total ? others.reduce((n, o) => n + o.failed, 0) / total : 0;
    return m.failed / m.exchanges > Math.max(0.05, 2 * otherRate);
  };
  const resident = (metrics.data?.nodes ?? []).flatMap((m) => m.resident.map((r) => ({ node: m.id, ...r })));

  return (
    <div>
      {header}

      {versions.length > 1 && (
        <InlineNotice>
          The nodes run different versions ({versions.join(", ")}). That is expected during an upgrade, not after one.
        </InlineNotice>
      )}
      {online.length > 0 && !runsDataSources && (
        <InlineNotice>
          No node runs data sources, so brokers and databases connect nowhere. Turn on Bitween:BusProvidersEnabled on
          at least one node. See <Link to="/data-sources" className="underline">Data sources</Link>.
        </InlineNotice>
      )}

      {all.length === 0 ? (
        <EmptyState icon={<Server />} title="No node has written in yet">
          Nodes write in when they start, on a version of Bitween that does. Check again in a minute.
        </EmptyState>
      ) : (
        <Table<Node>
          rows={all}
          rowKey={(n) => n.name}
          minWidth="min-w-200"
          columns={[
            {
              header: "Node",
              cell: (n) => (
                <span>
                  <span className="font-medium text-ink-900">{n.host}</span>
                  <code className="block font-mono text-[11.5px] text-ink-500">{n.name}</code>
                  {n.name === nodes.data.answeredBy && <span className="text-[11.5px] text-ink-500">This page came from this node</span>}
                </span>
              ),
            },
            {
              header: "Status",
              cell: (n) =>
                n.online ? (
                  <span>
                    <Badge tone="ok">Online</Badge>
                    <span className="block text-[11.5px] text-ink-500">since {formatDateTime(n.startedOn)}</span>
                  </span>
                ) : (
                  <span>
                    <Badge tone="danger">Gone</Badge>
                    <span className="block text-[11.5px] text-ink-500">last seen {timeAgo(n.lastSeenOn)}</span>
                  </span>
                ),
            },
            { header: "Version", cell: (n) => n.version },
            {
              header: "Data sources",
              cell: (n) =>
                n.dataSources ? (
                  <span>
                    <Badge tone="ok">Runs them</Badge>
                    {n.holdsDataSources.length > 0 && (
                      <span className="mt-0.5 block text-[12px] text-ink-600">
                        Holds{" "}
                        {n.holdsDataSources.map((d, i) => (
                          <span key={d.id}>
                            {i > 0 && ", "}
                            <Link to={`/data-sources/${d.id}`} className="text-crimson-700 hover:underline">
                              {d.name}
                            </Link>
                          </span>
                        ))}
                      </span>
                    )}
                  </span>
                ) : (
                  <span className="text-ink-500">No</span>
                ),
            },
            {
              header: "Adapter runtimes",
              cell: (n) =>
                n.runtimes.length ? (
                  <span className="flex flex-wrap gap-1">
                    {n.runtimes.map((r) => (
                      <Badge key={r} tone="neutral">
                        {RUNTIME_LABEL[r] ?? r}
                      </Badge>
                    ))}
                  </span>
                ) : (
                  <span className="text-ink-500">None found</span>
                ),
            },
            {
              header: "Last 24 hours",
              headerTitle: "Exchanges this node finished, and the adapter calls it made, with how they failed.",
              cell: (n) => {
                const m = byNode.get(n.name);
                if (!m || (m.exchanges === 0 && m.adapterCalls === 0)) return <span className="text-ink-500">-</span>;
                return (
                  <span className="block text-[12px] text-ink-700">
                    <span className="block tabular-nums">
                      {compact(m.exchanges)} exchanges
                      {m.failed > 0 && <span className="text-danger-700"> · {compact(m.failed)} failed</span>}
                      {m.p95Ms !== null && <span className="text-ink-500"> · p95 {formatDurationMs(m.p95Ms)}</span>}
                    </span>
                    <span className="block text-ink-500 tabular-nums">
                      {compact(m.adapterCalls)} adapter calls
                      {m.timeouts > 0 && ` · ${compact(m.timeouts)} timed out`}
                      {m.limitStops > 0 && ` · ${compact(m.limitStops)} over a limit`}
                      {m.crashes > 0 && ` · ${compact(m.crashes)} crashed`}
                    </span>
                    {standsOut(m) && <Badge tone="warn" className="mt-0.5">Fails more than the others</Badge>}
                  </span>
                );
              },
            },
            {
              header: "Leases",
              cell: (n) =>
                n.leases.length ? (
                  <span className="text-[12px] text-ink-700">
                    {n.leases.map((l) => (
                      <span key={l.resource} className="block" title={`Term ${l.term}, since ${formatDateTime(l.acquiredOn)}`}>
                        {l.resource}
                      </span>
                    ))}
                  </span>
                ) : (
                  <span className="text-ink-500">-</span>
                ),
            },
          ]}
        />
      )}

      {resident.length > 0 && (
        <Panel
          title="Resident adapter instances"
          description="Long-lived adapter processes, as each node last described them: what they do, what they use, and how often they have been restarted."
          className="mt-4"
        >
          <MiniTable
            rows={resident}
            rowKey={(r) => `${r.node}-${r.a}-${r.pid ?? ""}`}
            empty="None."
            columns={[
              { header: "Adapter", cell: (r) => <code className="font-mono text-[12px] text-ink-800">{r.a}</code> },
              { header: "Node", cell: (r) => <code className="font-mono text-[11.5px] text-ink-500">{r.node}</code> },
              {
                header: "State",
                cell: (r) => (
                  <span className="flex flex-wrap items-center gap-1">
                    <Badge tone={r.q ? "danger" : r.s === "Ready" || r.s === "Busy" ? "ok" : "neutral"}>{r.q ? "Quarantined" : r.s}</Badge>
                    {r.e && <span className="max-w-60 truncate text-[11.5px] text-danger-700" title={r.e}>{r.e}</span>}
                  </span>
                ),
              },
              { header: "Memory", align: "right", cell: (r) => <span className="tabular-nums">{r.mb} MB</span> },
              { header: "CPU", align: "right", cell: (r) => <span className="tabular-nums">{r.cpu}%</span> },
              { header: "Up", align: "right", cell: (r) => <span className="tabular-nums">{formatDurationMs(r.up * 1000)}</span> },
              {
                header: "Restarts",
                align: "right",
                cell: (r) => <span className={`tabular-nums ${r.r > 0 ? "text-warn-700" : ""}`}>{r.r}</span>,
              },
              { header: "In flight", align: "right", cell: (r) => <span className="tabular-nums">{r.f}</span> },
            ]}
          />
        </Panel>
      )}
    </div>
  );
}
