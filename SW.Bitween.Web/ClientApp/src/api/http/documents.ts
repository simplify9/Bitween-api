import type { ApiClient } from "../client";
import type { InformationType, InformationTypeDetail, InformationTypeFormat, InformationTypeRow } from "../types";
import { get, post, request } from "./request";

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
  const d = await get<RawDocument>(`/documents/${id}`);
  return {
    ...toInformationType(d),
    // Populated once their domains are wired (integrations = Batch 2, bus gateways =
    // Batch 3, exchanges = Batch 4); trail is available server-side but deferred too.
    integrationSetups: [],
    busGateways: [],
    trail: [],
    recentExchanges: [],
  };
}

export const documentMethods = {
  async listInformationTypes(): Promise<InformationTypeRow[]> {
    const res = await get<SearchyResponse<RawDocument>>("/documents");
    // usedByCount depends on integrations/bus gateways, not wired until later batches.
    return (res.result ?? []).map((d) => ({ ...toInformationType(d), usedByCount: 0 }));
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
