import { useMemo } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, type SubscriptionInfo, type SubscriptionRow } from "../../api";
import { useSessionCan } from "../../auth/useSessionCan";
import { type Column } from "../ui/Table";
import { keys } from "../../api/queryKeys";
import { ExceptionLine } from "../../components/ui/Exception";
import { SubscriptionStatusBadges, HealthBadge } from "./shared";
import { NONE } from "../../lib/none";

/**
 * All subscriptions, cached hard — pages use it to answer "who uses this
 * property/value/policy?" without extra requests.
 */
/**
 * Work group and retry policy are the two columns that sit between "Runs" and
 * "Status" and, rendered as plain links like it, gave the row three stretches of
 * identical 13px text with nothing but padding between them — which is what made
 * the table hard to read across. They are both *an assignment to a named thing*,
 * so they take a shared chip: it groups the pair, and separates them from the
 * subscription link on one side and the status badges on the other.
 */
const assignmentChip =
  "inline-flex items-center rounded-md bg-ink-50 px-1.5 py-0.5 text-[12px] text-ink-600 " +
  "hover:bg-ink-100 hover:text-crimson-700";

export function useSubscriptionsCache() {
  return useQuery({
    queryKey: keys.subscriptions.cache,
    queryFn: () => api.listSubscriptions(),
  });
}

/**
 * Which subscriptions each partner is reached through, keyed by partner id.
 *
 * A subscription's own `partnerId` only covers the legacy types. Everything
 * modern links a partner through a **gateway** — an API-gateway attachment or a
 * bus route — so those have to be folded in or a partner that is plainly in use
 * shows up as unused. Both gateway lists are the same cache entries the
 * Subscriptions page fills, and each is gated on its own view permission.
 */
export function usePartnerSubscriptions(): Map<number, SubscriptionInfo[]> {
  const subscriptions = useSubscriptionsCache().data ?? NONE;
  const canSeeApi = useSessionCan("api-gateways.view");
  const canSeeBus = useSessionCan("bus-gateways.view");
  const apiGateways =
    useQuery({
      queryKey: keys.apiGateways.list,
      queryFn: () => api.listApiGateways(),
      enabled: canSeeApi,
    }).data ?? NONE;
  const busGateways =
    useQuery({
      queryKey: keys.busGateways.list,
      queryFn: () => api.listBusGateways(),
      enabled: canSeeBus,
    }).data ?? NONE;

  return useMemo(() => {
    const byId = new Map(subscriptions.map((s) => [s.id, s]));
    const out = new Map<number, SubscriptionInfo[]>();
    const add = (partnerId: number | null, subscriptionId: number) => {
      if (partnerId === null) return;
      const setup = byId.get(subscriptionId);
      if (!setup) return;
      const list = out.get(partnerId) ?? [];
      if (!list.some((x) => x.id === setup.id)) {
        list.push(setup);
        out.set(partnerId, list);
      }
    };
    for (const s of subscriptions)
      for (const pid of s.partnerIds) add(pid, s.id);
    for (const g of apiGateways)
      for (const a of g.attachments) add(a.partnerId, a.subscriptionId);
    for (const g of busGateways)
      for (const r of g.routes) add(r.partnerId, r.subscriptionId);
    return out;
  }, [subscriptions, apiGateways, busGateways]);
}

