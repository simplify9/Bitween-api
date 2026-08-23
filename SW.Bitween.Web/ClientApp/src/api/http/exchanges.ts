import type { ApiClient } from "../client";
import type {
  ExchangeQuery,
  ExchangeRow,
  ExchangeStatus,
  Paged,
  ScheduledRetryQuery,
  ScheduledRetryRow,
} from "../types";
import { partnerMethods } from "./partners";
import { get, post } from "./request";

// ——— backend shapes (camelCase over the wire) ———
interface SearchyResponse<T> {
  result: T[];
  totalCount: number;
}
interface RawXchangeRow {
  id: string;
  subscriptionId: number | null;
  subscriptionName: string | null;
  documentId: number;
  documentName: string;
  mapperId: string | null;
  status: boolean | null;
  exception: string | null;
  finishedOn: string | null;
  startedOn: string;
  inputFileName: string | null;
  outputFileName: string | null;
  responseFileName: string | null;
  inputKey: string | null;
  outputKey: string | null;
  responseKey: string | null;
  promotedProperties: Record<string, string> | null;
  retryFor: string | null;
  aggregationXchangeId: string | null;
  responseBad: boolean | null;
  correlationId: string | null;
  partnerId: number | null;
  scheduledRetryOn: string | null;
}
interface RawDelayedRetryRow {
  id: string;
  on: string;
  subscriptionId: number | null;
  subscriptionName: string | null;
  documentId: number;
  documentName: string;
  exception: string | null;
  startedOn: string;
  promotedProperties: Record<string, string> | null;
  retryPolicyId: number | null;
  retryPolicyName: string | null;
}

/**
 * The backend has no real ExchangeStatus enum — it's derived from two
 * nullable booleans. `status == null` while still running; once set, a bad
 * *response* (the handler delivered but the receiver answered with an error)
 * is distinct from an outright failure.
 */
const deriveStatus = (raw: Pick<RawXchangeRow, "status" | "responseBad">): ExchangeStatus =>
  raw.status === null ? "processing" : !raw.status ? "failed" : raw.responseBad ? "badResponse" : "success";

const STATUS_FILTER: Record<ExchangeStatus, number> = {
  processing: 0,
  success: 1,
  badResponse: 2,
  failed: 3,
};

const toExchangeRow = (raw: RawXchangeRow, partnerNameById: Map<number, string>): ExchangeRow => ({
  id: raw.id,
  status: deriveStatus(raw),
  integrationId: raw.subscriptionId,
  integrationName: raw.subscriptionName,
  informationTypeId: raw.documentId,
  informationTypeCode: raw.documentName,
  partnerId: raw.partnerId,
  partnerName: raw.partnerId !== null ? (partnerNameById.get(raw.partnerId) ?? null) : null,
  startedOn: raw.startedOn,
  finishedOn: raw.finishedOn,
  correlationId: raw.correlationId,
  retryFor: raw.retryFor,
  aggregationXchangeId: raw.aggregationXchangeId,
  scheduledRetryOn: raw.scheduledRetryOn,
  exception: raw.exception,
  promotedProperties: raw.promotedProperties,
  mapperSkipped: raw.mapperId === null,
  // Search's projection never populates file sizes/hashes (always 0 at the
  // source) — show the name, which is real, with a size of 0 rather than
  // fabricating one. Existence is keyed off `*Key` (backend only emits one once
  // the file actually has bytes), not the file name, since gateway/manually
  // created exchanges have no name yet content still exists.
  files: {
    input: raw.inputKey ? { name: raw.inputFileName ?? "input", size: 0, key: raw.inputKey } : null,
    mapped: raw.outputKey ? { name: raw.outputFileName ?? "mapped", size: 0, key: raw.outputKey } : null,
    handled: raw.responseKey ? { name: raw.responseFileName ?? "handled", size: 0, key: raw.responseKey } : null,
  },
});

const toScheduledRetryRow = (raw: RawDelayedRetryRow): ScheduledRetryRow => ({
  id: raw.id,
  on: raw.on,
  integrationId: raw.subscriptionId,
  integrationName: raw.subscriptionName,
  informationTypeId: raw.documentId,
  informationTypeCode: raw.documentName,
  exception: raw.exception,
  startedOn: raw.startedOn,
  promotedProperties: raw.promotedProperties,
  retryPolicyId: raw.retryPolicyId,
  retryPolicyName: raw.retryPolicyName,
});

/**
 * The backend's date-`Range` filter (rule 21) is unconditionally broken: its
 * shared parser (`SearchyFilter.cs`, SW-PrimitiveTypes) does `DateTime.Parse`
 * with no `DateTimeStyles`, which can only produce `Local` or `Unspecified`
 * `Kind` — Npgsql then refuses to bind it to a `timestamptz` column and the
 * whole search 500s, regardless of what the client sends. Two scalar
 * comparisons (`GreaterThanOrEquals`/`LessThanOrEquals`, rules 6/8) go
 * through a different code path and work correctly — use those instead.
 */
