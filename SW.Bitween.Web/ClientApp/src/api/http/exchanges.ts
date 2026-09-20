import type { ApiClient } from "../client";
import type {
  BulkRetryPlan,
  BulkRetrySelection,
  ExchangeQuery,
  ExchangeRow,
  ExchangeStatus,
  Paged,
  RetryTree,
  ScheduledRetryQuery,
  ScheduledRetryRow,
} from "../types";
import { partnerMethods } from "./partners";
import { get, post } from "./request";
import { searchyQueryString } from "./searchQuery";

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
  inputFileSize: number;
  outputFileSize: number;
  responseFileSize: number;
  inputKey: string | null;
  outputKey: string | null;
  responseKey: string | null;
  promotedProperties: Record<string, string | null> | null;
  retryFor: string | null;
  aggregationXchangeId: string | null;
  responseBad: boolean | null;
  correlationId: string | null;
  partnerId: number | null;
  scheduledRetryOn: string | null;
  hasRetry: boolean;
}

interface RawRetryNode {
  id: string;
  retryFor: string | null;
  promotedProperties: Record<string, string | null> | null;
  startedOn: string;
  finishedOn: string | null;
  status: boolean | null;
  responseBad: boolean | null;
  exception: string | null;
  manualRetry: boolean;
  scheduledRetryOn: string | null;
  retryBlockedReason: string | null;
}

interface RawRetryTree {
  rootId: string;
  nodes: RawRetryNode[];
  truncated: boolean;
}

interface RawBulkRetryPlan {
  selected: number;
  willRetry: number;
  limit: number;
  overLimit: boolean;
  substituted: { selectedId: string; retryId: string }[] | null;
  skipped: { id: string; reason: string }[] | null;
  properties: Record<string, Record<string, string | null>> | null;
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
  promotedProperties: Record<string, string | null> | null;
  retryPolicyId: number | null;
  retryPolicyName: string | null;
}

/**
 * The backend has no real ExchangeStatus enum — it's derived from two
 * nullable booleans. `status == null` while still running; once set, a bad
 * *response* (the handler delivered but the receiver answered with an error)
 * is distinct from an outright failure.
 */
export const deriveStatus = (raw: Pick<RawXchangeRow, "status" | "responseBad">): ExchangeStatus =>
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
  subscriptionId: raw.subscriptionId,
  subscriptionName: raw.subscriptionName,
  informationTypeId: raw.documentId,
  informationTypeCode: raw.documentName,
  partnerId: raw.partnerId,
  partnerName: raw.partnerId !== null ? (partnerNameById.get(raw.partnerId) ?? null) : null,
  startedOn: raw.startedOn,
  finishedOn: raw.finishedOn,
  correlationId: raw.correlationId,
  retryFor: raw.retryFor,
  hasRetry: raw.hasRetry,
  aggregationXchangeId: raw.aggregationXchangeId,
  scheduledRetryOn: raw.scheduledRetryOn,
  exception: raw.exception,
  promotedProperties: raw.promotedProperties,
  mapperSkipped: raw.mapperId === null,
  // Existence is keyed off `*Key` (backend only emits one once the file actually
  // has bytes), not the file name, since gateway/manually created exchanges have
  // no name yet content still exists.
  files: {
    input: raw.inputKey
      ? { name: raw.inputFileName ?? "input", size: raw.inputFileSize ?? 0, key: raw.inputKey }
      : null,
    mapped: raw.outputKey
      ? { name: raw.outputFileName ?? "mapped", size: raw.outputFileSize ?? 0, key: raw.outputKey }
      : null,
    handled: raw.responseKey
      ? { name: raw.responseFileName ?? "handled", size: raw.responseFileSize ?? 0, key: raw.responseKey }
      : null,
  },
});

