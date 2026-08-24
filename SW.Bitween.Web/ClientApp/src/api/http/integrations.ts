import type { ApiClient } from "../client";
import {
  ApiRequestError,
  type Integration,
  type IntegrationDetail,
  type IntegrationInfo,
  type IntegrationLastRun,
  type IntegrationRow,
  type IntegrationRun,
  type IntegrationType,
  type InformationTypeRow,
  type Paged,
  type PartnerRow,
  type ReceiveAttemptRow,
  type ReceiveOutcome,
  type Schedule,
  type ScheduleHealth,
} from "../types";
import { schedulesSummary } from "../../lib/schedules";
import { documentMethods } from "./documents";
import { deriveStatus, exchangeMethods } from "./exchanges";
import { gatewayMethods } from "./gateways";
import { partnerMethods } from "./partners";
import { scanReferenceTokens } from "./references";
import { get, post, request } from "./request";
import { buildListQuery, SEARCHY_RULE, SEARCHY_SORT } from "./searchQuery";
import { toMatchGroup, toRawMatchExpression, type RawMatchSpec } from "./matchExpression";
import {
  toKvArray,
  toRawSchedules,
  type RawKeyAndValue,
  type RawSchedule,
} from "./subscriptionBody";

// ——— backend shapes (camelCase over the wire) ———
interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}

interface RawReceiveAttemptExchange {
  id: string;
  status: boolean | null;
  responseBad: boolean | null;
  promotedProperties: Record<string, string> | null;
}
interface RawReceiveAttempt {
  id: number;
  startedOn: string;
  finishedOn: string;
  // Enums may arrive as the numeric value or the name, depending on the endpoint.
  outcome: number | string;
  errorMessage: string | null;
  exchanges: RawReceiveAttemptExchange[];
}
const RECEIVE_OUTCOME_BY_NUM: Record<number, ReceiveOutcome> = {
  0: "Failed",
  1: "NoNewData",
  2: "Received",
};
const toReceiveOutcome = (o: number | string): ReceiveOutcome =>
  typeof o === "number" ? (RECEIVE_OUTCOME_BY_NUM[o] ?? "Failed") : (o as ReceiveOutcome);

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

// Empty-valued properties are dropped: an adapter property with no value means
// "not set", and keeping it would make a freshly-cleared field compare unequal to
// stored data that never had the key — leaving the Save bar up after an undo.
const toRecord = (kvs: RawKeyAndValue[] | null): Record<string, string> =>
  Object.fromEntries((kvs ?? []).filter((kv) => kv.value !== "").map((kv) => [kv.key, kv.value]));

const toSchedules = (raw: RawSchedule[] | null): Schedule[] =>
  (raw ?? []).map((s) => ({
    recurrence: s.recurrence,
    days: s.days,
    hours: s.hours,
    minutes: s.minutes,
    backwards: s.backwards,
  }));

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
    nextReceiveOn: raw.receiveOn ?? null,
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

