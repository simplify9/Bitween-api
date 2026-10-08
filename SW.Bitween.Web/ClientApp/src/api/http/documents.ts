import type { ApiClient } from "../client";
import type {
  InformationType,
  InformationTypeDetail,
  InformationTypeFormat,
  InformationTypeRow,
  SubscriptionType,
  Paged,
} from "../types";
import { exchangeMethods } from "./exchanges";
import { gatewayMethods } from "./gateways";
import { get, post, request } from "./request";
import { buildListQuery, SEARCHY_RULE } from "./searchQuery";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawKeyAndValue {
  key: string;
  value: string;
}
/** Seeded by the model — mirrors Document.AggregationDocumentId on the backend. */
const AGGREGATION_DOCUMENT_ID = 10001;

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
  validationSchema?: string | null;
  /** Counted by the backend — see DocumentRow.UsedByCount. */
  usedByCount: number;
  retiredOn: string | null;
}
interface RawSubscriptionRef {
  id: number;
  name: string;
  type: number | string;
}
const SUB_TYPE_BY_NUM: Record<number, SubscriptionType> = {
  1: "Internal",
  2: "ApiCall",
  4: "Receiving",
  8: "Aggregation",
  16: "GatewayApiCall",
  32: "BusGateway",
  64: "Response",
};
const SUBSCRIPTION_TYPES: SubscriptionType[] = [
  "Receiving",
  "GatewayApiCall",
  "BusGateway",
  "Internal",
  "ApiCall",
  "Aggregation",
  "Response",
];
/** Enums may arrive as the numeric value or the name in any case. */
const toSubscriptionType = (t: number | string): SubscriptionType => {
  if (typeof t === "number") return SUB_TYPE_BY_NUM[t] ?? "Internal";
  return SUBSCRIPTION_TYPES.find((k) => k.toLowerCase() === t.toLowerCase()) ?? "Internal";
};

async function fetchSubscriptionsByDocument(documentId: number): Promise<RawSubscriptionRef[]> {
  const res = await get<SearchyResponse<RawSubscriptionRef>>(
    `/subscriptions?filter=${encodeURIComponent(`DocumentId:1:${documentId}`)}`,
  );
  return res.result ?? [];
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
  validationSchema: d.validationSchema ?? null,
  createdOn: "",
  retiredOn: d.retiredOn ?? null,
  isSystem: d.id === AGGREGATION_DOCUMENT_ID,
});

async function fetchDetail(id: number): Promise<InformationTypeDetail> {
  const [d, subs, busGateways, recentExchanges] = await Promise.all([
    get<RawDocument>(`/documents/${id}`),
    fetchSubscriptionsByDocument(id),
    gatewayMethods.listBusGateways(),
    exchangeMethods.searchExchanges({ informationTypeId: id, offset: 0, limit: 8 }),
  ]);
  return {
    ...toInformationType(d),
    subscriptionSetups: subs.map((s) => ({ id: s.id, name: s.name, type: toSubscriptionType(s.type) })),
    busGateways: busGateways
      .filter((g) => g.informationTypeId === id)
      .map((g) => ({ gatewayId: g.id, gatewayName: g.name })),
    recentExchanges: recentExchanges.result.map((x) => ({
      id: x.id,
      partnerName: x.partnerName ?? undefined,
      informationTypeCode: x.informationTypeCode,
      status: x.status,
      on: x.startedOn,
      promotedProperties: x.promotedProperties,
    })),
  };
}

/** One wire shape for both create and update, so they cannot drift apart. */
const documentBody = (t: Omit<InformationType, "id" | "createdOn">) => ({
  code: t.code?.trim() || undefined,
  name: t.name,
  documentFormat: t.format,
  busEnabled: t.busEnabled,
  // Sent with the bus off too: the name is what a paused type resumes on.
  busMessageTypeName: t.busMessageTypeName || undefined,
  duplicateInterval: t.duplicateIntervalMinutes,
  disregardsUnfilteredMessages: t.disregardsUnfilteredMessages,
  promotedProperties: t.promotedProperties.map((p) => ({ key: p.key, value: p.path })),
  // Empty removes it; the server reads a missing one as "leave it alone".
  validationSchema: t.validationSchema ?? "",
});

export const documentMethods = {
  async listInformationTypes(): Promise<InformationTypeRow[]> {
    const res = await get<SearchyResponse<RawDocument>>("/documents");
    return (res.result ?? []).map((d) => ({ ...toInformationType(d), usedByCount: d.usedByCount }));
  },

  async searchInformationTypes(query: {
    search: string;
    format?: InformationTypeFormat | null;
    busEnabled?: boolean | null;
    offset: number;
    limit: number;
  }): Promise<Paged<InformationTypeRow>> {
    const qs = buildListQuery({
      filters: [
        ["Name", SEARCHY_RULE.contains, query.search.trim()],
        ["DocumentFormat", SEARCHY_RULE.equalsTo, query.format ?? ""],
        ["BusEnabled", SEARCHY_RULE.equalsTo, query.busEnabled == null ? "" : String(query.busEnabled)],
      ],
      offset: query.offset,
      limit: query.limit,
    });
    const res = await get<SearchyResponse<RawDocument>>(`/documents?${qs}`);
    return {
      total: res.totalCount,
      result: (res.result ?? []).map((d) => ({ ...toInformationType(d), usedByCount: d.usedByCount })),
    };
  },

  getInformationType: fetchDetail,

  async createInformationType(
    input: Omit<InformationType, "id" | "createdOn">,
  ): Promise<InformationType> {
    const id = await post<number>("/documents", documentBody(input));
    return fetchDetail(id);
  },

  async updateInformationType(
    id: number,
    changes: Omit<InformationType, "id" | "createdOn">,
  ): Promise<InformationType> {
    await post(`/documents/${id}`, { id, ...documentBody(changes) });
    return fetchDetail(id);
  },

  async deleteInformationType(id: number): Promise<void> {
    await request(`/documents/${id}`, { method: "DELETE" });
  },

  async retireInformationType(id: number): Promise<{ retiredOn: string | null }> {
    // Toggles server-side, so what comes back is the state it landed in rather than
    // one this caller chose.
    const res = await post<{ retiredOn: string | null }>(`/documents/${id}/retire`, {});
    return { retiredOn: res?.retiredOn ?? null };
  },
} satisfies Partial<ApiClient>;
