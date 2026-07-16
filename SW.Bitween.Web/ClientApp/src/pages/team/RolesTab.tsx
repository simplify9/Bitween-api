import { Link, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ChevronRight, Plus, ShieldCheck } from "lucide-react";
import { api } from "../../api";
import { ALL_PERMISSIONS } from "../../api/permissions";
import { Can } from "../../auth/guards";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";

export function RolesTab() {
  const navigate = useNavigate();
  const roles = useQuery({ queryKey: ["roles"], queryFn: () => api.listRoles() });

  if (roles.isPending) return <LoadingBlock label="Loading roles…" />;

  return (
    <div>
      <div className="mb-4 flex items-center justify-between gap-3">
        <p className="text-sm text-ink-500">
          A role is a reusable set of permissions. Open one to see — and shape — exactly what its
          members can do.
        </p>
        <Can permission="roles.create">
          <Button variant="primary" onClick={() => navigate("/team/roles/new")}>
            <Plus className="size-4" /> New role
          </Button>
        </Can>
      </div>

      {(roles.data ?? []).length === 0 ? (
        <EmptyState icon={<ShieldCheck />} title="No roles yet">
          Create a role to define what members can see and do.
        </EmptyState>
      ) : (
        <ul className="space-y-2">
          {(roles.data ?? []).map((role) => (
            <li key={role.id}>
              <Link
                to={`/team/roles/${role.id}`}
                className="flex items-center gap-4 rounded-xl border border-ink-200 bg-white px-5 py-4 transition-colors hover:border-ink-300 hover:bg-ink-50/50"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <h3 className="font-semibold text-ink-900">{role.name}</h3>
                    {role.isSystem && (
                      <Badge tone="ink">
                        <ShieldCheck className="size-3" /> Built-in
                      </Badge>
                    )}
                  </div>
                  <p className="mt-0.5 line-clamp-2 text-sm text-ink-500">{role.description}</p>
                </div>
                <div className="hidden shrink-0 text-right text-sm sm:block">
                  <p className="font-medium text-ink-800">
                    {role.memberCount} member{role.memberCount === 1 ? "" : "s"}
                  </p>
                  <p className="font-mono text-xs text-ink-500">
                    {role.permissions.length}/{ALL_PERMISSIONS.length} permissions
                  </p>
                </div>
                <ChevronRight className="size-4 shrink-0 text-ink-300" />
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
