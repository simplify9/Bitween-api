import type { ApiClient } from "../client";
import {
  ApiRequestError,
  type Integration,
  type IntegrationDetail,
  type IntegrationInfo,
  type IntegrationRow,
  type IntegrationType,
  type MatchGroup,
  type MatchNode,
  type Schedule,
} from "../types";
import { schedulesSummary } from "../../lib/schedules";
import { documentMethods } from "./documents";
import { partnerMethods } from "./partners";
import { get, post, request } from "./request";

// ——— backend shapes (camelCase over the wire) ———
interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawKeyAndValue {
  key: string;
  value: string;
}
interface RawSchedule {
  recurrence: Schedule["recurrence"];
  days: number;
  hours: number;
  minutes: number;
  backwards: boolean;
}
// The backend's match tree is strictly binary (and/or each take exactly two
// operands) and uses snake_case type discriminators, unlike the frontend's
// n-ary MatchGroup. See toMatchGroup/toRawMatchExpression below.
type RawMatchSpec =
  | { type: "one_of" | "not_one_of"; path: string; values: string[] | null }
  | { type: "and" | "or"; left: RawMatchSpec; right: RawMatchSpec };

interface RawSubscription {
  id?: number;
  name: string;
  documentId: number;
  partnerId: number | null;
  aggregationForId: number | null;
  type: string;
  handlerId: string | null;
  mapperId: string | null;
  receiverId: string | null;
  validatorId: string | null;
  inactive: boolean;
  temporary: boolean;
  categoryId: number | null;
  handlerProperties: RawKeyAndValue[] | null;
  mapperProperties: RawKeyAndValue[] | null;
  receiverProperties: RawKeyAndValue[] | null;
  validatorProperties: RawKeyAndValue[] | null;
  documentFilter: RawKeyAndValue[] | null;
  matchExpression: RawMatchSpec | null;
  workGroupId: number | null;
  retryPolicyId: number | null;
  customRetryPolicy: unknown | null;
  schedules: RawSchedule[] | null;
  responseSubscriptionId: number | null;
  responseMessageTypeName: string | null;
  receiveOn: string | null;
  aggregateOn: string | null;
  pausedOn: string | null;
  isRunning: boolean | null;
  consecutiveFailures: number;
  lastException: string | null;
  aggregationTarget?: string;
}

const SUB_TYPE_BY_NUM: Record<number, IntegrationType> = {
  1: "Internal",
  2: "ApiCall",
  4: "Receiving",
  8: "Aggregation",
  16: "GatewayApiCall",
  32: "BusGateway",
};
const INTEGRATION_TYPES: IntegrationType[] = [
  "Receiving",
  "GatewayApiCall",
  "BusGateway",
  "Internal",
  "ApiCall",
  "Aggregation",
];
/** Enums serialize as their C# member name string, but guard the numeric case too. */
const toIntegrationType = (t: number | string): IntegrationType =>
  typeof t === "number"
    ? (SUB_TYPE_BY_NUM[t] ?? "Internal")
    : (INTEGRATION_TYPES.find((k) => k.toLowerCase() === t.toLowerCase()) ?? "Internal");

const toRecord = (kvs: RawKeyAndValue[] | null): Record<string, string> =>
  Object.fromEntries((kvs ?? []).map((kv) => [kv.key, kv.value]));
const toKvArray = (record: Record<string, string>): RawKeyAndValue[] =>
  Object.entries(record).map(([key, value]) => ({ key, value }));

const toSchedules = (raw: RawSchedule[] | null): Schedule[] =>
  (raw ?? []).map((s) => ({
    recurrence: s.recurrence,
    days: s.days,
    hours: s.hours,
    minutes: s.minutes,
    backwards: s.backwards,
  }));
const toRawSchedules = (schedules: Schedule[]): RawSchedule[] =>
  schedules.map((s) => ({
    recurrence: s.recurrence,
    days: s.days,
    hours: s.hours,
    minutes: s.minutes,
    backwards: s.backwards,
  }));

