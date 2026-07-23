import type { ApiClient } from "../client";
import {
  ApiRequestError,
  type IntegrationType,
  type RetryDelay,
  type RetryGroup,
  type RetryPolicy,
  type RetryPolicyDetail,
  type RetryPolicyListRow,
  type RetryResultType,
  type RetryTestAttempt,
} from "../types";
import { get, post, request } from "./request";

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
interface RawRetryGroup extends Omit<RetryGroup, "budget"> {
  budget?: RawRetryBudget | null;
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
    integrations: subs.map((s) => ({ id: s.id, name: s.name, type: toIntegrationType(s.type) })),
  };
}

export const retryPolicyMethods = {
  async listRetryPolicies(): Promise<RetryPolicyListRow[]> {
    const [res, subs] = await Promise.all([
      get<SearchyResponse<RawRetryPolicyRow>>("/retrypolicies"),
      get<SearchyResponse<{ retryPolicyId: number | null }>>("/subscriptions"),
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

  getRetryPolicy: fetchDetail,

  async createRetryPolicy({ name }: { name: string }): Promise<RetryPolicy> {
    const id = await post<number>("/retrypolicies", { name, groups: [] });
    return { id, name, groups: [], createdOn: "" };
  },

  async updateRetryPolicy(id: number, changes: { name: string; groups: RetryGroup[] }): Promise<RetryPolicy> {
    await post(`/retrypolicies/${id}`, { name: changes.name, groups: changes.groups.map(toRawGroup) });
    return { id, name: changes.name, groups: changes.groups, createdOn: "" };
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
