import { useMemo, type ReactNode } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import {
  api,
  type ExchangeRef,
  type IntegrationInfo,
  type IntegrationRow,
  type IntegrationSetupRef,
  type IntegrationType,
  type ScheduleHealth,
  type TrailEntry,
} from "../../api";
import { useSessionCan } from "../../auth/guards";
import { Badge } from "../ui/basics";
import { Popover } from "../ui/Popover";
import { MiniTable, type Column } from "../ui/Table";
import { formatDate, timeAgo } from "../../lib/dates";

/**
 * Display names for integration types; Internal and ApiCall are legacy.
 *
 * `Receiving` reads "Scheduled job", not the backend's "Receiver" — it is the
 * same thing the sidebar and its own page call a scheduled job, and one entity
 * with two names in the same screen is just a puzzle for the reader.
 */
export const INTEGRATION_TYPE_LABELS: Record<IntegrationType, string> = {
  Receiving: "Scheduled job",
  GatewayApiCall: "API gateway",
  BusGateway: "Bus gateway",
  Internal: "Internal",
  ApiCall: "API call",
  Aggregation: "Aggregation",
};

export const isLegacyType = (type: IntegrationType) =>
  type === "Internal" || type === "ApiCall";

export function TypeBadge({ type }: { type: IntegrationType }) {
  return (
    <span className="inline-flex items-center gap-1">
      <Badge>{INTEGRATION_TYPE_LABELS[type]}</Badge>
      {isLegacyType(type) && <Badge tone="warn">Legacy</Badge>}
    </span>
  );
}

/** Enabled/paused pair — an integration can be both enabled and paused. */
export function IntegrationStatusBadges({
  enabled,
  paused,
}: {
  enabled: boolean;
  paused: boolean;
}) {
  return (
    <span className="inline-flex items-center gap-1">
      {enabled ? <Badge tone="ok">Active</Badge> : <Badge>Disabled</Badge>}
      {paused && <Badge tone="warn">Paused</Badge>}
    </span>
  );
}

/**
 * A fault the scheduler itself reports, which contradicts whatever the
 * integration's own badges say — "Active" with no trigger behind it is still a
 * job that never runs. Shared by the scheduled-jobs table and the pipeline rail
 * so the two can't drift apart.
 */
