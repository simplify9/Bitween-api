import type { ApiClient } from "../client";
import type { MarketplaceInstallResult, MarketplacePackage, MarketplaceSearch } from "../types";
import { get, post } from "./request";

export const marketplaceMethods = {
  searchMarketplace: (q: string, tier: string) =>
    get<MarketplaceSearch>(`/marketplace/search?${new URLSearchParams({ q, tier })}`),

  getMarketplacePackage: (id: string) => get<MarketplacePackage>(`/marketplace/package?${new URLSearchParams({ id })}`),

  installFromMarketplace: (id: string, version: string, current: boolean) =>
    post<MarketplaceInstallResult>("/marketplace/install", { id, version, current }),
} satisfies Partial<ApiClient>;
