import type { ApiClient } from "../client";
import {
  ApiRequestError,
  type AdapterKind,
  type ExchangeRef,
  type Integration,
  type IntegrationDetail,
  type IntegrationRow,
  type IntegrationType,
  type Schedule,
  type WorkGroup,
} from "../types";
import { schedulesSummary } from "../../lib/schedules";
import { ADAPTER_CATALOG } from "./adapters";
import { partnerIdsOf, setupRefOf } from "./derive";
import { type MockDb, delay, loadDb, saveDb } from "./store";

const fail = (code: string, message: string): never => {
  throw new ApiRequestError(code, message);
};

const SLUG_PATTERN = /^[a-z0-9][a-z0-9-]{1,49}$/;
const CREATABLE_TYPES: IntegrationType[] = ["Receiving", "GatewayApiCall", "BusGateway"];

const currentUserName = (db: MockDb) =>
  db.users.find((u) => u.id === db.sessionUserId)?.displayName ?? "Unknown";

const requireIntegration = (db: MockDb, id: number): Integration =>
  db.integrations.find((s) => s.id === id) ??
  fail("NOT_FOUND", "This integration no longer exists.");

const requireApiGateway = (db: MockDb, id: number) =>
  db.apiGateways.find((g) => g.id === id) ?? fail("NOT_FOUND", "This API gateway no longer exists.");

const requireBusGateway = (db: MockDb, id: number) =>
  db.busGateways.find((g) => g.id === id) ?? fail("NOT_FOUND", "This bus gateway no longer exists.");

const requireWorkGroup = (db: MockDb, id: number): WorkGroup =>
  db.workGroups.find((w) => w.id === id) ?? fail("NOT_FOUND", "This work group no longer exists.");

/** Consumer count is a live RabbitMQ read in the real backend; here it's proxied by enabled assignees. */
const workGroupRow = (db: MockDb, w: WorkGroup) => ({
  ...structuredClone(w),
  usedByCount: db.integrations.filter((s) => s.workGroupId === w.id).length,
  consumerCount: db.integrations.filter((s) => s.workGroupId === w.id && s.enabled).length,
});

/** Every non-optional startup value of the chosen adapter must be filled. */
const validateAdapterConfig = (
  kind: AdapterKind,
  adapterId: string | null,
  props: Record<string, string>,
) => {
  if (adapterId === null) return;
  const adapter = ADAPTER_CATALOG.find((a) => a.kind === kind && a.id === adapterId);
  if (!adapter) fail("UNKNOWN_ADAPTER", `Unknown ${kind} adapter "${adapterId}".`);
  const missing = adapter!.props.filter((p) => !p.optional && !props[p.key]?.trim());
  if (missing.length > 0)
    fail(
      "MISSING_ADAPTER_PROPERTY",
      `${adapter!.label} still needs: ${missing.map((p) => p.key).join(", ")}.`,
    );
};

const validateSchedules = (schedules: Schedule[]) => {
  for (const s of schedules) {
    if (s.recurrence === "Monthly" && (s.days < 1 || s.days > 27))
      fail("INVALID_SCHEDULE", "Monthly schedules run on day 1–27.");
    if (s.recurrence === "Weekly" && (s.days < 0 || s.days > 6))
      fail("INVALID_SCHEDULE", "Weekly schedules need a weekday.");
  }
};

const integrationRow = (db: MockDb, s: Integration): IntegrationRow => ({
  id: s.id,
  name: s.name,
  type: s.type,
  informationTypeId: s.informationTypeId,
  informationTypeCode:
    db.informationTypes.find((t) => t.id === s.informationTypeId)?.code ?? "UNKNOWN",
  partners: partnerIdsOf(db, s)
    .map((pid) => ({ id: pid, name: db.partners.find((p) => p.id === pid)?.name }))
    .filter((p): p is { id: number; name: string } => !!p.name),
  enabled: s.enabled,
  paused: s.pausedOn !== null,
  isRunning: s.isRunning,
  consecutiveFailures: s.consecutiveFailures,
  lastException: s.lastException,
  scheduleSummary: s.type === "Receiving" || s.type === "Aggregation" ? schedulesSummary(s.schedules) : undefined,
  lastReceiveOn: s.lastReceiveOn,
  createdOn: s.createdOn,
});