/**
 * Fold the backend's binary and/or tree into the frontend's n-ary MatchGroup,
 * flattening runs of the same operator so a flat group round-trips back flat
 * instead of as a deeply right-nested tree.
 */
function toMatchNode(spec: RawMatchSpec): MatchNode {
  if (!("left" in spec)) {
    return { op: spec.type === "one_of" ? "oneOf" : "notOneOf", path: spec.path, values: spec.values ?? [] };
  }
  const op = spec.type;
  const children: MatchNode[] = [];
  const collect = (s: RawMatchSpec) => {
    if (s.type === op) {
      collect(s.left);
      collect(s.right);
    } else {
      children.push(toMatchNode(s));
    }
  };
  collect(spec);
  return { op, children };
}

const toMatchGroup = (spec: RawMatchSpec | null): MatchGroup | null => {
  if (!spec) return null;
  const node = toMatchNode(spec);
  // The backend can't represent a single-condition group (and/or always need
  // two operands), so a lone condition arrives unwrapped and must be rewrapped
  // here to satisfy the "root is always a group" contract.
  return "children" in node ? node : { op: "and", children: [node] };
};

/** Unfold an n-ary MatchGroup into the backend's binary tree, right-associatively. */
function toBackendNode(node: MatchNode): RawMatchSpec | null {
  if ("path" in node) {
    return { type: node.op === "oneOf" ? "one_of" : "not_one_of", path: node.path, values: node.values };
  }
  const parts = node.children.map(toBackendNode).filter((s): s is RawMatchSpec => s !== null);
  if (parts.length === 0) return null; // empty group — matches everything, i.e. no constraint
  if (parts.length === 1) return parts[0];
  return parts.reduceRight((right, left) => ({ type: node.op, left, right }));
}

const toRawMatchExpression = (group: MatchGroup | null): RawMatchSpec | null => (group ? toBackendNode(group) : null);

function toIntegration(raw: RawSubscription, idOverride?: number): Integration {
  return {
    id: raw.id ?? idOverride!,
    name: raw.name,
    type: toIntegrationType(raw.type),
    informationTypeId: raw.documentId,
    partnerId: raw.partnerId,
    enabled: !raw.inactive,
    pausedOn: raw.pausedOn ?? null,
    workGroupId: raw.workGroupId ?? null,
    retryPolicyId: raw.retryPolicyId ?? null,
    receiverId: raw.receiverId ?? null,
    receiverProperties: toRecord(raw.receiverProperties),
    validatorId: raw.validatorId ?? null,
    validatorProperties: toRecord(raw.validatorProperties),
    mapperId: raw.mapperId ?? null,
    mapperProperties: toRecord(raw.mapperProperties),
    handlerId: raw.handlerId ?? null,
    handlerProperties: toRecord(raw.handlerProperties),
    matchExpression: toMatchGroup(raw.matchExpression),
    schedules: toSchedules(raw.schedules),
    responseIntegrationId: raw.responseSubscriptionId ?? null,
    responseMessageTypeName: raw.responseMessageTypeName ?? null,
    aggregationForId: raw.aggregationForId ?? null,
    isRunning: raw.isRunning ?? false,
    lastReceiveOn: raw.receiveOn ?? null,
    consecutiveFailures: raw.consecutiveFailures ?? 0,
    lastException: raw.lastException ?? null,
    // Subscription has no CreatedOn column on the backend.
    createdOn: "",
  };
}

async function fetchRaw(id: number): Promise<RawSubscription> {
  const raw = await get<RawSubscription | null>(`/subscriptions/${id}`);
  if (!raw) throw new ApiRequestError("NOT_FOUND", "This integration no longer exists.");
  return raw;
}

async function fetchAllRaw(): Promise<RawSubscription[]> {
  const res = await get<SearchyResponse<RawSubscription>>("/subscriptions");
  return res.result ?? [];
}

type UpdatableFields = Partial<
  Pick<
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
  >
>;

