import { Link, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ChevronRight, Plus, ShieldCheck } from "lucide-react";
import { allKeysIn, areasFor, usePermissionCatalog } from "../../api/permissions";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadError, LoadingBlock } from "../../components/ui/basics";
import { useTeamSource } from "./teamSource";

/** Ours, or one of their own: a partner's admin can give ours but not change them. */
export const READY_MADE_TITLE = "Made by us. You can give it, and duplicate it to make your own.";

export function RolesPage() {
  const navigate = useNavigate();
  const source = useTeamSource();
  const roles = useQuery({ queryKey: source.keys.roles, queryFn: () => source.listRoles() });
  const catalog = usePermissionCatalog().data ?? [];
  // Counted on each role's own side: our administrators hold the partner portal keys too, which is
  // what lets them hand out partner roles, but they are no part of what an administrator does.
  const sideKeys = (forPartners: boolean) => new Set(allKeysIn(areasFor(catalog, forPartners)));
  const teamKeys = sideKeys(false);
  const partnerKeys = sideKeys(true);

  const header = (
    <PageHeader
      title="Roles"
      description={
        source.partner
          ? "What your company's people can see and do here. Give them ours, or make your own."
          : "A role is a reusable set of permissions. Open one to see - and shape - exactly what its members can do."
      }
      help={source.partner ? undefined : {
        title: "How roles work",
        body: (
          <>
            <p>
              Members are given <strong>roles</strong>, and what they can see and do comes from the
              permissions in those roles. Each row shows how many members hold the role, and how many of
              all the permissions it grants.
            </p>
            <p>
              A <strong>Built-in</strong> role always holds every permission, so there is always someone who
              can manage the instance. It can't be edited or deleted, but you can{" "}
              <strong>Duplicate</strong> it as a starting point for your own.
            </p>
          </>
        ),
      }}
      actions={
        source.can.createRole && (
          <Button variant="primary" onClick={() => navigate(`${source.rolesPath}/new`)}>
            <Plus className="size-4" /> New role
          </Button>
        )
      }
    />
  );

  if (roles.isPending)
    return (
      <div>
        {header}
        <LoadingBlock label="Loading roles…" />
      </div>
    );

  if (roles.isError)
    return (
      <div>
        {header}
        <LoadError error={roles.error} what="roles" onRetry={() => void roles.refetch()} />
      </div>
    );

  return (
    <div>
      {header}

      {(roles.data ?? []).length === 0 ? (
        <EmptyState icon={<ShieldCheck />} title="No roles yet">
          Create a role to define what members can see and do.
        </EmptyState>
      ) : (
        <ul className="space-y-2">
          {(roles.data ?? []).map((role) => (
            <li key={role.id}>
              <Link
                to={`${source.rolesPath}/${role.id}`}
                className="flex items-center gap-4 rounded-xl border border-ink-200 bg-white px-5 py-4 transition-colors hover:border-ink-300 hover:bg-ink-50/50"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <h3 className="font-semibold text-ink-900">{role.name}</h3>
                    {source.partner ? (
                      (role.isSystem || role.partnerId === null) && (
                        <Badge tone="ink" title={READY_MADE_TITLE}>
                          <ShieldCheck className="size-3" /> Ready-made
                        </Badge>
                      )
                    ) : (
                      role.isSystem && (
                        <Badge tone="ink">
                          <ShieldCheck className="size-3" /> Built-in
                        </Badge>
                      )
                    )}
                    {!source.partner && role.partnerName && (
                      <Badge tone="ink" title={`Made by ${role.partnerName}'s admin, for ${role.partnerName}'s people only.`}>
                        {role.partnerName}&apos;s own
                      </Badge>
                    )}
                    {!source.partner && role.forPartners && !role.partnerName && (
                      <Badge tone="ink" title="For a partner's people: they sign in to the partner portal and see only their own partner's data.">
                        Partner role
                      </Badge>
                    )}
                  </div>
                  <p className="mt-0.5 line-clamp-2 text-sm text-ink-500">{role.description}</p>
                </div>
                <div className="hidden shrink-0 text-right text-sm sm:block">
                  <p className="font-medium text-ink-800">
                    {source.partner
                      ? `${role.memberCount} ${role.memberCount === 1 ? "person" : "people"}`
                      : `${role.memberCount} member${role.memberCount === 1 ? "" : "s"}`}
                  </p>
                  <p className="font-mono text-xs text-ink-500">
                    {role.permissions.filter((p) => (role.forPartners ? partnerKeys : teamKeys).has(p)).length}/
                    {(role.forPartners ? partnerKeys : teamKeys).size} permissions
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
