import type { ApiClient } from "../client";
import {
  ApiRequestError,
  type IntegrationType,
  type RetryAlertConfig,
  type RetryAlertLevel,
  type RetryAttempts,
  type RetryDelay,
  type RetryGroup,
  type RetryPolicy,
  type RetryPolicyDetail,
  type RetryPolicyListRow,
  type RetryResultType,
  type RetryTestAttempt,
  type RetryUsageRow,
  type Paged,
} from "../types";
import { get, getEnrichment, post, request } from "./request";
import { buildListQuery, SEARCHY_RULE } from "./searchQuery";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawRetryPolicyRow {
  id: number;
  name: string;
  groupCount: number;
}
interface RawRetryPolicy {
  name: string;
  groups: RawRetryGroup[] | null;
  alertHandlerId: string | null;
  alertHandlerProperties: Record<string, string> | null;
}
interface RawSubscriptionRef {
  id: number;
  name: string;
  type: number | string;
}

const SUB_TYPE_BY_NUM: Record<number, IntegrationType> = {
  1: "Internal",
  2: "ApiCall",
  4: "Receiving",
  8: "Aggregation",
  16: "GatewayApiCall",
  32: "BusGateway",
};
const INTEGRATION_TYPES: IntegrationType[] = [
  "Receiving",
  "GatewayApiCall",
  "BusGateway",
  "Internal",
  "ApiCall",
  "Aggregation",
];
/** Enums may arrive as the numeric value or the name in any case. */
const toIntegrationType = (t: number | string): IntegrationType => {
  if (typeof t === "number") return SUB_TYPE_BY_NUM[t] ?? "Internal";
  return INTEGRATION_TYPES.find((k) => k.toLowerCase() === t.toLowerCase()) ?? "Internal";
};

async function fetchSubscriptionsByRetryPolicy(retryPolicyId: number): Promise<RawSubscriptionRef[]> {
  const res = await get<SearchyResponse<RawSubscriptionRef>>(
    `/subscriptions?filter=${encodeURIComponent(`RetryPolicyId:1:${retryPolicyId}`)}`,
  );
  return res.result ?? [];
}

// The backend's DelayStrategy keeps durations in milliseconds; the UI works in seconds.
type RawDelayStrategy =
  | { type: "fixed"; delayMs: number }
  | { type: "linear"; initialDelayMs: number; incrementMs: number }
  | { type: "exponential"; initialDelayMs: number; multiplier: number; maxDelayMs: number };
interface RawRetryBudget {
  maxAttemptsPerError: number;
  maxAttemptsTotal: number;
  delayStrategy: RawDelayStrategy;
}
interface RawRetryGroup extends Omit<RetryGroup, "budget" | "alertHandlerProperties"> {
  budget?: RawRetryBudget | null;
  alertHandlerProperties?: Record<string, string> | null;
}
interface RawTestAttempt {
  attemptNumber: number;
  matchedGroupName: string | null;
  shouldRetry: boolean;
  delaySeconds: number | null;
  reason: string;
}

const toDelay = (d: RawDelayStrategy): RetryDelay => {
  switch (d.type) {
    case "fixed":
      return { type: "fixed", delaySeconds: d.delayMs / 1000 };
    case "linear":
      return { type: "linear", initialSeconds: d.initialDelayMs / 1000, incrementSeconds: d.incrementMs / 1000 };
    case "exponential":
      return {
        type: "exponential",
        initialSeconds: d.initialDelayMs / 1000,
        multiplier: d.multiplier,
        maxSeconds: d.maxDelayMs / 1000,
      };
  }
};

const toRawDelay = (d: RetryDelay): RawDelayStrategy => {
  switch (d.type) {
    case "fixed":
      return { type: "fixed", delayMs: Math.round(d.delaySeconds * 1000) };
    case "linear":
      return {
        type: "linear",
        initialDelayMs: Math.round(d.initialSeconds * 1000),
        incrementMs: Math.round(d.incrementSeconds * 1000),
      };
    case "exponential":
      return {
        type: "exponential",
        initialDelayMs: Math.round(d.initialSeconds * 1000),
        multiplier: d.multiplier,
        maxDelayMs: Math.round(d.maxSeconds * 1000),
      };
  }
};

const toGroup = (g: RawRetryGroup): RetryGroup => ({
  ...g,
  budget: g.budget
    ? {
        maxAttemptsPerError: g.budget.maxAttemptsPerError,
        maxAttemptsTotal: g.budget.maxAttemptsTotal,
        delay: toDelay(g.budget.delayStrategy),
      }
    : undefined,
  // Named rather than left to the spread: a policy saved from an older client has no
  // alert fields at all, and an undefined mode would compare unequal to "Inherit" and
  // leave the Save bar up on a page nobody had edited.
  alertMode: g.alertMode ?? "Inherit",
  alertHandlerId: g.alertHandlerId ?? null,
  alertHandlerProperties: g.alertHandlerProperties ?? {},
});