function toIntegrationRow(
  raw: RawSubscription,
  infoTypeById: Map<number, InformationTypeRow>,
  partnerById: Map<number, PartnerRow>,
): IntegrationRow {
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
    nextReceiveOn: raw.receiveOn ?? null,
    createdOn: "",
  };
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
      handlerId: raw.handlerId ?? null,
      responseMessageTypeName: raw.responseMessageTypeName ?? null,
      responseIntegrationId: raw.responseSubscriptionId ?? null,
      // No backend endpoint indexes reference tokens, but the search rows carry
      // every adapter property, so the scan costs nothing extra here.
      ...scanReferenceTokens(
        [
          raw.receiverProperties,
          raw.validatorProperties,
          raw.mapperProperties,
          raw.handlerProperties,
        ].flatMap((props) => (props ?? []).map((p) => p.value)),
      ),
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
    return rows.map((raw) => toIntegrationRow(raw, infoTypeById, partnerById));
  },

  async searchIntegrationRows(query: {
    search: string;
    type: IntegrationType | null;
    informationTypeId?: number | null;
    partnerId?: number | null;
    inactive?: boolean | null;
    offset: number;
    limit: number;
  }): Promise<Paged<IntegrationRow>> {
    const qs = buildListQuery({
      filters: [
        ["Name", SEARCHY_RULE.contains, query.search.trim()],
        ["Type", SEARCHY_RULE.equalsTo, query.type ?? ""],
        ["DocumentId", SEARCHY_RULE.equalsTo, query.informationTypeId ?? ""],
        ["PartnerId", SEARCHY_RULE.equalsTo, query.partnerId ?? ""],
        ["Inactive", SEARCHY_RULE.equalsTo, query.inactive == null ? "" : String(query.inactive)],
      ],
      sort: ["Name", SEARCHY_SORT.asc],
      offset: query.offset,
      limit: query.limit,
    });
    const [res, infoTypes, partners] = await Promise.all([
      get<SearchyResponse<RawSubscription>>(`/subscriptions?${qs}`),
      documentMethods.listInformationTypes(),
      partnerMethods.listPartners(),
    ]);
    const infoTypeById = new Map(infoTypes.map((t) => [t.id, t]));
    const partnerById = new Map(partners.map((p) => [p.id, p]));
    return {
      total: res.totalCount,
      result: (res.result ?? []).map((raw) => toIntegrationRow(raw, infoTypeById, partnerById)),
    };
  },

  async getIntegration(id: number): Promise<IntegrationDetail> {
    const [raw, apiGateways, busGateways, recentExchanges] = await Promise.all([
      fetchRaw(id),
      gatewayMethods.listApiGateways(),
      gatewayMethods.listBusGateways(),
      exchangeMethods.searchExchanges({ integrationId: id, offset: 0, limit: 8 }),
    ]);
    const infoType = await documentMethods.getInformationType(raw.documentId).catch(() => null);
    return {
      ...toIntegration(raw, id),
      informationTypeCode: infoType?.code ?? infoType?.name ?? "",
      informationTypeName: infoType?.name ?? "",
      apiGatewayAttachments: apiGateways.flatMap((g) =>
        g.attachments
          .filter((a) => a.integrationId === id)
          .map((a) => ({
            gatewayId: g.id,
            gatewayName: g.name,
            urlName: g.urlName,
            partnerId: a.partnerId,
            partnerName: a.partnerName,
          })),
      ),
      busGatewayRoutes: busGateways.flatMap((g) =>
        g.routes
          .filter((r) => r.integrationId === id)
          .map((r) => ({ gatewayId: g.id, gatewayName: g.name, partnerId: r.partnerId, partnerName: r.partnerName })),
      ),
      recentExchanges: recentExchanges.result.map((x) => ({
        id: x.id,
        partnerName: x.partnerName ?? undefined,
        informationTypeCode: x.informationTypeCode,
        status: x.status,
        on: x.startedOn,
        promotedProperties: x.promotedProperties,
      })),
      // Populated once notifiers and the trail (a distinct audit-log endpoint,
      // deferred alongside the mapper editor/aggregation) are wired.
      watchingNotifiers: [],
      trail: [],
    };
  },

  async createIntegration(input: {
    type: IntegrationType;
    name: string;
    informationTypeId: number;
    /** Required by the types that carry their own partner — Internal and ApiCall. */
    partnerId?: number | null;
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
    responseIntegrationId?: number | null;
    responseMessageTypeName?: string | null;
    enabled?: boolean;
  }): Promise<Integration> {
    // One call, one transaction. This used to be a POST followed by a PATCH,
    // because create accepted only the name/type/document — and since the POST
    // committed on its own, a rejected PATCH left an empty subscription behind.
    const id = await post<number>("/subscriptions", {
      name: input.name,
      documentId: input.informationTypeId,
      type: input.type,
      partnerId: input.partnerId ?? null,
      aggregationForId: null,
      receiverId: input.receiverId ?? null,
      receiverProperties: toKvArray(input.receiverProperties ?? {}),
      validatorId: input.validatorId ?? null,
      validatorProperties: toKvArray(input.validatorProperties ?? {}),
      mapperId: input.mapperId ?? null,
      mapperProperties: toKvArray(input.mapperProperties ?? {}),
      handlerId: input.handlerId ?? null,
      handlerProperties: toKvArray(input.handlerProperties ?? {}),
      documentFilter: [],
      // Undefined rather than [] when there is no schedule: an empty array on a
      // Receiving subscription is rejected, and a job created without one is a
      // legitimate (if idle) thing to have.
      schedules: input.schedules?.length ? toRawSchedules(input.schedules) : undefined,
      retryPolicyId: input.retryPolicyId ?? null,
      customRetryPolicy: null,
      responseSubscriptionId: input.responseIntegrationId ?? null,
      responseMessageTypeName: input.responseMessageTypeName ?? null,
      inactive: !(input.enabled ?? false),
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

  listIntegrationRuns(id: number, limit = 20): Promise<IntegrationRun[]> {
    return get<IntegrationRun[]>(`/subscriptions/runs?subscriptionId=${id}&limit=${limit}`);
  },

  async searchReceiveAttempts(
    subscriptionId: number,
    query: { outcome: ReceiveOutcome | null; offset: number; limit: number },
  ): Promise<Paged<ReceiveAttemptRow>> {
    const params = new URLSearchParams({
      subscriptionId: String(subscriptionId),
      offset: String(query.offset),
      limit: String(query.limit),
    });
    if (query.outcome) params.set("outcome", query.outcome);
    const res = await get<SearchyResponse<RawReceiveAttempt>>(`/subscriptions/receiveattempts?${params.toString()}`);
    return {
      total: res.totalCount,
      result: (res.result ?? []).map((a) => ({
        id: a.id,
        startedOn: a.startedOn,
        finishedOn: a.finishedOn,
        outcome: toReceiveOutcome(a.outcome),
        errorMessage: a.errorMessage,
        exchanges: a.exchanges.map((x) => ({
          id: x.id,
          status: deriveStatus(x),
          promotedProperties: x.promotedProperties,
        })),
      })),
    };
  },

  async listLastRuns(): Promise<IntegrationLastRun[]> {
    const rows =
      await get<(Omit<IntegrationLastRun, "integrationId"> & { subscriptionId: number })[]>(
        "/subscriptions/lastruns",
      );
    return rows.map(({ subscriptionId, ...run }) => ({ ...run, integrationId: subscriptionId }));
  },

  async listScheduleHealth(): Promise<ScheduleHealth[]> {
    const rows =
      await get<(Omit<ScheduleHealth, "integrationId"> & { subscriptionId: number })[]>(
        "/subscriptions/schedulehealth",
      );
    return rows.map(({ subscriptionId, ...health }) => ({ ...health, integrationId: subscriptionId }));
  },
} satisfies Partial<ApiClient>;
