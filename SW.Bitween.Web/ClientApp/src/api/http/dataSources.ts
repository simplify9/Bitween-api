import type { ApiClient } from "../client";
import type {
  DataSourceProvider,
  DataSourceDetail,
  DataSourceInspectResult,
  DataSourceRow,
  DataSourceTelemetry,
  DataSourceTestResult,
  Paged,
} from "../types";
import { get, post, request } from "./request";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}

interface RawDataSource {
  id: number;
  name: string;
  adapterId: string;
  kind: string;
  inactive: boolean | null;
  deduplicationWindowDays: number;
  softMemoryLimitMb?: number | null;
  hardMemoryLimitMb?: number | null;
  cpuPercentLimit?: number | null;
  cpuLimitSamples?: number | null;
  gatewayCount: number;
  subscriptionCount?: number | null;
  lastKnownState: string | null;
  lastHeartbeatOn: string | null;
  lastException: string | null;
  consecutiveFailures: number;
  ownedByNode: string | null;
  // Only the detail endpoint returns these — the list deliberately carries no connection
  // settings at all, because even masked they are exposure with no purpose in a table.
  properties?: Record<string, string> | null;
  secretProperties?: string[] | null;
}

interface RawTestStage {
  name: string;
  succeeded: boolean;
  detail: string | null;
}
interface RawTestResult {
  succeeded: boolean;
  error: string | null;
  stages: RawTestStage[] | null;
}

const toRow = (raw: RawDataSource): DataSourceRow => ({
  id: raw.id,
  name: raw.name,
  adapterId: raw.adapterId,
  kind: raw.kind,
  inactive: raw.inactive ?? false,
  deduplicationWindowDays: raw.deduplicationWindowDays,
  softMemoryLimitMb: raw.softMemoryLimitMb ?? 0,
  hardMemoryLimitMb: raw.hardMemoryLimitMb ?? 0,
  cpuPercentLimit: raw.cpuPercentLimit ?? 0,
  cpuLimitSamples: raw.cpuLimitSamples ?? 0,
  gatewayCount: raw.gatewayCount,
  subscriptionCount: raw.subscriptionCount ?? 0,
  lastKnownState: raw.lastKnownState,
  lastHeartbeatOn: raw.lastHeartbeatOn,
  lastException: raw.lastException,
  consecutiveFailures: raw.consecutiveFailures,
  ownedByNode: raw.ownedByNode,
});

const toDetail = (raw: RawDataSource): DataSourceDetail => ({
  ...toRow(raw),
  properties: raw.properties ?? {},
  secretProperties: raw.secretProperties ?? [],
});

// The backend's own default only applies when the caller omits the parameter, so asking for
// "everything" means naming a generously large number — same as the work groups module.
const EVERYTHING = 1_000_000;

export const dataSourceMethods = {
  async listDataSourceProviders(): Promise<DataSourceProvider[]> {
    const providers = await get<DataSourceProvider[]>(`/datasources/Providers`);
    return providers ?? [];
  },

  async listDataSources(): Promise<DataSourceRow[]> {
    const res = await get<SearchyResponse<RawDataSource>>(
      `/datasources?offset=0&limit=${EVERYTHING}`,
    );
    return (res.result ?? []).map(toRow);
  },

  async searchDataSources(query: {
    search: string;
    offset: number;
    limit: number;
  }): Promise<Paged<DataSourceRow>> {
    const params = new URLSearchParams({
      offset: String(query.offset),
      limit: String(query.limit),
    });
    if (query.search.trim()) params.set("name", query.search.trim());

    const res = await get<SearchyResponse<RawDataSource>>(`/datasources?${params.toString()}`);
    return { total: res.totalCount, result: (res.result ?? []).map(toRow) };
  },

  async getDataSource(id: number): Promise<DataSourceDetail> {
    return toDetail(await get<RawDataSource>(`/datasources/${id}`));
  },

  async createDataSource(input: {
    name: string;
    adapterId: string;
    properties: Record<string, string>;
    secretProperties: string[];
    kind?: string;
  }): Promise<{ id: number }> {
    const id = await post<number>("/datasources", {
      name: input.name,
      adapterId: input.adapterId,
      // What the provider says it connects to. Only a Broker can back a bus gateway.
      kind: input.kind ?? "Broker",
      properties: input.properties,
      secretProperties: input.secretProperties,
      inactive: false,
      deduplicationWindowDays: 30,
      softMemoryLimitMb: 0,
      hardMemoryLimitMb: 0,
      cpuPercentLimit: 0,
      cpuLimitSamples: 0,
    });
    return { id };
  },

  async updateDataSource(
    id: number,
    changes: {
      name: string;
      adapterId: string;
      kind: string;
      properties: Record<string, string>;
      secretProperties: string[];
      inactive: boolean;
      deduplicationWindowDays: number;
      softMemoryLimitMb: number;
      hardMemoryLimitMb: number;
      cpuPercentLimit: number;
      cpuLimitSamples: number;
    },
  ): Promise<void> {
    await post(`/datasources/${id}`, changes);
  },

  async deleteDataSource(id: number): Promise<void> {
    await request(`/datasources/${id}`, { method: "DELETE" });
  },

  async inspectDataSource(
    id: number,
    command: string,
    args?: Record<string, string>,
  ): Promise<DataSourceInspectResult> {
    return post<DataSourceInspectResult>(`/datasources/${id}/inspect`, {
      command,
      arguments: args,
    });
  },

  /** Live, from the heartbeat. Scoped to the node that answers — see the backend handler. */
  async getDataSourceTelemetry(id: number): Promise<DataSourceTelemetry> {
    const raw = await get<DataSourceTelemetry>(`/datasources/${id}/telemetry`);
    return { ...raw, details: raw.details ?? {}, commands: raw.commands ?? [] };
  },

  /**
   * Reaches out to the broker and reports stage by stage. It runs the real adapter against the
   * stored settings but never consumes, so the queues its gateways read are inspected rather than
   * drained.
   */
  async testDataSource(id: number): Promise<DataSourceTestResult> {
    const raw = await post<RawTestResult>(`/datasources/${id}/test`, {});
    return {
      succeeded: raw.succeeded,
      error: raw.error,
      stages: (raw.stages ?? []).map((s) => ({
        name: s.name,
        succeeded: s.succeeded,
        detail: s.detail,
      })),
    };
  },
} satisfies Partial<ApiClient>;