function buildExchangeQuery(query: ExchangeQuery): string {
  const params = new URLSearchParams();
  if (query.status) params.append("filter", `StatusFilter:1:${STATUS_FILTER[query.status]}`);
  if (query.integrationId !== undefined) params.append("filter", `SubscriptionId:1:${query.integrationId}`);
  if (query.partnerId !== undefined) params.append("filter", `PartnerId:1:${query.partnerId}`);
  if (query.informationTypeId !== undefined) params.append("filter", `DocumentId:1:${query.informationTypeId}`);
  if (query.ids?.trim()) {
    const ids = query.ids.split(/[\s,|]+/).filter(Boolean);
    params.append("filter", `Id:4:text|${ids.join("|")}`);
  }
  if (query.correlationId?.trim()) params.append("filter", `CorrelationId:1:${query.correlationId.trim()}`);
  // PromotedPropertiesRaw is stored as "key:value,key:value", so prefixing the key turns
  // the same substring match into a scoped one — no schema or endpoint change needed.
  // Typing "merchant:Acme" into the value box has therefore always worked; the picker
  // just makes it something you can find.
  const propertyValue = query.property?.trim() ?? "";
  const propertyTerm = query.propertyKey ? `${query.propertyKey}:${propertyValue}` : propertyValue;
  if (propertyTerm) params.append("filter", `PromotedPropertiesRaw:4:${propertyTerm}`);
  if (query.from) params.append("filter", `StartedOn:6:${query.from}`);
  if (query.to) params.append("filter", `StartedOn:8:${query.to}`);
  params.set("page", String(Math.floor(query.offset / query.limit)));
  params.set("size", String(query.limit));
  return params.toString();
}

function buildScheduledRetryQuery(query: ScheduledRetryQuery): string {
  const params = new URLSearchParams();
  if (query.integrationId !== undefined) params.append("filter", `SubscriptionId:1:${query.integrationId}`);
  if (query.informationTypeId !== undefined) params.append("filter", `DocumentId:1:${query.informationTypeId}`);
  if (query.exception?.trim()) params.append("filter", `Exception:4:${query.exception.trim()}`);
  if (query.from) params.append("filter", `On:6:${query.from}`);
  if (query.to) params.append("filter", `On:8:${query.to}`);
  params.set("page", String(Math.floor(query.offset / query.limit)));
  params.set("size", String(query.limit));
  return params.toString();
}

async function partnerNameMap(): Promise<Map<number, string>> {
  const partners = await partnerMethods.listPartners();
  return new Map(partners.map((p) => [p.id, p.name]));
}

export const exchangeMethods = {
  async getExchangeDocument(key: string): Promise<string> {
    const res = await get<{ data: string; key: string }>(`/bitweendocs?documentKey=${encodeURIComponent(key)}`);
    return res.data;
  },

  async searchExchanges(query: ExchangeQuery): Promise<Paged<ExchangeRow>> {
    const [res, partnerNameById] = await Promise.all([
      get<SearchyResponse<RawXchangeRow>>(`/xchanges?${buildExchangeQuery(query)}`),
      partnerNameMap(),
    ]);
    return { result: res.result.map((r) => toExchangeRow(r, partnerNameById)), total: res.totalCount };
  },

  async retryExchange(id: string, { reset }: { reset: boolean }): Promise<{ id: string }> {
    await post(`/xchanges/${id}/retry`, { reason: "Manual retry", reset });
    // Retry.cs returns null — look up the retry it just created (the newest
    // xchange with retryFor == id) for a real id to hand back to the caller.
    const res = await get<SearchyResponse<RawXchangeRow>>(
      `/xchanges?filter=${encodeURIComponent(`RetryFor:1:${id}`)}&sort=StartedOn:2&size=1`,
    );
    return { id: res.result[0]?.id ?? id };
  },

  async bulkRetryExchanges(ids: string[], { reset }: { reset: boolean }): Promise<{ retried: number; skipped: number }> {
    // BulkRetry.cs silently skips ids that already have a scheduled auto-retry
    // and returns null — mirror its exact skip rule ourselves beforehand so we
    // can report real counts back to the caller.
    const idFilter = `Id:4:text|${ids.join("|")}`;
    const current = await get<SearchyResponse<RawXchangeRow>>(
      `/xchanges?filter=${encodeURIComponent(idFilter)}&size=${ids.length}`,
    );
    // The Id filter also matches retryFor/aggregationXchangeId — narrow back
    // down to exactly the requested ids.
    const byId = new Map(current.result.filter((r) => ids.includes(r.id)).map((r) => [r.id, r]));
    const skipped = ids.filter((id) => byId.get(id)?.scheduledRetryOn != null).length;
    await post("/xchanges/bulkretry", { ids, reason: "Bulk retry", reset });
    return { retried: ids.length - skipped, skipped };
  },

  async createExchange(input: {
    target: "integration" | "informationType";
    integrationId?: number;
    informationTypeId?: number;
    data: string;
  }): Promise<{ id: string }> {
    const filter =
      input.target === "integration"
        ? `SubscriptionId:1:${input.integrationId}`
        : `DocumentId:1:${input.informationTypeId}`;
    await post("/xchanges", {
      option: input.target === "integration" ? "SubscriberId" : "DocumentId",
      subscriberId: input.target === "integration" ? input.integrationId : null,
      documentId: input.target === "informationType" ? input.informationTypeId : null,
      data: input.data,
    });
    // Create.cs returns null too — look up the exchange it just created. When
    // addressed at an information type, every matching integration gets its
    // own exchange; we can only link to one, so take the newest.
    const res = await get<SearchyResponse<RawXchangeRow>>(
      `/xchanges?filter=${encodeURIComponent(filter)}&sort=StartedOn:2&size=1`,
    );
    return { id: res.result[0]?.id ?? "" };
  },

  async searchScheduledRetries(query: ScheduledRetryQuery): Promise<Paged<ScheduledRetryRow>> {
    const res = await get<SearchyResponse<RawDelayedRetryRow>>(`/delayedretries?${buildScheduledRetryQuery(query)}`);
    return { result: res.result.map(toScheduledRetryRow), total: res.totalCount };
  },

  async runScheduledRetryNow(id: string): Promise<void> {
    await post(`/delayedretries/${id}/runnow`, {});
  },
} satisfies Partial<ApiClient>;
