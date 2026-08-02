import { useMemo } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import {
  api,
  type ExchangeRef,
  type IntegrationInfo,
  type IntegrationSetupRef,
  type IntegrationType,
  type TrailEntry,
} from "../../api";
import { useSessionCan } from "../../auth/guards";
import { Badge } from "../ui/basics";
import { Popover } from "../ui/Popover";
import { MiniTable } from "../ui/Table";
import { formatDate, timeAgo } from "../../lib/dates";

/** Display names for integration types; Internal and ApiCall are legacy. */
export const INTEGRATION_TYPE_LABELS: Record<IntegrationType, string> = {
  Receiving: "Receiver",
  GatewayApiCall: "API gateway",
  BusGateway: "Bus gateway",
  Internal: "Internal",
  ApiCall: "API call",
  Aggregation: "Aggregation",
};

export const isLegacyType = (type: IntegrationType) => type === "Internal" || type === "ApiCall";

export function TypeBadge({ type }: { type: IntegrationType }) {
  return (
    <span className="inline-flex items-center gap-1">
      <Badge>{INTEGRATION_TYPE_LABELS[type]}</Badge>
      {isLegacyType(type) && <Badge tone="warn">Legacy</Badge>}
    </span>
  );
}

/** Enabled/paused pair — an integration can be both enabled and paused. */
export function IntegrationStatusBadges({ enabled, paused }: { enabled: boolean; paused: boolean }) {
  return (
    <span className="inline-flex items-center gap-1">
      {enabled ? <Badge tone="ok">Active</Badge> : <Badge>Disabled</Badge>}
      {paused && <Badge tone="warn">Paused</Badge>}
    </span>
  );
}

export function HealthBadge({
  isRunning,
  consecutiveFailures,
}: {
  isRunning: boolean;
  consecutiveFailures: number;
}) {
  if (consecutiveFailures > 0)
    return (
      <Badge tone="danger">
        {consecutiveFailures} failure{consecutiveFailures === 1 ? "" : "s"}
      </Badge>
    );
  if (isRunning) return <Badge tone="ok">Running</Badge>;
  return <Badge>Idle</Badge>;
}

export function ExchangeStatusBadge({ status }: { status: ExchangeRef["status"] }) {
  if (status === "success") return <Badge tone="ok">Success</Badge>;
  if (status === "failed") return <Badge tone="danger">Failed</Badge>;
  if (status === "badResponse") return <Badge tone="warn">Bad response</Badge>;
  return <Badge tone="neutral">Processing</Badge>;
}

/**
 * Recent traffic for a hub page. Every field the row already carries is a
 * column — the old version spent a whole line on an id and left the rest of
 * the width empty, so what the exchange actually *was* never made it to the
 * screen.
 */
export function ExchangesList({ items }: { items: ExchangeRef[] }) {
  return (
    <MiniTable
      rows={items}
      rowKey={(x) => x.id}
      empty="No exchanges yet."
      columns={[
        {
          header: "Exchange",
          // A bounded width, not `truncate`: MiniTable ignores that flag, and an
          // unbounded 32-char id would push Status and When out of the panel.
          className: "max-w-24 overflow-hidden",
          cell: (x) => (
            <Link
              to={`/exchanges?ids=${encodeURIComponent(x.id)}`}
              title={x.id}
              className="block truncate font-mono text-xs text-ink-600 hover:text-crimson-700 hover:underline"
            >
              {x.id}
            </Link>
          ),
        },
        {
          header: "Type",
          cell: (x) => <code className="font-mono text-xs text-ink-500">{x.informationTypeCode}</code>,
        },
        {
          header: "Partner",
          cell: (x) => <span className="text-[13px] text-ink-600">{x.partnerName ?? "—"}</span>,
        },
        { header: "Status", cell: (x) => <ExchangeStatusBadge status={x.status} /> },
        {
          header: "When",
          align: "right",
          className: "whitespace-nowrap",
          cell: (x) => <span className="text-xs text-ink-400">{timeAgo(x.on)}</span>,
        },
      ]}
    />
  );
}

/** Integrations referencing this entity, each linking to its page. */
export function SetupList({ items }: { items: IntegrationSetupRef[] }) {
  return (
    <MiniTable
      rows={items}
      rowKey={(s) => s.id}
      empty="Not used by any integration."
      columns={[
        {
          header: "Integration",
          truncate: true,
          cell: (s) => (
            <Link
              to={`/subscriptions/${s.id}`}
              className="block truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
            >
              {s.name}
            </Link>
          ),
        },
        { header: "Type", align: "right", cell: (s) => <TypeBadge type={s.type} /> },
      ]}
    />
  );
}

/**
 * All integrations, cached hard — pages use it to answer "who uses this
 * property/value/policy?" without extra requests.
 */
export function useIntegrationsCache() {
  return useQuery({
    queryKey: ["integrations"],
    queryFn: () => api.listIntegrations(),
    staleTime: Infinity,
  });
}

/**
 * Which integrations each partner is reached through, keyed by partner id.
 *
 * A subscription's own `partnerId` only covers the legacy types. Everything
 * modern links a partner through a **gateway** — an API-gateway attachment or a
 * bus route — so those have to be folded in or a partner that is plainly in use
 * shows up as unused. Both gateway lists are the same cache entries the
 * Integrations page fills, and each is gated on its own view permission.
 */
