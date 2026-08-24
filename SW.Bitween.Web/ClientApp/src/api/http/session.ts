import { ApiRequestError, type Session, type User } from "../types";
import { getAppConfig } from "./appConfig";
import { clearToken, get, getToken, post, setToken } from "./request";

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
  };
};

const loadSession = async (): Promise<Session> => buildSession(await get<Profile>("/accounts/profile"));

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
    const { jwt } = await post<LoginResult>("/accounts/login", {
      Username: email,
      Password: password,
    });
    setToken(jwt);
    return loadSession();
  },

  async loginWithMicrosoft(): Promise<Session> {
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
    try {
      await post("/accounts/logout");
    } finally {
      clearToken();
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