/**
 * POST /subscriptions/{id} replaces the whole record, and Update.cs's model
 * (SubscriptionUpdate) carries several fields this UI never shows (categoryId,
 * aggregationTarget, temporary, …) — omitting them would silently reset them
 * to their type default. So every write reads the current full record first
 * and splices `changes` on top of it, mirroring writePartner()'s pattern.
 *
 * Secret adapter property values arrive masked as the literal string
 * "__private__" (Get.cs's PrivateSentinel); passed straight through unedited,
 * Update.cs's own merge logic restores the real stored value — this file
 * never needs to know about the sentinel itself.
 */
async function applyChanges(id: number, current: RawSubscription, changes: UpdatableFields): Promise<void> {
  await post(`/subscriptions/${id}`, {
    name: changes.name ?? current.name,
    documentId: current.documentId,
    partnerId: current.partnerId,
    aggregationForId: current.aggregationForId,
    categoryId: current.categoryId,
    inactive: changes.enabled !== undefined ? !changes.enabled : current.inactive,
    workGroupId: changes.workGroupId !== undefined ? changes.workGroupId : current.workGroupId,
    // This UI only ever assigns a named policy, never an inline one.
    retryPolicyId: changes.retryPolicyId !== undefined ? changes.retryPolicyId : current.retryPolicyId,
    customRetryPolicy: null,
    receiverId: changes.receiverId !== undefined ? changes.receiverId : current.receiverId,
    receiverProperties: toKvArray(changes.receiverProperties ?? toRecord(current.receiverProperties)),
    validatorId: changes.validatorId !== undefined ? changes.validatorId : current.validatorId,
    validatorProperties: toKvArray(changes.validatorProperties ?? toRecord(current.validatorProperties)),
    mapperId: changes.mapperId !== undefined ? changes.mapperId : current.mapperId,
    mapperProperties: toKvArray(changes.mapperProperties ?? toRecord(current.mapperProperties)),
    handlerId: changes.handlerId !== undefined ? changes.handlerId : current.handlerId,
    handlerProperties: toKvArray(changes.handlerProperties ?? toRecord(current.handlerProperties)),
    documentFilter: current.documentFilter ?? [],
    matchExpression:
      changes.matchExpression !== undefined ? toRawMatchExpression(changes.matchExpression) : current.matchExpression,
    schedules: changes.schedules !== undefined ? toRawSchedules(changes.schedules) : (current.schedules ?? []),
    responseSubscriptionId:
      changes.responseIntegrationId !== undefined ? changes.responseIntegrationId : current.responseSubscriptionId,
    responseMessageTypeName:
      changes.responseMessageTypeName !== undefined ? changes.responseMessageTypeName : current.responseMessageTypeName,
    temporary: current.temporary,
    aggregationTarget: current.aggregationTarget,
    pausedOn: current.pausedOn,
    receiveOn: current.receiveOn,
    aggregateOn: current.aggregateOn,
    consecutiveFailures: current.consecutiveFailures,
    lastException: current.lastException,
  });
}