const toRawGroup = (g: RetryGroup): RawRetryGroup => ({
  ...g,
  budget: g.budget
    ? {
        maxAttemptsPerError: g.budget.maxAttemptsPerError,
        maxAttemptsTotal: g.budget.maxAttemptsTotal,
        delayStrategy: toRawDelay(g.budget.delay),
      }
    : undefined,
});

async function fetchDetail(id: number): Promise<RetryPolicyDetail> {
  const [r, subs] = await Promise.all([
    get<RawRetryPolicy | null>(`/retrypolicies/${id}`),
    fetchSubscriptionsByRetryPolicy(id),
  ]);
  if (!r) throw new ApiRequestError("NOT_FOUND", "This retry policy no longer exists.");
  return {
    id,
    name: r.name,
    groups: (r.groups ?? []).map(toGroup),
    createdOn: "",
    alertHandlerId: r.alertHandlerId ?? null,
    alertHandlerProperties: r.alertHandlerProperties ?? {},
    integrations: subs.map((s) => ({ id: s.id, name: s.name, type: toIntegrationType(s.type) })),
  };
}

interface RawUsageRow {
  subscriptionId: number;
  subscriptionName: string;
  groupId: string;
  groupName: string;
  attemptsUsed: number;
  maxAttemptsTotal: number;
  exhausted: boolean;
  lastAttemptOn: string | null;
  exhaustedNotifiedOn: string | null;
  alertDelivered: boolean | null;
  alertError: string | null;
  alertMode: RetryAlertConfig["alertMode"];
  overrideHandlerId: string | null;
  overrideHandlerProperties: Record<string, string> | null;
  resolvedHandlerId: string | null;
  resolvedHandlerProperties: Record<string, string> | null;
  resolvedFrom: RetryAlertLevel | null;
  silencedAt: RetryAlertLevel | null;
}

const toUsageRow = (r: RawUsageRow): RetryUsageRow => ({
  integrationId: r.subscriptionId,
  integrationName: r.subscriptionName,
  groupId: r.groupId,
  groupName: r.groupName,
  used: r.attemptsUsed,
  total: r.maxAttemptsTotal,
  exhausted: r.exhausted,
  lastAttemptOn: r.lastAttemptOn,
  resolvedHandlerId: r.resolvedHandlerId,
  resolvedHandlerProperties: r.resolvedHandlerProperties ?? {},
  resolvedFrom: r.resolvedFrom,
  silencedAt: r.silencedAt,
  override: {
    alertMode: r.alertMode ?? "Inherit",
    alertHandlerId: r.overrideHandlerId,
    alertHandlerProperties: r.overrideHandlerProperties ?? {},
  },
  // Only an alert that was actually raised has an outcome. Delivery is reported apart from
  // the claim because the two can disagree, and the disagreement is the whole point.
  alert: r.exhaustedNotifiedOn
    ? { claimedOn: r.exhaustedNotifiedOn, delivered: r.alertDelivered, error: r.alertError }
    : null,
});

interface RawAttempt {
  xchangeId: string;
  attemptNumber: number | null;
  failedOn: string;
  exception: string;
  retryPending: boolean;
  retryBlockedReason: string | null;
}

