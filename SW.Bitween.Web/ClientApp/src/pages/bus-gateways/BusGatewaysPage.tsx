import { useMemo } from "react";
import { Link, useNavigate } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Cable, Network, Plus } from "lucide-react";
import { api, type BusGatewayRow, type SubscriptionRow } from "../../api";
import { Can } from "../../auth/guards";
import { useSessionCan } from "../../auth/useSessionCan";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Select } from "../../components/ui/forms";
import { Pagination } from "../../components/ui/Pagination";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { Table } from "../../components/ui/Table";
import { LinkListCell, WiredHealthBadge } from "../../components/config/shared";
import { useSubscriptionRowsById } from "../../components/config/lookups";
import { matchSummary } from "../../lib/match";
import { keys } from "../../api/queryKeys";
import { ConnectionBadge } from "../data-sources/ConnectionBadge";
import { useListParams } from "../../lib/listParams";
import { NONE } from "../../lib/none";
import { SearchBox } from "../../components/ui/SearchBox";

/**
 * Bus gateways — messages picked off the bus. A gateway listens for one
 * information type; its routes decide which subscription handles which message.
 */
const PAGE_SIZE = 25;

const STATUS_OPTIONS = [
  { value: "", label: "Any status" },
  { value: "false", label: "Active" },
  { value: "true", label: "Deactivated" },
];

