import type { ReactNode } from "react";
import { Navigate, Outlet, useLocation } from "react-router";
import { Lock } from "lucide-react";
import type { PermissionKey } from "../api";
import { PERMISSION_CATALOG, ACTION_LABELS } from "../api/permissions";
import { useSession } from "./SessionContext";

/** Redirects to /login when signed out; shows a splash while checking. */
export function RequireAuth() {
  const { session, initializing } = useSession();
  const location = useLocation();

  if (initializing) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-canvas">
        <img src={import.meta.env.BASE_URL + "brand/BitweenIcon.svg"} alt="Bitween" className="size-10 animate-pulse" />
      </div>
    );
  }
  if (!session) {
    return <Navigate to="/login" state={{ from: location.pathname + location.search }} replace />;
  }
  return <Outlet />;
}

export const permissionLabel = (key: PermissionKey): string => {
  const [areaId, actionId] = key.split(".");
  const area = PERMISSION_CATALOG.find((a) => a.id === areaId);
  const action = ACTION_LABELS[actionId as keyof typeof ACTION_LABELS];
  return area ? `${area.label} · ${action ?? actionId}` : key;
};

/** Full-page stop for routes the session's roles don't unlock. */
export function AccessDenied({ permission }: { permission: PermissionKey }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 px-6 py-24 text-center">
      <span className="flex size-12 items-center justify-center rounded-full bg-ink-100 text-ink-400">
        <Lock className="size-5" />
      </span>
      <h1 className="text-lg font-semibold text-ink-900">You don't have access to this page</h1>
      <p className="max-w-md text-sm text-ink-500">
        Seeing it needs the <strong className="font-medium text-ink-700">{permissionLabel(permission)}</strong>{" "}
        permission, which none of your roles grant. An administrator can change that under Team → Roles.
      </p>
    </div>
  );
}

/** Route-level permission gate. */
export function RequirePermission({
  permission,
  children,
}: {
  permission: PermissionKey;
  children: ReactNode;
}) {
  const { can } = useSession();
  if (!can(permission)) return <AccessDenied permission={permission} />;
  return <>{children}</>;
}

/** Inline gate: unauthorized actions are hidden, never dimmed. */
export function Can({ permission, children }: { permission: PermissionKey; children: ReactNode }) {
  const { can } = useSession();
  if (!can(permission)) return null;
  return <>{children}</>;
}

/** Convenience hook for components that branch on a permission. */
export function useSessionCan(permission: PermissionKey): boolean {
  const { can } = useSession();
  return can(permission);
}
