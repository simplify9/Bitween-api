import type { ApiClient } from "../client";
import {
  ApiRequestError,
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
  const r = await get<RawRetryPolicy | null>(`/retrypolicies/${id}`);
  if (!r) throw new ApiRequestError("NOT_FOUND", "This retry policy no longer exists.");
  return {
    id,
    name: r.name,
    groups: (r.groups ?? []).map(toGroup),
    createdOn: "",
    // Populated once integrations are wired (Batch 2).
    integrations: [],
  };
}

export const retryPolicyMethods = {
  async listRetryPolicies(): Promise<RetryPolicyListRow[]> {
    const res = await get<SearchyResponse<RawRetryPolicyRow>>("/retrypolicies");
    return (res.result ?? []).map((p) => ({
      id: p.id,
      name: p.name,
      groupCount: p.groupCount,
      createdOn: "",
      usedByCount: 0,
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
