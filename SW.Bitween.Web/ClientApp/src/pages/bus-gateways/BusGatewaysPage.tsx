import { useMemo } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Cable, Plus, Search } from "lucide-react";
import { api, type BusGatewayRow, type IntegrationRow } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Table } from "../../components/ui/Table";
import {
  LinkListCell,
  WiredHealthBadge,
  useIntegrationRowsById,
} from "../../components/config/shared";
import { matchSummary } from "../../lib/match";

/**
 * Bus gateways — messages picked off the bus. A gateway listens for one
 * information type; its routes decide which integration handles which message.
 */
export function BusGatewaysPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const canSeeInfoTypes = useSessionCan("documents.view");

  const gateways = useQuery({ queryKey: ["bus-gateways"], queryFn: () => api.listBusGateways() });
  const integrationsById = useIntegrationRowsById();
  const infoTypes =
    useQuery({
      queryKey: ["information-types"],
      queryFn: () => api.listInformationTypes(),
      enabled: canSeeInfoTypes,
    }).data ?? [];
  const infoTypeById = useMemo(() => new Map(infoTypes.map((t) => [t.id, t])), [infoTypes]);

  const setQ = (value: string) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set("q", value);
        else next.delete("q");
        return next;
      },
      { replace: true },
    );

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return (gateways.data ?? []).filter(
      (g) =>
        !needle ||
        g.name.toLowerCase().includes(needle) ||
        g.informationTypeCode.toLowerCase().includes(needle),
    );
  }, [gateways.data, q]);

  return (
    <div>
      <PageHeader
        title="Bus gateways"
        description="Listeners that pick messages off the bus and route them to an integration."
        actions={
          <Can permission="bus-gateways.create">
            <Button variant="primary" onClick={() => navigate("/bus-gateways/new")}>
              <Plus className="size-4" /> New bus gateway
            </Button>
          </Can>
        }
      />

      <div className="relative mb-4 max-w-xs">
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
        <input
          type="search"
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder="Search gateways"
          aria-label="Search bus gateways"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {gateways.isPending ? (
        <LoadingBlock label="Loading bus gateways…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<Cable />} title={q ? "No gateways match" : "No bus gateways yet"}>
          {q ? "Try a different search." : "Create a gateway to start handling messages from the bus."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(g) => g.id}
          minWidth="min-w-200"
          onRowClick={(g) => navigate(`/bus-gateways/${g.id}`)}
          columns={[
            {
              header: "Gateway",
              truncate: true,
              cell: (g) => <span className="block truncate font-medium text-ink-900">{g.name}</span>,
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
                  label="integrations"
                  items={g.routes.map((r) => ({
                    key: r.id,
                    name: r.integrationName,
                    href: `/subscriptions/${r.integrationId}`,
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
                      .map((r) => integrationsById.get(r.integrationId))
                      .filter((r): r is IntegrationRow => Boolean(r))}
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