export const retryPolicyMethods = {
  async listRetryPolicies(): Promise<RetryPolicyListRow[]> {
    const [res, subs] = await Promise.all([
      get<SearchyResponse<RawRetryPolicyRow>>("/retrypolicies"),
      getEnrichment<SearchyResponse<{ retryPolicyId: number | null }>>("/subscriptions", { result: [], totalCount: 0 }),
    ]);
    const countByRetryPolicy = new Map<number, number>();
    for (const s of subs.result ?? []) {
      if (s.retryPolicyId == null) continue;
      countByRetryPolicy.set(s.retryPolicyId, (countByRetryPolicy.get(s.retryPolicyId) ?? 0) + 1);
    }
    return (res.result ?? []).map((p) => ({
      id: p.id,
      name: p.name,
      groupCount: p.groupCount,
      createdOn: "",
      usedByCount: countByRetryPolicy.get(p.id) ?? 0,
    }));
  },

  async searchRetryPolicies(query: {
    search: string;
    offset: number;
    limit: number;
  }): Promise<Paged<RetryPolicyListRow>> {
    const qs = buildListQuery({
      filters: [["Name", SEARCHY_RULE.contains, query.search.trim()]],
      offset: query.offset,
      limit: query.limit,
    });
    const [res, subs] = await Promise.all([
      get<SearchyResponse<RawRetryPolicyRow>>(`/retrypolicies?${qs}`),
      getEnrichment<SearchyResponse<{ retryPolicyId: number | null }>>("/subscriptions", { result: [], totalCount: 0 }),
    ]);
    const countByRetryPolicy = new Map<number, number>();
    for (const s of subs.result ?? []) {
      if (s.retryPolicyId == null) continue;
      countByRetryPolicy.set(s.retryPolicyId, (countByRetryPolicy.get(s.retryPolicyId) ?? 0) + 1);
    }
    return {
      total: res.totalCount,
      result: (res.result ?? []).map((p) => ({
        id: p.id,
        name: p.name,
        groupCount: p.groupCount,
        createdOn: "",
        usedByCount: countByRetryPolicy.get(p.id) ?? 0,
      })),
    };
  },

  getRetryPolicy: fetchDetail,

  async createRetryPolicy({ name }: { name: string }): Promise<RetryPolicy> {
    const id = await post<number>("/retrypolicies", { name, groups: [] });
    return { id, name, groups: [], createdOn: "", alertHandlerId: null, alertHandlerProperties: {} };
  },

  async updateRetryPolicy(
    id: number,
    changes: {
      name: string;
      groups: RetryGroup[];
      alertHandlerId: string | null;
      alertHandlerProperties: Record<string, string>;
    },
  ): Promise<RetryPolicy> {
    // Update replaces the whole policy, so every field it accepts has to be sent back. Omitting
    // the alert cleared it on the server on every save — the settings were still on screen, and
    // gone from the database.
    await post(`/retrypolicies/${id}`, {
      name: changes.name,
      groups: changes.groups.map(toRawGroup),
      alertHandlerId: changes.alertHandlerId,
      alertHandlerProperties: changes.alertHandlerProperties,
    });
    return { id, ...changes, createdOn: "" };
  },

  async getRetryUsage(policyId: number): Promise<RetryUsageRow[]> {
    const rows = await post<RawUsageRow[]>(`/retrypolicies/${policyId}/usage`, {});
    return (rows ?? []).map(toUsageRow);
  },

  async getIntegrationRetryUsage(integrationId: number): Promise<RetryUsageRow[]> {
    const rows = await post<RawUsageRow[]>(`/subscriptions/${integrationId}/retryusage`, {});
    return (rows ?? []).map(toUsageRow);
  },

  async getRetryAttempts(
    policyId: number,
    pair: { integrationId: number; groupId: string },
  ): Promise<RetryAttempts> {
    const res = await post<{ total: number; attempts: RawAttempt[] }>(
      `/retrypolicies/${policyId}/attempts`,
      { subscriptionId: pair.integrationId, groupId: pair.groupId },
    );
    return {
      total: res?.total ?? 0,
      attempts: (res?.attempts ?? []).map((a) => ({
        exchangeId: a.xchangeId,
        attemptNumber: a.attemptNumber,
        failedOn: a.failedOn,
        error: a.exception,
        retryPending: a.retryPending,
        blockedReason: a.retryBlockedReason,
      })),
    };
  },

  async resetRetryUsage(policyId: number, pair?: { integrationId?: number; groupId?: string }): Promise<void> {
    await post(`/retrypolicies/${policyId}/resetusage`, {
      subscriptionId: pair?.integrationId ?? null,
      groupId: pair?.groupId ?? null,
    });
  },

  async resetIntegrationRetryUsage(integrationId: number, groupId?: string): Promise<void> {
    await post(`/subscriptions/${integrationId}/resetretryusage`, { groupId: groupId ?? null });
  },

  async saveRetryAlertOverride(
    policyId: number,
    input: { integrationId: number; groupId: string } & RetryAlertConfig,
  ): Promise<void> {
    await post(`/retrypolicies/${policyId}/savealertoverride`, {
      subscriptionId: input.integrationId,
      groupId: input.groupId,
      alertMode: input.alertMode,
      alertHandlerId: input.alertHandlerId,
      alertHandlerProperties: input.alertHandlerProperties,
    });
  },

  async deleteRetryPolicy(id: number): Promise<void> {
    await request(`/retrypolicies/${id}`, { method: "DELETE" });
  },

  async testRetryPolicy(input: {
    groups: RetryGroup[];
    resultType: RetryResultType;
    content: string;
    attempts: number;
  }): Promise<RetryTestAttempt[]> {
    const res = await post<{ attempts: RawTestAttempt[] }>("/retrypolicies/test", {
      groups: input.groups.map(toRawGroup),
      resultType: input.resultType,
      content: input.content,
      attemptsToSimulate: input.attempts,
    });
    return (res.attempts ?? []).map((a) => ({
      attempt: a.attemptNumber,
      shouldRetry: a.shouldRetry,
      delaySeconds: a.delaySeconds ?? undefined,
      matchedGroup: a.matchedGroupName ?? undefined,
      reason: a.reason,
    }));
  },
} satisfies Partial<ApiClient>;
