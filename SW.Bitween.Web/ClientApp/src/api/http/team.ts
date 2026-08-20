import type { ApiClient } from "../client";
import {
  ApiRequestError,
  type PermissionArea,
  type PermissionKey,
  type Role,
  type User,
} from "../types";
import { get, post, request } from "./request";

interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}

interface RawRoleSummary {
  id: number;
  name: string;
}

interface RawAccount {
  id: number;
  name: string;
  email: string;
  role: string;
  disabled: boolean;
  /** Set by the backend's failed-sign-in lockout; null or in the past means not locked. */
  lockoutEnd: string | null;
  createdOn: string;
  roles: RawRoleSummary[] | null;
}

interface RawRole {
  id: number;
  name: string;
  description: string | null;
  isSystem: boolean;
  permissions: string[] | null;
  memberCount: number;
  createdOn: string;
}

const toUser = (r: RawAccount): User => ({
  id: String(r.id),
  displayName: r.name,
  email: r.email,
  roleIds: (r.roles ?? []).map((role) => String(role.id)),
  status: r.disabled ? "disabled" : "active",
  // Past lockouts are not state anyone can act on, so they read as no lockout at all.
  lockedUntil: r.lockoutEnd && new Date(r.lockoutEnd) > new Date() ? r.lockoutEnd : null,
  // Not tracked by the backend: no login-method projection, no last-seen column.
  microsoftLinked: false,
  createdOn: r.createdOn,
});

const toRole = (r: RawRole): Role => ({
  id: String(r.id),
  name: r.name,
  description: r.description ?? "",
  permissions: r.permissions ?? [],
  isSystem: r.isSystem,
  createdOn: r.createdOn,
  memberCount: r.memberCount,
});

/** One page big enough for any realistic team — the backend defaults to 20. */
const ALL_MEMBERS = 500;

async function fetchUsers(): Promise<User[]> {
  const res = await get<SearchyResponse<RawAccount>>(`/accounts?limit=${ALL_MEMBERS}`);
  return (res.result ?? []).map(toUser);
}

async function fetchUser(id: string): Promise<User> {
  // No GET /accounts/{id} exists, so read the member out of the list.
  const user = (await fetchUsers()).find((u) => u.id === id);
  if (!user) throw new ApiRequestError("NOT_FOUND", "This member no longer exists.");
  return user;
}

export const teamMethods = {
  // — permission catalog —
  async getPermissionCatalog(): Promise<PermissionArea[]> {
    return await get<PermissionArea[]>("/permissions");
  },

  // — members —
  listUsers: fetchUsers,
  getUser: fetchUser,

  async createUser({
    displayName,
    email,
    password,
    roleIds,
  }: {
    displayName: string;
    email: string;
    password: string;
    roleIds: string[];
  }): Promise<User> {
    const id = await post<number>("/accounts", {
      name: displayName,
      email,
      password,
      roleIds: roleIds.map(Number),
    });
    return fetchUser(String(id));
  },

  async setUserPassword(id: string, password: string): Promise<void> {
    await post(`/accounts/${id}/setPassword`, { password });
  },

  async updateUserRoles(id: string, roleIds: string[]): Promise<User> {
    await post(`/accounts/${id}/setRoles`, { roleIds: roleIds.map(Number) });
    return fetchUser(id);
  },

  /** Clears a failed-sign-in lockout early, rather than waiting it out. */
  async unlockUser(id: string): Promise<User> {
    await post(`/accounts/${id}/unlock`, {});
    return fetchUser(id);
  },

  async setUserDisabled(id: string, disabled: boolean): Promise<User> {
    await post(`/accounts/${id}/setDisabled`, { disabled });
    return fetchUser(id);
  },

  async deleteUser(id: string): Promise<void> {
    await post(`/accounts/${id}/remove`, {});
  },

  // — roles —
  async listRoles(): Promise<Role[]> {
    const res = await get<SearchyResponse<RawRole>>("/roles?pageSize=200");
    return (res.result ?? []).map(toRole);
  },

  async getRole(id: string): Promise<Role> {
    const raw = await get<RawRole | null>(`/roles/${id}`);
    if (!raw) throw new ApiRequestError("NOT_FOUND", "This role no longer exists.");
    return toRole(raw);
  },

  async createRole(input: {
    name: string;
    description: string;
    permissions: PermissionKey[];
  }): Promise<Role> {
    const id = await post<number>("/roles", input);
    return teamMethods.getRole(String(id));
  },

  async updateRole(
    id: string,
    input: { name: string; description: string; permissions: PermissionKey[] },
  ): Promise<Role> {
    await post(`/roles/${id}`, input);
    return teamMethods.getRole(id);
  },

  async deleteRole(id: string): Promise<void> {
    await request(`/roles/${id}`, { method: "DELETE" });
  },
} satisfies Partial<ApiClient>;
