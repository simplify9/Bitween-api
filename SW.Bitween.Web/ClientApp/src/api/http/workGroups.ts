import type { ApiClient } from "../client";
import { ApiRequestError, type WorkGroup, type WorkGroupDetail, type WorkGroupRow } from "../types";
import { get, post } from "./request";

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

async function fetchRows(): Promise<RawWorkGroup[]> {
  const res = await get<SearchyResponse<RawWorkGroup>>("/workgroups");
  return res.result ?? [];
}

export const workGroupMethods = {
  async listWorkGroups(): Promise<WorkGroupRow[]> {
    const rows = await fetchRows();
    return rows.map((w) => ({
      ...toWorkGroup(w),
      // Depends on integrations (Batch 2) to know assignment counts.
      usedByCount: 0,
      consumerCount: w.processorNodeCount ?? 0,
    }));
  },

  async getWorkGroup(id: number): Promise<WorkGroupDetail> {
    const rows = await fetchRows();
    const w = rows.find((x) => x.id === id);
    if (!w) throw new ApiRequestError("NOT_FOUND", "This work group no longer exists.");
    // Populated once integrations are wired (Batch 2).
    return { ...toWorkGroup(w), integrations: [] };
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
