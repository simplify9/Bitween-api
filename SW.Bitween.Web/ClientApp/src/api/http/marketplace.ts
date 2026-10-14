import type { ApiClient } from "../client";
import type {
  BundleInstall,
  KitInfo,
  KitInstallRequest,
  KitRun,
  MarketplaceInstallResult,
  MarketplacePackage,
  MarketplaceRegistration,
  MarketplaceSearch,
  RegistryCheckSummary,
  RegistryLicenceSummary,
  UsageReportPreview,
} from "../types";
import { get, post, request } from "./request";

export const marketplaceMethods = {
  searchMarketplace: (q: string, tier: string) =>
    get<MarketplaceSearch>(`/marketplace/search?${new URLSearchParams({ q, tier })}`),

  getMarketplacePackage: (id: string) => get<MarketplacePackage>(`/marketplace/package?${new URLSearchParams({ id })}`),

  installFromMarketplace: (id: string, version: string, current: boolean) =>
    post<MarketplaceInstallResult>("/marketplace/install", { id, version, current }),

  getMarketplaceUpdates: () => get<RegistryCheckSummary>("/marketplace/updates"),

  checkMarketplace: () => post<RegistryCheckSummary>("/marketplace/check", {}),

  getKit: (id: string, version: string) => get<KitInfo>(`/marketplace/kit?${new URLSearchParams({ id, version })}`),

  planKit: (request: KitInstallRequest) => post<KitRun>("/marketplace/kitplan", request),

  installKit: (request: KitInstallRequest) => post<KitRun>("/marketplace/kitinstall", request),

  registerWithRegistry: (code: string, displayName: string) =>
    post<MarketplaceRegistration>("/marketplace/register", { code, displayName }),

  getMarketplaceLicences: () => get<RegistryLicenceSummary>("/marketplace/licences"),

  refreshMarketplaceLicences: () => post<RegistryLicenceSummary>("/marketplace/refreshlicences", {}),

  marketplaceUsage: (day: string | null, send: boolean) => post<UsageReportPreview>("/marketplace/usage", { day, send }),

  installBundle: (file: Blob, current: boolean) =>
    request<BundleInstall>(`/marketplace/bundle?${new URLSearchParams({ current: String(current) })}`, { method: "POST", file }),
} satisfies Partial<ApiClient>;
