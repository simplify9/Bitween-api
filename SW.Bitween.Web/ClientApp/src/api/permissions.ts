import { useQuery } from "@tanstack/react-query";
import { api } from ".";
import type { ActionId, PermissionArea, PermissionKey } from "./types";

/**
 * The permission catalog is defined and enforced in the backend (SW.Bitween.Sdk/Model/Permissions.cs)
 * and served by GET /permissions. It deliberately does not live here as well: a second copy would
 * silently drift, and the role matrix would start offering grants the API ignores.
 *
 * What stays here is presentation — how an action is worded and the order columns appear in.
 */
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

/** Static per deployment, so it's fetched once and kept. */
export function usePermissionCatalog() {
  return useQuery({
    queryKey: ["permission-catalog"],
    queryFn: () => api.getPermissionCatalog(),
    staleTime: Infinity,
  });
}

export const allKeysIn = (areas: PermissionArea[]): PermissionKey[] =>
  areas.flatMap((area) => area.actions.map((a) => permissionKey(area.id, a.id as ActionId)));

/** Navigation groups, in the order the backend lists them. */
export const groupsIn = (areas: PermissionArea[]): string[] => [
  ...new Set(areas.map((area) => area.group)),
];

/** "Subscriptions · Edit" for a key, falling back to the raw key for anything unrecognised. */
export const labelIn = (areas: PermissionArea[], key: PermissionKey): string => {
  const [areaId, actionId] = key.split(".");
  const area = areas.find((a) => a.id === areaId);
  return area ? `${area.label} · ${ACTION_LABELS[actionId as ActionId] ?? actionId}` : key;
};