/**
 * The same wiring as `usePartnerSubscriptions`, read the other way: partners
 * reached through a gateway, keyed by *subscription* id.
 *
 * `SubscriptionRow.partners` only carries a subscription's own `partnerId`, which
 * the modern types never have — without this, every gateway-fed subscription
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
      queryKey: keys.apiGateways.list,
      queryFn: () => api.listApiGateways(),
      enabled: canSeeApi,
    }).data ?? NONE;
  const busGateways =
    useQuery({
      queryKey: keys.busGateways.list,
      queryFn: () => api.listBusGateways(),
      enabled: canSeeBus,
    }).data ?? NONE;

  return useMemo(() => {
    const out = new Map<number, { id: number; name: string }[]>();
    const add = (
      subscriptionId: number,
      partnerId: number | null,
      partnerName: string | null,
    ) => {
      if (partnerId === null || partnerName === null) return;
      const list = out.get(subscriptionId) ?? [];
      if (!list.some((p) => p.id === partnerId)) {
        list.push({ id: partnerId, name: partnerName });
        out.set(subscriptionId, list);
      }
    };
    for (const g of apiGateways)
      for (const a of g.attachments)
        add(a.subscriptionId, a.partnerId, a.partnerName);
    for (const g of busGateways)
      for (const r of g.routes)
        add(r.subscriptionId, r.partnerId, r.partnerName);
    return out;
  }, [apiGateways, busGateways]);
}

/** Live status for every subscription, keyed by id — shared by the gateway pages. */
export function useSubscriptionRowsById(): Map<number, SubscriptionRow> {
  const rows =
    useQuery({
      queryKey: keys.subscriptions.rows,
      queryFn: () => api.listSubscriptionRows(),
    }).data ?? NONE;
  return useMemo(() => new Map(rows.map((r) => [r.id, r])), [rows]);
}

/** Work-group names by id; empty without the permission to read them. */
export function useWorkGroupNames(): Map<number, string> {
  const canSee = useSessionCan("workgroups.view");
  const groups =
    useQuery({
      queryKey: keys.workGroups.list,
      queryFn: () => api.listWorkGroups(),
      enabled: canSee,
    }).data ?? NONE;
  return useMemo(() => new Map(groups.map((g) => [g.id, g.name])), [groups]);
}

/** Retry-policy names by id; empty without the permission to read them. */
export function useRetryPolicyNames(): Map<number, string> {
  const canSee = useSessionCan("retry-policies.view");
  const policies =
    useQuery({
      queryKey: keys.retryPolicies.list,
      queryFn: () => api.listRetryPolicies(),
      enabled: canSee,
    }).data ?? NONE;
  return useMemo(
    () => new Map(policies.map((p) => [p.id, p.name])),
    [policies],
  );
}

export function useWiredSubscriptionColumns<T>(
  subscriptionIdOf: (row: T) => number,
  /** Off where the parent already fixes it — a bus gateway listens for one type. */
  { informationType = true }: { informationType?: boolean } = {},
): Column<T>[] {
  const rowsById = useSubscriptionRowsById();
  const setups = useSubscriptionsCache().data ?? NONE;
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
        const r = rowsById.get(subscriptionIdOf(row));
        if (!r) return <span className="text-ink-500">—</span>;
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
        const id = setupById.get(subscriptionIdOf(row))?.workGroupId ?? null;
        // "Ungrouped", not "Default": a null WorkGroupId isn't the absence of a
        // lane, it's `WorkGroup.None` — a real shared queue (`0Ungrouped`) that
        // every ungrouped subscription competes in. Matches the wording the
        // subscription page's work-group picker already uses.
        if (id === null)
          return <span className="text-[13px] text-ink-500">Ungrouped</span>;
        const name = workGroupNames.get(id);
        return name ? (
          <Link to={`/work-groups/${id}`} className={assignmentChip}>
            {name}
          </Link>
        ) : (
          <span className="text-[13px] text-ink-500">—</span>
        );
      },
    },
    {
      header: "Retry policy",
      cell: (row) => {
        const id = setupById.get(subscriptionIdOf(row))?.retryPolicyId ?? null;
        if (id === null)
          return <span className="text-[13px] text-ink-500">None</span>;
        const name = retryPolicyNames.get(id);
        return name ? (
          <Link to={`/retry-policies/${id}`} className={assignmentChip}>
            {name}
          </Link>
        ) : (
          <span className="text-[13px] text-ink-500">—</span>
        );
      },
    },
    {
      header: "Status",
      cell: (row) => {
        const r = rowsById.get(subscriptionIdOf(row));
        if (!r) return <span className="text-ink-500">—</span>;
        return (
          <span className="inline-flex items-center gap-1">
            <SubscriptionStatusBadges enabled={r.enabled} paused={r.paused} />
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
      truncate: true,
      cell: (row) => {
        const message = rowsById.get(subscriptionIdOf(row))?.lastException;
        return message ? (
          <ExceptionLine text={message} className="text-[12px] text-danger-700" />
        ) : (
          <span className="text-ink-500">—</span>
        );
      },
    },
  );

  return columns;
}
