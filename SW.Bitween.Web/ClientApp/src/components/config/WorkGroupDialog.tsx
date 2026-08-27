import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type WorkGroup } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { Button, FormError, LoadingBlock } from "../ui/basics";
import { Field, TextInput } from "../ui/forms";
import { Dialog } from "../ui/overlays";
import { suggestSlug } from "../../lib/identifiers";

/**
 * A work group's editable settings, as one component.
 *
 * Shared by the group's own page and the dialog opened from a subscription, so
 * there is one definition of what a work group is. Its live queue stats and
 * used-by list stay on the page.
 */
export interface WorkGroupDraft {
  name: string;
  busMessageName: string;
  prefetch: number;
  priority: number;
}

export const workGroupDraftOf = (g: WorkGroup): WorkGroupDraft => ({
  name: g.name,
  busMessageName: g.busMessageName,
  prefetch: g.options.rabbitMqOptions.consumerSettings.prefetch,
  priority: g.options.rabbitMqOptions.consumerSettings.priority,
});

export function WorkGroupFields({
  draft,
  onChange,
  canEdit,
  /** The page edits the name in its own title; a dialog has to ask for it. */
  showName = false,
  idPrefix = "wg",
}: {
  draft: WorkGroupDraft;
  onChange: (draft: WorkGroupDraft) => void;
  canEdit: boolean;
  showName?: boolean;
  idPrefix?: string;
}) {
  const [busNameTouched, setBusNameTouched] = useState(false);
  const set = <K extends keyof WorkGroupDraft>(key: K, value: WorkGroupDraft[K]) =>
    onChange({ ...draft, [key]: value });

  return (
    <div className="space-y-4">
      {showName && (
        <div className="max-w-sm">
          <Field label="Name" htmlFor={`${idPrefix}-name`}>
            <TextInput
              id={`${idPrefix}-name`}
              value={draft.name}
              disabled={!canEdit}
              placeholder="e.g. Priority lane"
              onChange={(e) => {
                // Derived until it is edited by hand, same as the create page did.
                onChange({
                  ...draft,
                  name: e.target.value,
                  busMessageName: busNameTouched ? draft.busMessageName : suggestSlug(e.target.value),
                });
              }}
            />
          </Field>
        </div>
      )}
      <div className="max-w-sm">
        <Field
          label="Bus message name"
          htmlFor={`${idPrefix}-busname`}
          hint="Combined with the group's id to form its queue name. No spaces."
        >
          <TextInput
            id={`${idPrefix}-busname`}
            value={draft.busMessageName}
            disabled={!canEdit}
            className="font-mono"
            placeholder="priority-lane"
            onChange={(e) => {
              setBusNameTouched(true);
              set("busMessageName", e.target.value.toLowerCase().replace(/\s+/g, ""));
            }}
          />
        </Field>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Prefetch" htmlFor={`${idPrefix}-prefetch`} hint="Messages pulled per consumer at once.">
          <TextInput
            id={`${idPrefix}-prefetch`}
            type="number"
            min={1}
            value={draft.prefetch}
            disabled={!canEdit}
            onChange={(e) => set("prefetch", Math.max(1, Number(e.target.value)))}
          />
        </Field>
        <Field label="Priority" htmlFor={`${idPrefix}-priority`} hint="Higher runs before lower.">
          <TextInput
            id={`${idPrefix}-priority`}
            type="number"
            min={0}
            value={draft.priority}
            disabled={!canEdit}
            onChange={(e) => set("priority", Math.max(0, Number(e.target.value)))}
          />
        </Field>
      </div>
    </div>
  );
}

const EMPTY: WorkGroupDraft = { name: "", busMessageName: "", prefetch: 10, priority: 5 };

/** A work group, created or edited in place — reached from a subscription's lane picker. */
export function WorkGroupDialog({
  groupId,
  onClose,
  onSaved,
}: {
  /** null opens it empty, to create. */
  groupId: number | null;
  onClose: () => void;
  onSaved?: (id: number) => void;
}) {
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("workgroups.edit");
  const [draft, setDraft] = useState<WorkGroupDraft | null>(groupId === null ? EMPTY : null);

  const existing = useQuery({
    queryKey: ["work-group", groupId],
    queryFn: () => api.getWorkGroup(groupId!),
    enabled: groupId !== null,
  });

  useEffect(() => {
    if (existing.data && draft === null) setDraft(workGroupDraftOf(existing.data));
  }, [existing.data, draft]);

  const save = useMutation({
    mutationFn: async () => {
      if (groupId !== null) {
        await api.updateWorkGroup(groupId, draft!);
        return groupId;
      }
      const created = await api.createWorkGroup(draft!);
      return created.id;
    },
    onSuccess: (id) => {
      void queryClient.invalidateQueries({ queryKey: ["work-groups"] });
      void queryClient.invalidateQueries({ queryKey: ["work-groups-search"] });
      void queryClient.invalidateQueries({ queryKey: ["work-group", id] });
      onSaved?.(id);
      onClose();
    },
  });

  const missing = draft
    ? [
        draft.name.trim().length < 2 && "a name",
        !draft.busMessageName.trim() && "a bus message name",
      ].filter((m): m is string => typeof m === "string")
    : [];

  return (
    <Dialog title={groupId === null ? "New work group" : "Work group"} onClose={onClose}>
      {!draft ? (
        <LoadingBlock label="Loading the work group…" />
      ) : (
        <div className="space-y-4">
          <p className="text-[13px] text-ink-500">
            Gives its own queue, priority and prefetch to whatever subscriptions you assign to it.
          </p>
          <WorkGroupFields draft={draft} onChange={setDraft} canEdit={canEdit} showName idPrefix="wgd" />
          <FormError>{save.error?.message}</FormError>
          <div className="flex items-center justify-end gap-3">
            {missing.length > 0 && (
              <p className="text-[13px] text-ink-500">Still needs {missing.join(" and ")}.</p>
            )}
            <Button onClick={onClose}>Cancel</Button>
            <Button
              variant="primary"
              busy={save.isPending}
              disabled={missing.length > 0}
              onClick={() => save.mutate()}
            >
              {groupId === null ? "Create work group" : "Save changes"}
            </Button>
          </div>
        </div>
      )}
    </Dialog>
  );
}
