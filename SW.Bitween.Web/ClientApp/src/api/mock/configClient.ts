import type { ApiClient } from "../client";
import {
  ApiRequestError,
  type ExchangeRef,
  type GlobalValuesSetRow,
  type InformationType,
  type Notifier,
  type NotifierChannel,
  type Partner,
  type PartnerRow,
  type RetryDelay,
  type RetryGroup,
  type RetryMatcher,
  type RetryResultType,
  type RetryTestAttempt,
} from "../types";
import { matchSummary } from "../../lib/match";
import { globalRefsOf, integrationInfoOf, partnerIdsOf, setupRefOf } from "./derive";
import { type MockDb, delay, loadDb, saveDb } from "./store";

const fail = (code: string, message: string): never => {
  throw new ApiRequestError(code, message);
};

const CODE_PATTERN = /^[A-Z][A-Z0-9_]{1,49}$/;
const SLUG_PATTERN = /^[a-z0-9][a-z0-9-]{1,49}$/;

const currentUserName = (db: MockDb) =>
  db.users.find((u) => u.id === db.sessionUserId)?.displayName ?? "Unknown";

const exchangeDto = (db: MockDb, e: MockDb["exchanges"][number]): ExchangeRef => ({
  id: e.id,
  partnerName: e.partnerId ? db.partners.find((p) => p.id === e.partnerId)?.name : undefined,
  informationTypeCode:
    db.informationTypes.find((t) => t.id === e.informationTypeId)?.code ?? "UNKNOWN",
  status: e.status,
  on: e.startedOn,
  documents: e.documents ? structuredClone(e.documents) : undefined,
});

const partnerUsedByCount = (db: MockDb, id: number) =>
  db.integrations.filter((s) => s.partnerId === id).length +
  db.apiGateways.flatMap((g) => g.attachments).filter((a) => a.partnerId === id).length +
  db.busGateways.flatMap((g) => g.routes).filter((r) => r.partnerId === id).length;

const requirePartner = (db: MockDb, id: number): Partner =>
  db.partners.find((p) => p.id === id) ?? fail("NOT_FOUND", "This partner no longer exists.");

const requireInfoType = (db: MockDb, id: number): InformationType =>
  db.informationTypes.find((t) => t.id === id) ??
  fail("NOT_FOUND", "This information type no longer exists.");

const valueSetRow = (db: MockDb, set: MockDb["valueSets"][number]): GlobalValuesSetRow => ({
  ...structuredClone(set),
  usedByCount: db.integrations.filter((s) => globalRefsOf(s).some((g) => g.setId === set.id)).length,
});

/**
 * Delivery channels = notifier handler adapters. The real backend already
 * discovers these; this list mirrors the adapters that ship today.
 */
const NOTIFIER_CHANNELS: NotifierChannel[] = [
  {
    id: "sendgrid",
    label: "Email (SendGrid)",
    props: [
      { key: "apiKey", label: "SendGrid API key", placeholder: "SG.…" },
      { key: "from", label: "From address", placeholder: "bitween@company.example" },
      { key: "to", label: "Recipients (comma-separated)", placeholder: "ops@company.example" },
    ],
  },
  {
    id: "msteams",
    label: "Microsoft Teams",
    props: [
      {
        key: "webhookUrl",
        label: "Incoming webhook URL",
        placeholder: "https://outlook.office.com/webhook/…",
      },
    ],
  },
];

// ——— retry test evaluator (mirrors the backend's group semantics) ———

const matcherHits = (matcher: RetryMatcher, content: string): boolean => {
  switch (matcher.type) {
    case "contains":
      return matcher.caseSensitive
        ? content.includes(matcher.value)
        : content.toLowerCase().includes(matcher.value.toLowerCase());
    case "regex":
      try {
        return new RegExp(matcher.pattern, matcher.flags).test(content);
      } catch {
        return false;
      }
    case "exceptionType":
      return content.toLowerCase().includes(matcher.value.toLowerCase());
    case "jsonPath": {
      let node: unknown;
      try {
        node = JSON.parse(content);
      } catch {
        return false;
      }
      for (const part of matcher.path.replace(/^\$\.?/, "").split(".").filter(Boolean)) {
        if (node !== null && typeof node === "object" && part in (node as object)) {
          node = (node as Record<string, unknown>)[part];
        } else {
          node = undefined;
          break;
        }
      }
      const text = node === undefined || node === null ? undefined : String(node);
      switch (matcher.op) {
        case "Exists":
          return text !== undefined;
        case "NotExists":
          return text === undefined;
        case "Eq":
          return text !== undefined && text === (matcher.value ?? "");
        case "Neq":
          return text !== undefined && text !== (matcher.value ?? "");
        case "Contains":
          return text !== undefined && text.includes(matcher.value ?? "");
      }
    }
  }
};

