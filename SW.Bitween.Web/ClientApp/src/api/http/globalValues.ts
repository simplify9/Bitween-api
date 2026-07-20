import type { ApiClient } from "../client";
import type { GlobalValuesSetDetail, GlobalValuesSetRow } from "../types";
import { get, post } from "./request";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawValueSet {
  id: string;
  name: string;
  values: Record<string, string> | null;
}

// GlobalAdapterValuesSet has no CreatedOn column on the backend.
const toRow = (r: RawValueSet): GlobalValuesSetRow => ({
  id: r.id,
  name: r.name,
  values: r.values ?? {},
  createdOn: "",
  // Depends on integrations (Batch 2) to know which adapters reference a set.
  usedByCount: 0,
});

export const globalValuesMethods = {
  async listValueSets(): Promise<GlobalValuesSetRow[]> {
    const res = await get<SearchyResponse<RawValueSet>>("/globaladaptervaluessets");
    return (res.result ?? []).map(toRow);
  },

  async getValueSet(id: string): Promise<GlobalValuesSetDetail> {
    const r = await get<RawValueSet>(`/globaladaptervaluessets/${id}`);
    return { ...toRow(r), usedBy: [] };
  },

  async createValueSet(input: { id: string; name: string; values: Record<string, string> }): Promise<GlobalValuesSetRow> {
    await post("/globaladaptervaluessets", { id: input.id, name: input.name, values: input.values });
    return toRow(input);
  },

  async updateValueSet(
    id: string,
    changes: { name: string; values: Record<string, string> },
  ): Promise<GlobalValuesSetRow> {
    await post(`/globaladaptervaluessets/${id}`, { name: changes.name, values: changes.values });
    return toRow({ id, ...changes });
  },

  async deleteValueSet(id: string): Promise<void> {
    await post(`/globaladaptervaluessets/${id}/delete`, {});
  },
} satisfies Partial<ApiClient>;
