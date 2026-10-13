import type { ApiClient } from "../client";
import type { KitInfo, KitInstallRequest, KitRun, MarketplaceInstallResult, MarketplacePackage, MarketplaceSearch } from "../types";
import { get, post } from "./request";

export const marketplaceMethods = {
  searchMarketplace: (q: string, tier: string) =>
    get<MarketplaceSearch>(`/marketplace/search?${new URLSearchParams({ q, tier })}`),

  getMarketplacePackage: (id: string) => get<MarketplacePackage>(`/marketplace/package?${new URLSearchParams({ id })}`),

  installFromMarketplace: (id: string, version: string, current: boolean) =>
    post<MarketplaceInstallResult>("/marketplace/install", { id, version, current }),

  getKit: (id: string, version: string) => get<KitInfo>(`/marketplace/kit?${new URLSearchParams({ id, version })}`),

  planKit: (request: KitInstallRequest) => post<KitRun>("/marketplace/kitplan", request),

  installKit: (request: KitInstallRequest) => post<KitRun>("/marketplace/kitinstall", request),
} satisfies Partial<ApiClient>;
