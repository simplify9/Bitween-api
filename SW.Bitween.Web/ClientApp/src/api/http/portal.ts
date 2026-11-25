import type { ApiClient } from "../client";
import type {
  PartnerChange,
  PartnerChangeDetail,
  PartnerChangeVersion,
  PartnerPropertyChangeDetail,
  PortalExchangePage,
  PortalExchangeQuery,
  PortalMapping,
  PortalMappingPlace,
  PortalMappingSample,
  PortalMappingSaved,
  PortalInvite,
  PortalPerson,
  PortalProperties,
  PortalRoleChoice,
  PortalSummary,
  SubscriptionPartnerMapping,
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
  | "withdrawPortalMapping"
  | "previewPortalMapping"
  | "listPortalMappingSamples"
  | "getPortalMappingSample"
  | "listPortalPropertyKeys"
  | "listSubscriptionPartnerMappings"
  | "removeSubscriptionPartnerMapping"
  | "setPartnerApproval"
  | "listPartnerChanges"
  | "countWaitingPartnerChanges"
  | "getPartnerChange"
  | "approvePartnerChange"
  | "rejectPartnerChange"
  | "listPartnerMappingHistory"
  | "getPartnerPropertyChange"
  | "approvePartnerPropertyChange"
  | "rejectPartnerPropertyChange"
  | "putBackPartnerMapping"
  | "getPortalProperties"
  | "savePortalProperties"
  | "withdrawPortalProperties"
  | "listPortalPeople"
  | "listPortalPeopleRoles"
  | "addPortalPerson"
  | "invitePortalPersonAgain"
  | "setPortalPersonRole"
  | "setPortalPersonDisabled"
  | "removePortalPerson"
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
  savePortalMapping: async (subscriptionId, mapperProperties) =>
    savedOf(await post<PortalMappingSaved | null>("/portal/savemapping", { subscriptionId, mapperProperties })),
  resetPortalMapping: async (subscriptionId) =>
    savedOf(await post<PortalMappingSaved | null>("/portal/resetmapping", { subscriptionId })),
  withdrawPortalMapping: async (subscriptionId) => {
    await post("/portal/withdrawmapping", { subscriptionId });
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

  // The other side of the same mappings, for our team on the subscription's page.
  listSubscriptionPartnerMappings: (subscriptionId) =>
    get<SubscriptionPartnerMapping[]>(`/subscriptions/${subscriptionId}/partnermappings`),
  removeSubscriptionPartnerMapping: async (subscriptionId, partnerId) => {
    await post(`/subscriptions/${subscriptionId}/removepartnermapping`, { partnerId });
  },

  setPartnerApproval: async (partnerId, changesNeedApproval) => {
    await post(`/partners/${partnerId}/approval`, { changesNeedApproval });
  },
  listPartnerChanges: (waiting) => get<PartnerChange[]>(`/partnerchanges?waiting=${waiting}`),
  countWaitingPartnerChanges: () => get<number>("/partnerchanges/count"),
  getPartnerChange: (id) => get<PartnerChangeDetail>(`/partnerchanges/${id}`),
  approvePartnerChange: async (id) => {
    await post(`/partnerchanges/${id}/approve`, {});
  },
  rejectPartnerChange: async (id, note) => {
    await post(`/partnerchanges/${id}/reject`, { note });
  },
  getPartnerPropertyChange: (id) => get<PartnerPropertyChangeDetail>(`/partnerpropertychanges/${id}`),
  approvePartnerPropertyChange: async (id) => {
    await post(`/partnerpropertychanges/${id}/approve`, {});
  },
  rejectPartnerPropertyChange: async (id, note) => {
    await post(`/partnerpropertychanges/${id}/reject`, { note });
  },
  listPartnerMappingHistory: (subscriptionId, partnerId) =>
    get<PartnerChangeVersion[]>(`/partnerchanges/history?subscriptionId=${subscriptionId}&partnerId=${partnerId}`),
  putBackPartnerMapping: async (changeId) => {
    await post(`/partnerchanges/${changeId}/putback`, {});
  },

  getPortalProperties: () => get<PortalProperties>("/portal/properties"),
  savePortalProperties: async (values) => savedOf(await post<PortalMappingSaved | null>("/portal/saveproperties", { values })),
  withdrawPortalProperties: async () => {
    await post("/portal/withdrawproperties", {});
  },
  listPortalPeople: () => get<PortalPerson[]>("/portal/people"),
  listPortalPeopleRoles: () => get<PortalRoleChoice[]>("/portal/peopleroles"),
  addPortalPerson: (input) => post<PortalInvite & { id: number }>("/portal/addperson", input),
  invitePortalPersonAgain: (accountId) => post<PortalInvite>("/portal/invitepersonagain", { accountId }),
  setPortalPersonRole: async (accountId, roleId) => {
    await post("/portal/setpersonrole", { accountId, roleId });
  },
  setPortalPersonDisabled: async (accountId, disabled) => {
    await post("/portal/setpersondisabled", { accountId, disabled });
  },
  removePortalPerson: async (accountId) => {
    await post("/portal/removeperson", { accountId });
  },
};

const savedOf = (r: PortalMappingSaved | null): PortalMappingSaved => ({ waiting: r?.waiting ?? false });
