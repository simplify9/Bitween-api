import type { ApiClient } from "../client";
import type {
  ApiGateway,
  ApiGatewayAttachment,
  ApiGatewayDetail,
  ApiGatewayRow,
  BusGateway,
  BusGatewayDetail,
  BusGatewayRoute,
  BusGatewayRow,
  MatchGroup,
  Paged,
} from "../types";
import { toMatchGroup, toRawMatchExpression, type RawMatchSpec } from "./matchExpression";
import { get, post, request } from "./request";
import { buildListQuery, SEARCHY_RULE } from "./searchQuery";

// ——— backend shapes (camelCase over the wire) ———
interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawApiGatewayPartner {
  partnerId: number;
  subscriptionId: number;
  partnerName: string;
  subscriptionName: string;
}
interface RawApiGateway {
  id: number;
  name: string;
  urlName: string;
  partnersCount: number | null;
  inactive: boolean | null;
  // Search's list projection includes this too (backend change made alongside
  // this batch) — but keep it optional since Create's bare POST response has none.
  partners: RawApiGatewayPartner[] | null;
}
interface RawBusGatewayRoute {
  id: number;
  subscriptionId: number;
  subscriptionName: string | null;
  partnerId: number | null;
  partnerName: string | null;
  matchExpression: RawMatchSpec | null;
}
interface RawBusGateway {
  id: number;
  name: string;
  documentId: number;
  documentName: string | null;
  routesCount: number | null;
  inactive: boolean | null;
  routes: RawBusGatewayRoute[] | null;
}

const toApiGatewayAttachment = (p: RawApiGatewayPartner): ApiGatewayAttachment => ({
  partnerId: p.partnerId,
  partnerName: p.partnerName,
  integrationId: p.subscriptionId,
  integrationName: p.subscriptionName,
});

const toApiGatewayRow = (raw: RawApiGateway): ApiGatewayRow => ({
  id: raw.id,
  name: raw.name,
  urlName: raw.urlName,
  inactive: raw.inactive ?? false,
  createdOn: "",
  partnerCount: raw.partnersCount ?? raw.partners?.length ?? 0,
  attachments: (raw.partners ?? []).map(toApiGatewayAttachment),
});

const toApiGatewayDetail = (raw: RawApiGateway): ApiGatewayDetail => ({
  id: raw.id,
  name: raw.name,
  urlName: raw.urlName,
  inactive: raw.inactive ?? false,
  createdOn: "",
  attachments: (raw.partners ?? []).map(toApiGatewayAttachment),
});

const toBusGatewayRoute = (r: RawBusGatewayRoute): BusGatewayRoute => ({
  id: r.id,
  integrationId: r.subscriptionId,
  integrationName: r.subscriptionName ?? "",
  partnerId: r.partnerId,
  partnerName: r.partnerName,
  matchExpression: toMatchGroup(r.matchExpression),
});

const toBusGatewayRow = (raw: RawBusGateway): BusGatewayRow => ({
  id: raw.id,
  name: raw.name,
  informationTypeId: raw.documentId,
  inactive: raw.inactive ?? false,
  createdOn: "",
  informationTypeCode: raw.documentName ?? "UNKNOWN",
  routeCount: raw.routesCount ?? raw.routes?.length ?? 0,
  routes: (raw.routes ?? []).map(toBusGatewayRoute),
});

const toBusGatewayDetail = (raw: RawBusGateway): BusGatewayDetail => ({
  id: raw.id,
  name: raw.name,
  informationTypeId: raw.documentId,
  inactive: raw.inactive ?? false,
  createdOn: "",
  informationTypeCode: raw.documentName ?? "UNKNOWN",
  informationTypeName: raw.documentName ?? "Unknown",
  routes: (raw.routes ?? []).map(toBusGatewayRoute),
});

