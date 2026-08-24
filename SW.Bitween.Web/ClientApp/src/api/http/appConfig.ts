import { API_BASE } from "./request";

/** The `[Unprotect]` GET /settings/config payload the login page needs pre-auth. */
export interface AppConfig {
  msalClientId?: string | null;
  msalTenantId?: string | null;
  msalRedirectUri?: string | null;
  /** When true the backend rejects email/password sign-in outright, so don't offer the form. */
  disableEmailPasswordLogin?: boolean;
  isRabbitMqManagementConfigured?: boolean;
  /** Effective brand values (any stored override already applied), keyed like `ThemeOptions`. */
  theme?: Record<string, string | boolean | null>;
  /** The same keys as configured, before any override — lets us tell "set" from "untouched". */
  themeDefaults?: Record<string, string>;
}

let cached: Promise<AppConfig> | null = null;

/**
 * Bootstrap config served before authentication (MSAL parameters, feature
 * flags, branding). Fetched once and memoised. Never throws to callers — an
 * unreachable backend just yields an empty config, so the login page degrades
 * gracefully.
 */
export function getAppConfig(): Promise<AppConfig> {
  if (!cached) {
    cached = fetch(`${API_BASE}/settings/config`, { credentials: "include" })
      .then((res) => (res.ok ? (res.json() as Promise<AppConfig>) : {}))
      .catch(() => ({}));
  }
  return cached;
}

/** Drops the memoised copy, so the next read picks up a brand setting that was just saved. */
export function resetAppConfig(): void {
  cached = null;
}
