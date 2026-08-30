import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { Button, FormError, LoadingBlock } from "../ui/basics";
import { Dialog } from "../ui/overlays";
import { keys } from "../../api/queryKeys";
import {
  PartnerFields,
  partnerChanges,
  partnerDirty,
  partnerDraftOf,
  type PartnerDraft,
} from "./PartnerFields";

/**
 * A partner, created or edited without leaving whatever you were doing.
 *
 * Reached from any partner picker. It renders the same `PartnerFields` the
 * partner's own page does, so there is one definition of what a partner is.
 *
 * Creating does not close it. A key cannot be issued to a partner that doesn't
 * exist yet, so instead of refusing — or sending you to the partner's page, which
 * is the jump this replaces — it saves, hands the id back to the caller so the
 * picker updates behind it, and reopens itself as an editor with API keys live.
 */
export function PartnerDialog({
  partnerId,
  onClose,
  onSaved,
}: {
  /** null opens it empty, to create. */
  partnerId: number | null;
  onClose: () => void;
  /** Fires as soon as a partner exists, so a picker can select it immediately. */
  onSaved?: (id: number) => void;
}) {
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("partners.edit");
  /** Set once the partner exists — from the prop, or from the create that just ran. */
  const [id, setId] = useState(partnerId);
  const [justCreated, setJustCreated] = useState(false);

  const existing = useQuery({
    queryKey: keys.partners.detail(id),
    queryFn: () => api.getPartner(id!),
    enabled: id !== null,
  });

  const [draft, setDraft] = useState<PartnerDraft | null>(
    partnerId === null ? { name: "", properties: [] } : null,
  );
  const [saved, setSaved] = useState<PartnerDraft | null>(
    partnerId === null ? { name: "", properties: [] } : null,
  );

  useEffect(() => {
    if (existing.data && draft === null) {
      const seeded = partnerDraftOf(existing.data);
      setDraft(seeded);
      setSaved(structuredClone(seeded));
    }
  }, [existing.data, draft]);

  const save = useMutation({
    mutationFn: async () => {
      const changes = partnerChanges(draft!);
      if (id !== null) {
        await api.updatePartner(id, changes);
        return id;
      }
      const created = await api.createPartner(changes);
      return created.id;
    },
    onSuccess: async (savedId) => {
      const creating = id === null;
      setId(savedId);
      onSaved?.(savedId);
      await queryClient.invalidateQueries({ queryKey: keys.partners.all });
      // Re-seed from the server rather than from the draft, so what the dialog
      // compares against is what was actually stored.
      setDraft(null);
      setSaved(null);
      if (creating) setJustCreated(true);
    },
  });

  const dirty = !!draft && !!saved && partnerDirty(draft, saved);
  const loading = id !== null && draft === null;
  // Escape and backdrop clicks belong to whichever dialog is on top; while a key
  // is being issued this one must not close underneath it.
  const closeUnlessBusy = () => {
    if (save.isPending) return;
    onClose();
  };

  return (
    <Dialog title={id === null ? "New partner" : "Partner"} onClose={closeUnlessBusy} wide>
      {loading || !draft ? (
        <LoadingBlock label="Loading the partner…" />
      ) : (
        <div className="space-y-4">
          {justCreated && (
            <p className="rounded-lg bg-ok-100 px-3 py-2 text-[13px] text-ok-600">
              Created and selected. Add its API keys below, or close — nothing else is waiting on you.
            </p>
          )}

          <PartnerFields
            draft={draft}
            onChange={setDraft}
            canEdit={canEdit}
            isSystem={existing.data?.isSystem ?? false}
            partnerId={id}
            credentials={existing.data?.apiCredentials ?? []}
            showName
          />

          <FormError>{save.error?.message}</FormError>
          <div className="flex items-center justify-end gap-2 border-t border-ink-100 pt-4">
            <Button onClick={onClose}>{id === null ? "Cancel" : "Close"}</Button>
            <Button
              variant="primary"
              busy={save.isPending}
              disabled={draft.name.trim().length < 2 || (id !== null && !dirty)}
              onClick={() => save.mutate()}
            >
              {id === null ? "Create partner" : "Save changes"}
            </Button>
          </div>
        </div>
      )}
    </Dialog>
  );
}
