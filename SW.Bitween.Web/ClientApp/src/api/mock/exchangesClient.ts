import type { ApiClient } from "../client";
import {
  ApiRequestError,
  type DashboardData,
  type ExchangeRow,
  type ExchangeStatus,
  type ScheduledRetryRow,
} from "../types";
import { type MockDb, delay, loadDb, saveDb } from "./store";
import type { SeedExchange } from "./seedConfig";

const fail = (code: string, message: string): never => {
  throw new ApiRequestError(code, message);
};

const newXid = () =>
  `0x8DD${Math.floor(Math.random() * 0xffffff).toString(16).toUpperCase().padStart(6, "0")}`;

const rowDto = (db: MockDb, e: SeedExchange): ExchangeRow => ({
  id: e.id,
  status: e.status,
  integrationId: e.integrationId,
  integrationName: e.integrationId
    ? (db.integrations.find((s) => s.id === e.integrationId)?.name ?? null)
    : null,
  informationTypeId: e.informationTypeId,
  informationTypeCode:
    db.informationTypes.find((t) => t.id === e.informationTypeId)?.code ?? "UNKNOWN",
  partnerId: e.partnerId,
  partnerName: e.partnerId ? (db.partners.find((p) => p.id === e.partnerId)?.name ?? null) : null,
  startedOn: e.startedOn,
  finishedOn: e.finishedOn,
  correlationId: e.correlationId,
  retryFor: e.retryFor,
  aggregationXchangeId: e.aggregationXchangeId,
  scheduledRetryOn: e.scheduledRetryOn,
  exception: e.exception,
  promotedProperties: e.promotedProperties ? structuredClone(e.promotedProperties) : null,
  mapperSkipped: e.mapperSkipped,
  files: structuredClone(e.files),
  documents: e.documents ? structuredClone(e.documents) : undefined,
});

const requireExchange = (db: MockDb, id: string): SeedExchange =>
  db.exchanges.find((e) => e.id === id) ?? fail("NOT_FOUND", "This exchange no longer exists.");

/** A retry re-runs the original input as a brand-new exchange. */
const createRetryRow = (original: SeedExchange): SeedExchange => ({
  ...structuredClone(original),
  id: newXid(),
  status: "processing",
  startedOn: new Date().toISOString(),
  finishedOn: null,
  retryFor: original.id,
  aggregationXchangeId: null,
  scheduledRetryOn: null,
  exception: null,
  files: { input: structuredClone(original.files.input), mapped: null, handled: null },
  documents: original.documents?.filter((d) => d.stage === "Input"),
});

export const exchangesClient: Pick<
  ApiClient,
  | "searchExchanges"
  | "retryExchange"
  | "bulkRetryExchanges"
  | "createExchange"
  | "searchScheduledRetries"
  | "runScheduledRetryNow"
  | "getDashboard"
