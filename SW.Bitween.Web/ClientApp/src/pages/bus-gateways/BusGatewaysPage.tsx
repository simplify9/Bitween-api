import { useMemo } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Cable, Network, Plus, Search } from "lucide-react";
import { api, type BusGatewayRow, type SubscriptionRow } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Select } from "../../components/ui/forms";
import { Pagination } from "../../components/ui/Pagination";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { Table } from "../../components/ui/Table";
import {
  LinkListCell,
  WiredHealthBadge,
  useSubscriptionRowsById,
} from "../../components/config/shared";
import { matchSummary } from "../../lib/match";

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
  const [searchParams, setSearchParams] = useSearchParams();
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
    queryKey: ["bus-gateways-search", q, informationTypeId, inactive, offset],
    queryFn: () => api.searchBusGateways({ search: q, informationTypeId, inactive, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const subscriptionsById = useSubscriptionRowsById();
  const infoTypes =
    useQuery({
      queryKey: ["information-types"],
      queryFn: () => api.listInformationTypes(),
      enabled: canSeeInfoTypes,
    }).data ?? [];
  const infoTypeById = useMemo(() => new Map(infoTypes.map((t) => [t.id, t])), [infoTypes]);

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

  const rows = gateways.data?.result ?? [];
  const total = gateways.data?.total ?? 0;

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
            <Can permission="bus-gateways.create">
              <Button variant="primary" onClick={() => navigate("/bus-gateways/new")}>
                <Plus className="size-4" /> New bus gateway
              </Button>
            </Can>
          </>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative w-full max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
          <input
            type="search"
            value={q}
            onChange={(e) => setParam("q", e.target.value || null)}
            placeholder="Search gateways"
            aria-label="Search bus gateways"
            className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
          />
        </div>
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
      ) : rows.length === 0 ? (
        <EmptyState
          icon={<Cable />}
          title={q || informationTypeId || inactive !== null ? "No gateways match" : "No bus gateways yet"}
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
              onOffsetChange={(o) => setParam("offset", String(o), false)}
            />
          }
          columns={[
            {
              header: "Gateway",
              truncate: true,
              cell: (g) => (
                <span className="flex min-w-0 items-center gap-1.5">
                  <span className={`truncate font-medium ${g.inactive ? "text-ink-400" : "text-ink-900"}`}>
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
                    truncate: true,
                    cell: (g: BusGatewayRow) => {
                      const t = infoTypeById.get(g.informationTypeId);
                      return t?.busMessageTypeName ? (
                        <code
                          className="block truncate font-mono text-xs text-ink-600"
                          title={t.busMessageTypeName}
                        >
                          {t.busMessageTypeName}
                        </code>
                      ) : (
                        <span className="text-ink-400">—</span>
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
                        <span className="text-[13px] text-ink-400">Nothing promoted</span>
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
              truncate: true,
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
              // No Partners column here, unlike the API gateway list: a route's
              // partner is optional, so a gateway of unattributed routes would
              // show an empty column and say nothing about what it does. The
              // route's target is the fact that always exists.
              header: "Health",
              cell: (g) => {
                // Nothing can reach this gateway at all if its type was taken off
                // the bus — no queue is declared for it. That outranks anything
                // the routes have to say.
                const t = infoTypeById.get(g.informationTypeId);
                if (t && !t.busEnabled) return <Badge tone="danger">Type not on bus</Badge>;
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
