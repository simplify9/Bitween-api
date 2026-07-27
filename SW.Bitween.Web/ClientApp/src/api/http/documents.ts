import type { ApiClient } from "../client";
import type {
  InformationType,
  InformationTypeDetail,
  InformationTypeFormat,
  InformationTypeRow,
  IntegrationType,
  TrailEntry,
} from "../types";
import { exchangeMethods } from "./exchanges";
import { gatewayMethods } from "./gateways";
import { get, getEnrichment, post, request } from "./request";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawKeyAndValue {
  key: string;
  value: string;
}
interface RawDocument {
  id: number;
  code: string | null;
  name: string;
  documentFormat: InformationTypeFormat;
  busEnabled: boolean;
  busMessageTypeName: string | null;
  duplicateInterval: number;
  disregardsUnfilteredMessages: boolean;
  promotedProperties: RawKeyAndValue[] | null;
}
interface RawSubscriptionRef {
  id: number;
  name: string;
  type: number | string;
}
interface RawTrailEntry {
  createdOn: string;
  code: "Created" | "Updated";
  createdBy: string;
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

async function fetchSubscriptionsByDocument(documentId: number): Promise<RawSubscriptionRef[]> {
  const res = await get<SearchyResponse<RawSubscriptionRef>>(
    `/subscriptions?filter=${encodeURIComponent(`DocumentId:1:${documentId}`)}`,
  );
  return res.result ?? [];
}

async function fetchTrail(documentId: number): Promise<TrailEntry[]> {
  const [trail, accountNames] = await Promise.all([
    get<SearchyResponse<RawTrailEntry>>(`/documents/trail?documentId=${documentId}&limit=8`),
    get<Record<string, string>>("/accounts?lookup=true"),
  ]);
  return (trail.result ?? []).map((t) => ({
    on: t.createdOn,
    action: t.code,
    by: accountNames[t.createdBy] ?? "System",
    byUserId: accountNames[t.createdBy] ? t.createdBy : undefined,
  }));
}

const toInformationType = (d: RawDocument): InformationType => ({
  id: d.id,
  code: d.code ?? undefined,
  name: d.name,
  format: d.documentFormat,
  busEnabled: d.busEnabled,
  busMessageTypeName: d.busMessageTypeName ?? undefined,
  duplicateIntervalMinutes: d.duplicateInterval,
  disregardsUnfilteredMessages: d.disregardsUnfilteredMessages,
  promotedProperties: (d.promotedProperties ?? []).map((p) => ({ key: p.key, path: p.value })),
  createdOn: "",
});

async function fetchDetail(id: number): Promise<InformationTypeDetail> {
  const [d, subs, busGateways, recentExchanges, trail] = await Promise.all([
    get<RawDocument>(`/documents/${id}`),
    fetchSubscriptionsByDocument(id),
    gatewayMethods.listBusGateways(),
    exchangeMethods.searchExchanges({ informationTypeId: id, offset: 0, limit: 8 }),
    fetchTrail(id),
  ]);
  return {
    ...toInformationType(d),
    integrationSetups: subs.map((s) => ({ id: s.id, name: s.name, type: toIntegrationType(s.type) })),
    busGateways: busGateways
      .filter((g) => g.informationTypeId === id)
      .map((g) => ({ gatewayId: g.id, gatewayName: g.name })),
    trail,
    recentExchanges: recentExchanges.result.map((x) => ({
      id: x.id,
      partnerName: x.partnerName ?? undefined,
      informationTypeCode: x.informationTypeCode,
      status: x.status,
      on: x.startedOn,
    })),
  };
}

export const documentMethods = {
  async listInformationTypes(): Promise<InformationTypeRow[]> {
    const [res, subs] = await Promise.all([
      get<SearchyResponse<RawDocument>>("/documents"),
      getEnrichment<SearchyResponse<{ documentId: number }>>("/subscriptions", { result: [], totalCount: 0 }),
    ]);
    const countByDocument = new Map<number, number>();
    for (const s of subs.result ?? [])
      countByDocument.set(s.documentId, (countByDocument.get(s.documentId) ?? 0) + 1);
    return (res.result ?? []).map((d) => ({ ...toInformationType(d), usedByCount: countByDocument.get(d.id) ?? 0 }));
  },

  getInformationType: fetchDetail,

  async createInformationType(input: {
    name: string;
    code?: string;
    format: InformationTypeFormat;
    busEnabled?: boolean;
    busMessageTypeName?: string;
  }): Promise<InformationType> {
    const id = await post<number>("/documents", {
      code: input.code?.trim() || undefined,
      name: input.name,
      documentFormat: input.format,
      busEnabled: input.busEnabled ?? false,
      busMessageTypeName: input.busEnabled ? input.busMessageTypeName : undefined,
    });
    return fetchDetail(id);
  },

  async updateInformationType(
    id: number,
    changes: Omit<InformationType, "id" | "createdOn">,
  ): Promise<InformationType> {
    await post(`/documents/${id}`, {
      id,
      code: changes.code?.trim() || undefined,
      name: changes.name,
      documentFormat: changes.format,
      busEnabled: changes.busEnabled,
      busMessageTypeName: changes.busEnabled ? changes.busMessageTypeName : undefined,
      duplicateInterval: changes.duplicateIntervalMinutes,
      disregardsUnfilteredMessages: changes.disregardsUnfilteredMessages,
      promotedProperties: changes.promotedProperties.map((p) => ({ key: p.key, value: p.path })),
    });
    return fetchDetail(id);
  },

  async deleteInformationType(id: number): Promise<void> {
    await request(`/documents/${id}`, { method: "DELETE" });
  },
} satisfies Partial<ApiClient>;
