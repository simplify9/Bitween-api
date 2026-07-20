import type { ApiClient } from "../client";
import { ApiRequestError, type IntegrationType, type Partner, type PartnerDetail, type PartnerRow } from "../types";
import { get, post, request } from "./request";

// The built-in SYSTEM partner (Partner.SystemId) can't be renamed or deleted.
const SYSTEM_PARTNER_ID = 1;

// ——— backend shapes (camelCase over the wire) ———
interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawPartnerRow {
  id: number;
  name: string;
  subscriptionsCount: number;
  keys: number;
}
interface RawKeyAndValue {
  key: string;
  value: string;
}
interface RawSubscriptionRef {
  id: number;
  name: string;
  type: number | string;
}
interface RawPartnerDetail {
  name: string;
  apiCredentials: RawKeyAndValue[] | null;
  subscriptions: RawSubscriptionRef[] | null;
  adapterProperties: Record<string, string> | null;
}

// GET masks keys as `<first-5>...(hidden)`; recover the visible prefix.
const MASK_SUFFIX = "...(hidden)";
const keyPrefixOf = (masked: string): string =>
  masked.endsWith(MASK_SUFFIX) ? masked.slice(0, -MASK_SUFFIX.length) : masked;

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

async function requireDetail(id: number): Promise<RawPartnerDetail> {
  const d = await get<RawPartnerDetail | null>(`/partners/${id}`);
  if (!d) throw new ApiRequestError("NOT_FOUND", "This partner no longer exists.");
  return d;
}

/**
 * `POST /partners/{id}` REPLACES the whole credential set, so every write must
 * carry the full list. The backend matches credentials by name and keeps the
 * stored secret, so re-sending the masked values is safe (verified against the
 * live DB); omitting one revokes it. `mutate` produces the next list.
 */
async function writePartner(
  id: number,
  d: RawPartnerDetail,
  patch: { name?: string; adapterProperties?: Record<string, string> },
  mutate: (creds: RawKeyAndValue[]) => RawKeyAndValue[] = (c) => c,
): Promise<Partner> {
  const name = patch.name ?? d.name;
  const adapterProperties = patch.adapterProperties ?? d.adapterProperties ?? {};
  const apiCredentials = mutate((d.apiCredentials ?? []).map((c) => ({ key: c.key, value: c.value })));
  await post(`/partners/${id}`, { name, adapterProperties, apiCredentials });
  return { id, name, adapterProperties, isSystem: id === SYSTEM_PARTNER_ID, createdOn: "" };
}

export const partnerMethods = {
  async listPartners(): Promise<PartnerRow[]> {
    const res = await get<SearchyResponse<RawPartnerRow>>("/partners");
    return (res.result ?? []).map((p) => ({
      id: p.id,
      name: p.name,
      adapterProperties: {},
      isSystem: p.id === SYSTEM_PARTNER_ID,
      createdOn: "",
      credentialCount: p.keys,
      usedByCount: p.subscriptionsCount,
    }));
  },

  async getPartner(id: number): Promise<PartnerDetail> {
    const d = await requireDetail(id);
    return {
      id,
      name: d.name,
      adapterProperties: d.adapterProperties ?? {},
      isSystem: id === SYSTEM_PARTNER_ID,
      createdOn: "",
      apiCredentials: (d.apiCredentials ?? []).map((c) => ({
        name: c.key,
        keyPrefix: keyPrefixOf(c.value),
        createdOn: "",
      })),
      integrationSetups: (d.subscriptions ?? []).map((s) => ({
        id: s.id,
        name: s.name,
        type: toIntegrationType(s.type),
      })),
      // Populated once their domains are wired (gateways = Batch 3, exchanges = Batch 4).
      apiGateways: [],
      busGatewayRoutes: [],
      recentExchanges: [],
    };
  },

  async createPartner({ name }: { name: string }): Promise<Partner> {
    const id = await post<number>("/partners", { name });
    return { id, name, adapterProperties: {}, isSystem: false, createdOn: "" };
  },

  async updatePartner(
    id: number,
    changes: { name?: string; adapterProperties?: Record<string, string> },
  ): Promise<Partner> {
    return writePartner(id, await requireDetail(id), changes);
  },

  async deletePartner(id: number): Promise<void> {
    await request(`/partners/${id}`, { method: "DELETE" });
  },

  async addPartnerCredential(id: number, name: string): Promise<{ key: string }> {
    const key = await get<string>("/partners/generatekey");
    const d = await requireDetail(id);
    if ((d.apiCredentials ?? []).some((c) => c.key.toLowerCase() === name.trim().toLowerCase()))
      throw new ApiRequestError("NAME_TAKEN", "This partner already has a key with that name.");
    await writePartner(id, d, {}, (creds) => [...creds, { key: name.trim(), value: key }]);
    return { key };
  },

  async revokePartnerCredential(id: number, name: string): Promise<void> {
    const d = await requireDetail(id);
    await writePartner(id, d, {}, (creds) => creds.filter((c) => c.key !== name));
  },
} satisfies Partial<ApiClient>;
