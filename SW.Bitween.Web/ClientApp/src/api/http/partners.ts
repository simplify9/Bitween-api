import type { ApiClient } from "../client";
import { ApiRequestError, type Partner, type PartnerDetail, type PartnerRow } from "../types";
import { exchangeMethods } from "./exchanges";
import { gatewayMethods } from "./gateways";
import { matchSummary } from "../../lib/match";
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
  propertyKeys: string[] | null;
}
interface RawKeyAndValue {
  key: string;
  value: string;
}
interface RawPartnerDetail {
  name: string;
  apiCredentials: RawKeyAndValue[] | null;
  adapterProperties: Record<string, string> | null;
}

// GET masks keys as `<first-5>...(hidden)`; recover the visible prefix.
const MASK_SUFFIX = "...(hidden)";
const keyPrefixOf = (masked: string): string =>
  masked.endsWith(MASK_SUFFIX) ? masked.slice(0, -MASK_SUFFIX.length) : masked;

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
      // The list endpoint sends property names, never their values — they can be
      // secrets. Anything needing values must fetch the partner's detail.
      adapterProperties: {},
      propertyKeys: p.propertyKeys ?? [],
      isSystem: p.id === SYSTEM_PARTNER_ID,
      createdOn: "",
      credentialCount: p.keys,
      usedByCount: p.subscriptionsCount,
    }));
  },

  /** Light single-field fetch for the mapper editor's test-partner selector — avoids getPartner's gateway/exchange lookups. */
  async getPartnerAdapterProperties(id: number): Promise<Record<string, string>> {
    const d = await requireDetail(id);
    return d.adapterProperties ?? {};
  },

  async getPartner(id: number): Promise<PartnerDetail> {
    const [d, apiGateways, busGateways, recentExchanges] = await Promise.all([
      requireDetail(id),
      gatewayMethods.listApiGateways(),
      gatewayMethods.listBusGateways(),
      exchangeMethods.searchExchanges({ partnerId: id, offset: 0, limit: 8 }),
    ]);
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
      apiGateways: apiGateways
        .filter((g) => g.attachments.some((a) => a.partnerId === id))
        .map((g) => ({ gatewayId: g.id, gatewayName: g.name, urlName: g.urlName })),
      busGatewayRoutes: busGateways.flatMap((g) =>
        g.routes
          .filter((r) => r.partnerId === id)
          .map((r) => ({ gatewayId: g.id, gatewayName: g.name, matchExpression: matchSummary(r.matchExpression) })),
      ),
      recentExchanges: recentExchanges.result.map((x) => ({
        id: x.id,
        informationTypeCode: x.informationTypeCode,
        status: x.status,
        on: x.startedOn,
        promotedProperties: x.promotedProperties,
      })),
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
