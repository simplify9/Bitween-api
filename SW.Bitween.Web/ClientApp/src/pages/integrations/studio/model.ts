import type { Integration, IntegrationDetail } from "../../../api";
import type { StageId } from "./stages";

/** The editable slice of an integration. One draft covers the whole rail. */
export type Draft = Pick<
  Integration,
  | "name"
  | "enabled"
  | "workGroupId"
  | "retryPolicyId"
  | "receiverId"
  | "receiverProperties"
  | "validatorId"
  | "validatorProperties"
  | "mapperId"
  | "mapperProperties"
  | "handlerId"
  | "handlerProperties"
  | "matchExpression"
  | "schedules"
  | "responseIntegrationId"
  | "responseMessageTypeName"
>;

export const draftOf = (d: IntegrationDetail): Draft => ({
  name: d.name,
  enabled: d.enabled,
  workGroupId: d.workGroupId,
  retryPolicyId: d.retryPolicyId,
  receiverId: d.receiverId,
  receiverProperties: structuredClone(d.receiverProperties),
  validatorId: d.validatorId,
  validatorProperties: structuredClone(d.validatorProperties),
  mapperId: d.mapperId,
  mapperProperties: structuredClone(d.mapperProperties),
  handlerId: d.handlerId,
  handlerProperties: structuredClone(d.handlerProperties),
  matchExpression: structuredClone(d.matchExpression),
  schedules: structuredClone(d.schedules),
  responseIntegrationId: d.responseIntegrationId,
  responseMessageTypeName: d.responseMessageTypeName,
});

/**
 * An integration being defined on a gateway's canvas, before it exists.
 *
 * The route already worked this way — see `NEW_ROUTE` — and an integration is the
 * same problem one level down: asking for it in a modal hides the diagram the
 * answers are about. It lives in the studio's state until one save writes it and
 * the thing pointing at it together.
 */
export const EMPTY_INTEGRATION: Draft = {
  name: "",
  enabled: true,
  workGroupId: null,
  retryPolicyId: null,
  receiverId: null,
  receiverProperties: {},
  validatorId: null,
  validatorProperties: {},
  mapperId: null,
  mapperProperties: {},
  handlerId: null,
  handlerProperties: {},
  matchExpression: null,
  schedules: [],
  responseIntegrationId: null,
  responseMessageTypeName: null,
};

/**
 * Stands in for "the integration being defined right here" wherever an id is
 * expected. Negative so it can never collide with a real one, and never sent to
 * the server: the save swaps it for the inline payload the gateway endpoints take.
 */
export const NEW_INTEGRATION_ID = -1;

/**
 * Which draft fields each stage owns — only so a card can carry an unsaved dot.
 * Name, enabled, work group and retry policy belong to no stage; they live on
 * the header and the overview, and the save bar covers them.
 */
const STAGE_FIELDS: Record<StageId, (keyof Draft)[]> = {
  trigger: ["matchExpression"],
  source: ["receiverId", "receiverProperties"],
  schedule: ["schedules"],
  aggregation: [],
  validation: ["validatorId", "validatorProperties"],
  transformation: ["mapperId", "mapperProperties"],
  delivery: ["handlerId", "handlerProperties"],
  response: ["responseIntegrationId", "responseMessageTypeName"],
};

/** Whether this stage is what's making the save bar show. */
export const stageDirty = (stage: StageId, draft: Draft, saved: Draft): boolean =>
  (STAGE_FIELDS[stage] ?? []).some((f) => JSON.stringify(draft[f]) !== JSON.stringify(saved[f]));

/**
 * The one adapter property worth putting on a pipeline node: *where* the step
 * points. "NativeS3Receiver" tells an operator nothing on its own — the bucket
 * does. Adapters name these keys differently, so this walks a priority list
 * rather than trying to know every adapter.
 *
 * Secrets are excluded by construction: nothing matching key/secret/password/
 * token can be reached, whatever an adapter calls it.
 */
const LOCATION_KEYS = [
  "Url",
  "ServiceUrl",
  "Endpoint",
  "Host",
  "Server",
  "BucketName",
  "Bucket",
  "FolderName",
  "Folder",
  "Directory",
  "Path",
  "Queue",
  "Topic",
  "MessageType",
];

const SECRETISH = /key|secret|password|token|credential/i;

export const locationHint = (properties: Record<string, string>): string | undefined => {
  for (const key of LOCATION_KEYS) {
    if (SECRETISH.test(key)) continue;
    const value = properties[key];
    if (value) return value;
  }
  return undefined;
};

export interface EntryPoint {
  key: string;
  name: string;
  href: string;
  kind: string;
  partnerId: number | null;
  partnerName: string | null;
  detail: string;
}

/**
 * Both kinds of entry point in one list — what matters is "who can feed this",
 * not which of the two mechanisms does it.
 */
export const entryPointsOf = (s: IntegrationDetail): EntryPoint[] => [
  ...s.apiGatewayAttachments.map((a) => ({
    key: `ag-${a.gatewayId}-${a.partnerId}`,
    name: a.gatewayName,
    href: `/api-gateways/${a.gatewayId}`,
    kind: "API gateway",
    partnerId: a.partnerId as number | null,
    partnerName: a.partnerName as string | null,
    detail: `/${a.urlName}`,
  })),
  ...s.busGatewayRoutes.map((r, i) => ({
    key: `bg-${r.gatewayId}-${i}`,
    name: r.gatewayName,
    href: `/bus-gateways/${r.gatewayId}`,
    kind: "Bus route",
    partnerId: r.partnerId,
    partnerName: r.partnerName,
    detail: "—",
  })),
];
