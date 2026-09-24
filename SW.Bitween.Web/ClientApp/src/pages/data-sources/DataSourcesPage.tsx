import { useNavigate, useSearchParams } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ArrowUpRight, Database, Plus, Search } from "lucide-react";
import { api, type DataSourceRow } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Pagination } from "../../components/ui/Pagination";
import { Table } from "../../components/ui/Table";
import { keys } from "../../api/queryKeys";
import { ConnectionBadge } from "./ConnectionBadge";
import { providerOf, useDataSourceProviders } from "./providers";

const PAGE_SIZE = 25;

/**
 * Connections to brokers outside Bitween.
 *
 * The health column is the reason this is a page rather than a dropdown on the gateway: a broker
 * that has gone away is invisible everywhere else, and the node that holds each connection is the
 * only place the exclusivity of a broker connection becomes visible at all.
 */
export function DataSourcesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;

  const providers = useDataSourceProviders();
  const sources = useQuery({
    queryKey: keys.dataSources.search({ q, offset }),
    queryFn: () => api.searchDataSources({ search: q, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
    // Health is written back by the supervisor on its own loop, so this is worth refreshing
    // while someone watches a connection come up.
    refetchInterval: 10_000,
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

  const rows = sources.data?.result ?? [];
  const total = sources.data?.total ?? 0;

  return (
    <div>
      <PageHeader
        title="Data sources"
        description="Connections to brokers and databases outside Bitween. A bus gateway reads from a broker instead of Bitween's own internal bus; a subscription runs its SQL on a database."
        help={{
          title: "How data sources work",
          body: (
            <>
              <p>
                A data source holds a <strong>connection</strong> and nothing else. What Bitween
                does with the messages belongs to the bus gateway pointing at it, which is why one
                data source can feed many gateways — the same way one broker connection serves many
                queues.
              </p>
              <p>
                A resident adapter holds the connection open and hands each message over. Nothing is
                acknowledged to the broker until Bitween has persisted it, so a crash means
                redelivery rather than a lost message — and every message carries a deduplication
                key so the redelivery does not become a second exchange.
              </p>
              <p>
                A broker connection is <strong>exclusive</strong>: exactly one node may hold it.
                Ownership is granted by a lease with a database-issued fencing term, so a node that
                was paused while ownership moved discovers it and stops.
              </p>
            </>
          ),
        }}
        actions={
          <Can permission="data-sources.create">
            <Button variant="primary" onClick={() => navigate("/data-sources/new")}>
              <Plus className="size-4" /> New data source
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
          placeholder="Search data sources"
          aria-label="Search data sources"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {sources.isPending ? (
        <LoadingBlock label="Loading data sources…" />
      ) : rows.length === 0 ? (
        <EmptyState icon={<Database />} title={q ? "No data sources match" : "No data sources yet"}>
          {q
            ? "Try a different search."
            : "A bus gateway with no data source reads Bitween's own internal bus. Add one here only to read a broker outside Bitween."}
        </EmptyState>
      ) : (
        <Table
          rows={rows}
          rowKey={(d) => d.id}
          minWidth="min-w-200"
          onRowClick={(d) => navigate(`/data-sources/${d.id}`)}
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
              header: "Name",
              cell: (d: DataSourceRow) => (
                <span className={d.inactive ? "text-ink-400 line-through" : "font-medium text-ink-900"}>
                  {d.name}
                </span>
              ),
            },
            {
              header: "Provider",
              cell: (d: DataSourceRow) => (
                <span className="text-ink-700">{providerOf(providers.data, d.adapterId)?.label ?? d.adapterId}</span>
              ),
            },
            {
              header: "Kind",
              headerTitle: "Broker (read by bus gateways) or database (used by subscriptions).",
              cell: (d: DataSourceRow) => (
                <span className="text-ink-700">{d.kind === "Relational" ? "Database" : d.kind}</span>
              ),
            },
            {
              header: "Connection",
              wrap: true,
              cell: (d: DataSourceRow) => (
                <div className="flex flex-col gap-1">
                  <ConnectionBadge state={d.lastKnownState} failures={d.consecutiveFailures} />
                  {d.lastException && (
                    <span className="max-w-80 truncate text-[11px] text-danger-700" title={d.lastException}>
                      {d.lastException}
                    </span>
                  )}
                </div>
              ),
            },
            {
              // A broker connection is exclusive, so which node holds it is not a detail —
              // it is the answer to "why is this one quiet?".
              header: "Held by",
              cell: (d: DataSourceRow) => (
                <span className="text-xs text-ink-600">{d.ownedByNode ?? "—"}</span>
              ),
            },
            {
              // A broker feeds bus gateways; a database is used by subscriptions. Counting the
              // wrong one would show every database as unused.
              header: "Used by",
              headerTitle: "Bus gateways reading a broker, or subscriptions running on a database. Deleting is refused while any remain.",
              align: "right",
              cell: (d: DataSourceRow) => (
                <span className="tabular-nums text-ink-700">
                  {d.kind === "Relational"
                    ? `${d.subscriptionCount} subscription${d.subscriptionCount === 1 ? "" : "s"}`
                    : `${d.gatewayCount} gateway${d.gatewayCount === 1 ? "" : "s"}`}
                </span>
              ),
            },
            {
              header: "",
              align: "right",
              cell: () => <ArrowUpRight className="size-4 text-ink-300" />,
            },
          ]}
        />
      )}
    </div>
  );
}