> = {
  async searchExchanges(query) {
    await delay();
    const db = loadDb();
    let rows = db.exchanges.slice();

    if (query.ids?.trim()) {
      // Mirrors the real API: an id matches the exchange itself, its retries
      // (retryFor) and its aggregation members (aggregationXchangeId).
      const ids = query.ids.split(/[\s,|]+/).filter(Boolean);
      rows = rows.filter(
        (e) =>
          ids.includes(e.id) ||
          (e.retryFor !== null && ids.includes(e.retryFor)) ||
          (e.aggregationXchangeId !== null && ids.includes(e.aggregationXchangeId)),
      );
    }
    if (query.status) rows = rows.filter((e) => e.status === query.status);
    if (query.integrationId !== undefined)
      rows = rows.filter((e) => e.integrationId === query.integrationId);
    if (query.partnerId !== undefined) rows = rows.filter((e) => e.partnerId === query.partnerId);
    if (query.informationTypeId !== undefined)
      rows = rows.filter((e) => e.informationTypeId === query.informationTypeId);
    if (query.correlationId?.trim()) {
      const cid = query.correlationId.trim();
      rows = rows.filter((e) => e.correlationId === cid);
    }
    if (query.property?.trim()) {
      const needle = query.property.trim().toLowerCase();
      rows = rows.filter(
        (e) =>
          e.promotedProperties !== null &&
          Object.entries(e.promotedProperties).some(
            ([k, v]) => k.toLowerCase().includes(needle) || v.toLowerCase().includes(needle),
          ),
      );
    }
    if (query.from) rows = rows.filter((e) => e.startedOn >= query.from!);
    if (query.to) rows = rows.filter((e) => e.startedOn <= query.to!);

    rows.sort((a, b) => b.startedOn.localeCompare(a.startedOn));
    const total = rows.length;
    const page = rows.slice(query.offset, query.offset + query.limit);
    return { result: page.map((e) => rowDto(db, e)), total };
  },

  async retryExchange(id, { reset }) {
    await delay();
    const db = loadDb();
    const original = requireExchange(db, id);
    if (original.scheduledRetryOn !== null)
      fail(
        "AUTO_RETRY_SCHEDULED",
        'An auto-retry is already scheduled for this exchange. Use "Run now" to execute it immediately instead of retrying manually.',
      );
    const retry = createRetryRow(original);
    // `reset` re-resolves adapter properties from the integration's current
    // configuration — invisible in the mock, but the semantics are honoured.
    if (reset && original.integrationId === null)
      fail("SUBSCRIPTION_NOT_FOUND", "Can't reset properties — the integration no longer exists.");
    db.exchanges.unshift(retry);
    saveDb(db);
    return { id: retry.id };
  },

  async bulkRetryExchanges(ids, { reset }) {
    await delay();
    const db = loadDb();
    let retried = 0;
    let skipped = 0;
    for (const id of ids) {
      const original = db.exchanges.find((e) => e.id === id);
      if (!original) continue;
      if (original.scheduledRetryOn !== null) {
        skipped++;
        continue;
      }
      if (reset && original.integrationId === null) {
        skipped++;
        continue;
      }
      db.exchanges.unshift(createRetryRow(original));
      retried++;
    }
    saveDb(db);
    return { retried, skipped };
  },

  async createExchange({ target, integrationId, informationTypeId, data }) {
    await delay();
    const db = loadDb();
    if (!data.trim()) fail("EMPTY_PAYLOAD", "Paste the payload the exchange should carry.");

    let infoTypeId: number;
    let intId: number | null;
    let partnerId: number | null = null;
    if (target === "integration") {
      const s = db.integrations.find((i) => i.id === integrationId);
      if (!s) fail("SUBSCRIPTION_NOT_FOUND", "That integration no longer exists.");
      infoTypeId = s!.informationTypeId;
      intId = s!.id;
    } else {
      const t = db.informationTypes.find((i) => i.id === informationTypeId);
      if (!t) fail("DOCUMENT_NOT_FOUND", "That information type no longer exists.");
      infoTypeId = t!.id;
      // Addressed at the type: every matching integration picks it up; the
      // mock attributes it to the first one for display.
      intId = db.integrations.find((i) => i.informationTypeId === t!.id)?.id ?? null;
    }
    const integration = db.integrations.find((i) => i.id === intId);
    if (integration) partnerId = integration.partnerId ?? null;

    const trimmed = data.trim();
    const isXml = trimmed.startsWith("<");
    const row: SeedExchange = {
      id: newXid(),
      status: "processing",
      partnerId,
      informationTypeId: infoTypeId,
      integrationId: intId,
      startedOn: new Date().toISOString(),
      finishedOn: null,
      correlationId: null,
      retryFor: null,
      aggregationXchangeId: null,
      scheduledRetryOn: null,
      exception: null,
      promotedProperties: null,
      mapperSkipped: integration ? !integration.mapperId : true,
      files: {
        input: { name: isXml ? "manual.xml" : "manual.json", size: trimmed.length },
        mapped: null,
        handled: null,
      },
      documents: [{ stage: "Input", content: trimmed }],
    };
    db.exchanges.unshift(row);
    saveDb(db);
    return { id: row.id };
  },

  async searchScheduledRetries(query) {
    await delay();
    const db = loadDb();
    let rows = db.exchanges.filter((e) => e.scheduledRetryOn !== null);

    if (query.integrationId !== undefined)
      rows = rows.filter((e) => e.integrationId === query.integrationId);
    if (query.informationTypeId !== undefined)
      rows = rows.filter((e) => e.informationTypeId === query.informationTypeId);
    if (query.exception?.trim()) {
      const needle = query.exception.trim().toLowerCase();
      rows = rows.filter((e) => e.exception?.toLowerCase().includes(needle));
    }
    if (query.from) rows = rows.filter((e) => e.scheduledRetryOn! >= query.from!);
    if (query.to) rows = rows.filter((e) => e.scheduledRetryOn! <= query.to!);

    rows.sort((a, b) => a.scheduledRetryOn!.localeCompare(b.scheduledRetryOn!));
    const total = rows.length;
    const page = rows.slice(query.offset, query.offset + query.limit);
    const dto = (e: SeedExchange): ScheduledRetryRow => ({
      id: e.id,
      on: e.scheduledRetryOn!,
      integrationId: e.integrationId,
      integrationName: e.integrationId
        ? (db.integrations.find((s) => s.id === e.integrationId)?.name ?? null)
        : null,
      informationTypeId: e.informationTypeId,
      informationTypeCode:
        db.informationTypes.find((t) => t.id === e.informationTypeId)?.code ?? "UNKNOWN",
      exception: e.exception,
      startedOn: e.startedOn,
    });
    return { result: page.map(dto), total };
  },

  async runScheduledRetryNow(id) {
    await delay();
    const db = loadDb();
    const original = requireExchange(db, id);
    if (original.scheduledRetryOn === null)
      fail("NOT_SCHEDULED", "This exchange has no pending auto-retry.");
    original.scheduledRetryOn = null;
    db.exchanges.unshift(createRetryRow(original));
    saveDb(db);
  },

  async getDashboard() {
    await delay();
    const db = loadDb();
    const now = Date.now();
    const dayMs = 86_400_000;
    const startOfToday = new Date(new Date(now).setHours(0, 0, 0, 0)).getTime();

    const started = (e: SeedExchange) => Date.parse(e.startedOn);
    const isBad = (s: ExchangeStatus) => s === "failed" || s === "badResponse";

    const todayRows = db.exchanges.filter((e) => started(e) >= startOfToday);
    const yesterdayRows = db.exchanges.filter((e) => {
      const t = started(e);
      return t >= startOfToday - dayMs && t < startOfToday;
    });

    const week = db.exchanges.filter((e) => started(e) >= now - 7 * dayMs);
    const finished7d = week.filter((e) => e.status !== "processing");
    const successRate7d =
      finished7d.length === 0
        ? 100
        : Math.round((finished7d.filter((e) => e.status === "success").length / finished7d.length) * 100);

    // Last 14 calendar days, oldest first.
    const trafficByDay = Array.from({ length: 14 }, (_, i) => {
      const dayStart = startOfToday - (13 - i) * dayMs;
      const dayRows = db.exchanges.filter((e) => {
        const t = started(e);
        return t >= dayStart && t < dayStart + dayMs;
      });
      return {
        date: new Date(dayStart).toISOString(),
        success: dayRows.filter((e) => !isBad(e.status)).length,
        failed: dayRows.filter((e) => isBad(e.status)).length,
      };
    });

    const byIntegration = new Map<number, { count: number; failed: number }>();
    for (const e of week) {
      if (e.integrationId === null) continue;
      const entry = byIntegration.get(e.integrationId) ?? { count: 0, failed: 0 };
      entry.count++;
      if (isBad(e.status)) entry.failed++;
      byIntegration.set(e.integrationId, entry);
    }
    const busiest = [...byIntegration.entries()]
      .map(([id, v]) => ({
        id,
        name: db.integrations.find((s) => s.id === id)?.name ?? `#${id}`,
        ...v,
      }))
      .sort((a, b) => b.count - a.count)
      .slice(0, 5);

    const latestFailures = db.exchanges
      .filter((e) => isBad(e.status))
      .sort((a, b) => b.startedOn.localeCompare(a.startedOn))
      .slice(0, 6)
      .map((e) => ({
        id: e.id,
        status: e.status,
        integrationId: e.integrationId,
        integrationName: e.integrationId
          ? (db.integrations.find((s) => s.id === e.integrationId)?.name ?? null)
          : null,
        informationTypeCode:
          db.informationTypes.find((t) => t.id === e.informationTypeId)?.code ?? "UNKNOWN",
        on: e.startedOn,
        exception: e.exception,
      }));

    const data: DashboardData = {
      today: {
        total: todayRows.length,
        failed: todayRows.filter((e) => isBad(e.status)).length,
        processing: todayRows.filter((e) => e.status === "processing").length,
      },
      yesterdayTotal: yesterdayRows.length,
      successRate7d,
      pendingRetries: db.exchanges.filter((e) => e.scheduledRetryOn !== null).length,
      queueAlerts: 1, // mirrors the opsClient simulation's steady warning
      trafficByDay,
      busiest,
      latestFailures,
      attention: {
        failingIntegrations: db.integrations
          .filter((s) => s.consecutiveFailures > 0)
          .map((s) => ({ id: s.id, name: s.name, consecutiveFailures: s.consecutiveFailures })),
        pausedIntegrations: db.integrations
          .filter((s) => s.pausedOn !== null)
          .map((s) => ({ id: s.id, name: s.name })),
      },
    };
    return data;
  },
};
