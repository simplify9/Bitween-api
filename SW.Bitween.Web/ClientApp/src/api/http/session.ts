import { ALL_PERMISSIONS, PERMISSION_CATALOG, permissionKey } from "../permissions";
import { ApiRequestError, type PermissionKey, type Role, type Session, type User } from "../types";
import { getAppConfig } from "./appConfig";
import { clearToken, get, getToken, post, setToken } from "./request";

/** GET /accounts/profile — camelCase AccountModel. */
interface Profile {
  id: number;
  email: string;
  name: string;
  role: string; // "Admin" | "Viewer" | "Member"
  createdOn: string;
}

/** POST /accounts/login → { jwt }. */
interface LoginResult {
  jwt: string;
}

// ——— coarse role → UI permissions ———
// The backend identity is a single AccountRole enum (Admin | Viewer | Member).
// The UI's fine-grained catalog collapses onto it: Admin does everything;
// Member manages Operate/Integrations/Configuration; Viewer reads those. The
// Administration group (members/roles/settings) stays Admin-only. This only
// gates nav visibility — real enforcement is the backend's per-handler role check.
const NON_ADMIN_GROUPS = ["Operate", "Integrations", "Configuration"];

const permissionsInGroups = (viewOnly: boolean): PermissionKey[] =>
  PERMISSION_CATALOG.filter((area) => NON_ADMIN_GROUPS.includes(area.group)).flatMap((area) =>
    area.actions
      .filter((a) => !viewOnly || a.id === "view")
      .map((a) => permissionKey(area.id, a.id)),
  );

const ROLE_LABEL: Record<string, string> = {
  Admin: "Administrator",
  Member: "Member",
  Viewer: "Viewer",
};

const permissionsForRole = (role: string): PermissionKey[] => {
  if (role === "Admin") return [...ALL_PERMISSIONS];
  if (role === "Member") return permissionsInGroups(false);
  return permissionsInGroups(true); // Viewer / unknown → least privilege
};

const buildSession = (profile: Profile): Session => {
  const roleName = ROLE_LABEL[profile.role] ? profile.role : "Viewer";
  const permissions = permissionsForRole(roleName);
  const user: User = {
    id: String(profile.id),
    displayName: profile.name,
    email: profile.email,
    roleIds: [roleName],
    status: "active",
    microsoftLinked: false,
    createdOn: profile.createdOn,
  };
  const role: Role = {
    id: roleName,
    name: ROLE_LABEL[roleName],
    description: `Built-in ${ROLE_LABEL[roleName]} role.`,
    permissions,
    isSystem: true,
    createdOn: profile.createdOn,
    memberCount: 0,
  };
  return { user, roles: [role], permissions };
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
};
