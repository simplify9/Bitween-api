import type { LucideIcon } from "lucide-react";
import type { PermissionKey, Session } from "./api";
import { PAGES, type NavGroupId, type PageId } from "./pages";

export interface NavItem {
  id: PageId;
  label: string;
  path: string;
  icon: LucideIcon;
  /** Visible when the session holds ANY of these. */
  permissions: PermissionKey[];
}

export interface NavGroup {
  label: string;
  items: NavItem[];
}

const GROUP_LABELS: Record<NavGroupId, string> = {
  operate: "Operate",
  subscriptions: "Subscriptions",
  configuration: "Configuration",
  administration: "Administration",
};

/**
 * The sidebar, built from the page registry (src/pages.ts): every page with a `nav` entry, in
 * the registry's order, under its group. The role editor's live access preview and the
 * post-login redirect read the same groups, so they can never drift apart.
 */
export const NAV_GROUPS: NavGroup[] = (Object.keys(GROUP_LABELS) as NavGroupId[]).map((group) => ({
  label: GROUP_LABELS[group],
  items: (Object.keys(PAGES) as PageId[]).flatMap((id) => {
    const page = PAGES[id];
    if (page.nav?.group !== group) return [];
    return [
      {
        id,
        label: page.title,
        path: `/${page.path}`,
        icon: page.nav.icon,
        permissions: page.permission ? [page.permission] : [],
      },
    ];
  }),
}));

export const navItemVisible = (item: NavItem, permissions: PermissionKey[]) =>
  item.permissions.some((p) => permissions.includes(p));

export const visibleGroups = (permissions: PermissionKey[]): NavGroup[] =>
  NAV_GROUPS.map((g) => ({ ...g, items: g.items.filter((i) => navItemVisible(i, permissions)) })).filter(
    (g) => g.items.length > 0,
  );

/**
 * Where to land after signing in with nowhere particular to go.
 *
 * The dashboard, which is deliberately not in the sidebar — reached from the logo — so the
 * rule below would never pick it. Landing there is the one moment it is the obviously right
 * page: you have just arrived and want to know how the system is doing before going anywhere.
 *
 * Signing in *to get somewhere* is a different thing and does not come through here: the guard
 * remembers the page it turned away and login returns to it, so an expired session and a link
 * from a colleague both still end where they were headed.
 */
export const homePath = (session: Session): string => {
  if (session.permissions.includes("dashboard.view")) return "/dashboard";
  const groups = visibleGroups(session.permissions);
  return groups[0]?.items[0]?.path ?? "/profile";
};
