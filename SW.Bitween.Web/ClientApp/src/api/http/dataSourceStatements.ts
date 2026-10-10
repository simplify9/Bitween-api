import type { ApiClient } from "../client";
import type { DataSourceStatement, DataSourceStatementUsage } from "../types";
import { buildListQuery } from "./searchQuery";
import { get, post, request } from "./request";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}

/**
 * What a save answers with. `checked` is false when the database was never asked — the adapter for
 * this connection is not running on the node that handled the request, so there was nobody to
 * validate against. Worth saying: a save that reported nothing would look exactly like one that
 * had been verified.
 */
export interface SaveResult {
  id: number;
  checked: boolean;
}

interface RawStatement {
  id: number;
  dataSourceId: number;
  name: string;
  sql: string;
  cursorColumn: string | null;
  keyColumn: string | null;
  description: string | null;
  workGroupId: number | null;
  workGroupName: string | null;
  inactive: boolean;
  usageCount: number;
  createdOn: string;
  createdBy: string | null;
  modifiedOn: string | null;
  modifiedBy: string | null;
}

const toStatement = (raw: RawStatement): DataSourceStatement => ({
  id: raw.id,
  dataSourceId: raw.dataSourceId,
  name: raw.name,
  sql: raw.sql,
  description: raw.description,
  workGroupId: raw.workGroupId,
  workGroupName: raw.workGroupName,
  inactive: raw.inactive,
  cursorColumn: raw.cursorColumn ?? null,
  keyColumn: raw.keyColumn ?? null,
  usageCount: raw.usageCount,
  createdOn: raw.createdOn,
  createdBy: raw.createdBy,
  modifiedOn: raw.modifiedOn,
  modifiedBy: raw.modifiedBy,
});

const EVERYTHING = 1_000_000;

export const dataSourceStatementMethods = {
  async listDataSourceStatements(dataSourceId: number): Promise<DataSourceStatement[]> {
    // Filtered server-side by data source: a statement is only ever meaningful next to the
    // connection it runs against, and no screen wants all of them at once. Rule 1 is EqualsTo.
    const query = buildListQuery({
      filters: [["DataSourceId", 1, dataSourceId]],
      sort: ["Name", 1],
      offset: 0,
      limit: EVERYTHING,
    });
    const res = await get<SearchyResponse<RawStatement>>(`/datasourcestatements?${query}`);
    return (res.result ?? []).map(toStatement);
  },

  async createDataSourceStatement(
    dataSourceId: number,
    input: {
      name: string;
      sql: string;
      description?: string | null;
      workGroupId?: number | null;
      cursorColumn?: string | null;
      keyColumn?: string | null;
    },
  ): Promise<SaveResult> {
    // The data source travels in the body, not the route: POST /datasourcestatements/{id} already
    // means "update that statement", so a keyed create would collide with it.
    const saved = await post<SaveResult>(`/datasourcestatements`, {
      dataSourceId,
      name: input.name,
      sql: input.sql,
      description: input.description ?? null,
      workGroupId: input.workGroupId ?? null,
      cursorColumn: input.cursorColumn || null,
      keyColumn: input.keyColumn || null,
      inactive: false,
    });
    return saved;
  },

  async updateDataSourceStatement(
    id: number,
    changes: {
      name: string;
      sql: string;
      description?: string | null;
      workGroupId?: number | null;
      inactive: boolean;
      cursorColumn?: string | null;
      keyColumn?: string | null;
    },
  ): Promise<SaveResult> {
    return post<SaveResult>(`/datasourcestatements/${id}`, {
      name: changes.name,
      sql: changes.sql,
      description: changes.description ?? null,
      workGroupId: changes.workGroupId ?? null,
      cursorColumn: changes.cursorColumn || null,
      keyColumn: changes.keyColumn || null,
      inactive: changes.inactive,
    });
  },

  async deleteDataSourceStatement(id: number): Promise<void> {
    await request(`/datasourcestatements/${id}`, { method: "DELETE" });
  },

  /** Which subscriptions name it, in which slot. What decides whether it can safely change. */
  async getDataSourceStatementUsage(id: number): Promise<DataSourceStatementUsage> {
    const raw = await post<DataSourceStatementUsage>(`/datasourcestatements/${id}/usage`, {});
    return { ...raw, usedBy: raw.usedBy ?? [] };
  },
} satisfies Partial<ApiClient>;
