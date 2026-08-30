import type { QueryClient } from "@tanstack/react-query";

/**
 * Every cache label in the app, in one place.
 *
 * Keys are hierarchical and grouped by *entity*, not by endpoint: a subscription's detail, its
 * paged rows and the whole-list cache all start with `["subscriptions"]`. React Query matches
 * invalidation by prefix, so `invalidateQueries({ queryKey: keys.subscriptions.all })` clears the
 * lot — one line where the old hand-written string keys needed one line per variant, and where
 * missing one was silent.
 *
 * It was not hypothetical: nothing in the app ever invalidated `partners-search`,
 * `information-types-search`, `retry-policies-search` or `api-gateways-search` — the four paged
 * tables people actually look at. They only appeared correct because the global 10s staleTime
 * expired almost immediately and a remount refetched. Two more pairs (`information-types` /
 * `information-types-all`, `partners` / `partners-all`) were the same fetch cached twice under
 * different names, and `appConfig` / `app-config` were one endpoint under two labels, only one of
 * which the settings page invalidated.
 *
 * Adding a query? Add its key here. A new key under an existing entity is covered by that
 * entity's existing invalidations automatically, which is the whole point.
 */
export const keys = {
  subscriptions: {
    all: ["subscriptions"] as const,
    /** The whole list, held once and shared by every "who uses this?" panel. */
    cache: ["subscriptions", "cache"] as const,
    /** Live status for every subscription, keyed by id — shared by the gateway pages. */
    rows: ["subscriptions", "rows"] as const,
    rowsSearch: (params: Record<string, unknown>) => ["subscriptions", "rows", params] as const,
    detail: (id: number | string | null | undefined) => ["subscriptions", "detail", id] as const,
    runs: (id: number) => ["subscriptions", "runs", id] as const,
    receiveAttempts: (id: number, params: Record<string, unknown>) =>
      ["subscriptions", "receive-attempts", id, params] as const,
    lastRuns: ["subscriptions", "last-runs"] as const,
    scheduleHealth: ["subscriptions", "schedule-health"] as const,
  },

  informationTypes: {
    all: ["information-types"] as const,
    list: ["information-types", "list"] as const,
    search: (params: Record<string, unknown>) => ["information-types", "search", params] as const,
    detail: (id: number | null | undefined) => ["information-types", "detail", id] as const,
  },

  partners: {
    all: ["partners"] as const,
    list: ["partners", "list"] as const,
    search: (params: Record<string, unknown>) => ["partners", "search", params] as const,
    detail: (id: number | null | undefined) => ["partners", "detail", id] as const,
    /** Adapter properties a partner overrides, per partner. */
    adapterProperties: (id: number | null | undefined) =>
      ["partners", "adapter-properties", id] as const,
  },

  apiGateways: {
    all: ["api-gateways"] as const,
    list: ["api-gateways", "list"] as const,
    search: (params: Record<string, unknown>) => ["api-gateways", "search", params] as const,
    detail: (id: number | string) => ["api-gateways", "detail", id] as const,
    attachments: (id: number | string, params: Record<string, unknown>) =>
      ["api-gateways", "attachments", id, params] as const,
  },

  busGateways: {
    all: ["bus-gateways"] as const,
    list: ["bus-gateways", "list"] as const,
    search: (params: Record<string, unknown>) => ["bus-gateways", "search", params] as const,
    detail: (id: number | string) => ["bus-gateways", "detail", id] as const,
  },

  workGroups: {
    all: ["work-groups"] as const,
    list: ["work-groups", "list"] as const,
    search: (params: Record<string, unknown>) => ["work-groups", "search", params] as const,
    detail: (id: number | null | undefined) => ["work-groups", "detail", id] as const,
  },

  retryPolicies: {
    all: ["retry-policies"] as const,
    list: ["retry-policies", "list"] as const,
    search: (params: Record<string, unknown>) => ["retry-policies", "search", params] as const,
    detail: (id: number | string) => ["retry-policies", "detail", id] as const,
  },

  /**
   * Budget consumption, kept apart from the policies themselves: it moves on its own as exchanges
   * fail and gets reset by operators, so it is never worth refetching a policy list for.
   */
  retryUsage: {
    all: ["retry-usage"] as const,
    forPolicy: (policyId: number | string) => ["retry-usage", "policy", policyId] as const,
    forSubscription: (id: number) => ["retry-usage", "subscription", id] as const,
    attempts: (policyId: number | string, subscriptionId: number, groupId: number | string) =>
      ["retry-usage", "attempts", policyId, subscriptionId, groupId] as const,
  },

  valueSets: {
    all: ["value-sets"] as const,
    list: ["value-sets", "list"] as const,
    detail: (id: string) => ["value-sets", "detail", id] as const,
  },

  notifiers: {
    all: ["notifiers"] as const,
    list: ["notifiers", "list"] as const,
    search: (params: Record<string, unknown>) => ["notifiers", "search", params] as const,
    detail: (id: number | string) => ["notifiers", "detail", id] as const,
  },

  roles: {
    all: ["roles"] as const,
    list: ["roles", "list"] as const,
    detail: (id: number | string | null | undefined) => ["roles", "detail", id] as const,
  },

  users: {
    all: ["users"] as const,
    list: ["users", "list"] as const,
    detail: (id: number | string) => ["users", "detail", id] as const,
  },

  exchanges: {
    all: ["exchanges"] as const,
    search: (params: string) => ["exchanges", "search", params] as const,
    document: (key: string | null) => ["exchanges", "document", key] as const,
  },

  scheduledRetries: {
    all: ["scheduled-retries"] as const,
    search: (params: string) => ["scheduled-retries", "search", params] as const,
  },

  queueHealth: ["queue-health"] as const,
  dashboard: ["dashboard"] as const,

  settings: {
    all: ["settings"] as const,
    list: ["settings", "list"] as const,
  },

  /** The anonymous branding/config endpoint, read by the sign-in page and the app shell alike. */
  appConfig: ["app-config"] as const,

  /** Fixed for the lifetime of a deployment. */
  permissionCatalog: ["permission-catalog"] as const,
  adapters: (kind: string) => ["adapters", kind] as const,
} as const;