const toScheduledRetryRow = (raw: RawDelayedRetryRow): ScheduledRetryRow => ({
  id: raw.id,
  on: raw.on,
  subscriptionId: raw.subscriptionId,
  subscriptionName: raw.subscriptionName,
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
/**
 * Just the filters, without paging — what "select all matching" sends, so a bulk retry acts on
 * the same set the list was showing rather than on the 25 rows that happened to be on screen.
 */
function buildExchangeFilters(query: ExchangeQuery): URLSearchParams {
  const params = new URLSearchParams();
  if (query.status) params.append("filter", `StatusFilter:1:${STATUS_FILTER[query.status]}`);
  if (query.subscriptionId !== undefined) params.append("filter", `SubscriptionId:1:${query.subscriptionId}`);
  if (query.partnerId !== undefined) params.append("filter", `PartnerId:1:${query.partnerId}`);
  if (query.informationTypeId !== undefined) params.append("filter", `DocumentId:1:${query.informationTypeId}`);
  if (query.ids?.trim()) {
    const ids = query.ids.split(/[\s,|]+/).filter(Boolean);
    params.append("filter", `Id:4:text|${ids.join("|")}`);
  }
  // The server turns this into the same Id filter above, from the run's own record of what it
  // created — so a run of any size is one short parameter rather than every id in the URL.
  if (query.receiveAttemptId !== undefined)
    params.append("filter", `ReceiveAttemptId:1:${query.receiveAttemptId}`);
  if (query.correlationId?.trim()) params.append("filter", `CorrelationId:1:${query.correlationId.trim()}`);
  if (query.latest) params.append("filter", "LatestOnly:1:true");
  // PromotedPropertiesRaw is stored as "key:value,key:value", so prefixing the key turns
  // the same substring match into a scoped one — no schema or endpoint change needed.
  // Typing "merchant:Acme" into the value box has therefore always worked; the picker
  // just makes it something you can find.
  const propertyValue = query.property?.trim() ?? "";
  const propertyTerm = query.propertyKey ? `${query.propertyKey}:${propertyValue}` : propertyValue;
  if (propertyTerm) params.append("filter", `PromotedPropertiesRaw:4:${propertyTerm}`);
  if (query.from) params.append("filter", `StartedOn:6:${query.from}`);
  if (query.to) params.append("filter", `StartedOn:8:${query.to}`);
  return params;
}

function buildExchangeQuery(query: ExchangeQuery): string {
  const params = buildExchangeFilters(query);
  params.set("page", String(Math.floor(query.offset / query.limit)));
  params.set("size", String(query.limit));
  return searchyQueryString(params);
}

function buildScheduledRetryQuery(query: ScheduledRetryQuery): string {
  const params = new URLSearchParams();
  if (query.subscriptionId !== undefined) params.append("filter", `SubscriptionId:1:${query.subscriptionId}`);
  if (query.informationTypeId !== undefined) params.append("filter", `DocumentId:1:${query.informationTypeId}`);
  if (query.exception?.trim()) params.append("filter", `Exception:4:${query.exception.trim()}`);
  if (query.from) params.append("filter", `On:6:${query.from}`);
  if (query.to) params.append("filter", `On:8:${query.to}`);
  params.set("page", String(Math.floor(query.offset / query.limit)));
  params.set("size", String(query.limit));
  return searchyQueryString(params);
}

/**
 * Both bulk-retry endpoints take the same request, so the preview cannot describe a different
 * selection than the retry acts on. The filter goes over as the same query-string fragment the
 * list itself sends, percent-encoded the way the backend's parser expects (see
 * `searchyQueryString`), and the backend re-runs it — the client never has to enumerate ids it
 * has not loaded.
 */
async function postBulkRetry(
  url: string,
  selection: BulkRetrySelection,
  reset: boolean,
): Promise<BulkRetryPlan> {
  const body =
    "ids" in selection
      ? { ids: selection.ids }
      : {
          filter: searchyQueryString(buildExchangeFilters(selection.matching)),
          excludeIds: selection.excludeIds,
        };

  const plan = await post<RawBulkRetryPlan>(url, { ...body, reason: "Bulk retry", reset });
  return {
    selected: plan.selected,
    willRetry: plan.willRetry,
    limit: plan.limit,
    overLimit: plan.overLimit,
    substituted: plan.substituted ?? [],
    skipped: plan.skipped ?? [],
    properties: plan.properties ?? {},
  };
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

  /**
   * Every attempt related to one exchange. Worth asking for only when the row says there is
   * something to see — `retryFor` or `hasRetry` — since most exchanges have neither.
   */
  async getRetryTree(id: string): Promise<RetryTree> {
    const raw = await get<RawRetryTree>(`/xchanges/retrytree?id=${encodeURIComponent(id)}`);
    return {
      rootId: raw.rootId,
      truncated: raw.truncated,
      attempts: (raw.nodes ?? []).map((n) => ({
        id: n.id,
        retryFor: n.retryFor,
        promotedProperties: n.promotedProperties,
        startedOn: n.startedOn,
        finishedOn: n.finishedOn,
        status: deriveStatus(n),
        exception: n.exception,
        manualRetry: n.manualRetry,
        scheduledRetryOn: n.scheduledRetryOn,
        retryBlockedReason: n.retryBlockedReason,
      })),
    };
  },

  /** What a bulk retry would do, so it can be shown before anyone commits to it. */
  previewBulkRetry(selection: BulkRetrySelection, { reset }: { reset: boolean }): Promise<BulkRetryPlan> {
    return postBulkRetry("/xchanges/bulkretrypreview", selection, reset);
  },

  /** Runs the retry and reports what it actually did, in the same shape as the preview. */
  bulkRetryExchanges(selection: BulkRetrySelection, { reset }: { reset: boolean }): Promise<BulkRetryPlan> {
    return postBulkRetry("/xchanges/bulkretry", selection, reset);
  },

  async createExchange(input: {
    target: "subscription" | "informationType";
    subscriptionId?: number;
    informationTypeId?: number;
    data: string;
  }): Promise<{ id: string }> {
    const filter =
      input.target === "subscription"
        ? `SubscriptionId:1:${input.subscriptionId}`
        : `DocumentId:1:${input.informationTypeId}`;
    await post("/xchanges", {
      option: input.target === "subscription" ? "SubscriberId" : "DocumentId",
      subscriberId: input.target === "subscription" ? input.subscriptionId : null,
      documentId: input.target === "informationType" ? input.informationTypeId : null,
      data: input.data,
    });
    // Create.cs returns null too — look up the exchange it just created. When
    // addressed at an information type, every matching subscription gets its
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
