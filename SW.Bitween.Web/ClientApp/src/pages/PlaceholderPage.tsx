import { Link } from "react-router";
import type { NavItem } from "../nav";
import { RequirePermission } from "../auth/guards";

/**
 * Stands in for the areas that belong to later phases of the redesign.
 * They're real routes with real permissions so role gating can be
 * demonstrated across the whole navigation today.
 */
export function PlaceholderPage({ item }: { item: NavItem }) {
  return (
    <RequirePermission permission={item.permissions[0]}>
      <div className="flex flex-col items-center justify-center gap-3 px-6 py-24 text-center">
        <span className="flex size-12 items-center justify-center rounded-2xl bg-crimson-50 text-crimson-600">
          <item.icon className="size-5" />
        </span>
        <h1 className="text-lg font-semibold tracking-tight text-ink-900">{item.label}</h1>
        <p className="max-w-md text-sm leading-relaxed text-ink-500">
          This area is planned for a later phase of the redesign. It's already wired into roles and
          navigation, so you can control who will see it from{" "}
          <Link to="/team/roles" className="font-medium text-crimson-700 hover:underline">
            Team → Roles
          </Link>
          .
        </p>
      </div>
    </RequirePermission>
  );
}

export function NotFoundPage() {
  return (
    <div className="flex flex-col items-center justify-center gap-3 px-6 py-24 text-center">
      <p className="font-mono text-sm text-ink-400">404</p>
      <h1 className="text-lg font-semibold tracking-tight text-ink-900">This page doesn't exist</h1>
      <Link to="/" className="text-[13px] font-medium text-crimson-700 hover:underline">
        Go to your home page
      </Link>
    </div>
  );
}