export function scheduleFault(
  health: ScheduleHealth | undefined,
): { label: string; tone: "warn" | "danger"; title: string } | null {
  if (!health) return null;

  if (health.stuck)
    return {
      label: "Stuck",
      tone: "danger",
      title:
        "Flagged as running with nothing executing — every later run is being skipped. Usually a run that was killed rather than failing.",
    };

  switch (health.state) {
    case "Missing":
      return {
        label: "Not scheduled",
        tone: "danger",
        title:
          "The scheduler has no trigger for this schedule — it will never fire.",
      };
    case "Error":
      return {
        label: "Trigger error",
        tone: "danger",
        title:
          "The scheduler put this trigger in an error state; it will not fire again until fixed.",
      };
    case "Paused":
      return {
        label: "Trigger paused",
        tone: "warn",
        title:
          "Paused inside the scheduler — this is not the integration's own pause.",
      };
    case "Blocked":
      return {
        label: "Blocked",
        tone: "warn",
        title:
          "A previous run is still going and this job doesn't allow overlap, so fires are being held.",
      };
    case "Complete":
      return {
        label: "Schedule ended",
        tone: "warn",
        title:
          "The schedule has run to completion and has no future fire times.",
      };
    default:
      return null;
  }
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

export function ExchangeStatusBadge({
  status,
}: {
  status: ExchangeRef["status"];
}) {
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
export function PromotedProps({
  properties,
  max = 3,
  fallbackId,
}: {
  properties: Record<string, string> | null;
  max?: number;
  /**
   * Shown when the information type promotes nothing, or promotes nothing this
   * payload carried. A bare em dash left the row with no identity at all — the id
   * is a poor name but it is the only one left, and it makes the row addressable.
   * Truncated because the drawer carries it in full, with a copy button.
   */
  fallbackId?: string;
}) {
  const entries = Object.entries(properties ?? {});
  if (entries.length === 0)
    return fallbackId ? (
      <span className="font-mono text-xs text-ink-400" title={fallbackId}>
        {fallbackId.slice(0, 8)}…
      </span>
    ) : (
      <span className="text-[13px] text-ink-400">—</span>
    );
  const shown = entries.slice(0, max);
  const rest = entries.length - shown.length;
  return (
    <span
      className="flex flex-wrap items-center gap-1"
      title={entries.map(([k, v]) => `${k}=${v}`).join("\n")}
    >
      {shown.map(([k, v]) => (
        <code
          key={k}
          className="rounded bg-ink-100 px-1.5 py-0.5 font-mono text-[11px] text-ink-700"
        >
          <span className="text-ink-500">{k}=</span>
          {v}
        </code>
      ))}
      {rest > 0 && <span className="text-[11px] text-ink-400">+{rest}</span>}
    </span>
  );
}

/**
 * Recent exchanges for one partner / information type / integration.
 *
 * Leads with the promoted properties, because they are the only column that says
 * what the exchange *was*. The id led before and answered the question nobody
 * asks — it's a 32-character guid, so all you could fit was the first eight
 * characters of something meaningless even in full. "Northwind says three orders
 * are missing" is answered by reading order numbers off these rows; it used to
 * take eight clicks.
 *
 * The id gets no column of its own. These panels are ~360px wide, and a complete
 * guid pushes the table past the panel edge — MiniTable scrolls rather than
 * truncating, so the last column ends up clipped instead of shortened. Clicking
 * the row opens it in Exchanges, where the full id is shown and copyable.
 *
 * When an exchange has no promoted properties there is nothing to lead with, so
 * the short id stands in — a row still needs a handle, and admitting "no
 * properties, here's the id" beats an empty cell.
 *
 * `hide` drops a column that is constant for the host page — Partner on a
 * partner's page, Type on an information type's — which is where the width for
 * the properties comes from.
 */
export function ExchangesList({
  items,
  hide = [],
}: {
  items: ExchangeRef[];
  hide?: ("partner" | "type")[];
}) {
  const columns = [
    {
      header: "What",
      cell: (x: ExchangeRef) => {
        const properties = Object.entries(x.promotedProperties ?? {});
        return (
          <Link
            to={`/exchanges?ids=${encodeURIComponent(x.id)}`}
            title={
              properties.length > 0
                ? `${x.id}\n\n${properties.map(([k, v]) => `${k}=${v}`).join("\n")}`
                : x.id
            }
            className="block hover:opacity-70"
          >
            <PromotedProps
              properties={x.promotedProperties ?? null}
              fallbackId={x.id}
            />
          </Link>
        );
      },
    },
    ...(hide.includes("type")
      ? []
      : [
          {
            header: "Type",
            cell: (x: ExchangeRef) => (
              <code className="font-mono text-xs text-ink-500">
                {x.informationTypeCode}
              </code>
            ),
          },
        ]),
    ...(hide.includes("partner")
      ? []
      : [
          {
            header: "Partner",
            cell: (x: ExchangeRef) => (
              <span className="text-[13px] text-ink-600">
                {x.partnerName ?? "—"}
              </span>
            ),
          },
        ]),
    {
      header: "Status",
      cell: (x: ExchangeRef) => <ExchangeStatusBadge status={x.status} />,
    },
    {
      header: "When",
      align: "right" as const,
      className: "whitespace-nowrap",
      cell: (x: ExchangeRef) => (
        <span className="text-xs text-ink-400">{timeAgo(x.on)}</span>
      ),
    },
  ];

  return (
    <MiniTable
      rows={items}
      rowKey={(x) => x.id}
      empty="No exchanges yet."
      columns={columns}
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
        {
          header: "Type",
          align: "right",
          cell: (s) => <TypeBadge type={s.type} />,
        },
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
    useQuery({
      queryKey: ["api-gateways"],
      queryFn: () => api.listApiGateways(),
      enabled: canSeeApi,
    }).data ?? [];
  const busGateways =
    useQuery({
      queryKey: ["bus-gateways"],
      queryFn: () => api.listBusGateways(),
      enabled: canSeeBus,
    }).data ?? [];

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
    for (const s of integrations)
      for (const pid of s.partnerIds) add(pid, s.id);
    for (const g of apiGateways)
      for (const a of g.attachments) add(a.partnerId, a.integrationId);
    for (const g of busGateways)
      for (const r of g.routes) add(r.partnerId, r.integrationId);
    return out;
  }, [integrations, apiGateways, busGateways]);
}

/**
 * The same wiring as `usePartnerIntegrations`, read the other way: partners
 * reached through a gateway, keyed by *integration* id.
 *
 * `IntegrationRow.partners` only carries a subscription's own `partnerId`, which
 * the modern types never have — without this, every gateway-fed integration
 * shows a dash where its partner should be.
 */
export function useGatewayPartners(): Map<
  number,
  { id: number; name: string }[]
> {
  const canSeeApi = useSessionCan("api-gateways.view");
  const canSeeBus = useSessionCan("bus-gateways.view");
  const apiGateways =
    useQuery({
      queryKey: ["api-gateways"],
      queryFn: () => api.listApiGateways(),
      enabled: canSeeApi,
    }).data ?? [];
  const busGateways =
    useQuery({
      queryKey: ["bus-gateways"],
      queryFn: () => api.listBusGateways(),
      enabled: canSeeBus,
    }).data ?? [];

  return useMemo(() => {
    const out = new Map<number, { id: number; name: string }[]>();
    const add = (
      integrationId: number,
      partnerId: number | null,
      partnerName: string | null,
    ) => {
      if (partnerId === null || partnerName === null) return;
      const list = out.get(integrationId) ?? [];
      if (!list.some((p) => p.id === partnerId)) {
        list.push({ id: partnerId, name: partnerName });
        out.set(integrationId, list);
      }
    };
    for (const g of apiGateways)
      for (const a of g.attachments)
        add(a.integrationId, a.partnerId, a.partnerName);
    for (const g of busGateways)
      for (const r of g.routes)
        add(r.integrationId, r.partnerId, r.partnerName);
    return out;
  }, [apiGateways, busGateways]);
}

/** Live status for every integration, keyed by id — shared by the gateway pages. */
export function useIntegrationRowsById(): Map<number, IntegrationRow> {
  const rows =
    useQuery({
      queryKey: ["integration-rows"],
      queryFn: () => api.listIntegrationRows(),
    }).data ?? [];
  return useMemo(() => new Map(rows.map((r) => [r.id, r])), [rows]);
}

/** Work-group names by id; empty without the permission to read them. */
export function useWorkGroupNames(): Map<number, string> {
  const canSee = useSessionCan("workgroups.view");
  const groups =
    useQuery({
      queryKey: ["work-groups"],
      queryFn: () => api.listWorkGroups(),
      enabled: canSee,
    }).data ?? [];
  return useMemo(() => new Map(groups.map((g) => [g.id, g.name])), [groups]);
}

/** Retry-policy names by id; empty without the permission to read them. */
export function useRetryPolicyNames(): Map<number, string> {
  const canSee = useSessionCan("retry-policies.view");
  const policies =
    useQuery({
      queryKey: ["retry-policies"],
      queryFn: () => api.listRetryPolicies(),
      enabled: canSee,
    }).data ?? [];
  return useMemo(
    () => new Map(policies.map((p) => [p.id, p.name])),
    [policies],
  );
}

/**
 * Roll-up health for what a gateway feeds. A gateway itself has no state worth
 * reporting — it is only as healthy as the pipelines behind it, and "3 partners
 * attached" says nothing about whether any of them currently works.
 */
export function WiredHealthBadge({
  rows,
  empty,
}: {
  rows: IntegrationRow[];
  empty: string;
}) {
  if (rows.length === 0) return <Badge tone="warn">{empty}</Badge>;
  const failing = rows.filter((r) => r.consecutiveFailures > 0).length;
  if (failing > 0) return <Badge tone="danger">{failing} failing</Badge>;
  const paused = rows.filter((r) => r.paused).length;
  if (paused > 0) return <Badge tone="warn">{paused} paused</Badge>;
  const disabled = rows.filter((r) => !r.enabled).length;
  if (disabled > 0) return <Badge>{disabled} disabled</Badge>;
  return <Badge tone="ok">Healthy</Badge>;
}

/**
 * The columns describing the pipeline behind one gateway attachment or route.
 *
 * These tables are the only 1:1 place in the gateway story — one row is exactly
 * one partner and one integration — so this is where its configuration can be
 * stated in separate columns without the reader having to guess which value
 * pairs with which. The parent list can't do it: two parallel lists in a row
 * lose their pairing, which is why the gateway tables carry only aggregates.
 *
 * Everything here comes from caches the app already holds, keyed by integration
 * id; the gateway endpoints know none of it.
 */
export function useWiredIntegrationColumns<T>(
  integrationIdOf: (row: T) => number,
  /** Off where the parent already fixes it — a bus gateway listens for one type. */
  { informationType = true }: { informationType?: boolean } = {},
): Column<T>[] {
  const rowsById = useIntegrationRowsById();
  const setups = useIntegrationsCache().data ?? [];
  const setupById = useMemo(
    () => new Map(setups.map((s) => [s.id, s])),
    [setups],
  );
  const workGroupNames = useWorkGroupNames();
  const retryPolicyNames = useRetryPolicyNames();
  const canSeeInfoTypes = useSessionCan("documents.view");

  const columns: Column<T>[] = [];

  if (informationType)
    columns.push({
      header: "Information type",
      cell: (row) => {
        const r = rowsById.get(integrationIdOf(row));
        if (!r) return <span className="text-ink-400">—</span>;
        return canSeeInfoTypes ? (
          <Link
            to={`/information-types/${r.informationTypeId}`}
            className="font-mono text-xs text-ink-600 hover:text-crimson-700 hover:underline"
          >
            {r.informationTypeCode}
          </Link>
        ) : (
          <code className="font-mono text-xs text-ink-600">
            {r.informationTypeCode}
          </code>
        );
      },
    });

  columns.push(
    {
      header: "Work group",
      cell: (row) => {
        const id = setupById.get(integrationIdOf(row))?.workGroupId ?? null;
        // "Ungrouped", not "Default": a null WorkGroupId isn't the absence of a
        // lane, it's `WorkGroup.None` — a real shared queue (`0Ungrouped`) that
        // every ungrouped integration competes in. Matches the wording the
        // integration page's work-group picker already uses.
        if (id === null)
          return <span className="text-[13px] text-ink-400">Ungrouped</span>;
        const name = workGroupNames.get(id);
        return name ? (
          <Link
            to={`/work-groups/${id}`}
            className="text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
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
      cell: (row) => {
        const id = setupById.get(integrationIdOf(row))?.retryPolicyId ?? null;
        if (id === null)
          return <span className="text-[13px] text-ink-400">None</span>;
        const name = retryPolicyNames.get(id);
        return name ? (
          <Link
            to={`/retry-policies/${id}`}
            className="text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
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
      cell: (row) => {
        const r = rowsById.get(integrationIdOf(row));
        if (!r) return <span className="text-ink-400">—</span>;
        return (
          <span className="inline-flex items-center gap-1">
            <IntegrationStatusBadges enabled={r.enabled} paused={r.paused} />
            <HealthBadge
              isRunning={r.isRunning}
              consecutiveFailures={r.consecutiveFailures}
            />
          </span>
        );
      },
    },
    {
      header: "Last error",
      // A bounded width, not `truncate`: MiniTable ignores that flag, and an
      // unbounded stack trace would push everything else out of the panel.
      className: "max-w-48 overflow-hidden",
      cell: (row) => {
        const message = rowsById.get(integrationIdOf(row))?.lastException;
        return message ? (
          <span
            className="block truncate font-mono text-[11px] text-danger-700"
            title={message}
          >
            {message}
          </span>
        ) : (
          <span className="text-ink-400">—</span>
        );
      },
    },
  );

  return columns;
}

export interface CellLink {
  key: string | number;
  name: string;
  href: string;
  /** Shown beside the name inside the popover, e.g. the record's type. */
  note?: ReactNode;
}

/**
 * A cell listing the records something is wired to: the first couple inline,
 * the rest behind a popover.
 *
 * "3 places" tells you a thing is in use but nothing you can act on — you still
 * have to open the row to learn whether it's safe to touch. Names answer that in
 * the table. The overflow goes into a popover rather than wrapping, so row
 * height stays fixed and the table keeps its rhythm, and nothing ends up
 * reachable only by opening the record's own page.
 */
export function LinkListCell({
  items,
  label,
}: {
  items: CellLink[];
  /** Plural noun for the popover heading, e.g. "integrations". */
  label: string;
}) {
  if (items.length === 0) return <span className="text-ink-400">—</span>;
  // "1 integrations" reads as a bug, and every label here is a simple plural.
  const noun = items.length === 1 ? label.replace(/s$/, "") : label;
  return (
    <span className="flex items-baseline gap-1 text-[13px]">
      <Popover
        label={items.length === 1 ? `Show 1 ${noun}` : `Show all ${items.length} ${label}`}
        width="w-80"
        button={
          // A chip rather than a bare digit: a muted number alone in a table cell reads as
          // data you can't act on, and the list behind it would never be found. The names
          // come back on hover, and in full when it is opened.
          <span
            className="rounded bg-ink-100 px-1.5 py-0.5 text-[12px] font-medium text-ink-700 tabular-nums"
            title={items.map((s) => s.name).join(", ")}
          >
            {items.length}
          </span>
        }
      >
        <p className="px-1.5 pb-1.5 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
          {items.length} {noun}
        </p>
        <ul className="border-t border-ink-100 pt-1">
          {items.map((s) => (
            <li key={s.key}>
              <Link
                to={s.href}
                className="flex items-center justify-between gap-2 rounded-lg px-1.5 py-1.5 hover:bg-ink-50"
              >
                <span className="truncate text-[13px] font-medium text-ink-800">
                  {s.name}
                </span>
                {s.note}
              </Link>
            </li>
          ))}
        </ul>
      </Popover>
    </span>
  );
}

/** `LinkListCell` for the commonest case: the integrations using something. */
export function UsedByCell({ items }: { items: IntegrationInfo[] }) {
  return (
    <LinkListCell
      label="integrations"
      items={items.map((s) => ({
        key: s.id,
        name: s.name,
        href: `/subscriptions/${s.id}`,
        note: <Badge>{INTEGRATION_TYPE_LABELS[s.type]}</Badge>,
      }))}
    />
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
        {
          header: "Action",
          cell: (e) => (
            <span className="font-medium text-ink-800">{e.action}</span>
          ),
        },
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
          cell: (e) => (
            <span className="text-xs text-ink-400">{formatDate(e.on)}</span>
          ),
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
