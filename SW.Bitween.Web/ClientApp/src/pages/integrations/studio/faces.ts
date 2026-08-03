import type { AdapterInfo, IntegrationType } from "../../../api";
import { schedulesSummary } from "../../../lib/schedules";
import { formatDateTime } from "../../../lib/dates";
import type { StageFace } from "./StageRail";
import type { StageId } from "./stages";
import { locationHint, stageDirty, type Draft, type EntryPoint } from "./model";

const labelOf = (catalog: { data: AdapterInfo[] | undefined }, id: string | null) =>
  catalog.data?.find((a) => a.id === id)?.label ?? id;

/**
 * Whether a chosen adapter still has a required property empty.
 *
 * Both create pages gate on this, and it is not cosmetic: creating is two calls
 * — a bare subscription, then the pipeline — so a rejected pipeline leaves an
 * empty subscription behind. Catching what the backend would reject keeps the
 * common path from orphaning one. See backend gap 27.
 */
export const adapterIncomplete = (
  catalog: { data: AdapterInfo[] | undefined },
  adapterId: string | null,
  properties: Record<string, string>,
): boolean => {
  const adapter = catalog.data?.find((a) => a.id === adapterId);
  return !!adapter && adapter.props.some((p) => !p.optional && !properties[p.key]);
};

export interface AdapterCatalogs {
  receivers: { data: AdapterInfo[] | undefined };
  validators: { data: AdapterInfo[] | undefined };
  mappers: { data: AdapterInfo[] | undefined };
  handlers: { data: AdapterInfo[] | undefined };
}

export interface FaceInput {
  type: IntegrationType;
  draft: Draft;
  catalogs: AdapterCatalogs;
  /** Absent while creating — nothing is saved yet, so no node is "unsaved". */
  saved?: Draft;
  entryPoints?: EntryPoint[];
  /** Bitween's own next-fire computation, for the Schedule node's detail line. */
  nextRunOn?: string | null;
  /** Only ever set on the Schedule node — see `StageFace.fault`. */
  fault?: StageFace["fault"];
  /** Names for the "feed the response into" target. */
  integrationNames?: { id: number; name: string }[];
}

/**
 * What each node says about itself.
 *
 * Shared by the studio page and the create page on purpose: the two show the
 * same pipeline, and a node that reads one way while building and another way
 * while editing is how the two drift apart.
 */
export function faceOf(stageId: StageId, input: FaceInput): StageFace {
  const { type, draft: d, catalogs, saved, entryPoints = [], nextRunOn, fault, integrationNames } = input;
  const dirty = saved ? stageDirty(stageId, d, saved) : false;

  switch (stageId) {
    case "trigger":
      if (type === "Internal")
        return {
          id: stageId,
          dirty,
          title: d.matchExpression ? "Message filter" : "Every document",
          state: d.matchExpression ? "set" : "none",
        };
      if (type === "ApiCall") return { id: stageId, dirty, title: "Called by id", state: "none" };
      return {
        id: stageId,
        dirty,
        title: entryPoints.length
          ? `${entryPoints.length} entry point${entryPoints.length === 1 ? "" : "s"}`
          : "Not wired up",
        detail: entryPoints[0]?.name,
        state: entryPoints.length ? "set" : "missing",
      };
    case "source":
      return {
        id: stageId,
        dirty,
        title: labelOf(catalogs.receivers, d.receiverId) ?? "Not configured",
        detail: locationHint(d.receiverProperties),
        state: d.receiverId ? "set" : "missing",
      };
    case "schedule":
      return {
        id: stageId,
        dirty,
        title: d.schedules.length ? schedulesSummary(d.schedules) : "No schedule",
        detail: nextRunOn ? `next ${formatDateTime(nextRunOn)}` : undefined,
        state: d.schedules.length ? "set" : "missing",
        fault,
      };
    case "aggregation":
      return { id: stageId, dirty, title: "Not editable yet", state: "none", fault };
    case "validation":
      return {
        id: stageId,
        dirty,
        title: labelOf(catalogs.validators, d.validatorId) ?? "Accepts everything",
        detail: locationHint(d.validatorProperties),
        state: d.validatorId ? "set" : "none",
      };
    case "transformation":
      return {
        id: stageId,
        dirty,
        title: labelOf(catalogs.mappers, d.mapperId) ?? "Passes through",
        detail: locationHint(d.mapperProperties),
        state: d.mapperId ? "set" : "none",
      };
    case "delivery":
      // "none", not "missing": an integration that records the document and
      // stops is a legal configuration. The create page requires a handler
      // anyway, but it says so at the Create button rather than by calling a
      // saved integration broken.
      return {
        id: stageId,
        dirty,
        title: labelOf(catalogs.handlers, d.handlerId) ?? "Stops here",
        detail: locationHint(d.handlerProperties),
        state: d.handlerId ? "set" : "none",
      };
    case "response":
      if (!d.handlerId) return { id: stageId, dirty, title: "Nothing delivered", state: "none" };
      if (d.responseIntegrationId !== null)
        return {
          id: stageId,
          dirty,
          title:
            integrationNames?.find((x) => x.id === d.responseIntegrationId)?.name ?? "Fed onward",
          detail: "chained to another integration",
          state: "set",
        };
      if (d.responseMessageTypeName)
        return {
          id: stageId,
          dirty,
          title: d.responseMessageTypeName,
          detail: "published on the bus",
          state: "set",
        };
      return { id: stageId, dirty, title: "Recorded only", state: "none" };
  }
}