const exchangeDto = (db: MockDb, e: MockDb["exchanges"][number]): ExchangeRef => ({
  id: e.id,
  partnerName: e.partnerId ? db.partners.find((p) => p.id === e.partnerId)?.name : undefined,
  informationTypeCode:
    db.informationTypes.find((t) => t.id === e.informationTypeId)?.code ?? "UNKNOWN",
  status: e.status,
  on: e.on,
  documents: e.documents ? structuredClone(e.documents) : undefined,
});

const integrationDetail = (db: MockDb, s: Integration): IntegrationDetail => {
  const infoType = db.informationTypes.find((t) => t.id === s.informationTypeId);
  return {
    ...structuredClone(s),
    informationTypeCode: infoType?.code ?? "UNKNOWN",
    informationTypeName: infoType?.name ?? "Unknown",
    apiGatewayAttachments: db.apiGateways.flatMap((g) =>
      g.attachments
        .filter((a) => a.integrationId === s.id)
        .map((a) => ({
          gatewayId: g.id,
          gatewayName: g.name,
          urlName: g.urlName,
          partnerId: a.partnerId,
          partnerName: db.partners.find((p) => p.id === a.partnerId)?.name ?? "Unknown",
        })),
    ),
    busGatewayRoutes: db.busGateways.flatMap((g) =>
      g.routes
        .filter((r) => r.integrationId === s.id)
        .map((r) => ({
          gatewayId: g.id,
          gatewayName: g.name,
          partnerId: r.partnerId,
          partnerName: r.partnerId ? (db.partners.find((p) => p.id === r.partnerId)?.name ?? null) : null,
        })),
    ),
    watchingNotifiers: db.notifiers
      .filter((n) => n.integrationIds.includes(s.id))
      .map((n) => ({ id: n.id, name: n.name })),
    recentExchanges: db.exchanges
      .filter((e) => e.integrationId === s.id)
      .sort((a, b) => b.on.localeCompare(a.on))
      .slice(0, 8)
      .map((e) => exchangeDto(db, e)),
    trail: structuredClone(db.integrationTrails[s.id] ?? []),
  };
};

const appendTrail = (db: MockDb, id: number, action: "Created" | "Updated") => {
  (db.integrationTrails[id] ??= []).push({
    on: new Date().toISOString(),
    action,
    by: currentUserName(db),
    byUserId: db.sessionUserId ?? undefined,
  });
};

