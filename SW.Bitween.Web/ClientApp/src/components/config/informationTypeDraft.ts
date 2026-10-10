import { useQuery } from "@tanstack/react-query";
import { api, type InformationType, type InformationTypeDetail } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { useRabbitMqManagementConfigured } from "../../lib/appConfig";
import { busMessageNameProblem } from "../../lib/busMessageName";
import { readsContent } from "../../lib/informationTypeFormat";
import type { InformationTypeDraft } from "./InformationTypeFields";

export const hasPromotedRows = (draft: InformationTypeDraft) =>
  draft.promotedProperties.some((r) => r.key.trim() || r.value.trim());


export const informationTypeDraftOf = (t: InformationType): InformationTypeDraft => ({
  name: t.name,
  code: t.code ?? "",
  format: t.format,
  busEnabled: t.busEnabled,
  busMessageTypeName: t.busMessageTypeName ?? "",
  duplicateIntervalMinutes: t.duplicateIntervalMinutes,
  disregardsUnfilteredMessages: t.disregardsUnfilteredMessages,
  promotedProperties: t.promotedProperties.map((p) => ({ key: p.key, value: p.path })),
  validationSchema: t.validationSchema ?? "",
});

export const EMPTY_INFORMATION_TYPE: InformationTypeDraft = {
  name: "",
  code: "",
  format: "Json",
  busEnabled: false,
  busMessageTypeName: "",
  duplicateIntervalMinutes: 0,
  disregardsUnfilteredMessages: false,
  promotedProperties: [],
  validationSchema: "",
};

export const informationTypeDirty = (
  draft: InformationTypeDraft,
  saved: InformationTypeDraft,
): boolean => JSON.stringify(draft) !== JSON.stringify(saved);

/** What the host sends to `updateInformationType`. */
export const informationTypeChanges = (draft: InformationTypeDraft) => ({
  name: draft.name.trim(),
  code: draft.code.trim() || undefined,
  format: draft.format,
  busEnabled: draft.busEnabled,
  // Unticking the bus puts the stored name back (see the checkbox), so with the bus off this is
  // the name a paused type resumes on, or nothing for a type that was never on it.
  busMessageTypeName: draft.busMessageTypeName.trim() || undefined,
  duplicateIntervalMinutes: draft.duplicateIntervalMinutes,
  disregardsUnfilteredMessages: draft.disregardsUnfilteredMessages,
  promotedProperties: draft.promotedProperties
    .filter((r) => r.key.trim() || r.value.trim())
    .map((r) => ({ key: r.key, path: r.value })),
  validationSchema: draft.validationSchema.trim() ? draft.validationSchema : null,
});

/** Why a draft can't be saved yet, in the operator's words. */
export function informationTypeMissing(draft: InformationTypeDraft): string[] {
  return [
    draft.name.trim().length < 2 && "a name",
    draft.busEnabled && !draft.busMessageTypeName.trim() && "a bus message name",
    // Same rule the field shows under itself, so the gate and the message cannot disagree.
    draft.busEnabled &&
      busMessageNameProblem(draft.busMessageTypeName.trim()) !== null &&
      "a bus message name without spaces",
    // The server refuses these, so say it before the save rather than after.
    !readsContent(draft.format) && hasPromotedRows(draft) && "its promoted properties removed",
    !readsContent(draft.format) && draft.validationSchema.trim() !== "" && "its schema removed",
  ].filter((m): m is string => typeof m === "string");
}

/**
 * Messages in a type's bus queue — retries and dead letters included — from the same live
 * snapshot Queue health polls. A paused type has no consumer, so its queue is found among the
 * ones nothing reads. `null` when it can't be known: no right to see queue health, RabbitMQ
 * management not configured, or not loaded yet.
 */
export function useInformationTypeMessages(typeId: number | null): number | null {
  const canMonitor = useSessionCan("monitoring.view");
  const rabbitMqConfigured = useRabbitMqManagementConfigured();
  const { data } = useQuery({
    queryKey: keys.queueHealth,
    queryFn: () => api.getQueueHealth(),
    enabled: typeId !== null && canMonitor && rabbitMqConfigured,
  });
  if (typeId === null || !data) return null;
  const live = data.consumers.find((c) => c.informationTypeId === typeId);
  if (live) return live.queueCount + live.retryCount + live.failedCount;
  const paused = data.unattended.find((q) => q.informationTypeId === typeId);
  return paused ? paused.messages + paused.retryMessages + paused.deadMessages : 0;
}

/**
 * Whether saving moves the type to a new bus queue: a stored name changed to another one. Case
 * alone doesn't count, since queue names are lowercase, and turning the bus off isn't a rename.
 */
export const renamesBusQueue = (stored: string | undefined, draft: InformationTypeDraft): boolean =>
  !!stored?.trim() &&
  draft.busEnabled &&
  !!draft.busMessageTypeName.trim() &&
  stored.trim().toLowerCase() !== draft.busMessageTypeName.trim().toLowerCase();

/** The page passes its detail straight through; the dialog only ever has the base type. */
export const draftOfDetail = (t: InformationTypeDetail): InformationTypeDraft =>
  informationTypeDraftOf(t);
