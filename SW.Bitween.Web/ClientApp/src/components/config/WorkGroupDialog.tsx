import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type WorkGroup } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { Button, FormError, LoadingBlock } from "../ui/basics";
import { Field, TextInput } from "../ui/forms";
import { ConfirmDialog, Dialog } from "../ui/overlays";
import { suggestSlug } from "../../lib/identifiers";
import { keys } from "../../api/queryKeys";
import { useRabbitMqManagementConfigured } from "../../lib/appConfig";

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

/**
 * Messages still in a group's queues — both of its lanes, retries and dead letters included —
 * from the same live snapshot Queue health polls. `null` when it can't be known: no right to see
 * queue health, RabbitMQ management not configured, or not loaded yet.
 */
export function useQueuedMessages(groupId: number | null): number | null {
  const canMonitor = useSessionCan("monitoring.view");
  const rabbitMqConfigured = useRabbitMqManagementConfigured();
  const { data } = useQuery({
    queryKey: keys.queueHealth,
    queryFn: () => api.getQueueHealth(),
    enabled: groupId !== null && canMonitor && rabbitMqConfigured,
  });
  if (groupId === null || !data) return null;
  return data.consumers
    .filter((c) => c.workGroupId === groupId)
    .reduce((n, c) => n + c.queueCount + c.retryCount + c.failedCount, 0);
}

/**
 * The messages that go with a group's current queues, which are deleted along with the group or
 * when its bus message name changes.
 */
export function QueuedMessagesWarning({ groupId }: { groupId: number }) {
  const queued = useQueuedMessages(groupId);
  if (queued === 0) return null;
  return (
    <p className={queued === null ? undefined : "font-medium text-danger-700"}>
      {queued === null
        ? "Any messages still in them are deleted too."
        : `${queued === 1 ? "1 message is" : `${queued} messages are`} still in them and will be deleted.`}{" "}
      The exchanges behind them stay in Bitween and can be retried from Exchanges.
    </p>
  );
}

/** Whether a new bus message name means new queues. Case alone doesn't: queue names are lowercase. */
export const renamesQueues = (from: string, to: string): boolean => from.toLowerCase() !== to.toLowerCase();

/** Asked before saving a new bus message name, which moves the group to new queues. */
export function BusRenameConfirm({
  groupId,
  busMessageName,
  onConfirm,
  onClose,
}: {
  groupId: number;
  busMessageName: string;
  onConfirm: () => Promise<void>;
  onClose: () => void;
}) {
  return (
    <ConfirmDialog
      title="Change the bus message name?"
      body={
        <div className="space-y-2">
          <p>
            This group moves to new queues named after{" "}
            <code className="font-mono text-xs text-ink-800">{busMessageName}</code>, and its current
            queues are deleted.
          </p>
          <QueuedMessagesWarning groupId={groupId} />
        </div>
      }
      confirmLabel="Change and save"
      onConfirm={onConfirm}
      onClose={onClose}
    />
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
  // A new group is filled in to create it, which workgroups.create allows on its own.
  const canCreate = useSessionCan("workgroups.create");
  const canChange = useSessionCan("workgroups.edit");
  const canEdit = groupId === null ? canCreate : canChange;
  const [draft, setDraft] = useState<WorkGroupDraft | null>(groupId === null ? EMPTY : null);
  const [confirmingRename, setConfirmingRename] = useState(false);

  const existing = useQuery({
    queryKey: keys.workGroups.detail(groupId),
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
      void queryClient.invalidateQueries({ queryKey: keys.workGroups.all });
      onSaved?.(id);
      onClose();
    },
  });

  const renaming =
    groupId !== null && !!existing.data && !!draft && renamesQueues(existing.data.busMessageName, draft.busMessageName);

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
              onClick={() => (renaming ? setConfirmingRename(true) : save.mutate())}
            >
              {groupId === null ? "Create work group" : "Save changes"}
            </Button>
          </div>
        </div>
      )}
      {confirmingRename && groupId !== null && draft && (
        <BusRenameConfirm
          groupId={groupId}
          busMessageName={draft.busMessageName}
          onConfirm={async () => {
            await save.mutateAsync();
          }}
          onClose={() => setConfirmingRename(false)}
        />
      )}
    </Dialog>
  );
}