export const integrationsClient = {
  // ——— integrations ———

  async listIntegrationRows() {
    await delay();
    const db = loadDb();
    return db.integrations.map((s) => integrationRow(db, s));
  },

  async getIntegration(id: number) {
    await delay();
    const db = loadDb();
    return integrationDetail(db, requireIntegration(db, id));
  },

  async createIntegration(input) {
    await delay();
    const db = loadDb();
    const name = input.name.trim();
    if (name.length < 2) fail("INVALID_NAME", "Give the integration a name.");
    if (db.integrations.some((s) => s.name.toLowerCase() === name.toLowerCase()))
      fail("NAME_TAKEN", "An integration with this name already exists.");
    if (!CREATABLE_TYPES.includes(input.type))
      fail("INVALID_TYPE", "New integrations are created from the gateway and scheduled-jobs pages.");
    if (!db.informationTypes.some((t) => t.id === input.informationTypeId))
      fail("NOT_FOUND", "This information type no longer exists.");
    if (input.type === "Receiving") {
      if (!input.receiverId) fail("MISSING_RECEIVER", "Receivers need a source adapter.");
      if (!input.schedules || input.schedules.length === 0)
        fail("MISSING_SCHEDULE", "Receivers need at least one schedule.");
    }
    validateAdapterConfig("receiver", input.receiverId ?? null, input.receiverProperties ?? {});
    validateAdapterConfig("validator", input.validatorId ?? null, input.validatorProperties ?? {});
    validateAdapterConfig("mapper", input.mapperId ?? null, input.mapperProperties ?? {});
    validateAdapterConfig("handler", input.handlerId ?? null, input.handlerProperties ?? {});
    validateSchedules(input.schedules ?? []);

    const integration: Integration = {
      id: Math.max(500, ...db.integrations.map((s) => s.id)) + 1,
      name,
      type: input.type,
      informationTypeId: input.informationTypeId,
      partnerId: null,
      enabled: input.enabled ?? false,
      pausedOn: null,
      workGroupId: null,
      retryPolicyId: input.retryPolicyId ?? null,
      receiverId: input.receiverId ?? null,
      receiverProperties: { ...(input.receiverProperties ?? {}) },
      validatorId: input.validatorId ?? null,
      validatorProperties: { ...(input.validatorProperties ?? {}) },
      mapperId: input.mapperId ?? null,
      mapperProperties: { ...(input.mapperProperties ?? {}) },
      handlerId: input.handlerId ?? null,
      handlerProperties: { ...(input.handlerProperties ?? {}) },
      matchExpression: null,
      schedules: structuredClone(input.schedules ?? []),
      responseIntegrationId: null,
      responseMessageTypeName: null,
      aggregationForId: null,
      isRunning: false,
      lastReceiveOn: null,
      consecutiveFailures: 0,
      lastException: null,
      createdOn: new Date().toISOString(),
    };
    db.integrations.push(integration);
    appendTrail(db, integration.id, "Created");
    saveDb(db);
    return structuredClone(integration);
  },

  async updateIntegration(id, changes) {
    await delay();
    const db = loadDb();
    const s = requireIntegration(db, id);
    const name = (changes.name ?? s.name).trim();
    if (name.length < 2) fail("INVALID_NAME", "Give the integration a name.");
    if (db.integrations.some((x) => x.id !== id && x.name.toLowerCase() === name.toLowerCase()))
      fail("NAME_TAKEN", "An integration with this name already exists.");

    const next = { ...s, ...structuredClone(changes), name };
    if (s.type === "Receiving") {
      if (!next.receiverId) fail("MISSING_RECEIVER", "Receivers need a source adapter.");
      if (next.schedules.length === 0) fail("MISSING_SCHEDULE", "Receivers need at least one schedule.");
    }
    validateAdapterConfig("receiver", next.receiverId, next.receiverProperties);
    validateAdapterConfig("validator", next.validatorId, next.validatorProperties);
    validateAdapterConfig("mapper", next.mapperId, next.mapperProperties);
    validateAdapterConfig("handler", next.handlerId, next.handlerProperties);
    validateSchedules(next.schedules);
    if (next.responseIntegrationId === id)
      fail("INVALID_RESPONSE_TARGET", "An integration can't feed its response into itself.");
    if (next.responseIntegrationId !== null && !db.integrations.some((x) => x.id === next.responseIntegrationId))
      fail("NOT_FOUND", "The response integration no longer exists.");
    if (next.retryPolicyId !== null && !db.retryPolicies.some((p) => p.id === next.retryPolicyId))
      fail("NOT_FOUND", "The chosen retry policy no longer exists.");
    if (next.workGroupId !== null && !db.workGroups.some((w) => w.id === next.workGroupId))
      fail("NOT_FOUND", "The chosen work group no longer exists.");

    Object.assign(s, next);
    appendTrail(db, id, "Updated");
    saveDb(db);
    return structuredClone(s);
  },

  async deleteIntegration(id) {
    await delay();
    const db = loadDb();
    const s = requireIntegration(db, id);
    const attachedTo = db.apiGateways.filter((g) => g.attachments.some((a) => a.integrationId === id));
    const routedFrom = db.busGateways.filter((g) => g.routes.some((r) => r.integrationId === id));
    if (attachedTo.length > 0 || routedFrom.length > 0) {
      const names = [...attachedTo, ...routedFrom].map((g) => g.name).join(", ");
      fail("IN_USE", `${s.name} is wired into ${names} — detach it from the gateway first.`);
    }
    const chainedFrom = db.integrations.find((x) => x.responseIntegrationId === id);
    if (chainedFrom)
      fail("IN_USE", `${chainedFrom.name} feeds its responses into ${s.name} — unlink that first.`);
    db.integrations = db.integrations.filter((x) => x.id !== id);
    for (const n of db.notifiers) n.integrationIds = n.integrationIds.filter((x) => x !== id);
    delete db.integrationTrails[id];
    saveDb(db);
  },

  async pauseIntegration(id) {
    await delay();
    const db = loadDb();
    const s = requireIntegration(db, id);
    s.pausedOn = s.pausedOn === null ? new Date().toISOString() : null;
    saveDb(db);
    return structuredClone(s);
  },

  async receiveNow(id) {
    await delay();
    const db = loadDb();
    const s = requireIntegration(db, id);
    if (s.type !== "Receiving") fail("NOT_A_RECEIVER", "Only receivers can receive on demand.");
    if (!s.enabled) fail("DISABLED", "Enable this integration before running it.");
    s.lastReceiveOn = new Date().toISOString();
    db.exchanges.unshift({
      id: `0x8DD${Math.floor(Math.random() * 0xffffff).toString(16).toUpperCase().padStart(6, "0")}`,
      partnerId: null,
      informationTypeId: s.informationTypeId,
      integrationId: s.id,
      partnerName: undefined,
      informationTypeCode: "",
      status: "processing",
      on: new Date().toISOString(),
    });
    saveDb(db);
    return structuredClone(s);
  },

  async listAdapters(kind: AdapterKind) {
    await delay();
    return structuredClone(ADAPTER_CATALOG.filter((a) => a.kind === kind));
  },

  // ——— work groups ———

  async listWorkGroups() {
    await delay();
    const db = loadDb();
    return db.workGroups.map((w) => workGroupRow(db, w));
  },

  async getWorkGroup(id: number) {
    await delay();
    const db = loadDb();
    const w = requireWorkGroup(db, id);
    return {
      ...structuredClone(w),
      integrations: db.integrations.filter((s) => s.workGroupId === id).map(setupRefOf),
    };
  },

  async createWorkGroup({
    name,
    busMessageName,
    prefetch,
    priority,
  }: {
    name: string;
    busMessageName: string;
    prefetch: number;
    priority: number;
  }) {
    await delay();
    const db = loadDb();
    const trimmedName = name.trim();
    const trimmedBusName = busMessageName.trim();
    if (trimmedName.length < 2) fail("INVALID_NAME", "Give the work group a name.");
    if (!trimmedBusName) fail("INVALID_BUS_MESSAGE_NAME", "Give the work group a bus message name.");
    // no uniqueness constraint on name/busMessageName — mirrors the real backend
    const group: WorkGroup = {
      id: Math.max(0, ...db.workGroups.map((w) => w.id)) + 1,
      name: trimmedName,
      busMessageName: trimmedBusName,
      options: { rabbitMqOptions: { consumerSettings: { prefetch, priority } } },
      createdOn: new Date().toISOString(),
    };
    db.workGroups.push(group);
    saveDb(db);
    return structuredClone(group);
  },

  async updateWorkGroup(
    id: number,
    { name, busMessageName, prefetch, priority }: { name: string; busMessageName: string; prefetch: number; priority: number },
  ) {
    await delay();
    const db = loadDb();
    const w = requireWorkGroup(db, id);
    const trimmedName = name.trim();
    const trimmedBusName = busMessageName.trim();
    if (trimmedName.length < 2) fail("INVALID_NAME", "Give the work group a name.");
    if (!trimmedBusName) fail("INVALID_BUS_MESSAGE_NAME", "Give the work group a bus message name.");
    w.name = trimmedName;
    w.busMessageName = trimmedBusName;
    w.options = { rabbitMqOptions: { consumerSettings: { prefetch, priority } } };
    saveDb(db);
    return structuredClone(w);
  },

  async deleteWorkGroup(id: number) {
    await delay();
    const db = loadDb();
    const w = requireWorkGroup(db, id);
    const used = db.integrations.filter((s) => s.workGroupId === id).length;
    if (used > 0)
      fail(
        "CANT_BE_DELETED",
        `${w.name} is assigned to ${used} integration${used === 1 ? "" : "s"} — unassign it first.`,
      );
    db.workGroups = db.workGroups.filter((x) => x.id !== id);
    saveDb(db);
  },

  // ——— API gateways ———

  async listApiGateways() {
    await delay();
    const db = loadDb();
    return db.apiGateways.map((g) => ({
      id: g.id,
      name: g.name,
      urlName: g.urlName,
      createdOn: g.createdOn,
      partnerCount: g.attachments.length,
      attachments: g.attachments.map((a) => ({
        partnerId: a.partnerId,
        partnerName: db.partners.find((p) => p.id === a.partnerId)?.name ?? "Unknown",
        integrationId: a.integrationId,
        integrationName: db.integrations.find((s) => s.id === a.integrationId)?.name ?? "Unknown",
      })),
    }));
  },

  async getApiGateway(id) {
    await delay();
    const db = loadDb();
    const g = requireApiGateway(db, id);
    return {
      id: g.id,
      name: g.name,
      urlName: g.urlName,
      createdOn: g.createdOn,
      attachments: g.attachments.map((a) => ({
        partnerId: a.partnerId,
        partnerName: db.partners.find((p) => p.id === a.partnerId)?.name ?? "Unknown",
        integrationId: a.integrationId,
        integrationName: db.integrations.find((s) => s.id === a.integrationId)?.name ?? "Unknown",
      })),
    };
  },

  async createApiGateway({ name, urlName }) {
    await delay();
    const db = loadDb();
    const trimmed = name.trim();
    const slug = urlName.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the gateway a name.");
    if (!SLUG_PATTERN.test(slug))
      fail("INVALID_URL_NAME", "URL names are lowercase letters, digits and dashes (2–50 chars).");
    if (db.apiGateways.some((g) => g.urlName === slug))
      fail("URL_NAME_TAKEN", "Another API gateway already uses this URL name.");
    if (db.apiGateways.some((g) => g.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "An API gateway with this name already exists.");
    const gateway = {
      id: Math.max(0, ...db.apiGateways.map((g) => g.id)) + 1,
      name: trimmed,
      urlName: slug,
      createdOn: new Date().toISOString(),
      attachments: [],
    };
    db.apiGateways.push(gateway);
    saveDb(db);
    const { attachments: _a, ...dto } = gateway;
    return structuredClone(dto);
  },

  async updateApiGateway(id, changes) {
    await delay();
    const db = loadDb();
    const g = requireApiGateway(db, id);
    const trimmed = changes.name.trim();
    const slug = changes.urlName.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the gateway a name.");
    if (!SLUG_PATTERN.test(slug))
      fail("INVALID_URL_NAME", "URL names are lowercase letters, digits and dashes (2–50 chars).");
    if (db.apiGateways.some((x) => x.id !== id && x.urlName === slug))
      fail("URL_NAME_TAKEN", "Another API gateway already uses this URL name.");
    g.name = trimmed;
    g.urlName = slug;
    saveDb(db);
    const { attachments: _a, ...dto } = g;
    return structuredClone(dto);
  },

  async deleteApiGateway(id) {
    await delay();
    const db = loadDb();
    requireApiGateway(db, id);
    db.apiGateways = db.apiGateways.filter((g) => g.id !== id);
    saveDb(db);
  },

  async attachGatewayPartner(id, { partnerId, integrationId }) {
    await delay();
    const db = loadDb();
    const g = requireApiGateway(db, id);
    if (!db.partners.some((p) => p.id === partnerId && !p.isSystem))
      fail("NOT_FOUND", "This partner no longer exists.");
    if (g.attachments.some((a) => a.partnerId === partnerId))
      fail("ALREADY_ATTACHED", "This partner is already attached — edit its attachment instead.");
    const integration = requireIntegration(db, integrationId);
    if (integration.type !== "GatewayApiCall")
      fail("WRONG_TYPE", "API gateways can only run API-gateway integrations.");
    g.attachments.push({ partnerId, integrationId });
    saveDb(db);
  },

  async updateGatewayAttachment(id, { partnerId, integrationId }) {
    await delay();
    const db = loadDb();
    const g = requireApiGateway(db, id);
    const attachment = g.attachments.find((a) => a.partnerId === partnerId);
    if (!attachment) fail("NOT_FOUND", "This attachment no longer exists.");
    const integration = requireIntegration(db, integrationId);
    if (integration.type !== "GatewayApiCall")
      fail("WRONG_TYPE", "API gateways can only run API-gateway integrations.");
    attachment!.integrationId = integrationId;
    saveDb(db);
  },

  async removeGatewayAttachment(id, partnerId) {
    await delay();
    const db = loadDb();
    const g = requireApiGateway(db, id);
    g.attachments = g.attachments.filter((a) => a.partnerId !== partnerId);
    saveDb(db);
  },

  // ——— bus gateways ———

  async listBusGateways() {
    await delay();
    const db = loadDb();
    return db.busGateways.map((g) => ({
      id: g.id,
      name: g.name,
      informationTypeId: g.informationTypeId,
      informationTypeCode:
        db.informationTypes.find((t) => t.id === g.informationTypeId)?.code ?? "UNKNOWN",
      createdOn: g.createdOn,
      routeCount: g.routes.length,
      routes: g.routes.map((r) => ({
        id: r.id,
        integrationId: r.integrationId,
        integrationName: db.integrations.find((s) => s.id === r.integrationId)?.name ?? "Unknown",
        partnerId: r.partnerId,
        partnerName: r.partnerId ? (db.partners.find((p) => p.id === r.partnerId)?.name ?? null) : null,
        matchExpression: structuredClone(r.matchExpression),
      })),
    }));
  },

  async getBusGateway(id) {
    await delay();
    const db = loadDb();
    const g = requireBusGateway(db, id);
    const infoType = db.informationTypes.find((t) => t.id === g.informationTypeId);
    return {
      id: g.id,
      name: g.name,
      informationTypeId: g.informationTypeId,
      informationTypeCode: infoType?.code ?? "UNKNOWN",
      informationTypeName: infoType?.name ?? "Unknown",
      createdOn: g.createdOn,
      routes: g.routes.map((r) => ({
        id: r.id,
        integrationId: r.integrationId,
        integrationName: db.integrations.find((s) => s.id === r.integrationId)?.name ?? "Unknown",
        partnerId: r.partnerId,
        partnerName: r.partnerId ? (db.partners.find((p) => p.id === r.partnerId)?.name ?? null) : null,
        matchExpression: structuredClone(r.matchExpression),
      })),
    };
  },

  async createBusGateway({ name, informationTypeId }) {
    await delay();
    const db = loadDb();
    const trimmed = name.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the gateway a name.");
    if (db.busGateways.some((g) => g.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "A bus gateway with this name already exists.");
    const infoType = db.informationTypes.find((t) => t.id === informationTypeId);
    if (!infoType) fail("NOT_FOUND", "This information type no longer exists.");
    if (!infoType!.busEnabled)
      fail("NOT_BUS_ENABLED", `${infoType!.code} isn't available on the message bus — enable that on its page first.`);
    const gateway = {
      id: Math.max(0, ...db.busGateways.map((g) => g.id)) + 1,
      name: trimmed,
      informationTypeId,
      createdOn: new Date().toISOString(),
      routes: [],
    };
    db.busGateways.push(gateway);
    saveDb(db);
    const { routes: _r, ...dto } = gateway;
    return structuredClone(dto);
  },

  async updateBusGateway(id, changes) {
    await delay();
    const db = loadDb();
    const g = requireBusGateway(db, id);
    const trimmed = changes.name.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the gateway a name.");
    if (db.busGateways.some((x) => x.id !== id && x.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "A bus gateway with this name already exists.");
    g.name = trimmed;
    saveDb(db);
    const { routes: _r, ...dto } = g;
    return structuredClone(dto);
  },

  async deleteBusGateway(id) {
    await delay();
    const db = loadDb();
    requireBusGateway(db, id);
    db.busGateways = db.busGateways.filter((g) => g.id !== id);
    saveDb(db);
  },

  async addBusRoute(id, { integrationId, partnerId, matchExpression }) {
    await delay();
    const db = loadDb();
    const g = requireBusGateway(db, id);
    const integration = requireIntegration(db, integrationId);
    if (integration.type !== "BusGateway")
      fail("WRONG_TYPE", "Bus gateways can only route to bus-gateway integrations.");
    if (integration.informationTypeId !== g.informationTypeId)
      fail("WRONG_TYPE", "The integration must carry the same information type as the gateway.");
    if (partnerId !== null && !db.partners.some((p) => p.id === partnerId && !p.isSystem))
      fail("NOT_FOUND", "This partner no longer exists.");
    g.routes.push({
      id: Math.max(0, ...g.routes.map((r) => r.id)) + 1,
      integrationId,
      partnerId,
      matchExpression: structuredClone(matchExpression),
    });
    saveDb(db);
  },

  async updateBusRoute(id, routeId, { integrationId, partnerId, matchExpression }) {
    await delay();
    const db = loadDb();
    const g = requireBusGateway(db, id);
    const route = g.routes.find((r) => r.id === routeId);
    if (!route) fail("NOT_FOUND", "This route no longer exists.");
    const integration = requireIntegration(db, integrationId);
    if (integration.type !== "BusGateway")
      fail("WRONG_TYPE", "Bus gateways can only route to bus-gateway integrations.");
    if (integration.informationTypeId !== g.informationTypeId)
      fail("WRONG_TYPE", "The integration must carry the same information type as the gateway.");
    if (partnerId !== null && !db.partners.some((p) => p.id === partnerId && !p.isSystem))
      fail("NOT_FOUND", "This partner no longer exists.");
    Object.assign(route!, { integrationId, partnerId, matchExpression: structuredClone(matchExpression) });
    saveDb(db);
  },

  async removeBusRoute(id, routeId) {
    await delay();
    const db = loadDb();
    const g = requireBusGateway(db, id);
    g.routes = g.routes.filter((r) => r.id !== routeId);
    saveDb(db);
  },
} satisfies Partial<ApiClient>;