const delayForAttempt = (delay: RetryDelay, attempt: number): number => {
  if (delay.type === "fixed") return delay.delaySeconds;
  if (delay.type === "linear") return delay.initialSeconds + delay.incrementSeconds * (attempt - 1);
  return Math.min(delay.initialSeconds * Math.pow(delay.multiplier, attempt - 1), delay.maxSeconds);
};

const evaluateRetry = (
  groups: RetryGroup[],
  resultType: RetryResultType,
  content: string,
  attempts: number,
): RetryTestAttempt[] => {
  const ordered = [...groups].filter((g) => g.enabled).sort((a, b) => a.priority - b.priority);
  const results: RetryTestAttempt[] = [];
  let stopped = false;
  for (let attempt = 1; attempt <= attempts && !stopped; attempt++) {
    const group = ordered.find(
      (g) =>
        g.appliesTo.includes(resultType) &&
        (g.matchers.length === 0 || g.matchers.some((m) => matcherHits(m, content))),
    );
    if (!group) {
      results.push({ attempt, shouldRetry: false, reason: "No group matches — default is to stop." });
      stopped = true;
    } else if (group.action === "Block") {
      results.push({ attempt, shouldRetry: false, matchedGroup: group.name, reason: "Group blocks retries." });
      stopped = true;
    } else if (!group.budget) {
      results.push({ attempt, shouldRetry: false, matchedGroup: group.name, reason: "Group allows retries but has no budget." });
      stopped = true;
    } else if (attempt >= group.budget.maxAttemptsPerError) {
      results.push({
        attempt,
        shouldRetry: false,
        matchedGroup: group.name,
        reason: `Budget spent — ${group.budget.maxAttemptsPerError} attempts per error.`,
      });
      stopped = true;
    } else {
      results.push({
        attempt,
        shouldRetry: true,
        matchedGroup: group.name,
        delaySeconds: Math.round(delayForAttempt(group.budget.delay, attempt)),
        reason: "Within budget.",
      });
    }
  }
  return results;
};