export function BusGatewaysPage() {
  const { params: searchParams, set: setParam, searchText, setSearchText } = useListParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const informationTypeId = searchParams.get("informationTypeId")
    ? Number(searchParams.get("informationTypeId"))
    : null;
  const inactiveParam = searchParams.get("inactive");
  const inactive = inactiveParam === "true" ? true : inactiveParam === "false" ? false : null;
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;
  const canSeeInfoTypes = useSessionCan("documents.view");

  const gateways = useQuery({
    queryKey: keys.busGateways.search({ q, informationTypeId, inactive, offset }),
    queryFn: () => api.searchBusGateways({ search: q, informationTypeId, inactive, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const subscriptionsById = useSubscriptionRowsById();
  const infoTypes =
    useQuery({
      queryKey: keys.informationTypes.list,
      queryFn: () => api.listInformationTypes(),
      enabled: canSeeInfoTypes,
    }).data ?? NONE;
  const infoTypeById = useMemo(() => new Map(infoTypes.map((t) => [t.id, t])), [infoTypes]);


  const rows = gateways.data?.result ?? [];
  const total = gateways.data?.total ?? 0;

  // In the header, and in the empty list where there is nothing else to do.
  const createAction = (
    <Can permission="bus-gateways.create">
      <Button variant="primary" onClick={() => navigate("/bus-gateways/new")}>
        <Plus className="size-4" /> New bus gateway
      </Button>
    </Can>
  );

  return (
    <div>
      <PageHeader
        title="Bus gateways"
        description="Listeners that pick messages off the bus and route them to a subscription."
        actions={
          <>
            {/* Offered here because this is where someone asking "what feeds what?"
                is already standing — the map is one level above this table. */}
            <Button variant="secondary" onClick={() => navigate("/flow")}>
              <Network className="size-4" /> Flow map
            </Button>
            {createAction}
          </>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <SearchBox value={searchText} onChange={setSearchText} label="Search bus gateways" placeholder="Search gateways" />
        {canSeeInfoTypes && (
          <div className="w-56">
            <SearchSelect
              aria-label="Filter by information type"
              size="sm"
              clearLabel="Any information type"
              value={informationTypeId?.toString() ?? ""}
              onChange={(v) => setParam("informationTypeId", v || null)}
              options={infoTypes.map((t) => ({ value: String(t.id), label: t.name, code: t.code }))}
            />
          </div>
        )}
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

      {gateways.isPending ? (
        <LoadingBlock label="Loading bus gateways…" />
      ) : gateways.isError ? (
        <LoadError error={gateways.error} what="bus gateways" onRetry={() => void gateways.refetch()} />
      ) : rows.length === 0 ? (
        <EmptyState
          icon={<Cable />}
          title={q || informationTypeId || inactive !== null ? "No gateways match" : "No bus gateways yet"} action={q || informationTypeId || inactive !== null ? undefined : createAction}
        >
          {q || informationTypeId || inactive !== null
            ? "Try a different search or filter."
            : "Create a gateway to start handling messages from the bus."}
        </EmptyState>
      ) : (
        <Table
          rows={rows}
          rowKey={(g) => g.id}
          minWidth="min-w-200"
          onRowClick={(g) => navigate(`/bus-gateways/${g.id}`)}
          footer={
            <Pagination
              offset={offset}
              limit={PAGE_SIZE}
              total={total}
              onOffsetChange={(o) => setParam("offset", String(o))}
            />
          }
          columns={[
            {
              header: "Gateway",
              wrap: true,
              cell: (g) => (
                <span className="flex flex-wrap items-center gap-1.5">
                  <span className={`font-medium ${g.inactive ? "text-ink-500" : "text-ink-900"}`}>
                    {g.name}
                  </span>
                  {/* Beside the name, not in the Health column: health reports on what the
                      gateway feeds, and a deactivated one feeds nothing, so it would read
                      as healthy. */}
                  {g.inactive && <Badge tone="warn">Off</Badge>}
                </span>
              ),
            },
            {
              header: "Listens for",
              cell: (g) =>
                canSeeInfoTypes ? (
                  <Link
                    to={`/information-types/${g.informationTypeId}`}
                    onClick={(e) => e.stopPropagation()}
                    className="font-mono text-xs text-ink-600 hover:text-crimson-700 hover:underline"
                  >
                    {g.informationTypeCode}
                  </Link>
                ) : (
                  <code className="font-mono text-xs text-ink-600">{g.informationTypeCode}</code>
                ),
            },
            // A bus gateway listens for exactly one information type, so unlike
            // the API gateway list these CAN be columns — one row, one value, no
            // pairing to lose.
            ...(canSeeInfoTypes
              ? [
                  {
                    // The name on the wire, which is what the inbound queue is
                    // built from (`…busservice.<name>`) — the fact you need when
                    // a publisher says it sent something and nothing arrived.
                    header: "Message type",
                    wrap: true,
                    cell: (g: BusGatewayRow) => {
                      const t = infoTypeById.get(g.informationTypeId);
                      return t?.busMessageTypeName ? (
                        <code
                          className="block font-mono text-xs text-ink-600"
                          title={t.busMessageTypeName}
                        >
                          {t.busMessageTypeName}
                        </code>
                      ) : (
                        <span className="text-ink-500">—</span>
                      );
                    },
                  },
                  {
                    // Routes match on promoted properties, so this is the set of
                    // keys a route on this gateway can actually filter by.
                    header: "Matchable on",
                    truncate: true,
                    cell: (g: BusGatewayRow) => {
                      const keys = (infoTypeById.get(g.informationTypeId)?.promotedProperties ?? []).map(
                        (p) => p.key,
                      );
                      return keys.length === 0 ? (
                        <span className="text-[13px] text-ink-500">Nothing promoted</span>
                      ) : (
                        <span
                          className="block truncate font-mono text-[11px] text-ink-600"
                          title={keys.join(", ")}
                        >
                          {keys.join(", ")}
                        </span>
                      );
                    },
                  },
                ]
              : []),
            {
              header: "Routes to",
              wrap: true,
              cell: (g) => (
                <LinkListCell
                  label="subscriptions"
                  items={g.routes.map((r) => ({
                    key: r.id,
                    name: r.subscriptionName,
                    href: `/subscriptions/${r.subscriptionId}`,
                    note: <Badge>{matchSummary(r.matchExpression)}</Badge>,
                  }))}
                />
              ),
            },
            {
              // Which bus this gateway actually reads. Worth a column: an operator should be able
              // to see which of their gateways reach outside Bitween without opening each one.
              header: "Source",
              wrap: true,
              cell: (g) =>
                g.dataSourceId == null ? (
                  <span className="text-ink-500">Internal bus</span>
                ) : (
                  <div className="flex flex-col gap-0.5">
                    <span className="font-medium text-ink-800">{g.dataSourceName}</span>
                    <code className="font-mono text-[11px] text-ink-500">{g.endpoint}</code>
                    <ConnectionBadge state={g.dataSourceState} />
                  </div>
                ),
            },
            {
              // No Partners column here, unlike the API gateway list: a route's
              // partner is optional, so a gateway of unattributed routes would
              // show an empty column and say nothing about what it does. The
              // route's target is the fact that always exists.
              header: "Health",
              cell: (g) => {
                // Nothing can reach this gateway at all if its type was taken off
                // the bus — no queue is declared for it. That outranks anything
                // the routes have to say.
                //
                // Only for a gateway on the INTERNAL bus, though: an external one is fed by its
                // data source's adapter and never touches Bitween's own bus, so the information
                // type's bus setting says nothing about whether messages arrive.
                const t = infoTypeById.get(g.informationTypeId);
                if (g.dataSourceId == null && t && !t.busEnabled)
                  return <Badge tone="danger">Type not on bus</Badge>;
                return (
                  <WiredHealthBadge
                    empty="No routes"
                    rows={g.routes
                      .map((r) => subscriptionsById.get(r.subscriptionId))
                      .filter((r): r is SubscriptionRow => Boolean(r))}
                  />
                );
              },
            },
          ]}
        />
      )}
    </div>
  );
}
