import { API_BASE } from "./request";

/** The `[Unprotect]` GET /settings/config payload the login page needs pre-auth. */
export interface AppConfig {
  msalClientId?: string | null;
  msalTenantId?: string | null;
  msalRedirectUri?: string | null;
  isRabbitMqManagementConfigured?: boolean;
}

let cached: Promise<AppConfig> | null = null;

/**
 * Bootstrap config served before authentication (MSAL parameters, feature
 * flags). Fetched once and memoised. Never throws to callers — an unreachable
 * backend just yields an empty config, so the login page degrades gracefully.
 */
export function getAppConfig(): Promise<AppConfig> {
  if (!cached) {
    cached = fetch(`${API_BASE}/settings/config`, { credentials: "include" })
      .then((res) => (res.ok ? (res.json() as Promise<AppConfig>) : {}))
      .catch(() => ({}));
  }
  return cached;
}