export const integrationMethods = {
  async listIntegrations(): Promise<IntegrationInfo[]> {
    const rows = await fetchAllRaw();
    return rows.map((raw) => ({
      id: raw.id!,
      name: raw.name,
      type: toIntegrationType(raw.type),
      partnerIds: raw.partnerId !== null ? [raw.partnerId] : [],
      informationTypeId: raw.documentId,
      workGroupId: raw.workGroupId ?? null,
      retryPolicyId: raw.retryPolicyId ?? null,
      // Requires scanning adapter properties for {{partner.KEY}}/{{globals…}}
      // reference tokens — no backend endpoint for this; deferred.
      partnerPropKeys: [],
      globals: [],
    }));
  },

  async listIntegrationRows(): Promise<IntegrationRow[]> {
    const [rows, infoTypes, partners] = await Promise.all([
      fetchAllRaw(),
      documentMethods.listInformationTypes(),
      partnerMethods.listPartners(),
    ]);
    const infoTypeById = new Map(infoTypes.map((t) => [t.id, t]));
    const partnerById = new Map(partners.map((p) => [p.id, p]));

    return rows.map((raw) => {
      const type = toIntegrationType(raw.type);
      const infoType = infoTypeById.get(raw.documentId);
      const partner = raw.partnerId !== null ? partnerById.get(raw.partnerId) : undefined;
      const schedules = toSchedules(raw.schedules);
      return {
        id: raw.id!,
        name: raw.name,
        type,
        informationTypeId: raw.documentId,
        informationTypeCode: infoType?.code ?? infoType?.name ?? "",
        // Gateway-derived partners (GatewayApiCall/BusGateway) land in Batch 3.
        partners: partner ? [{ id: partner.id, name: partner.name }] : [],
        enabled: !raw.inactive,
        paused: raw.pausedOn !== null,
        isRunning: raw.isRunning ?? false,
        consecutiveFailures: raw.consecutiveFailures ?? 0,
        lastException: raw.lastException ?? null,
        // Search.cs can't select Schedules in this joined query without
        // breaking SQL translation (Postgres date_part type mismatch), so
        // schedules is always empty here — showing "No schedule" would be
        // actively wrong for a job that has one. Leave it unset instead.
        scheduleSummary:
          schedules.length > 0 && (type === "Receiving" || type === "Aggregation")
            ? schedulesSummary(schedules)
            : undefined,
        lastReceiveOn: raw.receiveOn ?? null,
        createdOn: "",
      };
    });
  },

  async getIntegration(id: number): Promise<IntegrationDetail> {
    const raw = await fetchRaw(id);
    const infoType = await documentMethods.getInformationType(raw.documentId).catch(() => null);
    return {
      ...toIntegration(raw, id),
      informationTypeCode: infoType?.code ?? infoType?.name ?? "",
      informationTypeName: infoType?.name ?? "",
      // Populated once gateways (Batch 3), notifiers, and exchanges/trail are wired.
      apiGatewayAttachments: [],
      busGatewayRoutes: [],
      watchingNotifiers: [],
      recentExchanges: [],
      trail: [],
    };
  },

  async createIntegration(input: {
    type: IntegrationType;
    name: string;
    informationTypeId: number;
    receiverId?: string | null;
    receiverProperties?: Record<string, string>;
    validatorId?: string | null;
    validatorProperties?: Record<string, string>;
    mapperId?: string | null;
    mapperProperties?: Record<string, string>;
    handlerId?: string | null;
    handlerProperties?: Record<string, string>;
    schedules?: Schedule[];
    retryPolicyId?: number | null;
    enabled?: boolean;
  }): Promise<Integration> {
    // Create has no field for adapters/schedules/retry policy/enabled at all —
    // every subscription is born Inactive with empty pipelines — so those all
    // need an immediate follow-up update.
    const id = await post<number>("/subscriptions", {
      name: input.name,
      documentId: input.informationTypeId,
      type: input.type,
      partnerId: null,
      aggregationForId: null,
    });
    const current = await fetchRaw(id);
    await applyChanges(id, current, {
      receiverId: input.receiverId ?? null,
      receiverProperties: input.receiverProperties ?? {},
      validatorId: input.validatorId ?? null,
      validatorProperties: input.validatorProperties ?? {},
      mapperId: input.mapperId ?? null,
      mapperProperties: input.mapperProperties ?? {},
      handlerId: input.handlerId ?? null,
      handlerProperties: input.handlerProperties ?? {},
      schedules: input.schedules ?? [],
      retryPolicyId: input.retryPolicyId ?? null,
      enabled: input.enabled ?? false,
    });
    return toIntegration(await fetchRaw(id), id);
  },

  async updateIntegration(id: number, changes: UpdatableFields): Promise<Integration> {
    const current = await fetchRaw(id);
    await applyChanges(id, current, changes);
    return toIntegration(await fetchRaw(id), id);
  },

  async deleteIntegration(id: number): Promise<void> {
    await request(`/subscriptions/${id}`, { method: "DELETE" });
  },

  async pauseIntegration(id: number): Promise<Integration> {
    await post(`/subscriptions/${id}/pause`, {});
    return toIntegration(await fetchRaw(id), id);
  },

  async receiveNow(id: number): Promise<Integration> {
    await post(`/subscriptions/${id}/receivenow`, {});
    return toIntegration(await fetchRaw(id), id);
  },
} satisfies Partial<ApiClient>;