export const configClient = {
  // ——— partners ———

  async listPartners(): Promise<PartnerRow[]> {
    await delay();
    const db = loadDb();
    return db.partners.map((p) => ({
      ...structuredClone(p),
      credentialCount: db.credentials.filter((c) => c.partnerId === p.id).length,
      usedByCount: partnerUsedByCount(db, p.id),
    }));
  },

  async getPartner(id: number) {
    await delay();
    const db = loadDb();
    const partner = requirePartner(db, id);
    return {
      ...structuredClone(partner),
      apiCredentials: db.credentials
        .filter((c) => c.partnerId === id)
        .map((c) => ({ name: c.name, keyPrefix: c.key.slice(0, 5), createdOn: c.createdOn })),
      integrationSetups: db.integrations
        .filter((s) => partnerIdsOf(db, s).includes(id))
        .map(setupRefOf),
      apiGateways: db.apiGateways
        .filter((g) => g.attachments.some((a) => a.partnerId === id))
        .map((g) => ({ gatewayId: g.id, gatewayName: g.name, urlName: g.urlName })),
      busGatewayRoutes: db.busGateways.flatMap((g) =>
        g.routes
          .filter((r) => r.partnerId === id)
          .map((r) => ({ gatewayId: g.id, gatewayName: g.name, matchExpression: matchSummary(r.matchExpression) })),
      ),
      recentExchanges: db.exchanges
        .filter((e) => e.partnerId === id)
        .sort((a, b) => b.startedOn.localeCompare(a.startedOn))
        .slice(0, 8)
        .map((e) => exchangeDto(db, e)),
    };
  },

  async createPartner({ name }: { name: string }) {
    await delay();
    const db = loadDb();
    const trimmed = name.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the partner a name.");
    if (db.partners.some((p) => p.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "A partner with this name already exists.");
    const partner: Partner = {
      id: Math.max(...db.partners.map((p) => p.id)) + 1,
      name: trimmed,
      adapterProperties: {},
      isSystem: false,
      createdOn: new Date().toISOString(),
    };
    db.partners.push(partner);
    saveDb(db);
    return structuredClone(partner);
  },

  async updatePartner(
    id: number,
    changes: { name?: string; adapterProperties?: Record<string, string> },
  ) {
    await delay();
    const db = loadDb();
    const partner = requirePartner(db, id);
    if (changes.name !== undefined) {
      if (partner.isSystem) fail("SYSTEM_PARTNER", "The SYSTEM partner can't be renamed.");
      const trimmed = changes.name.trim();
      if (trimmed.length < 2) fail("INVALID_NAME", "Give the partner a name.");
      if (db.partners.some((p) => p.id !== id && p.name.toLowerCase() === trimmed.toLowerCase()))
        fail("NAME_TAKEN", "A partner with this name already exists.");
      partner.name = trimmed;
    }
    if (changes.adapterProperties !== undefined) {
      if (Object.keys(changes.adapterProperties).some((k) => !k.trim()))
        fail("INVALID_PROPERTY", "Property names can't be empty.");
      partner.adapterProperties = { ...changes.adapterProperties };
    }
    saveDb(db);
    return structuredClone(partner);
  },

  async deletePartner(id: number) {
    await delay();
    const db = loadDb();
    const partner = requirePartner(db, id);
    if (partner.isSystem) fail("SYSTEM_PARTNER", "The SYSTEM partner can't be deleted.");
    const used = partnerUsedByCount(db, id);
    if (used > 0)
      fail(
        "IN_USE",
        `${partner.name} is referenced by ${used} integration${used === 1 ? "" : "s"} or gateway attachment${used === 1 ? "" : "s"} — detach those first.`,
      );
    db.partners = db.partners.filter((p) => p.id !== id);
    db.credentials = db.credentials.filter((c) => c.partnerId !== id);
    saveDb(db);
  },

  async addPartnerCredential(id: number, name: string) {
    await delay();
    const db = loadDb();
    requirePartner(db, id);
    const trimmed = name.trim();
    if (!trimmed) fail("INVALID_NAME", "Give the key a name (e.g. Production).");
    if (db.credentials.some((c) => c.partnerId === id && c.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "This partner already has a key with that name.");
    const key = crypto.randomUUID().replaceAll("-", "");
    db.credentials.push({ partnerId: id, name: trimmed, key, createdOn: new Date().toISOString() });
    saveDb(db);
    return { key };
  },

  async revokePartnerCredential(id: number, name: string) {
    await delay();
    const db = loadDb();
    requirePartner(db, id);
    if (!db.credentials.some((c) => c.partnerId === id && c.name === name))
      fail("NOT_FOUND", "This key no longer exists.");
    db.credentials = db.credentials.filter((c) => !(c.partnerId === id && c.name === name));
    saveDb(db);
  },

  // ——— information types ———

  async listInformationTypes() {
    await delay();
    const db = loadDb();
    return db.informationTypes.map((t) => ({
      ...structuredClone(t),
      usedByCount:
        db.integrations.filter((s) => s.informationTypeId === t.id).length +
        db.busGateways.filter((g) => g.informationTypeId === t.id).length,
    }));
  },

  async getInformationType(id: number) {
    await delay();
    const db = loadDb();
    const type = requireInfoType(db, id);
    return {
      ...structuredClone(type),
      integrationSetups: db.integrations.filter((s) => s.informationTypeId === id).map(setupRefOf),
      busGateways: db.busGateways
        .filter((g) => g.informationTypeId === id)
        .map((g) => ({ gatewayId: g.id, gatewayName: g.name })),
      trail: structuredClone(db.trails[id] ?? []),
      recentExchanges: db.exchanges
        .filter((e) => e.informationTypeId === id)
        .sort((a, b) => b.startedOn.localeCompare(a.startedOn))
        .slice(0, 8)
        .map((e) => exchangeDto(db, e)),
    };
  },

  async createInformationType(input: {
    name: string;
    code: string;
    format: "Json" | "Xml";
    busEnabled?: boolean;
    busMessageTypeName?: string;
  }) {
    await delay();
    const db = loadDb();
    const name = input.name.trim();
    const code = input.code.trim();
    if (name.length < 2) fail("INVALID_NAME", "Give the information type a name.");
    if (!CODE_PATTERN.test(code))
      fail("INVALID_CODE", "Codes are UPPER_CASE letters, digits and underscores (2–50 chars).");
    if (db.informationTypes.some((t) => t.name.toLowerCase() === name.toLowerCase()))
      fail("NAME_TAKEN", "An information type with this name already exists.");
    if (db.informationTypes.some((t) => t.code === code))
      fail("CODE_TAKEN", "This code is already in use.");
    let busMessageTypeName: string | undefined;
    if (input.busEnabled) {
      busMessageTypeName = input.busMessageTypeName?.trim();
      if (!busMessageTypeName) fail("INVALID_BUS_NAME", "Bus-enabled types need a bus message type name.");
      if (db.informationTypes.some((t) => t.busMessageTypeName?.toLowerCase() === busMessageTypeName!.toLowerCase()))
        fail("DUPLICATED_BUS_TYPE_NAME", "Another information type already uses this bus message type name.");
    }
    const type: InformationType = {
      id: Math.max(...db.informationTypes.map((t) => t.id)) + 1,
      code,
      name,
      format: input.format,
      busEnabled: input.busEnabled ?? false,
      busMessageTypeName,
      duplicateIntervalMinutes: 0,
      disregardsUnfilteredMessages: false,
      promotedProperties: [],
      createdOn: new Date().toISOString(),
    };
    db.informationTypes.push(type);
    db.trails[type.id] = [
      { on: type.createdOn, action: "Created", by: currentUserName(db), byUserId: db.sessionUserId ?? undefined },
    ];
    saveDb(db);
    return structuredClone(type);
  },

  async updateInformationType(id: number, changes: Omit<InformationType, "id" | "createdOn">) {
    await delay();
    const db = loadDb();
    const type = requireInfoType(db, id);
    const name = changes.name.trim();
    const code = changes.code.trim();
    if (name.length < 2) fail("INVALID_NAME", "Give the information type a name.");
    if (!CODE_PATTERN.test(code))
      fail("INVALID_CODE", "Codes are UPPER_CASE letters, digits and underscores (2–50 chars).");
    if (db.informationTypes.some((t) => t.id !== id && t.name.toLowerCase() === name.toLowerCase()))
      fail("NAME_TAKEN", "An information type with this name already exists.");
    if (db.informationTypes.some((t) => t.id !== id && t.code === code))
      fail("CODE_TAKEN", "This code is already in use.");
    if (changes.busEnabled) {
      const busName = changes.busMessageTypeName?.trim();
      if (!busName) fail("INVALID_BUS_NAME", "Bus-enabled types need a bus message type name.");
      if (
        db.informationTypes.some(
          (t) => t.id !== id && t.busMessageTypeName?.toLowerCase() === busName!.toLowerCase(),
        )
      )
        fail("DUPLICATED_BUS_TYPE_NAME", "Another information type already uses this bus message type name.");
    }
    const keys = changes.promotedProperties.map((p) => p.key.trim());
    if (changes.promotedProperties.some((p) => !p.key.trim() || !p.path.trim()))
      fail("INVALID_PROMOTED_PROPERTY", "Promoted properties need both a name and a path.");
    if (new Set(keys.map((k) => k.toLowerCase())).size !== keys.length)
      fail("DUPLICATE_PROMOTED_PROPERTY_KEY", "Promoted property names must be unique.");

    Object.assign(type, {
      ...changes,
      name,
      code,
      busMessageTypeName: changes.busEnabled ? changes.busMessageTypeName?.trim() : undefined,
      promotedProperties: changes.promotedProperties.map((p) => ({
        key: p.key.trim(),
        path: p.path.trim(),
      })),
    });
    (db.trails[id] ??= []).push({
      on: new Date().toISOString(),
      action: "Updated",
      by: currentUserName(db),
      byUserId: db.sessionUserId ?? undefined,
    });
    saveDb(db);
    return structuredClone(type);
  },

  async deleteInformationType(id: number) {
    await delay();
    const db = loadDb();
    const type = requireInfoType(db, id);
    const used =
      db.integrations.filter((s) => s.informationTypeId === id).length +
      db.busGateways.filter((g) => g.informationTypeId === id).length;
    if (used > 0)
      fail("IN_USE", `${type.code} is used by ${used} integration${used === 1 ? "" : "s"} or bus gateway${used === 1 ? "" : "s"} — detach those first.`);
    db.informationTypes = db.informationTypes.filter((t) => t.id !== id);
    delete db.trails[id];
    saveDb(db);
  },

  // ——— global values ———

  async listValueSets() {
    await delay();
    const db = loadDb();
    return db.valueSets.map((set) => valueSetRow(db, set));
  },

  async getValueSet(id: string) {
    await delay();
    const db = loadDb();
    const set = db.valueSets.find((s) => s.id === id);
    if (!set) fail("NOT_FOUND", "This value set no longer exists.");
    return {
      ...structuredClone(set!),
      usedBy: db.integrations
        .map((s) => ({ s, refs: globalRefsOf(s).find((g) => g.setId === id) }))
        .filter((x) => x.refs)
        .map(({ s, refs }) => ({ integrationSetup: setupRefOf(s), keys: refs!.keys })),
    };
  },

  async createValueSet(input: { id: string; name: string; values: Record<string, string> }) {
    await delay();
    const db = loadDb();
    const slug = input.id.trim();
    const name = input.name.trim();
    if (name.length < 2) fail("INVALID_NAME", "Give the value set a name.");
    if (!SLUG_PATTERN.test(slug))
      fail("INVALID_ID", "IDs are lowercase letters, digits and dashes (2–50 chars).");
    if (db.valueSets.some((s) => s.id === slug)) fail("ID_EXISTS", "This ID is already in use.");
    if (Object.keys(input.values).some((k) => !k.trim()))
      fail("INVALID_VALUE", "Value names can't be empty.");
    const set = { id: slug, name, values: { ...input.values }, createdOn: new Date().toISOString() };
    db.valueSets.push(set);
    saveDb(db);
    return valueSetRow(db, set);
  },

  async updateValueSet(id: string, changes: { name: string; values: Record<string, string> }) {
    await delay();
    const db = loadDb();
    const set = db.valueSets.find((s) => s.id === id);
    if (!set) fail("NOT_FOUND", "This value set no longer exists.");
    if (changes.name.trim().length < 2) fail("INVALID_NAME", "Give the value set a name.");
    if (Object.keys(changes.values).some((k) => !k.trim()))
      fail("INVALID_VALUE", "Value names can't be empty.");
    set!.name = changes.name.trim();
    set!.values = { ...changes.values };
    saveDb(db);
    return valueSetRow(db, set!);
  },

  async deleteValueSet(id: string) {
    await delay();
    const db = loadDb();
    const set = db.valueSets.find((s) => s.id === id);
    if (!set) fail("NOT_FOUND", "This value set no longer exists.");
    const used = db.integrations.filter((s) => globalRefsOf(s).some((g) => g.setId === id)).length;
    if (used > 0)
      fail("IN_USE", `${set!.name} is referenced by ${used} integration${used === 1 ? "" : "s"} — remove those references first.`);
    db.valueSets = db.valueSets.filter((s) => s.id !== id);
    saveDb(db);
  },

  // ——— integrations (cached summaries) ———

  async listIntegrations() {
    await delay();
    const db = loadDb();
    return db.integrations.map((s) => integrationInfoOf(db, s));
  },

  // ——— retry policies ———

  async listRetryPolicies() {
    await delay();
    const db = loadDb();
    return db.retryPolicies.map((p) => ({
      id: p.id,
      name: p.name,
      groupCount: p.groups.length,
      createdOn: p.createdOn,
      usedByCount: db.integrations.filter((s) => s.retryPolicyId === p.id).length,
    }));
  },

  async getRetryPolicy(id: number) {
    await delay();
    const db = loadDb();
    const policy = db.retryPolicies.find((p) => p.id === id);
    if (!policy) fail("NOT_FOUND", "This retry policy no longer exists.");
    return {
      ...structuredClone(policy!),
      integrations: db.integrations.filter((s) => s.retryPolicyId === id).map(setupRefOf),
    };
  },

  async createRetryPolicy({ name }: { name: string }) {
    await delay();
    const db = loadDb();
    const trimmed = name.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the policy a name.");
    if (db.retryPolicies.some((p) => p.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "A retry policy with this name already exists.");
    const policy = {
      id: Math.max(0, ...db.retryPolicies.map((p) => p.id)) + 1,
      name: trimmed,
      groups: [],
      createdOn: new Date().toISOString(),
    };
    db.retryPolicies.push(policy);
    saveDb(db);
    return structuredClone(policy);
  },

  async updateRetryPolicy(id: number, changes: { name: string; groups: RetryGroup[] }) {
    await delay();
    const db = loadDb();
    const policy = db.retryPolicies.find((p) => p.id === id);
    if (!policy) fail("NOT_FOUND", "This retry policy no longer exists.");
    const trimmed = changes.name.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the policy a name.");
    if (db.retryPolicies.some((p) => p.id !== id && p.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "A retry policy with this name already exists.");
    if (changes.groups.some((g) => !g.name.trim()))
      fail("INVALID_GROUP", "Every group needs a name.");
    policy!.name = trimmed;
    policy!.groups = structuredClone(changes.groups);
    saveDb(db);
    return structuredClone(policy!);
  },

  async deleteRetryPolicy(id: number) {
    await delay();
    const db = loadDb();
    const policy = db.retryPolicies.find((p) => p.id === id);
    if (!policy) fail("NOT_FOUND", "This retry policy no longer exists.");
    const used = db.integrations.filter((s) => s.retryPolicyId === id).length;
    if (used > 0)
      fail("IN_USE", `${policy!.name} is assigned to ${used} integration${used === 1 ? "" : "s"} — unassign it first.`);
    db.retryPolicies = db.retryPolicies.filter((p) => p.id !== id);
    saveDb(db);
  },

  async testRetryPolicy({ groups, resultType, content, attempts }) {
    await delay();
    if (!content.trim()) fail("NO_CONTENT", "Paste an error message or result body to simulate.");
    return evaluateRetry(groups, resultType, content, Math.min(Math.max(attempts, 1), 20));
  },

  // ——— notifiers ———

  async listNotifierChannels() {
    await delay();
    return structuredClone(NOTIFIER_CHANNELS);
  },

  async listNotifiers() {
    await delay();
    return structuredClone(loadDb().notifiers);
  },

  async getNotifier(id: number) {
    await delay();
    const db = loadDb();
    const notifier = db.notifiers.find((n) => n.id === id);
    if (!notifier) fail("NOT_FOUND", "This notifier no longer exists.");
    return {
      ...structuredClone(notifier!),
      recentNotifications: db.notifications
        .filter((n) => n.notifierId === id)
        .sort((a, b) => b.on.localeCompare(a.on))
        .slice(0, 8)
        .map(({ notifierId: _n, ...entry }) => structuredClone(entry)),
    };
  },

  async createNotifier({ name }: { name: string }) {
    await delay();
    const db = loadDb();
    const trimmed = name.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the notifier a name.");
    if (db.notifiers.some((n) => n.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "A notifier with this name already exists.");
    const notifier: Notifier = {
      id: Math.max(0, ...db.notifiers.map((n) => n.id)) + 1,
      name: trimmed,
      enabled: true,
      onFailed: true,
      onBadResult: false,
      onSuccess: false,
      channelId: NOTIFIER_CHANNELS[0].id,
      channelProperties: {},
      integrationIds: [],
      createdOn: new Date().toISOString(),
    };
    db.notifiers.push(notifier);
    saveDb(db);
    return structuredClone(notifier);
  },

  async updateNotifier(id: number, changes: Omit<Notifier, "id" | "createdOn">) {
    await delay();
    const db = loadDb();
    const notifier = db.notifiers.find((n) => n.id === id);
    if (!notifier) fail("NOT_FOUND", "This notifier no longer exists.");
    const trimmed = changes.name.trim();
    if (trimmed.length < 2) fail("INVALID_NAME", "Give the notifier a name.");
    if (db.notifiers.some((n) => n.id !== id && n.name.toLowerCase() === trimmed.toLowerCase()))
      fail("NAME_TAKEN", "A notifier with this name already exists.");
    if (!NOTIFIER_CHANNELS.some((c) => c.id === changes.channelId))
      fail("INVALID_CHANNEL", "Pick a delivery channel.");
    Object.assign(notifier!, {
      ...changes,
      name: trimmed,
      channelProperties: { ...changes.channelProperties },
      integrationIds: changes.integrationIds.filter((sid) =>
        db.integrations.some((s) => s.id === sid),
      ),
    });
    saveDb(db);
    return structuredClone(notifier!);
  },

  async deleteNotifier(id: number) {
    await delay();
    const db = loadDb();
    if (!db.notifiers.some((n) => n.id === id))
      fail("NOT_FOUND", "This notifier no longer exists.");
    db.notifiers = db.notifiers.filter((n) => n.id !== id);
    db.notifications = db.notifications.filter((n) => n.notifierId !== id);
    saveDb(db);
  },

  async testNotifier({ channelId, channelProperties }) {
    await delay();
    const channel = NOTIFIER_CHANNELS.find((c) => c.id === channelId);
    if (!channel) fail("INVALID_CHANNEL", "Pick a delivery channel.");
    const missing = channel!.props.filter((p) => !channelProperties[p.key]?.trim());
    if (missing.length > 0)
      fail("MISSING_PROPERTY", `${channel!.label} still needs: ${missing.map((p) => p.label).join(", ")}.`);
    return { message: `Test notification sent via ${channel!.label}.` };
  },
} satisfies Partial<ApiClient>;
