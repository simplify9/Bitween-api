import { createContext, useContext, useMemo, type ReactNode } from "react";
import { useQuery, type QueryKey } from "@tanstack/react-query";
import { api, type PermissionKey, type PortalPerson, type Role, type User } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSession } from "../../auth/useSession";

/**
 * Where the Members and Roles pages read from, and what they offer. Our team's: everyone, through
 * the staff API. A partner's admin's (`partner`): their own company's people and the roles they may
 * work with, through the portal's API, which limits them on the server. One set of pages for both,
 * as the Exchanges page is.
 */
export interface TeamSource {
  partner: boolean;
  rolesPath: string;
  membersPath: string;
  keys: { roles: QueryKey; role: (id: string | null | undefined) => QueryKey; members: QueryKey; all: QueryKey[] };
  listRoles(): Promise<Role[]>;
  getRole(id: string): Promise<Role>;
  saveRole(
    id: string | null,
    input: { name: string; description: string; permissions: PermissionKey[] },
    forPartners: boolean,
  ): Promise<unknown>;
  deleteRole(id: string): Promise<void>;
  listMembers(): Promise<User[]>;
  can: {
    createRole: boolean;
    editRole: boolean;
    deleteRole: boolean;
    addMember: boolean;
    editMember: boolean;
    removeMember: boolean;
  };
}

/** The roles one side's account may hold: ours, or a partner's (ours for partners, and that partner's own). */
export const rolesForSide = (roles: Role[], partnerId: string | null) =>
  roles.filter((r) => (partnerId === null ? !r.forPartners : r.forPartners && (r.partnerId === null || r.partnerId === partnerId)));

const Context = createContext<TeamSource | null>(null);

/**
 * The roles a member may be given. On our side, the roles of the member's side, a partner's own only
 * to that partner's people. In the portal, those the server says the admin may give, least access
 * first so a new person starts there.
 */
export function useGrantableRoles(partnerId: string | null): { data: Role[] | undefined; error: Error | null } {
  const source = useTeamSource();
  const roles = useQuery({ queryKey: source.keys.roles, queryFn: () => source.listRoles() });
  const choices = useQuery({
    queryKey: keys.portal.peopleRoles,
    queryFn: () => api.listPortalPeopleRoles(),
    enabled: source.partner && source.can.addMember,
  });
  if (!source.partner) return { data: roles.data && rolesForSide(roles.data, partnerId), error: roles.error };
  const data =
    roles.data && choices.data
      ? choices.data.map((c) => roles.data.find((r) => r.id === String(c.id))).filter((r): r is Role => r !== undefined)
      : undefined;
  return { data, error: roles.error ?? choices.error };
}

export function useTeamSource(): TeamSource {
  const partner = useContext(Context);
  const { can } = useSession();
  const staff = useMemo<TeamSource>(
    () => ({
      partner: false,
      rolesPath: "/team/roles",
      membersPath: "/team/members",
      keys: { roles: keys.roles.list, role: keys.roles.detail, members: keys.users.list, all: [keys.users.all, keys.roles.all] },
      listRoles: () => api.listRoles(),
      getRole: (id) => api.getRole(id),
      saveRole: (id, input, forPartners) => (id ? api.updateRole(id, input) : api.createRole({ ...input, forPartners })),
      deleteRole: (id) => api.deleteRole(id),
      listMembers: () => api.listUsers(),
      can: {
        createRole: can("roles.create"),
        editRole: can("roles.edit"),
        deleteRole: can("roles.delete"),
        addMember: can("users.create"),
        editMember: can("users.edit"),
        removeMember: can("users.delete"),
      },
    }),
    [can],
  );
  return partner ?? staff;
}

/** One of their company's people as our team's pages show a member. */
const memberOf = (p: PortalPerson, partner: { id: string; name: string | null }): User => ({
  id: String(p.id),
  displayName: p.name,
  email: p.email,
  roleIds: p.role ? [String(p.role.id)] : [],
  status: p.disabled ? "disabled" : "active",
  lockedUntil: null,
  microsoftLinked: false,
  createdOn: p.createdOn ?? "",
  lastActiveOn: p.lastSignInOn ?? undefined,
  hasPassword: p.hasPassword ?? !p.invited,
  invite: p.invited && p.inviteExpiresOn ? { expiresOn: p.inviteExpiresOn, emailedOn: null, emailError: null } : null,
  partner,
  canChange: p.canChange,
});

/** The Members and Roles pages, and everything they open, as a partner's admin sees them. */
export function PartnerTeamSource({ children }: { children: ReactNode }) {
  const { session, can } = useSession();
  const partner = useMemo(
    () => ({ id: String(session?.partner?.id ?? ""), name: session?.partner?.name ?? null }),
    [session?.partner?.id, session?.partner?.name],
  );
  const manages = can("portal-people.edit");
  const value = useMemo<TeamSource>(
    () => ({
      partner: true,
      rolesPath: "/portal/roles",
      membersPath: "/portal/people",
      keys: {
        roles: keys.portal.roles,
        role: keys.portal.role,
        members: keys.portal.people,
        all: [keys.portal.people, keys.portal.roles, keys.portal.peopleRoles],
      },
      listRoles: () => api.listPortalRoles(),
      getRole: (id) => api.getPortalRole(id),
      saveRole: (id, input) => api.savePortalRole({ ...input, ...(id ? { id } : {}) }),
      deleteRole: (id) => api.deletePortalRole(id),
      listMembers: async () => (await api.listPortalPeople()).map((p) => memberOf(p, partner)),
      can: {
        createRole: manages,
        editRole: manages,
        deleteRole: manages,
        addMember: manages,
        editMember: manages,
        removeMember: manages,
      },
    }),
    [manages, partner],
  );
  return <Context.Provider value={value}>{children}</Context.Provider>;
}
