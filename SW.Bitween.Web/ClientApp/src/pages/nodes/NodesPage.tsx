import { useQuery } from "@tanstack/react-query";
import { Server } from "lucide-react";
import { Link } from "react-router";
import { api, type ClusterNodes } from "../../api";
import { keys } from "../../api/queryKeys";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, EmptyState, InlineNotice, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Table } from "../../components/ui/Table";
import { formatDateTime, timeAgo } from "../../lib/dates";

type Node = ClusterNodes["nodes"][number];

const RUNTIME_LABEL: Record<string, string> = { dotnet: ".NET", python: "Python", node: "Node.js" };

/**
 * The nodes of this Bitween, as each last described itself: which are there, which run data
 * sources, what each can run, and what each holds. A cluster used to be visible only as a data
 * source saying it ran "on no node", with nothing to say why.
 */
export function NodesPage() {
  const nodes = useQuery({ queryKey: keys.settings.nodes, queryFn: () => api.getClusterNodes(), refetchInterval: 15_000 });

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
                  <span className="text-ink-500">—</span>
                ),
            },
          ]}
        />
      )}
    </div>
  );
}
