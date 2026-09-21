import type { ReactNode } from "react";
import { Navigate, Outlet, useLocation } from "react-router";
import { CloudOff, Lock } from "lucide-react";
import type { PermissionKey } from "../api";
import { labelIn, usePermissionCatalog } from "../api/permissions";
import { Button } from "../components/ui/basics";
import { useSession } from "./SessionContext";

/** Redirects to /login when signed out; shows a splash while checking. */
export function RequireAuth() {
  const { session, initializing, unreachable, retry } = useSession();
  const location = useLocation();

  if (initializing) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-canvas">
        <img src={import.meta.env.BASE_URL + "brand/BitweenIcon.svg"} alt="Bitween" className="size-10 animate-pulse" />
      </div>
    );
  }
  // Nobody said this session was over — the server could not be asked. Sending someone
  // to sign in here would be a lie, and one that loses the page they were on and invites
  // them to re-enter a password they never needed to.
  if (unreachable) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-3 px-6 text-center">
        <span className="flex size-12 items-center justify-center rounded-full bg-ink-100 text-ink-400">
          <CloudOff className="size-5" />
        </span>
        <h1 className="text-lg font-semibold text-ink-900">Can't reach Bitween</h1>
        <p className="max-w-prose text-sm text-ink-500">
          You are still signed in — the server just didn't answer. This usually clears on its own.
        </p>
        <Button onClick={retry}>Try again</Button>
      </div>
    );
  }
  if (!session) {
    return <Navigate to="/login" state={{ from: location.pathname + location.search }} replace />;
  }
  // An account whose password nobody chose holds a token that grants nothing, so every screen
  // past here would refuse it one at a time. Sent to the one screen that can end that instead.
  if (session.mustChangePassword && location.pathname !== "/change-password") {
    return <Navigate to="/change-password" replace />;
  }
  return <Outlet />;
}

/** Full-page stop for routes the session's roles don't unlock. */
export function AccessDenied({ permission }: { permission: PermissionKey }) {
  // Falls back to the raw key until the catalog lands — never blocks the message.
  const label = labelIn(usePermissionCatalog().data ?? [], permission);
  return (
    <div className="flex flex-col items-center justify-center gap-3 px-6 py-24 text-center">
      <span className="flex size-12 items-center justify-center rounded-full bg-ink-100 text-ink-400">
        <Lock className="size-5" />
      </span>
      <h1 className="text-lg font-semibold text-ink-900">You don't have access to this page</h1>
      <p className="max-w-md text-sm text-ink-500">
        Seeing it needs the <strong className="font-medium text-ink-700">{label}</strong>{" "}
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
