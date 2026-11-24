import type { ApiClient } from "../client";
import type { PortalExchangePage, PortalExchangeQuery, PortalSummary } from "../types";
import { get } from "./request";

/** The partner portal: always the signed-in partner's own data, scoped by the server. */
export const portalMethods: Pick<ApiClient, "getPortalSummary" | "listPortalSubscriptions" | "searchPortalExchanges"> = {
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
};
