import type { ActionId, PermissionArea, PermissionKey } from "./types";

/**
 * The permission catalog: every gated page and action in Bitween.
 * Roles are built by picking grants from this list. The area groups
 * mirror the app's navigation groups so "what a role unlocks" maps
 * one-to-one onto what its members see.
 */
export const PERMISSION_CATALOG: PermissionArea[] = [
  // ——— Operate ———
  {
    id: "dashboard",
    label: "Dashboard",
    group: "Operate",
    description: "Traffic and health overview.",
    actions: [{ id: "view", description: "See the dashboard." }],
  },
  {
    id: "exchanges",
    label: "Exchanges",
    group: "Operate",
    description: "Every message that flows through Bitween.",
    actions: [
      { id: "view", description: "Browse exchanges, payloads and traces." },
      { id: "operate", description: "Retry or resubmit failed exchanges." },
    ],
  },
  {
    id: "monitoring",
    label: "Queue health",
    group: "Operate",
    description: "Live message-queue throughput and consumers.",
    actions: [{ id: "view", description: "See queue health and rates." }],
  },

  // ——— Integrations ———
  {
    id: "subscriptions",
    label: "Integrations",
    group: "Integrations",
    description: "The configured pipelines that process exchanges.",
    actions: [
      { id: "view", description: "Browse integrations and their configuration." },
      { id: "create", description: "Create integrations." },
      { id: "edit", description: "Change adapters, mappings and settings." },
      { id: "delete", description: "Delete integrations." },
      { id: "operate", description: "Pause, resume, receive now, aggregate now." },
    ],
  },
  {
    id: "api-gateways",
    label: "API gateways",
    group: "Integrations",
    description: "HTTP entry points partners call into.",
    actions: [
      { id: "view", description: "Browse API gateways and attached partners." },
      { id: "create", description: "Create new API gateways." },
      { id: "edit", description: "Change gateways and partner attachments." },
      { id: "delete", description: "Delete API gateways." },
    ],
  },
  {
    id: "bus-gateways",
    label: "Bus gateways",
    group: "Integrations",
    description: "Bus listeners that route documents to integrations.",
    actions: [
      { id: "view", description: "Browse bus gateways and routes." },
      { id: "create", description: "Create new bus gateways." },
      { id: "edit", description: "Change gateways and routes." },
      { id: "delete", description: "Delete bus gateways." },
    ],
  },
  {
    id: "workgroups",
    label: "Work groups",
    group: "Integrations",
    description: "Processing lanes that spread load across queues.",
    actions: [
      { id: "view", description: "See work groups and their throughput." },
      { id: "create", description: "Create work groups." },
      { id: "edit", description: "Change work group settings." },
      { id: "delete", description: "Delete unused work groups." },
    ],
  },

  // ——— Configuration ———
  {
    id: "partners",
    label: "Partners",
    group: "Configuration",
    description: "The external parties you exchange data with.",
    actions: [
      { id: "view", description: "Browse partners and their properties." },
      { id: "create", description: "Create partners." },
      { id: "edit", description: "Change partner details, properties and API keys." },
      { id: "delete", description: "Delete partners." },
    ],
  },
  {
    id: "documents",
    label: "Information types",
    group: "Configuration",
    description: "The kinds of business documents that flow between partners.",
    actions: [
      { id: "view", description: "Browse information types." },
      { id: "create", description: "Create information types." },
      { id: "edit", description: "Change information types, codes and promoted properties." },
      { id: "delete", description: "Delete unused information types." },
    ],
  },
  {
    id: "global-values",
    label: "Global values",
    group: "Configuration",
    description: "Shared value sets adapters can reference.",
    actions: [
      { id: "view", description: "Browse global value sets." },
      { id: "create", description: "Create value sets." },
      { id: "edit", description: "Change value sets." },
      { id: "delete", description: "Delete value sets." },
    ],
  },
  {
    id: "retry-policies",
    label: "Retry policies",
    group: "Configuration",
    description: "Rules for retrying failed exchanges.",
    actions: [
      { id: "view", description: "Browse retry policies and scheduled retries." },
      { id: "create", description: "Create retry policies." },
      { id: "edit", description: "Change retry policies." },
      { id: "delete", description: "Delete retry policies." },
      { id: "operate", description: "Run scheduled retries now." },
    ],
  },
  {
    id: "notifiers",
    label: "Notifiers",
    group: "Configuration",
    description: "Alerts sent when exchanges fail or succeed.",
    actions: [
      { id: "view", description: "Browse notifiers and their delivery history." },
      { id: "create", description: "Create notifiers." },
      { id: "edit", description: "Change notifiers." },
      { id: "delete", description: "Delete notifiers." },
    ],
  },

  // ——— Administration ———
  {
    id: "users",
    label: "Members",
    group: "Administration",
    description: "The people who can sign in to this Bitween instance.",
    actions: [
      { id: "view", description: "See the member list." },
      { id: "create", description: "Invite new members." },
      {
        id: "edit",
        description: "Change members' roles, disable accounts, reset passwords.",
      },
      { id: "delete", description: "Remove members." },
    ],
  },
  {
    id: "roles",
    label: "Roles",
    group: "Administration",
    description: "What each kind of member is allowed to do.",
    actions: [
      { id: "view", description: "See roles and their permissions." },
      { id: "create", description: "Create roles." },
      { id: "edit", description: "Change role permissions." },
      { id: "delete", description: "Delete unassigned roles." },
    ],
  },
  {
    id: "settings",
    label: "Settings",
    group: "Administration",
    description: "Instance-wide configuration.",
    actions: [
      { id: "view", description: "See instance settings." },
      { id: "edit", description: "Change instance settings." },
    ],
  },
];

export const PERMISSION_GROUPS = ["Operate", "Integrations", "Configuration", "Administration"];

export const ACTION_LABELS: Record<ActionId, string> = {
  view: "View",
  create: "Create",
  edit: "Edit",
  delete: "Delete",
  operate: "Operate",
};

/** All actions in the order matrix columns render. */
export const ACTION_ORDER: ActionId[] = ["view", "create", "edit", "delete", "operate"];

export const permissionKey = (areaId: string, actionId: ActionId): PermissionKey =>
  `${areaId}.${actionId}`;

export const ALL_PERMISSIONS: PermissionKey[] = PERMISSION_CATALOG.flatMap((area) =>
  area.actions.map((a) => permissionKey(area.id, a.id)),
);
