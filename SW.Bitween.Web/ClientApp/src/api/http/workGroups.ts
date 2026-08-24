import type { ApiClient } from "../client";
import {
  ApiRequestError,
  type IntegrationType,
  type WorkGroup,
  type WorkGroupDetail,
  type WorkGroupRow,
  type Paged,
} from "../types";
import { get, getEnrichment, post } from "./request";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawWorkGroup {
  id: number;
  name: string;
  busMessageName: string;
  options: { rabbitMqOptions: { prefetch: number | null; priority: number | null } | null } | null;
  processorNodeCount: number | null;
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

async function fetchSubscriptionsByWorkGroup(workGroupId: number): Promise<RawSubscriptionRef[]> {
  const res = await get<SearchyResponse<RawSubscriptionRef>>(
    `/subscriptions?filter=${encodeURIComponent(`WorkGroupId:1:${workGroupId}`)}`,
  );
  return res.result ?? [];
}

// WorkGroup has no CreatedOn column on the backend.
const toWorkGroup = (w: RawWorkGroup): WorkGroup => ({
  id: w.id,
  name: w.name,
  busMessageName: w.busMessageName,
  options: {
    rabbitMqOptions: {
      consumerSettings: {
        prefetch: w.options?.rabbitMqOptions?.prefetch ?? 0,
        priority: w.options?.rabbitMqOptions?.priority ?? 0,
      },
    },
  },
  createdOn: "",
});

// The backend's own default kicks in only when the caller omits the param
// entirely (`request.Limit ??= 20`), so passing an explicit, generously large
// one is how "everything" is asked for — there is no separate "unbounded"
// value the way the shared Searchy endpoints have with size=0.
const EVERYTHING = 1_000_000;

async function fetchRows(): Promise<RawWorkGroup[]> {
  const res = await get<SearchyResponse<RawWorkGroup>>(`/workgroups?offset=0&limit=${EVERYTHING}`);
  return res.result ?? [];
}

async function fetchPagedRows(query: {
  search: string;
  offset: number;
  limit: number;
}): Promise<{ rows: RawWorkGroup[]; total: number }> {
  const params = new URLSearchParams({ offset: String(query.offset), limit: String(query.limit) });
  if (query.search.trim()) params.set("name", query.search.trim());
  const res = await get<SearchyResponse<RawWorkGroup>>(`/workgroups?${params.toString()}`);
  return { rows: res.result ?? [], total: res.totalCount };
}

export const workGroupMethods = {
  async listWorkGroups(): Promise<WorkGroupRow[]> {
    const [rows, subs] = await Promise.all([
      fetchRows(),
      getEnrichment<SearchyResponse<{ workGroupId: number | null }>>("/subscriptions", { result: [], totalCount: 0 }),
    ]);
    const countByWorkGroup = new Map<number, number>();
    for (const s of subs.result ?? []) {
      if (s.workGroupId == null) continue;
      countByWorkGroup.set(s.workGroupId, (countByWorkGroup.get(s.workGroupId) ?? 0) + 1);
    }
    return rows.map((w) => ({
      ...toWorkGroup(w),
      usedByCount: countByWorkGroup.get(w.id) ?? 0,
      consumerCount: w.processorNodeCount ?? 0,
    }));
  },

  async searchWorkGroups(query: {
    search: string;
    offset: number;
    limit: number;
  }): Promise<Paged<WorkGroupRow>> {
    const [{ rows, total }, subs] = await Promise.all([
      fetchPagedRows(query),
      getEnrichment<SearchyResponse<{ workGroupId: number | null }>>("/subscriptions", { result: [], totalCount: 0 }),
    ]);
    const countByWorkGroup = new Map<number, number>();
    for (const s of subs.result ?? []) {
      if (s.workGroupId == null) continue;
      countByWorkGroup.set(s.workGroupId, (countByWorkGroup.get(s.workGroupId) ?? 0) + 1);
    }
    return {
      total,
      result: rows.map((w) => ({
        ...toWorkGroup(w),
        usedByCount: countByWorkGroup.get(w.id) ?? 0,
        consumerCount: w.processorNodeCount ?? 0,
      })),
    };
  },

  async getWorkGroup(id: number): Promise<WorkGroupDetail> {
    const [rows, subs] = await Promise.all([fetchRows(), fetchSubscriptionsByWorkGroup(id)]);
    const w = rows.find((x) => x.id === id);
    if (!w) throw new ApiRequestError("NOT_FOUND", "This work group no longer exists.");
    return {
      ...toWorkGroup(w),
      integrations: subs.map((s) => ({ id: s.id, name: s.name, type: toIntegrationType(s.type) })),
    };
  },

  async createWorkGroup(input: {
    name: string;
    busMessageName: string;
    prefetch: number;
    priority: number;
  }): Promise<WorkGroup> {
    const created = await post<{ id: number }>("/workgroups", {
      name: input.name,
      busMessageName: input.busMessageName,
      options: { rabbitMqOptions: { prefetch: input.prefetch, priority: input.priority } },
    });
    return {
      id: created.id,
      name: input.name,
      busMessageName: input.busMessageName,
      options: { rabbitMqOptions: { consumerSettings: { prefetch: input.prefetch, priority: input.priority } } },
      createdOn: "",
    };
  },

  async updateWorkGroup(
    id: number,
    changes: { name: string; busMessageName: string; prefetch: number; priority: number },
  ): Promise<WorkGroup> {
    await post(`/workgroups/${id}`, {
      name: changes.name,
      busMessageName: changes.busMessageName,
      options: { rabbitMqOptions: { prefetch: changes.prefetch, priority: changes.priority } },
    });
    return {
      id,
      name: changes.name,
      busMessageName: changes.busMessageName,
      options: {
        rabbitMqOptions: { consumerSettings: { prefetch: changes.prefetch, priority: changes.priority } },
      },
      createdOn: "",
    };
  },

  async deleteWorkGroup(id: number): Promise<void> {
    await post(`/workgroups/${id}/delete`, {});
  },
} satisfies Partial<ApiClient>;