const MINUTE = 60_000;

/**
 * How long each kind of data is trusted before a remount refetches it.
 *
 * Declared centrally rather than at 97 call sites, so the policy for an entity is one number in
 * one place. Mutations invalidate explicitly, so these windows only govern *background*
 * refetching — an edit still shows up immediately.
 *
 * Three tiers:
 *  - **Fixed per deployment** — never refetched.
 *  - **Reference/config data** — changes only when someone edits it here, and that path
 *    invalidates. Minutes are safe.
 *  - **Operational data** — moves on its own as messages flow. Always refetched on mount; most of
 *    these screens also declare their own `refetchInterval`.
 */
export function applyQueryDefaults(queryClient: QueryClient): void {
  const fixed = [keys.permissionCatalog, ["adapters"]];
  const reference = [
    keys.informationTypes.all,
    keys.partners.all,
    keys.workGroups.all,
    keys.retryPolicies.all,
    keys.valueSets.all,
    keys.notifiers.all,
    keys.roles.all,
    keys.users.all,
    keys.apiGateways.all,
    keys.busGateways.all,
    keys.subscriptions.all,
    keys.settings.all,
    keys.appConfig,
  ];
  const operational = [
    keys.exchanges.all,
    keys.scheduledRetries.all,
    keys.queueHealth,
    keys.dashboard,
    keys.retryUsage.all,
    keys.subscriptions.lastRuns,
    keys.subscriptions.scheduleHealth,
    ["subscriptions", "receive-attempts"],
    ["subscriptions", "runs"],
    // The row shape carries live state — is it running, how many consecutive failures, when it
    // fires next — so the tables built on it have to refetch on mount like any other live view.
    // (The whole-list `cache` below is a different query and stays held for the session.)
    keys.subscriptions.rows,
  ];

  for (const key of fixed) queryClient.setQueryDefaults(key, { staleTime: Infinity });
  for (const key of reference) queryClient.setQueryDefaults(key, { staleTime: 5 * MINUTE });
  // Registered after `reference` on purpose: these are nested under `subscriptions`, and defaults
  // merge in registration order, so the more specific prefix has to come second to win.
  for (const key of operational) queryClient.setQueryDefaults(key, { staleTime: 0 });

  // The heaviest response in the app, and the one every "who uses this?" panel reads. Held for the
  // session rather than re-fetched every five minutes; edits still invalidate it explicitly.
  queryClient.setQueryDefaults(keys.subscriptions.cache, { staleTime: Infinity });
}
