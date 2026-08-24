import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { Button, FormError, LoadingBlock } from "../ui/basics";
import { Dialog } from "../ui/overlays";
import {
  EMPTY_INFORMATION_TYPE,
  InformationTypeFields,
  informationTypeChanges,
  informationTypeDirty,
  informationTypeDraftOf,
  informationTypeMissing,
  type InformationTypeDraft,
} from "./InformationTypeFields";

/**
 * An information type, created or edited without leaving whatever you were doing.
 *
 * Creating is one save: `POST /documents` carries the whole definition, promoted
 * properties included, so the dialog asks for everything once and closes. (It used
 * to stay open as an editor, because create could not carry them — unlike
 * `PartnerDialog`, which still must, since a key can only be issued to a partner
 * that already exists.)
 */
export function InformationTypeDialog({
  typeId,
  busRequired = false,
  onClose,
  onSaved,
}: {
  /** null opens it empty, to create. */
  typeId: number | null;
  /** Force "available on the message bus" — the caller reaches this type over the bus. */
  busRequired?: boolean;
  onClose: () => void;
  /** Fires as soon as the type exists, so a picker can select it immediately. */
  onSaved?: (type: { id: number; busMessageTypeName: string }) => void;
}) {
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("documents.edit");

  const existing = useQuery({
    queryKey: ["information-type", typeId],
    queryFn: () => api.getInformationType(typeId!),
    enabled: typeId !== null,
  });

  const seed = { ...EMPTY_INFORMATION_TYPE, busEnabled: busRequired };
  const [draft, setDraft] = useState<InformationTypeDraft | null>(typeId === null ? seed : null);
  const [saved, setSaved] = useState<InformationTypeDraft | null>(typeId === null ? seed : null);

  useEffect(() => {
    if (existing.data && draft === null) {
      const next = informationTypeDraftOf(existing.data);
      setDraft(next);
      setSaved(structuredClone(next));
    }
  }, [existing.data, draft]);

  const save = useMutation({
    mutationFn: async () => {
      const changes = informationTypeChanges(draft!);
      const body = {
        name: changes.name,
        code: changes.code,
        format: changes.format,
        busEnabled: changes.busEnabled,
        busMessageTypeName: changes.busMessageTypeName,
        duplicateIntervalMinutes: changes.duplicateIntervalMinutes,
        disregardsUnfilteredMessages: changes.disregardsUnfilteredMessages,
        promotedProperties: changes.promotedProperties,
      };
      if (typeId !== null) {
        await api.updateInformationType(typeId, body);
        return typeId;
      }
      return (await api.createInformationType(body)).id;
    },
    onSuccess: async (savedId) => {
      void queryClient.invalidateQueries({ queryKey: ["information-types"] });
      await queryClient.invalidateQueries({ queryKey: ["information-type", savedId] });
      onSaved?.({ id: savedId, busMessageTypeName: informationTypeChanges(draft!).busMessageTypeName ?? "" });
      if (typeId === null) {
        onClose();
        return;
      }
      // Re-seed from the server, so what the dialog compares against is what was stored.
      setDraft(null);
      setSaved(null);
    },
  });

  const dirty = !!draft && !!saved && informationTypeDirty(draft, saved);
  const missing = draft ? informationTypeMissing(draft) : [];

  return (
    <Dialog title={typeId === null ? "New information type" : "Information type"} onClose={onClose} wide>
      {!draft ? (
        <LoadingBlock label="Loading the information type…" />
      ) : (
        <div className="space-y-4">
          <InformationTypeFields
            draft={draft}
            onChange={setDraft}
            canEdit={canEdit}
            busRequired={busRequired}
            idPrefix="itd"
          />

          <FormError>{save.error?.message}</FormError>
          <div className="flex items-center justify-end gap-3 border-t border-ink-100 pt-4">
            {missing.length > 0 && (
              <p className="text-[13px] text-ink-500">
                Still needs {missing.slice(0, -1).join(", ")}
                {missing.length > 1 ? " and " : ""}
                {missing.at(-1)}.
              </p>
            )}
            <Button onClick={onClose}>{typeId === null ? "Cancel" : "Close"}</Button>
            <Button
              variant="primary"
              busy={save.isPending}
              disabled={missing.length > 0 || (typeId !== null && !dirty)}
              onClick={() => save.mutate()}
            >
              {typeId === null ? "Create information type" : "Save changes"}
            </Button>
          </div>
        </div>
      )}
    </Dialog>
  );
}
