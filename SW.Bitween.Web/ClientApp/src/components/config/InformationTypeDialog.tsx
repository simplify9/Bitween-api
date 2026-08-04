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
 * Same shape as `PartnerDialog`, and for the same reason: creating does not close
 * it. Promoted properties can only be attached to a type that exists, so instead
 * of asking for them in a create call that cannot carry them — or sending you to
 * the type's page — it saves, hands the id back so the picker updates behind it,
 * and reopens as an editor with that section live.
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
  const [id, setId] = useState(typeId);
  const [justCreated, setJustCreated] = useState(false);

  const existing = useQuery({
    queryKey: ["information-type", id],
    queryFn: () => api.getInformationType(id!),
    enabled: id !== null,
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
      if (id !== null) {
        await api.updateInformationType(id, {
          name: changes.name,
          code: changes.code,
          format: changes.format,
          busEnabled: changes.busEnabled,
          busMessageTypeName: changes.busMessageTypeName,
          duplicateIntervalMinutes: changes.duplicateIntervalMinutes,
          disregardsUnfilteredMessages: changes.disregardsUnfilteredMessages,
          promotedProperties: changes.promotedProperties,
        });
        return id;
      }
      // Create carries the definition but not promoted properties, which is why
      // the dialog stays open afterwards rather than asking for them here.
      const created = await api.createInformationType({
        name: changes.name,
        code: changes.code ?? "",
        format: changes.format,
        busEnabled: changes.busEnabled,
        ...(changes.busMessageTypeName ? { busMessageTypeName: changes.busMessageTypeName } : {}),
      });
      return created.id;
    },
    onSuccess: async (savedId) => {
      const creating = id === null;
      setId(savedId);
      void queryClient.invalidateQueries({ queryKey: ["information-types"] });
      await queryClient.invalidateQueries({ queryKey: ["information-type", savedId] });
      onSaved?.({ id: savedId, busMessageTypeName: informationTypeChanges(draft!).busMessageTypeName ?? "" });
      setDraft(null);
      setSaved(null);
      if (creating) setJustCreated(true);
    },
  });

  const dirty = !!draft && !!saved && informationTypeDirty(draft, saved);
  const missing = draft ? informationTypeMissing(draft) : [];

  return (
    <Dialog title={id === null ? "New information type" : "Information type"} onClose={onClose} wide>
      {!draft ? (
        <LoadingBlock label="Loading the information type…" />
      ) : (
        <div className="space-y-4">
          {justCreated && (
            <p className="rounded-lg bg-ok-100 px-3 py-2 text-[13px] text-ok-600">
              Created and selected. Promote the properties you want to filter on below, or close.
            </p>
          )}

          <InformationTypeFields
            draft={draft}
            onChange={setDraft}
            canEdit={canEdit}
            typeId={id}
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
            <Button onClick={onClose}>{id === null ? "Cancel" : "Close"}</Button>
            <Button
              variant="primary"
              busy={save.isPending}
              disabled={missing.length > 0 || (id !== null && !dirty)}
              onClick={() => save.mutate()}
            >
              {id === null ? "Create information type" : "Save changes"}
            </Button>
          </div>
        </div>
      )}
    </Dialog>
  );
}
