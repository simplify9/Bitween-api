import { Navigate, NavLink, Outlet } from "react-router";
import { useSession } from "../../auth/SessionContext";
import { AccessDenied } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";

/**
 * One place for everything people-related: who can sign in (Members)
 * and what they're allowed to do (Roles). Tabs are URL paths so every
 * view is linkable.
 */
export function TeamPage() {
  const { can } = useSession();
  const canMembers = can("users.view");
  const canRoles = can("roles.view");

  if (!canMembers && !canRoles) return <AccessDenied permission="users.view" />;

  const tabs = [
    ...(canMembers ? [{ to: "/team/members", label: "Members" }] : []),
    ...(canRoles ? [{ to: "/team/roles", label: "Roles" }] : []),
  ];

  return (
    <div>
      <PageHeader
        title="Team"
        description="The people who can sign in to this Bitween instance, and what each of them is allowed to do."
        help={{
          title: "How access works",
          body: (
            <>
              <p>
                Every member holds one or more <strong>roles</strong>, and every role is a list of{" "}
                <strong>permissions</strong> — page by page, action by action. A member can do
                something if any of their roles allows it; everything else is hidden from them.
              </p>
              <p>
                To bring someone in: create or pick a role on the Roles tab, then invite them from
                the Members tab. They'll get a link to choose their own password.
              </p>
            </>
          ),
        }}
      />

      <div className="mb-6 flex gap-1 border-b border-ink-200">
        {tabs.map((tab) => (
          <NavLink
            key={tab.to}
            to={tab.to}
            className={({ isActive }) =>
              `-mb-px border-b-2 px-3.5 py-2 text-sm font-medium transition-colors ${
                isActive
                  ? "border-crimson-600 text-crimson-700"
                  : "border-transparent text-ink-500 hover:border-ink-300 hover:text-ink-800"
              }`
            }
          >
            {tab.label}
          </NavLink>
        ))}
      </div>

      <Outlet />
    </div>
  );
}

export function TeamIndexRedirect() {
  const { can } = useSession();
  return <Navigate to={can("users.view") ? "/team/members" : "/team/roles"} replace />;
}
