import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Copy, EyeOff, ShieldCheck, Trash2 } from "lucide-react";
import { api, type ActionId, type PermissionKey } from "../../api";
import {
  ACTION_LABELS,
  ACTION_ORDER,
  allKeysIn,
  groupsIn,
  permissionKey,
  usePermissionCatalog,
} from "../../api/permissions";
import { useSession } from "../../auth/SessionContext";
import { visibleGroups } from "../../nav";
import { Badge, Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";

/** Live answer to "what would someone with this role actually see?" */
function AccessPreview({ permissions, total }: { permissions: Set<PermissionKey>; total: number }) {
  const groups = visibleGroups([...permissions]);
  return (
    <div className="rounded-xl border border-ink-200 bg-white p-4">
      <h3 className="text-xs font-semibold tracking-wide text-ink-500 uppercase">
        What members with this role see
      </h3>
      <p className="mt-1 mb-3 text-[13px] text-ink-500">
        The navigation below updates as you change permissions.
      </p>
      {groups.length === 0 ? (
        <div className="flex items-center gap-2 rounded-lg bg-ink-50 px-3 py-2.5 text-[13px] text-ink-500">
          <EyeOff className="size-4 shrink-0" />
          No pages yet — grant a View permission.
        </div>
      ) : (
        <div className="space-y-3 rounded-lg bg-ink-950 p-3">
          {groups.map((group) => (
            <div key={group.label}>
              <p className="px-1 pb-1 text-[10px] font-semibold tracking-widest text-ink-500 uppercase">
                {group.label}
              </p>
              <ul className="space-y-0.5">
                {group.items.map((item) => (
                  <li key={item.path} className="flex items-center gap-2 rounded-md px-1.5 py-1 text-[13px] text-ink-200">
                    <item.icon className="size-3.5 shrink-0 text-ink-400" />
                    {item.label}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}
      <p className="mt-3 font-mono text-xs text-ink-500">
        {permissions.size}/{total} permissions granted
      </p>
    </div>
  );
}

export function RoleEditor() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const isNew = id === undefined;
  const sourceId = isNew ? searchParams.get("from") : id;

  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { can } = useSession();
  const catalog = usePermissionCatalog();
  const areas = catalog.data ?? [];

  const source = useQuery({
    queryKey: ["role", sourceId],
    queryFn: () => api.getRole(sourceId!),
    enabled: sourceId !== null,
    retry: false,
  });

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [granted, setGranted] = useState<Set<PermissionKey> | null>(null);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  // Which role this form is currently filled from. React Router reuses this component when the
  // route changes between /new and /:id, so tracking identity — not a plain "have we loaded"
  // flag — is what makes Duplicate refill instead of keeping the role it came from.
  const identity = isNew ? `new:${sourceId ?? ""}` : `edit:${id}`;
  const [loadedFor, setLoadedFor] = useState<string | null>(null);
  const loaded = loadedFor === identity;

  // prefill once the source role arrives (edit, or duplicate via ?from=)
  useEffect(() => {
    if (loaded) return;
    if (sourceId === null) {
      setName("");
      setDescription("");
      setGranted(new Set());
      setLoadedFor(identity);
    } else if (source.data) {
      setName(isNew ? `Copy of ${source.data.name}` : source.data.name);
      setDescription(source.data.description);
      setGranted(new Set(source.data.permissions));
      setLoadedFor(identity);
    }
  }, [source.data, sourceId, isNew, loaded, identity]);

  const isSystem = !isNew && (source.data?.isSystem ?? false);
  const editable = !isSystem && (isNew ? can("roles.create") : can("roles.edit"));

  const dirty = useMemo(() => {
    if (!granted) return false;
    if (isNew) return true;
    if (!source.data) return false;
    return (
      name !== source.data.name ||
      description !== source.data.description ||
      granted.size !== source.data.permissions.length ||
      source.data.permissions.some((p) => !granted.has(p))
    );
  }, [granted, name, description, isNew, source.data]);

  const save = useMutation({
    mutationFn: () => {
      const input = { name, description, permissions: [...granted!] };
      return isNew ? api.createRole(input) : api.updateRole(id!, input);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["roles"] });
      void queryClient.invalidateQueries({ queryKey: ["role", id] });
      navigate("/team/roles");
    },
  });

  if (catalog.isPending) return <LoadingBlock label="Loading permissions…" />;
  if (!loaded && source.isPending) return <LoadingBlock label="Loading role…" />;
  if (!isNew && source.isError)
    return (
      <div className="py-16 text-center text-sm text-ink-500">
        This role no longer exists.{" "}
        <Link to="/team/roles" className="font-medium text-crimson-700 hover:underline">
          Back to roles
        </Link>
      </div>
    );
  if (!granted) return <LoadingBlock label="Loading role…" />;

  const toggle = (areaId: string, actionId: ActionId) => {
    if (!editable) return;
    const key = permissionKey(areaId, actionId);
    const next = new Set(granted);
    if (next.has(key)) {
      next.delete(key);
      // an area you can't view is an area you can't use at all
      if (actionId === "view") {
        for (const action of ACTION_ORDER) next.delete(permissionKey(areaId, action));
      }
    } else {
      next.add(key);
      if (actionId !== "view") next.add(permissionKey(areaId, "view"));
    }
    setGranted(next);
  };

  const toggleArea = (areaId: string, actionIds: ActionId[], allOn: boolean) => {
    if (!editable) return;
    const next = new Set(granted);
    for (const action of actionIds) {
      const key = permissionKey(areaId, action);
      if (allOn) next.delete(key);
      else next.add(key);
    }
    setGranted(next);
  };

  return (
    <div className="pb-24">
      <Link
        to="/team/roles"
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> Roles
      </Link>

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            {isNew ? "New role" : source.data?.name}
            {isSystem && (
              <Badge tone="ink">
                <ShieldCheck className="size-3" /> Built-in
              </Badge>
            )}
          </h1>
          {!isNew && source.data && (
            <p className="mt-1 text-sm text-ink-500">
              {source.data.memberCount} member{source.data.memberCount === 1 ? "" : "s"} hold this role.
            </p>
          )}
        </div>
        {!isNew && can("roles.create") && (
          <Button onClick={() => navigate(`/team/roles/new?from=${id}`)}>
            <Copy className="size-4" /> Duplicate
          </Button>
        )}
      </div>

      {isSystem && (
        <div className="mb-6 rounded-xl border border-ink-200 bg-ink-50 px-4 py-3 text-sm text-ink-600">
          This role is built in: it always holds every permission, so there's always someone who can
          manage the instance. It can't be edited or deleted.
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-[1fr_300px]">
        <div className="min-w-0 space-y-6">
          {!isSystem && (
            <div className="grid gap-4 rounded-xl border border-ink-200 bg-white p-5 sm:grid-cols-2">
              <Field label="Name" htmlFor="role-name">
                <TextInput
                  id="role-name"
                  value={name}
                  disabled={!editable}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="e.g. Night-shift operator"
                />
              </Field>
              <Field label="Description" htmlFor="role-desc" hint="Shown wherever the role is picked.">
                <TextInput
                  id="role-desc"
                  value={description}
                  disabled={!editable}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="What is this role for?"
                />
              </Field>
            </div>
          )}

          {groupsIn(areas).map((group) => {
            const groupAreas = areas.filter((a) => a.group === group);
            return (
              <section key={group} className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
                <table className="w-full min-w-130 text-sm">
                  <thead>
                    <tr className="border-b border-ink-100">
                      <th className="px-5 py-3 text-left text-xs font-semibold tracking-wide text-ink-500 uppercase">
                        {group}
                      </th>
                      {ACTION_ORDER.map((action) => (
                        <th key={action} className="w-18 px-2 py-3 text-center text-xs font-medium text-ink-500">
                          {ACTION_LABELS[action]}
                        </th>
                      ))}
                      <th className="w-14 px-3 py-3 text-center text-xs font-medium text-ink-500">All</th>
                    </tr>
                  </thead>
                  <tbody>
                    {groupAreas.map((area) => {
                      const actionIds = area.actions.map((a) => a.id);
                      const allOn = actionIds.every((a) => granted.has(permissionKey(area.id, a)));
                      return (
                        <tr key={area.id} className="border-b border-ink-100 last:border-b-0">
                          <td className="px-5 py-3">
                            <p className="font-medium text-ink-800">{area.label}</p>
                            <p className="text-[13px] text-ink-500">{area.description}</p>
                          </td>
                          {ACTION_ORDER.map((action) => {
                            const def = area.actions.find((a) => a.id === action);
                            if (!def)
                              return (
                                <td key={action} className="px-2 py-3 text-center text-ink-200">
                                  —
                                </td>
                              );
                            const key = permissionKey(area.id, action);
                            return (
                              <td key={action} className="px-2 py-3 text-center">
                                <input
                                  type="checkbox"
                                  aria-label={`${area.label}: ${ACTION_LABELS[action]}`}
                                  title={`${key} — ${def.description}`}
                                  checked={granted.has(key)}
                                  disabled={!editable}
                                  onChange={() => toggle(area.id, action)}
                                  className="size-4 cursor-pointer rounded accent-crimson-600 disabled:cursor-default"
                                />
                              </td>
                            );
                          })}
                          <td className="px-3 py-3 text-center">
                            <input
                              type="checkbox"
                              aria-label={`${area.label}: everything`}
                              checked={allOn}
                              disabled={!editable}
                              onChange={() => toggleArea(area.id, actionIds, allOn)}
                              className="size-4 cursor-pointer rounded accent-ink-700 disabled:cursor-default"
                            />
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </section>
            );
          })}
        </div>

        <div className="space-y-4 lg:sticky lg:top-8 lg:self-start">
          <AccessPreview permissions={granted} total={allKeysIn(areas).length} />

          {!isNew && !isSystem && can("roles.delete") && (
            <div className="rounded-xl border border-danger-200 bg-white p-4">
              <h3 className="text-xs font-semibold tracking-wide text-danger-800 uppercase">
                Danger zone
              </h3>
              <p className="mt-1 mb-3 text-[13px] text-ink-500">
                Roles can only be deleted once no member holds them.
              </p>
              <Button variant="danger" size="sm" onClick={() => setConfirmingDelete(true)}>
                <Trash2 className="size-3.5" /> Delete role
              </Button>
            </div>
          )}
        </div>
      </div>

      {editable && dirty && (
        <div className="fixed inset-x-0 bottom-0 z-30 border-t border-ink-200 bg-white/95 backdrop-blur lg:left-62">
          {/* right padding keeps the fixed demo pill clear of the save button */}
          <div className="flex items-center justify-between gap-3 py-2.5 pl-4 pr-40 sm:pl-6">
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-ink-800">
                {isNew ? "New role" : "Unsaved changes"}
              </p>
              <FormError>{save.error?.message}</FormError>
            </div>
            <div className="flex shrink-0 gap-2">
              <Button onClick={() => navigate("/team/roles")}>Cancel</Button>
              <Button variant="primary" busy={save.isPending} onClick={() => save.mutate()}>
                {isNew ? "Create role" : "Save changes"}
              </Button>
            </div>
          </div>
        </div>
      )}

      {confirmingDelete && (
        <ConfirmDialog
          title="Delete this role?"
          body={
            <>
              <strong className="font-medium text-ink-800">{source.data?.name}</strong> will be gone
              for good. Members holding it must be moved to another role first.
            </>
          }
          confirmLabel="Delete role"
          onConfirm={async () => {
            await api.deleteRole(id!);
            void queryClient.invalidateQueries({ queryKey: ["roles"] });
            navigate("/team/roles");
          }}
          onClose={() => setConfirmingDelete(false)}
        />
      )}
    </div>
  );
}
