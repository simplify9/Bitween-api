import {
  Activity,
  ArrowLeftRight,
  BellRing,
  Cable,
  CalendarClock,
  FileText,
  Handshake,
  Layers,
  Network,
  RefreshCw,
  RotateCcw,
  Settings,
  SlidersHorizontal,
  Users,
  Webhook,
  Workflow,
  type LucideIcon,
} from "lucide-react";
import type { PermissionKey, Session } from "./api";

export interface NavItem {
  label: string;
  path: string;
  icon: LucideIcon;
  /** Visible when the session holds ANY of these. */
  permissions: PermissionKey[];
  /** True for areas that belong to a later phase of the redesign. */
  planned?: boolean;
}

export interface NavGroup {
  label: string;
  items: NavItem[];
}

/**
 * The whole information architecture in one place. The sidebar, the
 * role editor's live access preview, and the post-login redirect all
 * derive from this registry, so they can never drift apart.
 */
export const NAV_GROUPS: NavGroup[] = [
  {
    label: "Operate",
    items: [
      { label: "Exchanges", path: "/exchanges", icon: ArrowLeftRight, permissions: ["exchanges.view"] },
      {
        label: "Scheduled retries",
        path: "/scheduled-retries",
        icon: RefreshCw,
        permissions: ["exchanges.view"],
      },
      { label: "Queue health", path: "/queue-health", icon: Activity, permissions: ["monitoring.view"] },
    ],
  },
  {
    // The overview first, then entry points — how a document gets in — then the
    // pipelines it runs through, then who it's with. A gateway is not an integration.
    label: "Integrations",
    items: [
      // Gated on the bus alone: bus messages are what carry work *between*
      // gateways, so without that permission there is no flow left to map.
      { label: "Flow map", path: "/flow", icon: Network, permissions: ["bus-gateways.view"] },
      { label: "API gateways", path: "/api-gateways", icon: Webhook, permissions: ["api-gateways.view"] },
      { label: "Bus gateways", path: "/bus-gateways", icon: Cable, permissions: ["bus-gateways.view"] },
      { label: "Scheduled jobs", path: "/scheduled-jobs", icon: CalendarClock, permissions: ["subscriptions.view"] },
      { label: "All integrations", path: "/subscriptions", icon: Workflow, permissions: ["subscriptions.view"] },
      { label: "Partners", path: "/partners", icon: Handshake, permissions: ["partners.view"] },
    ],
  },
  {
    label: "Configuration",
    items: [
      { label: "Information types", path: "/information-types", icon: FileText, permissions: ["documents.view"] },
      { label: "Global values", path: "/global-values", icon: SlidersHorizontal, permissions: ["global-values.view"] },
      { label: "Notifiers", path: "/notifiers", icon: BellRing, permissions: ["notifiers.view"] },
      { label: "Work groups", path: "/work-groups", icon: Layers, permissions: ["workgroups.view"] },
      { label: "Retry policies", path: "/retry-policies", icon: RotateCcw, permissions: ["retry-policies.view"] },
    ],
  },
  {
    label: "Administration",
    items: [
      { label: "Team", path: "/team", icon: Users, permissions: ["users.view", "roles.view"] },
      { label: "Settings", path: "/settings", icon: Settings, permissions: ["settings.view"] },
    ],
  },
];

export const navItemVisible = (item: NavItem, permissions: PermissionKey[]) =>
  item.permissions.some((p) => permissions.includes(p));

export const visibleGroups = (permissions: PermissionKey[]): NavGroup[] =>
  NAV_GROUPS.map((g) => ({ ...g, items: g.items.filter((i) => navItemVisible(i, permissions)) })).filter(
    (g) => g.items.length > 0,
  );

/** Where to land after signing in: the first page this session can see. */
export const homePath = (session: Session): string => {
  const groups = visibleGroups(session.permissions);
  return groups[0]?.items[0]?.path ?? "/profile";
};
