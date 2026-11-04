/** An exchange's status, as the Exchanges page's tabs divide them. */
export type ExchangeStatus = "processing" | "success" | "badResponse" | "failed";

/** Which exchanges, filtered the way the Exchanges page filters them. Everything set has to hold. */
export interface ExchangeQuery {
  status?: ExchangeStatus;
  /** Started at or after this. */
  from?: Date | string;
  /** Started at or before this. */
  to?: Date | string;
  partnerIds?: number[];
  subscriptionIds?: number[];
  /** Of any of these information types (documents, in the API). */
  informationTypeIds?: number[];
  /** These exchanges, with the retry of each and the exchanges each rolled up. `get` is one alone. */
  ids?: string[];
  /** The retry of this exchange: an exchange has at most one. */
  retryFor?: string;
  correlationId?: string;
  /** Only the newest attempt of each retry chain. */
  latestOnly?: boolean;
  /** The error contains this text, ignoring case. */
  errorContains?: string;
  /** By key: the value contains the text, ignoring case, or with null, the exchange promoted the key. */
  promotedProperties?: Record<string, string | null>;
  /** Filters in Bitween's own form, `Field:Rule:Value`, for what the rest doesn't cover. */
  filters?: string[];
}

/** Which exchanges a bulk retry or an export covers: picked by id, or every one a query matches. */
export type ExchangeSelection = { ids: string[] } | { matching: ExchangeQuery; except?: string[] };

export interface Page<T> {
  result: T[];
  /** How many match. An exchange search stops counting at 10,001, meaning more than 10,000. */
  totalCount: number;
}

export interface ExchangeRow {
  id: string;
  subscriptionId: number | null;
  subscriptionName: string | null;
  /** The information type's id. */
  documentId: number;
  documentName: string;
  partnerId: number | null;
  handlerId: string | null;
  mapperId: string | null;
  references: string[] | null;
  /** Null while running, true when it succeeded, false when it failed. */
  status: boolean | null;
  /** 0 processing, 1 success, 2 bad response, 3 failed. */
  statusFilter: number;
  statusString: string | null;
  exception: string | null;
  startedOn: string;
  finishedOn: string | null;
  deliveredOn: string | null;
  aggregatedOn: string | null;
  duration: string | null;
  inputFileName: string | null;
  inputFileSize: number;
  inputFileHash: string | null;
  outputFileName: string | null;
  outputFileSize: number;
  responseFileName: string | null;
  responseFileSize: number;
  /** Keys for `readFile`. */
  inputKey: string | null;
  outputKey: string | null;
  responseKey: string | null;
  promotedProperties: Record<string, string> | null;
  correlationId: string | null;
  /** The exchange this one retries. */
  retryFor: string | null;
  /** Something has been retried from this exchange already, so it can't be retried again. */
  hasRetry: boolean;
  aggregationXchangeId: string | null;
  sourceXchangeId: string | null;
  sourceValues: Record<string, string> | null;
  outputBad: boolean | null;
  responseBad: boolean | null;
  scheduledRetryOn: string | null;
  retryBlockedReason: string | null;
  warning: string | null;
}

export interface RetryTree {
  /** The original attempt every node descends from. */
  rootId: string;
  /** Oldest first; each names the one it retries. */
  nodes: {
    id: string;
    retryFor: string | null;
    promotedProperties: Record<string, string> | null;
    startedOn: string;
    finishedOn: string | null;
    status: boolean | null;
    responseBad: boolean | null;
    exception: string | null;
    /** A person asked for this attempt, not a retry policy. */
    manualRetry: boolean;
    scheduledRetryOn: string | null;
    retryBlockedReason: string | null;
  }[];
  /** The walk stopped at its depth limit. */
  truncated: boolean;
}

export interface BulkRetryPlan {
  selected: number;
  willRetry: number;
  /** The most one bulk retry carries out. */
  limit: number;
  /** The most a preview works out. */
  maxSelection: number;
  overLimit: boolean;
  /** The attempts that will run, to retry as `{ ids }` in rounds of `limit`. */
  targets: string[];
  substituted: { selectedId: string; retryId: string }[];
  skipped: { id: string; reason: string }[];
  substitutedCount: number;
  skippedCount: number;
  listed: number;
  stoppedAfter: number | null;
  properties: Record<string, Record<string, string>>;
}

export interface ExportCheck {
  count: number;
  /** Older than storage keeps files, so in the zip without them. */
  withoutFiles: number;
  keptDays: number | null;
}

export interface ScheduledRetryQuery {
  subscriptionId?: number;
  informationTypeId?: number;
  errorContains?: string;
  /** Due at or after this. */
  from?: Date | string;
  /** Due at or before this. */
  to?: Date | string;
}

export interface ScheduledRetryRow {
  /** The exchange's id. */
  id: string;
  /** When it is due. */
  on: string;
  /** Kept because its group's retry budget is spent. */
  held: boolean;
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

export interface Exchanges {
  search(query?: ExchangeQuery, paging?: { page?: number; size?: number; count?: boolean }): Promise<Page<ExchangeRow>>;
  get(id: string): Promise<ExchangeRow | null>;
  readFile(key: string): Promise<string>;
  retryTree(id: string): Promise<RetryTree>;
  retry(id: string, options?: { reset?: boolean; reason?: string }): Promise<string | null>;
  previewBulkRetry(selection: ExchangeSelection, options?: { reset?: boolean }): Promise<BulkRetryPlan>;
  bulkRetry(selection: ExchangeSelection, options?: { reset?: boolean; reason?: string }): Promise<BulkRetryPlan>;
  checkExport(selection: ExchangeSelection): Promise<ExportCheck>;
  /** The zip, read as it downloads. */
  export(selection: ExchangeSelection): Promise<ReadableStream<Uint8Array>>;
}

export interface ScheduledRetries {
  search(query?: ScheduledRetryQuery, paging?: { page?: number; size?: number }): Promise<Page<ScheduledRetryRow>>;
  runNow(id: string): Promise<void>;
}

export declare class BitweenClient {
  constructor(options: { url: string; token: string; fetch?: typeof fetch });
  readonly exchanges: Exchanges;
  readonly scheduledRetries: ScheduledRetries;
}

export declare class BitweenClientError extends Error {
  readonly status: number;
  /** Bitween's reasons by code, such as ALREADY_RETRIED, each with its messages. */
  readonly errors: Record<string, string[]>;
  /** The first code, or null. */
  readonly code: string | null;
}
