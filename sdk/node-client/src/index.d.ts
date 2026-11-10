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
  /** By key: the value contains the text, ignoring case, or with null or blank text, the exchange promoted the key. */
  promotedProperties?: Record<string, string | null>;
  /** Filters in Bitween's own form, `Field:Rule:Value`, for what the rest doesn't cover. */
  filters?: string[];
}

/** Which exchanges a bulk retry or an export covers: picked by id, or every one a query matches. */
export type ExchangeSelection = { ids: string[] } | { matching: ExchangeQuery; except?: string[] };

export interface Page<T> {
  result: T[];
  /** How many match, or 0 when searched with `count: false`. An exchange search stops counting at 10,001, meaning more than 10,000. */
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


/** A list's search: names containing `nameContains` (case-sensitively on PostgreSQL), and raw filters. */
export interface ListQuery {
  nameContains?: string;
  /** Filters in Bitween's own form, `Field:Rule:Value`. Rule 1 is equals, 4 contains. */
  filters?: string[];
}

export type SubscriptionType = "internal" | "apiCall" | "receiving" | "aggregation" | "gatewayApiCall" | "busGateway" | "response";

export interface SubscriptionQuery extends ListQuery {
  type?: SubscriptionType;
  informationTypeId?: number;
  partnerId?: number;
}

/** A setting, an API key or an adapter property: `{ key, value }`. For a partner's keys, `key` is the key's name. */
export interface KeyAndValue {
  key: string;
  value: string;
}

/** A match expression, as a subscription or a bus gateway route carries it. */
export type MatchExpression =
  | { type: "one_of" | "not_one_of"; path: string; values: string[] }
  | { type: "and" | "or"; left: MatchExpression; right: MatchExpression };

/** A partner. Secret settings come back as `__private__`, keys masked; both survive being saved back. */
export interface Partner {
  name: string;
  adapterProperties?: Record<string, string> | null;
  /** The settings stored as secrets. */
  secretProperties?: string[];
  loginIdentity?: string | null;
  apiCredentials?: KeyAndValue[];
  /** By key name. Only on `get`. */
  keysLastUsedOn?: Record<string, string>;
  [more: string]: unknown;
}

export interface PartnerRow {
  id: number;
  name: string;
  subscriptionsCount: number | null;
  keys: number | null;
  /** Its settings' names; their values never come in a list. */
  propertyKeys: string[];
  [more: string]: unknown;
}

/** A subscription, in the shape `get` answers and `create` / `update` take. Bitween's API reference lists every field. */
export interface Subscription {
  name: string;
  type?: SubscriptionType | string;
  documentId: number;
  partnerId?: number | null;
  inactive?: boolean;
  pausedOn?: string | null;
  handlerId?: string | null;
  mapperId?: string | null;
  receiverId?: string | null;
  validatorId?: string | null;
  handlerProperties?: KeyAndValue[];
  mapperProperties?: KeyAndValue[];
  receiverProperties?: KeyAndValue[];
  validatorProperties?: KeyAndValue[];
  matchExpression?: MatchExpression | null;
  responseSubscriptionId?: number | null;
  responseMessageTypeName?: string | null;
  retryPolicyId?: number | null;
  notifications?: { channelId: number; onFailure: boolean; onBadResult: boolean; onSuccess: boolean; onQuiet: boolean }[] | null;
  [more: string]: unknown;
}

export interface Area<TRow, TDetail, TCreate = TDetail, TUpdate = TDetail, TQuery = ListQuery> {
  search(query?: TQuery, paging?: { page?: number; size?: number }): Promise<Page<TRow>>;
  get(id: number): Promise<TDetail | null>;
  create(body: TCreate): Promise<number>;
  update(id: number, body: TUpdate): Promise<void>;
  delete(id: number): Promise<void>;
}

export interface Partners extends Area<PartnerRow, Partner> {
  newKey(): Promise<string>;
}

export interface Subscriptions extends Area<Subscription & { id: number }, Subscription & { id?: number }, Subscription, Subscription, SubscriptionQuery> {
  activate(id: number): Promise<boolean>;
  deactivate(id: number): Promise<boolean>;
  pause(id: number): Promise<boolean>;
  resume(id: number): Promise<boolean>;
}

export interface InformationTypes {
  search(query?: ListQuery, paging?: { page?: number; size?: number }): Promise<Page<Record<string, unknown> & { id: number; name: string }>>;
  get(id: number): Promise<(Record<string, unknown> & { id: number; name: string }) | null>;
}

export interface ApiGateway {
  name: string;
  urlName: string;
  inactive?: boolean;
  partners?: { partnerId: number; subscriptionId: number; partnerName: string; subscriptionName: string }[];
  [more: string]: unknown;
}

export interface ApiGateways extends Area<ApiGateway & { id: number }, ApiGateway & { id: number }, ApiGateway> {
  attachPartner(id: number, attachment: { partnerId: number; subscriptionId?: number; newIntegration?: unknown }): Promise<void>;
  updatePartner(id: number, partnerId: number, subscriptionId: number): Promise<void>;
  detachPartner(id: number, partnerId: number): Promise<void>;
}

export interface BusGatewayRoute {
  subscriptionId?: number;
  newIntegration?: unknown;
  partnerId?: number | null;
  matchExpression?: MatchExpression | null;
}

export interface BusGateway {
  name: string;
  documentId: number;
  inactive?: boolean;
  routes?: (BusGatewayRoute & { id: number; subscriptionName: string; partnerName: string | null })[];
  [more: string]: unknown;
}

export interface BusGateways extends Area<BusGateway & { id: number }, BusGateway & { id: number }, BusGateway> {
  addRoute(id: number, route: BusGatewayRoute): Promise<number>;
  updateRoute(id: number, route: BusGatewayRoute & { routeId: number }): Promise<void>;
  removeRoute(id: number, routeId: number): Promise<void>;
}

export interface NotificationChannel {
  name: string;
  handlerId: string;
  handlerProperties: Record<string, string>;
  usedBy?: { kind: string; subscriptionId: number | null; retryPolicyId: number | null; name: string }[];
  [more: string]: unknown;
}

export interface NotificationChannels extends Area<NotificationChannel & { id: number }, NotificationChannel & { id: number }, NotificationChannel> {
  test(id: number): Promise<{ sent: boolean; error: string | null }>;
}

export type GatewayOutcome = "delivered" | "badResponse" | "failed" | "retrying" | "held" | "stillRunning";

export interface GatewayAccepted {
  exchangeId: string;
  /** The same Idempotency-Key was sent before, and this is that call's exchange. */
  replayed: boolean;
  /** The payload repeats one its information type drops as a duplicate: the exchange it repeats. */
  duplicateOf: string | null;
}

export interface GatewayAnswer {
  outcome: GatewayOutcome;
  status: number;
  exchangeId: string | null;
  /** The destination's response, for delivered and badResponse. */
  body: string | null;
  contentType: string | null;
  error: string | null;
  nextAttemptOn: string | null;
  replayed: boolean;
  duplicateOf: string | null;
}

/** Sends to API gateways as a partner, with one of its keys. */
export declare class BitweenGateway {
  constructor(options: { url: string; partnerKey: string; keyHeader?: string; fetch?: typeof fetch });
  send(urlName: string, payload: string | object, options?: { idempotencyKey?: string; contentType?: string }): Promise<GatewayAccepted>;
  sendAndWait(urlName: string, payload: string | object,
    options?: { waitSeconds?: number; idempotencyKey?: string; contentType?: string }): Promise<GatewayAnswer>;
}

export declare class BitweenClient {
  constructor(options: { url: string; token: string; fetch?: typeof fetch });
  readonly exchanges: Exchanges;
  readonly scheduledRetries: ScheduledRetries;
  readonly partners: Partners;
  readonly subscriptions: Subscriptions;
  readonly informationTypes: InformationTypes;
  readonly apiGateways: ApiGateways;
  readonly busGateways: BusGateways;
  readonly notificationChannels: NotificationChannels;
}

export declare class BitweenClientError extends Error {
  readonly status: number;
  /** Bitween's reasons by code, such as ALREADY_RETRIED, each with its messages. */
  readonly errors: Record<string, string[]>;
  /** The first code, or null. */
  readonly code: string | null;
}
