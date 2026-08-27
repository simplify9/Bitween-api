import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowUpRight, Trash2 } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import {
  WorkGroupFields,
  workGroupDraftOf,
  type WorkGroupDraft,
} from "../../components/config/WorkGroupDialog";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { SetupList } from "../../components/config/shared";
import { LiveQueueStats } from "./LiveQueueStats";
import { BackLink } from "../../components/ui/BackLink";

/**
 * This group's slice of the live RabbitMQ picture — the same numbers the
 * Queue health page shows, brought to where the group is configured.
 */
function LiveQueuePanel({ groupId }: { groupId: number }) {
  return (
    <Panel
      title="Live queue"
      description="This group's consumer, right now."
      action={
        <Link
          to="/queue-health"
          className="inline-flex items-center gap-1 text-[13px] font-medium text-crimson-700 hover:underline"
        >
          Queue health <ArrowUpRight className="size-3.5" aria-hidden />
        </Link>
      }
    >
      <LiveQueueStats groupId={groupId} />
    </Panel>
  );
}

export function WorkGroupPage() {
  const { id = "" } = useParams();
  const groupId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("workgroups.edit");

  const group = useQuery({
    queryKey: ["work-group", groupId],
    queryFn: () => api.getWorkGroup(groupId),
    retry: false,
  });

  const [draft, setDraft] = useState<WorkGroupDraft | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && group.data) {
      setDraft(workGroupDraftOf(group.data));
      setLoaded(true);
    }
  }, [group.data, loaded]);

  const dirty = useMemo(() => {
    if (!group.data || !draft) return false;
    return JSON.stringify(draft) !== JSON.stringify(workGroupDraftOf(group.data));
  }, [group.data, draft]);

  const save = useMutation({
    mutationFn: () => api.updateWorkGroup(groupId, draft!),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await queryClient.invalidateQueries({ queryKey: ["work-group", groupId] });
      void queryClient.invalidateQueries({ queryKey: ["work-groups"] });
      void queryClient.invalidateQueries({ queryKey: ["work-groups-search"] });
      setLoaded(false);
    },
  });

  if (group.isPending) return <LoadingBlock label="Loading work group…" />;
  if (group.isError)
    return (
      <EmptyState title="This work group no longer exists">
        <Link to="/work-groups" className="font-medium text-crimson-700 hover:underline">
          Back to work groups
        </Link>
      </EmptyState>
    );

  const g = group.data;

  return (
    <div className="pb-24">
      <BackLink to="/work-groups" label="Work groups" />

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle value={draft?.name ?? g.name} onChange={(name) => setDraft((d) => (d ? { ...d, name } : d))} disabled={!canEdit} placeholder="Work group name" />
          </h1>
        </div>
        <Can permission="workgroups.delete">
          <Button variant="danger" onClick={() => setDeleting(true)}>
            <Trash2 className="size-4" /> Delete
          </Button>
        </Can>
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          <Panel title="Queue settings" description="Groups get their own queue, priority and prefetch.">
            {draft && <WorkGroupFields draft={draft} onChange={setDraft} canEdit={canEdit} />}
          </Panel>
        </div>

        <div className="min-w-0 space-y-5">
          <Can permission="monitoring.view">
            <LiveQueuePanel groupId={groupId} />
          </Can>
          <Panel title="Used by" description="Subscriptions assigned to this work group.">
            <SetupList items={g.subscriptions} />
          </Panel>
        </div>
      </div>

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={save.error?.message}
          onSave={() => save.mutate()}
          onDiscard={() => setLoaded(false)}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete this work group?"
          body={
            <>
              <strong className="font-medium text-ink-800">{g.name}</strong> will be gone for good.
              Groups still assigned to subscriptions can't be deleted.
            </>
          }
          confirmLabel="Delete work group"
          onConfirm={async () => {
            await api.deleteWorkGroup(groupId);
            void queryClient.invalidateQueries({ queryKey: ["work-groups"] });
      void queryClient.invalidateQueries({ queryKey: ["work-groups-search"] });
            navigate("/work-groups");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
