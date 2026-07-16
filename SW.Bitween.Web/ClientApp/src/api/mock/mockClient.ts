import type { ApiClient } from "../client";
import { PERMISSION_CATALOG } from "../permissions";
import {
  ApiRequestError,
  type Invite,
  type PermissionKey,
  type Role,
  type Session,
  type User,
} from "../types";
import { type MockDb, delay, loadDb, resetDb, saveDb } from "./store";
import type { SeedRole } from "./seed";
import { configClient } from "./configClient";
import { integrationsClient } from "./integrationsClient";
import { settingsClient } from "./settingsClient";

const ADMIN_ROLE_ID = "role-administrator";
const INVITE_LIFETIME_DAYS = 7;

const fail = (code: string, message: string): never => {
  throw new ApiRequestError(code, message);
};

const withMemberCount = (db: MockDb, role: SeedRole): Role => ({
  ...role,
  memberCount: db.users.filter((u) => u.roleIds.includes(role.id)).length,
});

const buildSession = (db: MockDb, user: User): Session => {
  const roles = db.roles.filter((r) => user.roleIds.includes(r.id));
  const permissions = [...new Set(roles.flatMap((r) => r.permissions))] as PermissionKey[];
  return { user: structuredClone(user), roles: roles.map((r) => withMemberCount(db, r)), permissions };
};

const requireUser = (db: MockDb, id: string): User =>
  db.users.find((u) => u.id === id) ?? fail("NOT_FOUND", "This member no longer exists.");

/** True if, after applying `change`, no active administrator would remain. */
const wouldOrphanAdmins = (db: MockDb, change: (u: User) => User | null): boolean =>
  !db.users
    .map((u) => change(u) ?? null)
    .some((u) => u !== null && u.status === "active" && u.roleIds.includes(ADMIN_ROLE_ID));

const newToken = (prefix: string) => `${prefix}-${crypto.randomUUID().slice(0, 8)}`;

/** Invites carry resolved role names so the public accept page can show them. */
const inviteDto = (db: MockDb, invite: Invite): Invite => ({
  ...structuredClone(invite),
  roleNames: invite.roleIds.map((id) => db.roles.find((r) => r.id === id)?.name ?? "Unknown role"),
});

const startSession = (db: MockDb, user: User): Session => {
  user.lastActiveOn = new Date().toISOString();
  db.sessionUserId = user.id;
  saveDb(db);
  return buildSession(db, user);
};

