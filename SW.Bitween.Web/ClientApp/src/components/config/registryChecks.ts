import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type AdapterUse, type RegistryAdapterCheck, type RegistryRevokedVersion } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";

/**
 * What the last registry check found, by adapter id lowercased: newer versions and revoked ones for
 * the adapters installed from it. Read from what Bitween stored, so it never waits on the registry.
 */
export function useRegistryChecks() {
  const canView = useSessionCan("subscriptions.view");
  const query = useQuery({
    queryKey: keys.marketplaceUpdates,
    queryFn: () => api.getMarketplaceUpdates(),
    enabled: canView,
    staleTime: 5 * 60_000,
  });
  const byAdapter = useMemo(
    () => new Map((query.data?.adapters ?? []).map((c) => [c.adapterId.toLowerCase(), c])),
    [query.data],
  );
  return { summary: query.data, byAdapter, query };
}

/** The revoked version a slot runs: the one it is pinned to, or the current one when it follows current. */
export function revokedRun(use: AdapterUse, check: RegistryAdapterCheck | undefined): RegistryRevokedVersion | null {
  if (!check) return null;
  const runs = use.version ?? check.current;
  return check.revoked.find((r) => r.version === runs) ?? null;
}
