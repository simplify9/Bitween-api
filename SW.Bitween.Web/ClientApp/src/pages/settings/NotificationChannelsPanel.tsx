import { useState } from "react";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { api, type NotificationChannelRow } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { useAdapterCatalog } from "../../components/config/AdapterConfig";
import { NotificationChannelDialog } from "../../components/config/NotificationChannelDialog";
import { describeChannelUse } from "../../lib/notificationChannels";
import { Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { MiniTable } from "../../components/ui/Table";
import { keys } from "../../api/queryKeys";

/**
 * The Settings section for notification channels. Channels are entities rather than settings, so
 * they save from their own dialog instead of staging into the page's draft — there is nothing to
 * preview across the app, and a half-made channel is no use to anything.
 */
export function NotificationChannelsPanel() {
  const queryClient = useQueryClient();
  const canCreate = useSessionCan("notifiers.create");
  const canEdit = useSessionCan("notifiers.edit");
  const canDelete = useSessionCan("notifiers.delete");
  const channels = useQuery({
    queryKey: keys.notificationChannels.list,
    queryFn: () => api.listNotificationChannels(),
  });
  const handlers = useAdapterCatalog("handler");
  // Legacy notifiers still fire, and this is now the only way to them.
  const legacy = useQuery({
    queryKey: keys.notifiers.search({ legacyCount: true }),
    queryFn: () => api.searchNotifiers({ search: "", offset: 0, limit: 1 }),
  });

  /** undefined = closed, null = creating, number = editing that channel. */
  const [dialog, setDialog] = useState<number | null | undefined>(undefined);
  const [deleting, setDeleting] = useState<NotificationChannelRow | null>(null);

  const remove = useMutation({
    mutationFn: (id: number) => api.deleteNotificationChannel(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.notificationChannels.all });
      setDeleting(null);
    },
  });

  const handlerLabel = (id: string) => handlers.data?.find((a) => a.id === id)?.label ?? id;
  const legacyCount = legacy.data?.total ?? 0;

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <p className="max-w-xl text-[13px] text-ink-500">
          Where notifications and retry alerts are sent. A subscription picks a channel for its own
          notifications, and a retry policy picks one for its budget-exhausted alerts.
        </p>
        {canCreate && (
          <Button size="sm" onClick={() => setDialog(null)}>
            <Plus className="size-3.5" aria-hidden />
            New channel
          </Button>
        )}
      </div>

      {channels.isPending ? (
        <LoadingBlock label="Loading channels…" />
      ) : (
        <MiniTable
          rows={channels.data ?? []}
          rowKey={(c) => c.id}
          empty="No channels yet. Create one, then pick it on a subscription or a retry policy."
          onRowClick={(c) => setDialog(c.id)}
          search={{ text: (c) => `${c.name} ${handlerLabel(c.handlerId)}`, noun: "channels" }}
          columns={[
            {
              header: "Name",
              wrap: true,
              cell: (c) => <span className="font-medium text-ink-800">{c.name}</span>,
            },
            {
              header: "Delivered by",
              headerTitle: "The handler adapter that sends it — built in, or a custom one deployed to this instance.",
              cell: (c) => <span className="text-[13px] text-ink-600">{handlerLabel(c.handlerId)}</span>,
            },
            {
              header: "Used by",
              headerTitle: "The subscriptions and retry policies that send through this channel.",
              cell: (c) =>
                c.usedBy.length === 0 ? (
                  <span className="text-[13px] text-ink-400">Not used</span>
                ) : (
                  <span
                    className="cursor-help text-[13px] text-ink-600"
                    title={c.usedBy.map(describeChannelUse).join("\n")}
                  >
                    {c.usedBy.length === 1 ? c.usedBy[0].name : `${c.usedBy.length} places`}
                  </span>
                ),
            },
            {
              header: "",
              align: "right",
              cell: (c) => (
                <span className="flex justify-end gap-1" onClick={(e) => e.stopPropagation()}>
                  {canEdit && (
                    <button
                      type="button"
                      onClick={() => setDialog(c.id)}
                      aria-label={`Edit ${c.name}`}
                      title="Edit"
                      className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                    >
                      <Pencil className="size-3.5" />
                    </button>
                  )}
                  {canDelete && (
                    <button
                      type="button"
                      onClick={() => setDeleting(c)}
                      disabled={c.usedBy.length > 0}
                      aria-label={`Delete ${c.name}`}
                      title={c.usedBy.length > 0 ? "Still in use — point its users at another channel first." : "Delete"}
                      className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-danger-700 disabled:cursor-not-allowed disabled:opacity-40"
                    >
                      <Trash2 className="size-3.5" />
                    </button>
                  )}
                </span>
              ),
            },
          ]}
        />
      )}

      {legacyCount > 0 && (
        <p className="text-[13px] text-ink-500">
          {legacyCount === 1 ? "1 legacy notifier still runs" : `${legacyCount} legacy notifiers still run`} alongside
          these.{" "}
          <Link to="/notifiers" className="font-medium text-crimson-700 hover:underline">
            View {legacyCount === 1 ? "it" : "them"}
          </Link>
        </p>
      )}

      {dialog !== undefined && <NotificationChannelDialog channelId={dialog} onClose={() => setDialog(undefined)} />}

      {deleting && (
        <ConfirmDialog
          title={`Delete ${deleting.name}?`}
          body={
            <>
              <p>Nothing uses it, so nothing stops sending.</p>
              <FormError>{remove.error?.message}</FormError>
            </>
          }
          confirmLabel="Delete channel"
          onConfirm={async () => {
            await remove.mutateAsync(deleting.id);
          }}
          onClose={() => setDeleting(null)}
        />
      )}
    </div>
  );
}
