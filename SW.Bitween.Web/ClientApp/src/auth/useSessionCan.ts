import type { PermissionKey } from "../api";
import { useSession } from "./useSession";

/** Convenience hook for components that branch on a permission. */
export function useSessionCan(permission: PermissionKey): boolean {
  const { can } = useSession();
  return can(permission);
}
