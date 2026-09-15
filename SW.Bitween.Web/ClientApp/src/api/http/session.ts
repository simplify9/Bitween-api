import { ApiRequestError, type Session, type User } from "../types";
import { getAppConfig } from "./appConfig";
import { clearToken, get, getToken, post, request, setToken } from "./request";

/** GET /accounts/profile — camelCase ProfileModel. */
interface Profile {
  id: number;
  email: string;
  name: string;
  /** Legacy coarse role, kept for older API clients. Authorization uses `permissions`. */
  role: string;
  disabled: boolean;
  createdOn: string;
  roles: { id: number; name: string }[] | null;
  permissions: string[] | null;
  mustChangePassword?: boolean;
}

/** POST /accounts/login → { jwt }. */
interface LoginResult {
  jwt: string;
}

const buildSession = (profile: Profile): Session => {
  const user: User = {
    id: String(profile.id),
    displayName: profile.name,
    email: profile.email,
    roleIds: (profile.roles ?? []).map((r) => String(r.id)),
    status: profile.disabled ? "disabled" : "active",
    // Always null here: you cannot be signed in and locked out at the same time.
    lockedUntil: null,
    microsoftLinked: false,
    createdOn: profile.createdOn,
  };
  return {
    user,
    roles: (profile.roles ?? []).map((r) => ({ id: String(r.id), name: r.name })),
    // Resolved server-side from the user's roles, so a revoked role takes effect on next load.
    permissions: profile.permissions ?? [],
    mustChangePassword: profile.mustChangePassword ?? false,
  };
};

const loadSession = async (): Promise<Session> => buildSession(await get<Profile>("/accounts/profile"));

/**
 * The sign-out request that may still be in flight, so a sign-in can cancel it.
 *
 * Its response carries `Clear-Site-Data: "cookies", "storage"`, which the browser
 * applies to the whole origin the moment it arrives — so landing *after* a fresh
 * sign-in, it wipes that sign-in's Jwt and refresh cookie and throws the user back
 * to the page they just left. The two genuinely can overlap now that signing out
 * no longer blocks the UI: the login page appears at once, and a password manager
 * can fill and submit it before the logout lands.
 *
 * Waiting for the logout first was the obvious guard and the wrong one — a request
 * that hangs rather than fails would block sign-in for as long as it hung. Nothing
 * in the response is worth waiting for: it was already sent, so the server deletes
 * the refresh token either way, and only the header we don't want is discarded.
 */
let logoutInFlight: AbortController | null = null;

/** A sign-in supersedes any sign-out still in flight. */
const abortPendingLogout = () => logoutInFlight?.abort();

export const sessionMethods = {
  async getSession(): Promise<Session | null> {
    // No stored Jwt → anonymous; don't probe the backend (an expired token still
    // gets refreshed via cookie inside request() on its 401). The token is only
    // cleared on logout or an unrecoverable 401, so returning users keep it.
    if (!getToken()) return null;
    try {
      return await loadSession();
    } catch {
      // Token invalid and no refresh cookie → signed out. Show login, don't fake it.
      return null;
    }
  },

  async login(email: string, password: string): Promise<Session> {
    abortPendingLogout();
    const { jwt } = await post<LoginResult>("/accounts/login", {
      Username: email,
      Password: password,
    });
    setToken(jwt);
    return loadSession();
  },

  async loginWithMicrosoft(): Promise<Session> {
    abortPendingLogout();
    const cfg = await getAppConfig();
    if (!cfg.msalClientId)
      throw new ApiRequestError("MS_NOT_CONFIGURED", "Microsoft sign-in isn't configured.");

    // Lazy so MSAL stays out of the initial bundle.
    const { PublicClientApplication } = await import("@azure/msal-browser");
    const msal = new PublicClientApplication({
      auth: {
        clientId: cfg.msalClientId,
        ...(cfg.msalTenantId
          ? { authority: `https://login.microsoftonline.com/${cfg.msalTenantId}` }
          : {}),
      },
    });
    await msal.initialize();
    const result = await msal.loginPopup({
      ...(cfg.msalRedirectUri ? { redirectUri: cfg.msalRedirectUri } : {}),
      scopes: ["openid"],
    });
    if (!result.idToken)
      throw new ApiRequestError("MS_LOGIN_FAILED", "Microsoft didn't return a sign-in token.");

    const { jwt } = await post<LoginResult>("/accounts/login", { MsToken: result.idToken });
    setToken(jwt);
    return loadSession();
  },

  async logout(): Promise<void> {
    // Cleared first and unconditionally. This used to run in a `finally`, which
    // deleted the Jwt and then let the error through — the worst pairing, because
    // the caller aborted before it could end the session and the app carried on
    // rendering as if signed in. Removing the key here is also what wakes the
    // other tabs (see the `storage` listener in SessionContext).
    clearToken();
    const controller = new AbortController();
    logoutInFlight = controller;
    try {
      await request("/accounts/logout", { method: "POST", signal: controller.signal });
    } catch {
      // Swallowed on purpose: signing out must not depend on the server answering,
      // and a sign-in aborting this is a normal outcome rather than a fault. Only
      // the server can invalidate the refresh cookie — it is HttpOnly, so JS cannot
      // touch it — so if this never lands the cookie outlives the sign-out. This
      // browser has no Jwt to pair with it, and the next sign-in replaces it.
    } finally {
      if (logoutInFlight === controller) logoutInFlight = null;
    }
  },

  async updateProfile(changes: { displayName: string }): Promise<Session> {
    const current = await loadSession();
    await post(`/accounts/${current.user.id}`, { name: changes.displayName });
    return loadSession();
  },

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await post("/accounts/changePassword", {
      oldPassword: currentPassword,
      newPassword,
    });
  },
};
