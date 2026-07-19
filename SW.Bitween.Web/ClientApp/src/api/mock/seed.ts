import { ALL_PERMISSIONS, permissionKey } from "../permissions";
import type { Invite, PermissionKey, Role, User } from "../types";

/** Everything a role needs for the given areas/actions, view included. */
const grants = (spec: Record<string, string[]>): PermissionKey[] =>
  Object.entries(spec).flatMap(([area, actions]) =>
    actions.map((a) => permissionKey(area, a as never)),
  );

const daysAgo = (n: number) => new Date(Date.now() - n * 86_400_000).toISOString();
const daysAhead = (n: number) => new Date(Date.now() + n * 86_400_000).toISOString();

export interface SeedRole extends Omit<Role, "memberCount"> {}

export const SEED_ROLES: SeedRole[] = [
  {
    id: "role-administrator",
    name: "Administrator",
    description:
      "Full access to everything, including members, roles and settings. Built in — always holds every permission.",
    permissions: [...ALL_PERMISSIONS],
    isSystem: true,
    createdOn: daysAgo(400),
  },
  {
    id: "role-integration-manager",
    name: "Integration manager",
    description:
      "Builds and maintains integrations: subscriptions, gateways, partners and configuration. No member or role management.",
    permissions: grants({
      dashboard: ["view"],
      exchanges: ["view", "operate"],
      monitoring: ["view"],
      subscriptions: ["view", "create", "edit", "delete", "operate"],
      "api-gateways": ["view", "create", "edit", "delete"],
      "bus-gateways": ["view", "create", "edit", "delete"],
      workgroups: ["view", "create", "edit", "delete"],
      partners: ["view", "create", "edit", "delete"],
      documents: ["view", "create", "edit", "delete"],
      "global-values": ["view", "create", "edit", "delete"],
      "retry-policies": ["view", "create", "edit", "delete", "operate"],
      notifiers: ["view", "create", "edit", "delete"],
    }),
    isSystem: false,
    createdOn: daysAgo(320),
  },
  {
    id: "role-operator",
    name: "Operator",
    description:
      "Runs day-to-day traffic: watches exchanges, retries failures, pauses and resumes — without changing configuration.",
    permissions: grants({
      dashboard: ["view"],
      exchanges: ["view", "operate"],
      monitoring: ["view"],
      subscriptions: ["view", "operate"],
      "api-gateways": ["view"],
      "bus-gateways": ["view"],
      workgroups: ["view"],
      partners: ["view"],
      documents: ["view"],
      "global-values": ["view"],
      "retry-policies": ["view", "operate"],
      notifiers: ["view"],
    }),
    isSystem: false,
    createdOn: daysAgo(320),
  },
  {
    id: "role-auditor",
    name: "Auditor",
    description: "Read-only access to everything, including members and roles.",
    permissions: ALL_PERMISSIONS.filter((p) => p.endsWith(".view")),
    isSystem: false,
    createdOn: daysAgo(200),
  },
];

export const SEED_USERS: User[] = [
  {
    id: "u-lina",
    displayName: "Lina Haddad",
    email: "lina@northline.co",
    phone: "+962 79 000 1111",
    roleIds: ["role-administrator"],
    status: "active",
    microsoftLinked: false,
    createdOn: daysAgo(400),
    lastActiveOn: daysAgo(0),
  },
  {
    id: "u-omar",
    displayName: "Omar Nasser",
    email: "omar@northline.co",
    roleIds: ["role-integration-manager"],
    status: "active",
    microsoftLinked: false,
    createdOn: daysAgo(310),
    lastActiveOn: daysAgo(1),
  },
  {
    id: "u-sara",
    displayName: "Sara Kanaan",
    email: "sara@northline.co",
    roleIds: ["role-operator"],
    status: "active",
    microsoftLinked: true,
    createdOn: daysAgo(150),
    lastActiveOn: daysAgo(0),
  },
  {
    id: "u-jude",
    displayName: "Jude Farah",
    email: "jude@northline.co",
    roleIds: ["role-auditor"],
    status: "active",
    microsoftLinked: false,
    createdOn: daysAgo(90),
    lastActiveOn: daysAgo(6),
  },
  {
    id: "u-tariq",
    displayName: "Tariq Salem",
    email: "tariq@northline.co",
    roleIds: ["role-integration-manager"],
    status: "disabled",
    microsoftLinked: false,
    createdOn: daysAgo(280),
    lastActiveOn: daysAgo(60),
  },
  {
    id: "u-dana",
    displayName: "dana@northline.co",
    email: "dana@northline.co",
    roleIds: ["role-operator"],
    status: "invited",
    microsoftLinked: false,
    createdOn: daysAgo(2),
  },
];

export const SEED_INVITES: Invite[] = [
  {
    token: "inv-dana-7f3a",
    email: "dana@northline.co",
    roleIds: ["role-operator"],
    roleNames: ["Operator"],
    invitedByName: "Lina Haddad",
    createdOn: daysAgo(2),
    expiresOn: daysAhead(5),
  },
];

/** Demo password shared by all seeded active accounts. */
export const DEMO_PASSWORD = "bitween";
