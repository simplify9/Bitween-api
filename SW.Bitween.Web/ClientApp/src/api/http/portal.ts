import type { ApiClient } from "../client";
import type {
  PortalExchangePage,
  PortalExchangeQuery,
  PortalMapping,
  PortalMappingPlace,
  PortalMappingSample,
  PortalSummary,
} from "../types";
import { get, post } from "./request";

/** The partner portal: always the signed-in partner's own data, scoped by the server. */
export const portalMethods: Pick<
  ApiClient,
  | "getPortalSummary"
  | "listPortalSubscriptions"
  | "searchPortalExchanges"
  | "listPortalMappings"
  | "getPortalMapping"
  | "savePortalMapping"
  | "resetPortalMapping"
  | "previewPortalMapping"
  | "listPortalMappingSamples"
  | "getPortalMappingSample"
  | "listPortalPropertyKeys"
> = {
  getPortalSummary: () => get<PortalSummary>("/portal/summary"),

  listPortalSubscriptions: () =>
    get<{ id: number; name: string; type: string; documentName: string; paused: boolean }[]>("/portal/subscriptions").then(
      (rows) => rows.map((r) => ({ ...r, id: String(r.id) })),
    ),

  searchPortalExchanges: (query: PortalExchangeQuery) => {
    const params = new URLSearchParams({ page: String(query.page), size: String(query.size) });
    if (query.status) params.set("status", query.status);
    if (query.subscriptionId) params.set("subscriptionId", query.subscriptionId);
    return get<PortalExchangePage>(`/portal/xchanges?${params}`);
  },

  listPortalMappings: () => get<PortalMappingPlace[]>("/portal/mappings"),
  getPortalMapping: (subscriptionId) => get<PortalMapping>(`/portal/mapping?subscriptionId=${subscriptionId}`),
  savePortalMapping: async (subscriptionId, mapperProperties) => {
    await post("/portal/savemapping", { subscriptionId, mapperProperties });
  },
  resetPortalMapping: async (subscriptionId) => {
    await post("/portal/resetmapping", { subscriptionId });
  },
  previewPortalMapping: async (input) => {
    const r = await post<{
      outputDocument?: string | null;
      contentType?: string | null;
      ruleErrors?: { target: string; reason: string }[] | null;
      error?: string | null;
    }>("/portal/previewmapping", input);
    return {
      outputDocument: r.outputDocument ?? null,
      contentType: r.contentType ?? null,
      ruleErrors: r.ruleErrors ?? [],
      error: r.error ?? null,
    };
  },
  listPortalMappingSamples: (subscriptionId) =>
    get<PortalMappingSample[]>(`/portal/mappingsamples?subscriptionId=${subscriptionId}`),
  getPortalMappingSample: async (xchangeId) =>
    (await get<{ data: string }>(`/portal/mappingsample?xchangeId=${encodeURIComponent(xchangeId)}`)).data,
  listPortalPropertyKeys: () => get<string[]>("/portal/propertykeys"),
};