export const mockClient: ApiClient = {
  ...configClient,
  ...integrationsClient,
  ...settingsClient,

  async getSession() {
    await delay();
    const db = loadDb();
    if (!db.sessionUserId) return null;
    const user = db.users.find((u) => u.id === db.sessionUserId);
    if (!user || user.status !== "active") {
      db.sessionUserId = null;
      saveDb(db);
      return null;
    }
    return buildSession(db, user);
  },

  async login(email, password) {
    await delay();
    const db = loadDb();
    const user = db.users.find((u) => u.email.toLowerCase() === email.trim().toLowerCase());
    if (!user || db.passwords[user.id] !== password)
      fail("INVALID_CREDENTIALS", "That email and password don't match.");
    if (user!.status === "invited")
      fail("INVITE_PENDING", "This account hasn't been set up yet — use the invite link instead.");
    if (user!.status === "disabled")
      fail("ACCOUNT_DISABLED", "This account is disabled. Ask an administrator to re-enable it.");
    return startSession(db, user!);
  },

  async loginWithMicrosoft() {
    await delay();
    const db = loadDb();
    const user = db.users.find((u) => u.microsoftLinked && u.status === "active");
    if (!user)
      fail(
        "NO_LINKED_ACCOUNT",
        "No active member has a linked Microsoft account in this demo data.",
      );
    return startSession(db, user!);
  },

  async logout() {
    await delay();
    const db = loadDb();
    db.sessionUserId = null;
    saveDb(db);
  },

  async updateProfile(changes) {
    await delay();
    const db = loadDb();
    if (!db.sessionUserId) fail("UNAUTHENTICATED", "You're signed out.");
    const user = requireUser(db, db.sessionUserId!);
    if (changes.displayName !== undefined) {
      if (changes.displayName.trim().length < 2)
        fail("INVALID_NAME", "Display name needs at least 2 characters.");
      user.displayName = changes.displayName.trim();
    }
    if (changes.phone !== undefined) user.phone = changes.phone.trim() || undefined;
    saveDb(db);
    return buildSession(db, user);
  },

  async changePassword(currentPassword, newPassword) {
    await delay();
    const db = loadDb();
    if (!db.sessionUserId) fail("UNAUTHENTICATED", "You're signed out.");
    if (db.passwords[db.sessionUserId!] !== currentPassword)
      fail("WRONG_PASSWORD", "Your current password isn't right.");
    if (newPassword.length < 8) fail("WEAK_PASSWORD", "Use at least 8 characters.");
    db.passwords[db.sessionUserId!] = newPassword;
    saveDb(db);
  },

  async requestPasswordReset(email) {
    await delay();
    const db = loadDb();
    const user = db.users.find(
      (u) => u.email.toLowerCase() === email.trim().toLowerCase() && u.status === "active",
    );
    if (!user) return { resetLink: "" }; // same outward response either way
    const token = newToken("rst");
    db.resetTokens[token] = user.id;
    saveDb(db);
    return { resetLink: `${location.origin}${import.meta.env.BASE_URL}reset-password/${token}` };
  },

  async resetPassword(token, newPassword) {
    await delay();
    const db = loadDb();
    const userId = db.resetTokens[token];
    if (!userId) fail("INVALID_TOKEN", "This reset link is no longer valid.");
    if (newPassword.length < 8) fail("WEAK_PASSWORD", "Use at least 8 characters.");
    db.passwords[userId] = newPassword;
    delete db.resetTokens[token];
    saveDb(db);
  },

  async listUsers() {
    await delay();
    return structuredClone(loadDb().users);
  },

  async getUser(id) {
    await delay();
    return structuredClone(requireUser(loadDb(), id));
  },

  async inviteUser({ email, roleIds }) {
    await delay();
    const db = loadDb();
    const normalized = email.trim().toLowerCase();
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(normalized))
      fail("INVALID_EMAIL", "That doesn't look like an email address.");
    if (db.users.some((u) => u.email.toLowerCase() === normalized))
      fail("EMAIL_TAKEN", "A member with this email already exists.");
    if (roleIds.length === 0) fail("NO_ROLES", "Pick at least one role.");
    const inviter = db.users.find((u) => u.id === db.sessionUserId);
    const user: User = {
      id: `u-${crypto.randomUUID().slice(0, 8)}`,
      displayName: normalized,
      email: normalized,
      roleIds: [...roleIds],
      status: "invited",
      microsoftLinked: false,
      createdOn: new Date().toISOString(),
    };
    const invite: Invite = {
      token: newToken("inv"),
      email: normalized,
      roleIds: [...roleIds],
      roleNames: [],
      invitedByName: inviter?.displayName ?? "Unknown",
      createdOn: new Date().toISOString(),
      expiresOn: new Date(Date.now() + INVITE_LIFETIME_DAYS * 86_400_000).toISOString(),
    };
    db.users.push(user);
    db.invites.push(invite);
    saveDb(db);
    return inviteDto(db, invite);
  },

  async getInviteForUser(userId) {
    await delay();
    const db = loadDb();
    const user = requireUser(db, userId);
    const invite = db.invites.find((i) => i.email === user.email);
    return invite ? inviteDto(db, invite) : null;
  },

  async resendInvite(userId) {
    await delay();
    const db = loadDb();
    const user = requireUser(db, userId);
    const invite = db.invites.find((i) => i.email === user.email);
    if (!invite) fail("NOT_FOUND", "There's no pending invite for this member.");
    invite!.token = newToken("inv");
    invite!.createdOn = new Date().toISOString();
    invite!.expiresOn = new Date(Date.now() + INVITE_LIFETIME_DAYS * 86_400_000).toISOString();
    saveDb(db);
    return inviteDto(db, invite!);
  },

  async revokeInvite(userId) {
    await delay();
    const db = loadDb();
    const user = requireUser(db, userId);
    if (user.status !== "invited") fail("NOT_INVITED", "This member already joined.");
    db.invites = db.invites.filter((i) => i.email !== user.email);
    db.users = db.users.filter((u) => u.id !== userId);
    saveDb(db);
  },

  async getInvite(token) {
    await delay();
    const db = loadDb();
    const invite = db.invites.find((i) => i.token === token);
    if (!invite) fail("INVALID_TOKEN", "This invite link is no longer valid.");
    if (new Date(invite!.expiresOn) < new Date())
      fail("EXPIRED", "This invite has expired. Ask for a new one.");
    return inviteDto(db, invite!);
  },

  async acceptInvite(token, { displayName, password }) {
    await delay();
    const db = loadDb();
    const invite = db.invites.find((i) => i.token === token);
    if (!invite) fail("INVALID_TOKEN", "This invite link is no longer valid.");
    if (displayName.trim().length < 2) fail("INVALID_NAME", "Tell us your name.");
    if (password.length < 8) fail("WEAK_PASSWORD", "Use at least 8 characters.");
    const user = db.users.find((u) => u.email === invite!.email);
    if (!user) fail("NOT_FOUND", "This invite's account was removed.");
    user!.displayName = displayName.trim();
    user!.status = "active";
    db.passwords[user!.id] = password;
    db.invites = db.invites.filter((i) => i.token !== token);
    return startSession(db, user!);
  },

  async updateUserRoles(id, roleIds) {
    await delay();
    const db = loadDb();
    const user = requireUser(db, id);
    if (roleIds.length === 0) fail("NO_ROLES", "Members need at least one role.");
    if (roleIds.some((r) => !db.roles.some((role) => role.id === r)))
      fail("NOT_FOUND", "One of those roles no longer exists.");
    if (wouldOrphanAdmins(db, (u) => (u.id === id ? { ...u, roleIds } : u)))
      fail("LAST_ADMIN", "This is the only active administrator — grant the role to someone else first.");
    user.roleIds = [...roleIds];
    saveDb(db);
    return structuredClone(user);
  },

  async setUserDisabled(id, disabled) {
    await delay();
    const db = loadDb();
    const user = requireUser(db, id);
    if (id === db.sessionUserId) fail("SELF", "You can't disable your own account.");
    if (disabled && wouldOrphanAdmins(db, (u) => (u.id === id ? { ...u, status: "disabled" } : u)))
      fail("LAST_ADMIN", "This is the only active administrator — grant the role to someone else first.");
    user.status = disabled ? "disabled" : "active";
    saveDb(db);
    return structuredClone(user);
  },

  async deleteUser(id) {
    await delay();
    const db = loadDb();
    const user = requireUser(db, id);
    if (id === db.sessionUserId) fail("SELF", "You can't remove your own account.");
    if (wouldOrphanAdmins(db, (u) => (u.id === id ? null : u)))
      fail("LAST_ADMIN", "This is the only active administrator — grant the role to someone else first.");
    db.users = db.users.filter((u) => u.id !== id);
    db.invites = db.invites.filter((i) => i.email !== user.email);
    delete db.passwords[id];
    saveDb(db);
  },

  async adminResetPassword(id) {
    await delay();
    const db = loadDb();
    const user = requireUser(db, id);
    if (user.status !== "active") fail("NOT_ACTIVE", "Only active members can reset a password.");
    const token = newToken("rst");
    db.resetTokens[token] = id;
    saveDb(db);
    return { resetLink: `${location.origin}${import.meta.env.BASE_URL}reset-password/${token}` };
  },

  async getPermissionCatalog() {
    await delay();
    return structuredClone(PERMISSION_CATALOG);
  },

  async listRoles() {
    await delay();
    const db = loadDb();
    return db.roles.map((r) => withMemberCount(db, structuredClone(r)));
  },

  async getRole(id) {
    await delay();
    const db = loadDb();
    const role = db.roles.find((r) => r.id === id);
    if (!role) fail("NOT_FOUND", "This role no longer exists.");
    return withMemberCount(db, structuredClone(role!));
  },

  async createRole({ name, description, permissions }) {
    await delay();
    const db = loadDb();
    const trimmed = name.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the role a name.");
    if (db.roles.some((r) => r.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "A role with this name already exists.");
    if (permissions.length === 0) fail("NO_PERMISSIONS", "Pick at least one permission.");
    const role: SeedRole = {
      id: `role-${crypto.randomUUID().slice(0, 8)}`,
      name: trimmed,
      description: description.trim(),
      permissions: [...new Set(permissions)],
      isSystem: false,
      createdOn: new Date().toISOString(),
    };
    db.roles.push(role);
    saveDb(db);
    return withMemberCount(db, structuredClone(role));
  },

  async updateRole(id, { name, description, permissions }) {
    await delay();
    const db = loadDb();
    const role = db.roles.find((r) => r.id === id);
    if (!role) fail("NOT_FOUND", "This role no longer exists.");
    if (role!.isSystem) fail("SYSTEM_ROLE", "Built-in roles can't be changed.");
    const trimmed = name.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the role a name.");
    if (db.roles.some((r) => r.id !== id && r.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "A role with this name already exists.");
    if (permissions.length === 0) fail("NO_PERMISSIONS", "Pick at least one permission.");
    role!.name = trimmed;
    role!.description = description.trim();
    role!.permissions = [...new Set(permissions)];
    saveDb(db);
    return withMemberCount(db, structuredClone(role!));
  },

  async deleteRole(id) {
    await delay();
    const db = loadDb();
    const role = db.roles.find((r) => r.id === id);
    if (!role) fail("NOT_FOUND", "This role no longer exists.");
    if (role!.isSystem) fail("SYSTEM_ROLE", "Built-in roles can't be deleted.");
    const members = db.users.filter((u) => u.roleIds.includes(id)).length;
    if (members > 0)
      fail("ROLE_IN_USE", `${members} member${members === 1 ? "" : "s"} still hold this role — move them to another role first.`);
    db.roles = db.roles.filter((r) => r.id !== id);
    saveDb(db);
  },

  demo: {
    async listPersonas() {
      await delay();
      return structuredClone(loadDb().users);
    },
    async switchTo(userId) {
      await delay();
      const db = loadDb();
      const user = requireUser(db, userId);
      if (user.status !== "active") fail("NOT_ACTIVE", "Only active members can be impersonated.");
      return startSession(db, user);
    },
    async reset() {
      await delay();
      resetDb();
    },
  },
};