export function usePartnerIntegrations(): Map<number, IntegrationInfo[]> {
  const integrations = useIntegrationsCache().data ?? [];
  const canSeeApi = useSessionCan("api-gateways.view");
  const canSeeBus = useSessionCan("bus-gateways.view");
  const apiGateways =
    useQuery({ queryKey: ["api-gateways"], queryFn: () => api.listApiGateways(), enabled: canSeeApi }).data ?? [];
  const busGateways =
    useQuery({ queryKey: ["bus-gateways"], queryFn: () => api.listBusGateways(), enabled: canSeeBus }).data ?? [];

  return useMemo(() => {
    const byId = new Map(integrations.map((s) => [s.id, s]));
    const out = new Map<number, IntegrationInfo[]>();
    const add = (partnerId: number | null, integrationId: number) => {
      if (partnerId === null) return;
      const setup = byId.get(integrationId);
      if (!setup) return;
      const list = out.get(partnerId) ?? [];
      if (!list.some((x) => x.id === setup.id)) {
        list.push(setup);
        out.set(partnerId, list);
      }
    };
    for (const s of integrations) for (const pid of s.partnerIds) add(pid, s.id);
    for (const g of apiGateways) for (const a of g.attachments) add(a.partnerId, a.integrationId);
    for (const g of busGateways) for (const r of g.routes) add(r.partnerId, r.integrationId);
    return out;
  }, [integrations, apiGateways, busGateways]);
}

/**
 * The "used by" cell on list pages: the integrations themselves, not a count.
 *
 * "3 places" tells you a thing is in use but nothing you can act on — you still
 * have to open the row to learn whether it's safe to touch. Names answer that in
 * the table. Overflow goes into a popover rather than wrapping, so row height
 * stays fixed and the table keeps its rhythm; nothing is reachable only by
 * opening the entity's own page.
 */
export function UsedByCell({ items, max = 2 }: { items: IntegrationInfo[]; max?: number }) {
  if (items.length === 0) return <span className="text-ink-400">—</span>;
  const shown = items.slice(0, max);
  const rest = items.length - shown.length;
  return (
    <span className="flex items-baseline gap-1 text-[13px]">
      <span className="min-w-0 truncate" title={shown.map((s) => s.name).join(", ")}>
        {shown.map((s, i) => (
          <span key={s.id}>
            {i > 0 && <span className="text-ink-300">, </span>}
            <Link
              to={`/subscriptions/${s.id}`}
              onClick={(e) => e.stopPropagation()}
              className="text-ink-700 hover:text-crimson-700 hover:underline"
            >
              {s.name}
            </Link>
          </span>
        ))}
      </span>
      {rest > 0 && (
        <Popover
          label={`Show all ${items.length} integrations`}
          width="w-80"
          button={<span className="whitespace-nowrap">+{rest} more</span>}
        >
          <UsedByPanel items={items} />
        </Popover>
      )}
    </span>
  );
}

/** The full list behind a "+N more" — every entry a link out. */
function UsedByPanel({ items }: { items: IntegrationInfo[] }) {
  return (
    <>
      <p className="px-1.5 pb-1.5 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
        Used by {items.length} integrations
      </p>
      <ul className="border-t border-ink-100 pt-1">
        {items.map((s) => (
          <li key={s.id}>
            <Link
              to={`/subscriptions/${s.id}`}
              className="flex items-center justify-between gap-2 rounded-lg px-1.5 py-1.5 hover:bg-ink-50"
            >
              <span className="truncate text-[13px] font-medium text-ink-800">{s.name}</span>
              <Badge>{INTEGRATION_TYPE_LABELS[s.type]}</Badge>
            </Link>
          </li>
        ))}
      </ul>
    </>
  );
}

/** Audit trail for an entity, newest first. Shared by every hub page that has one. */
export function TrailTable({ entries }: { entries: TrailEntry[] }) {
  return (
    <MiniTable
      rows={[...entries].reverse().map((e, i) => ({ ...e, i }))}
      rowKey={(e) => e.i}
      empty="Nothing recorded yet."
      columns={[
        { header: "Action", cell: (e) => <span className="font-medium text-ink-800">{e.action}</span> },
        {
          header: "By",
          truncate: true,
          cell: (e) =>
            e.byUserId ? (
              <Link
                to={`/team/members/${e.byUserId}`}
                className="block truncate text-ink-600 hover:text-crimson-700 hover:underline"
              >
                {e.by}
              </Link>
            ) : (
              <span className="block truncate text-ink-600">{e.by}</span>
            ),
        },
        {
          header: "When",
          align: "right",
          className: "whitespace-nowrap",
          cell: (e) => <span className="text-xs text-ink-400">{formatDate(e.on)}</span>,
        },
      ]}
    />
  );
}

/** Integrations referencing one particular key or value, with their type. */
export function IntegrationMiniList({
  items,
  emptyText,
}: {
  items: IntegrationInfo[];
  emptyText: string;
}) {
  return (
    <MiniTable
      rows={items}
      rowKey={(s) => s.id}
      empty={emptyText}
      columns={[
        {
          header: "Integration",
          truncate: true,
          cell: (s) => (
            <Link
              to={`/subscriptions/${s.id}`}
              className="block truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
            >
              {s.name}
            </Link>
          ),
        },
        {
          header: "Type",
          align: "right",
          cell: (s) => <Badge>{INTEGRATION_TYPE_LABELS[s.type]}</Badge>,
        },
      ]}
    />
  );
}