export const gatewayMethods = {
  // ——— API gateways ———

  async listApiGateways(): Promise<ApiGatewayRow[]> {
    const res = await get<SearchyResponse<RawApiGateway>>("/apigateways");
    return (res.result ?? []).map(toApiGatewayRow);
  },

  async searchApiGateways(query: { search: string; offset: number; limit: number }): Promise<Paged<ApiGatewayRow>> {
    const qs = buildListQuery({
      filters: [["Name", SEARCHY_RULE.contains, query.search.trim()]],
      offset: query.offset,
      limit: query.limit,
    });
    const res = await get<SearchyResponse<RawApiGateway>>(`/apigateways?${qs}`);
    return { total: res.totalCount, result: (res.result ?? []).map(toApiGatewayRow) };
  },

  async getApiGateway(id: number): Promise<ApiGatewayDetail> {
    return toApiGatewayDetail(await get<RawApiGateway>(`/apigateways/${id}`));
  },

  /** Paged, searched view of one gateway's attachments, for the gateway page's own
   * table — `getApiGateway` keeps returning the full list, still needed by the
   * attach-partner picker's exclude list. */
  async searchGatewayAttachments(
    apiGatewayId: number,
    query: { search: string; offset: number; limit: number },
  ): Promise<Paged<ApiGatewayAttachment>> {
    const params = new URLSearchParams({
      apiGatewayId: String(apiGatewayId),
      offset: String(query.offset),
      limit: String(query.limit),
    });
    if (query.search.trim()) params.set("search", query.search.trim());
    const res = await get<SearchyResponse<RawApiGatewayPartner>>(`/apigateways/attachments?${params.toString()}`);
    return { total: res.totalCount, result: (res.result ?? []).map(toApiGatewayAttachment) };
  },

  async createApiGateway({ name, urlName }: { name: string; urlName: string }): Promise<ApiGateway> {
    const id = await post<number>("/apigateways", { name, urlName, inactive: false });
    return { id, name, urlName, inactive: false, createdOn: "" };
  },

  async updateApiGateway(
    id: number,
    changes: { name: string; urlName: string; inactive: boolean },
  ): Promise<ApiGateway> {
    // Update replaces the record, so every field it accepts has to be sent back —
    // omitting `inactive` would quietly reactivate a paused gateway on a rename.
    await post(`/apigateways/${id}`, {
      name: changes.name,
      urlName: changes.urlName,
      inactive: changes.inactive,
    });
    return { id, ...changes, createdOn: "" };
  },

  async deleteApiGateway(id: number): Promise<void> {
    await request(`/apigateways/${id}`, { method: "DELETE" });
  },

  async attachGatewayPartner(id: number, input: { partnerId: number; integrationId: number }): Promise<void> {
    await post(`/apigateways/${id}/addpartner`, { partnerId: input.partnerId, subscriptionId: input.integrationId });
  },

  async updateGatewayAttachment(id: number, input: { partnerId: number; integrationId: number }): Promise<void> {
    // Not a plain POST to updatepartner: ApiGatewayPartner's PK is the composite
    // (gatewayId, partnerId, subscriptionId), and the backend's UpdatePartner
    // handler tries to mutate subscriptionId in place on a tracked entity — EF
    // Core rejects changes to a key column. Remove-then-add sidesteps it.
    await post(`/apigateways/${id}/removepartner`, { partnerId: input.partnerId });
    await post(`/apigateways/${id}/addpartner`, { partnerId: input.partnerId, subscriptionId: input.integrationId });
  },

  async removeGatewayAttachment(id: number, partnerId: number): Promise<void> {
    await post(`/apigateways/${id}/removepartner`, { partnerId });
  },

  // ——— bus gateways ———

  async listBusGateways(): Promise<BusGatewayRow[]> {
    const res = await get<SearchyResponse<RawBusGateway>>("/busgateways");
    return (res.result ?? []).map(toBusGatewayRow);
  },

  async searchBusGateways(query: {
    search: string;
    informationTypeId?: number | null;
    inactive?: boolean | null;
    offset: number;
    limit: number;
  }): Promise<Paged<BusGatewayRow>> {
    const qs = buildListQuery({
      filters: [
        ["Name", SEARCHY_RULE.contains, query.search.trim()],
        ["DocumentId", SEARCHY_RULE.equalsTo, query.informationTypeId ?? ""],
        ["Inactive", SEARCHY_RULE.equalsTo, query.inactive == null ? "" : String(query.inactive)],
      ],
      offset: query.offset,
      limit: query.limit,
    });
    const res = await get<SearchyResponse<RawBusGateway>>(`/busgateways?${qs}`);
    return { total: res.totalCount, result: (res.result ?? []).map(toBusGatewayRow) };
  },

  async getBusGateway(id: number): Promise<BusGatewayDetail> {
    return toBusGatewayDetail(await get<RawBusGateway>(`/busgateways/${id}`));
  },

  async createBusGateway({
    name,
    informationTypeId,
  }: {
    name: string;
    informationTypeId: number;
  }): Promise<BusGateway> {
    const id = await post<number>("/busgateways", {
      name,
      documentId: informationTypeId,
      inactive: false,
    });
    return { id, name, informationTypeId, inactive: false, createdOn: "" };
  },

  async updateBusGateway(
    id: number,
    changes: { name: string; inactive: boolean },
  ): Promise<BusGateway> {
    // The bound information type is fixed at creation — Update.cs silently
    // ignores documentId — but the request DTO still requires a value, so
    // fetch the current one to round-trip it rather than sending a bogus 0.
    const current = await get<RawBusGateway>(`/busgateways/${id}`);
    await post(`/busgateways/${id}`, {
      name: changes.name,
      documentId: current.documentId,
      inactive: changes.inactive,
    });
    return {
      id,
      name: changes.name,
      informationTypeId: current.documentId,
      inactive: changes.inactive,
      createdOn: "",
    };
  },

  async deleteBusGateway(id: number): Promise<void> {
    await request(`/busgateways/${id}`, { method: "DELETE" });
  },

  async addBusRoute(
    id: number,
    input: { integrationId: number; partnerId: number | null; matchExpression: MatchGroup | null },
  ): Promise<void> {
    await post(`/busgateways/${id}/addroute`, {
      subscriptionId: input.integrationId,
      partnerId: input.partnerId,
      matchExpression: toRawMatchExpression(input.matchExpression),
    });
  },

  async updateBusRoute(
    id: number,
    routeId: number,
    input: { integrationId: number; partnerId: number | null; matchExpression: MatchGroup | null },
  ): Promise<void> {
    await post(`/busgateways/${id}/updateroute`, {
      routeId,
      subscriptionId: input.integrationId,
      partnerId: input.partnerId,
      matchExpression: toRawMatchExpression(input.matchExpression),
    });
  },

  async removeBusRoute(id: number, routeId: number): Promise<void> {
    await post(`/busgateways/${id}/removeroute`, { routeId });
  },
} satisfies Partial<ApiClient>;
