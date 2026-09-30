import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { Button, FormError, LoadingBlock } from "../ui/basics";
import { Field, TextInput } from "../ui/forms";
import { Dialog } from "../ui/overlays";
import { AdapterConfig } from "./AdapterConfig";
import { keys } from "../../api/queryKeys";
import { describeChannelUse } from "../../lib/notificationChannels";

interface ChannelDraft {
  name: string;
  handlerId: string | null;
  handlerProperties: Record<string, string>;
}

const EMPTY: ChannelDraft = { name: "", handlerId: null, handlerProperties: {} };

/**
 * A notification channel, created or edited in place — from the Settings page, and from any
 * picker that chooses one, so choosing a channel never means leaving what you were doing.
 */
export function NotificationChannelDialog({
  channelId,
  onClose,
  onSaved,
}: {
  /** null opens it empty, to create. */
  channelId: number | null;
  onClose: () => void;
  onSaved?: (id: number) => void;
}) {
  const queryClient = useQueryClient();
  const canEdit = useSessionCan(channelId === null ? "notifiers.create" : "notifiers.edit");
  const [draft, setDraft] = useState<ChannelDraft | null>(channelId === null ? EMPTY : null);

  const existing = useQuery({
    queryKey: keys.notificationChannels.detail(channelId ?? "new"),
    queryFn: () => api.getNotificationChannel(channelId!),
    enabled: channelId !== null,
  });

  useEffect(() => {
    if (existing.data && draft === null)
      setDraft({
        name: existing.data.name,
        handlerId: existing.data.handlerId,
        handlerProperties: existing.data.handlerProperties ?? {},
      });
  }, [existing.data, draft]);

  const save = useMutation({
    mutationFn: async () => {
      const body = { name: draft!.name.trim(), handlerId: draft!.handlerId!, handlerProperties: draft!.handlerProperties };
      if (channelId !== null) {
        await api.updateNotificationChannel(channelId, body);
        return channelId;
      }
      return api.createNotificationChannel(body);
    },
    onSuccess: (id) => {
      void queryClient.invalidateQueries({ queryKey: keys.notificationChannels.all });
      onSaved?.(id);
      onClose();
    },
  });

  const missing = draft
    ? [!draft.name.trim() && "a name", !draft.handlerId && "a handler"].filter(
        (m): m is string => typeof m === "string",
      )
    : [];
  const usedBy = existing.data?.usedBy ?? [];

  return (
    <Dialog title={channelId === null ? "New notification channel" : "Notification channel"} onClose={onClose} wide>
      {!draft ? (
        <LoadingBlock label="Loading the channel…" />
      ) : (
        <div className="space-y-4">
          <p className="text-[13px] text-ink-500">
            Where a notification goes and everything needed to deliver it — the server, the login, the
            recipients. Every place that picks this channel sends exactly this; for other recipients, make
            another channel.
          </p>
          <div className="max-w-sm">
            <Field label="Name" htmlFor="channel-name">
              <TextInput
                id="channel-name"
                value={draft.name}
                disabled={!canEdit}
                placeholder="e.g. Ops email"
                onChange={(e) => setDraft({ ...draft, name: e.target.value })}
              />
            </Field>
          </div>
          <AdapterConfig
            kind="handler"
            adapterId={draft.handlerId}
            properties={draft.handlerProperties}
            disabled={!canEdit}
            required
            noneLabel="Pick how it is delivered"
            onChange={(handlerId, handlerProperties) => setDraft({ ...draft, handlerId, handlerProperties })}
          />
          {channelId !== null && existing.data && draft.handlerId !== existing.data.handlerId && (
            <p className="text-[13px] text-warn-700">
              A new handler doesn't inherit the old one's hidden values — type any password again.
            </p>
          )}
          {usedBy.length > 0 && (
            <div className="text-[13px] text-ink-500">
              <p className="font-medium text-ink-700">Used by</p>
              <ul className="mt-1 list-disc pl-5">
                {usedBy.map((u, i) => (
                  <li key={i}>{describeChannelUse(u)}</li>
                ))}
              </ul>
            </div>
          )}
          <FormError>{save.error?.message}</FormError>
          <div className="flex items-center justify-end gap-3">
            {missing.length > 0 && <p className="text-[13px] text-ink-500">Still needs {missing.join(" and ")}.</p>}
            <Button onClick={onClose}>Cancel</Button>
            {canEdit && (
              <Button
                variant="primary"
                busy={save.isPending}
                disabled={missing.length > 0}
                onClick={() => save.mutate()}
              >
                {channelId === null ? "Create channel" : "Save changes"}
              </Button>
            )}
          </div>
        </div>
      )}
    </Dialog>
  );
}
