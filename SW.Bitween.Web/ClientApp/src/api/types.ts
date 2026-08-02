/** Shared API shapes. These model the contract the real backend will need to satisfy. */

export type ActionId = "view" | "create" | "edit" | "delete" | "operate";

/** e.g. "subscriptions.edit" */
export type PermissionKey = string;

export interface PermissionAction {
  id: ActionId;
  /** What this specific grant allows, in end-user words. */
  description: string;
}

export interface PermissionArea {
  id: string;
  label: string;
  group: string;
  description: string;
  actions: PermissionAction[];
}

export interface Role {
  id: string;
  name: string;
  description: string;
  permissions: PermissionKey[];
  /** Built-in roles (Administrator) can't be edited or deleted. */
  isSystem: boolean;
  createdOn: string;
  memberCount: number;
}

export type UserStatus = "active" | "disabled";

export interface User {
  id: string;
  displayName: string;
  email: string;
  roleIds: string[];
  status: UserStatus;
  /** Whether a Microsoft account is linked for SSO. */
  microsoftLinked: boolean;
  createdOn: string;
  lastActiveOn?: string;
}

/** Just enough to name a role. Full definitions need the roles.view permission. */
export interface RoleSummary {
  id: string;
  name: string;
}

export interface Session {
  user: User;
  roles: RoleSummary[];
  /** The union of every permission the user's roles grant — resolved by the backend. */
  permissions: PermissionKey[];
}

export interface ApiError {
  code: string;
  message: string;
}

export class ApiRequestError extends Error {
  code: string;
  constructor(code: string, message: string) {
    super(message);
    this.code = code;
  }
}

/**
 * Thrown by any ApiClient method whose domain hasn't been wired to the real
 * backend yet. Screens surface this as an honest "Not connected yet" state —
 * never fake data. Batches remove these as they land.
 */
export class NotWiredError extends Error {
  code = "NOT_WIRED";
  constructor(method: string) {
    super(`"${method}" isn't connected to the backend yet.`);
  }
}

// ——— Configuration entities (sub-phase 2) ———

/** Lightweight references for "used by" panels. */
export interface IntegrationSetupRef {
  id: number;
  name: string;
  type: IntegrationType;
}
export interface ApiGatewayAttachmentRef {
  gatewayId: number;
  gatewayName: string;
  urlName: string;
}
export interface BusGatewayRouteRef {
  gatewayId: number;
  gatewayName: string;
  matchExpression: string;
}
/** The pipeline stages that can each produce a document for an exchange. */
export type ExchangeDocStage = "Input" | "Mapped" | "Handled";
export interface ExchangeDocument {
  stage: ExchangeDocStage;
  content: string;
}
export interface ExchangeRef {
  id: string;
  partnerName?: string;
  informationTypeCode: string;
  status: ExchangeStatus;
  on: string;
  /** Documents produced as the exchange moved through the pipeline, for drill-down previews. */
  documents?: ExchangeDocument[];
}

/**
 * Lightweight summary of every integration, cached client-side so pages
 * can answer "who uses this property/value/policy?" without new requests.
 * Derived server-side by scanning adapter property values for tokens.
 */
export interface IntegrationInfo {
  id: number;
  name: string;
  type: IntegrationType;
  /**
   * The integration's OWN partner, which only the legacy types carry. Partners
   * linked through a gateway attachment or a bus route are NOT here — the list
   * endpoint doesn't know about them. Use `usePartnerIntegrations()` when you
   * need the full picture.
   */
  partnerIds: number[];
  informationTypeId: number;
  workGroupId: number | null;
  retryPolicyId: number | null;
  /**
   * Reference tokens found in its adapter properties. Both are matched
   * case-insensitively, as the backend resolver does — compare them with
   * `referencesPartnerProp`/`referencesGlobal` rather than `===`.
   */
  partnerPropKeys: string[];
  globals: { setId: string; keys: string[] }[];
}
export interface TrailEntry {
  on: string;
  action: "Created" | "Updated";
  by: string;
  /** Absent for system-attributed entries with no real team member behind them. */
  byUserId?: string;
}

export interface ApiCredentialRef {
  name: string;
  /** Only the first characters — full keys are shown once, at creation. */
  keyPrefix: string;
  createdOn: string;
}

export interface Partner {
  id: number;
  name: string;
  /** Referenced in adapter configs as {{partner.KEY}}. */
  adapterProperties: Record<string, string>;
  /** The built-in SYSTEM partner can't be renamed or deleted. */
  isSystem: boolean;
  createdOn: string;
}
export interface PartnerRow extends Partner {
  credentialCount: number;
  usedByCount: number;
  /** Property names only — the list endpoint never sends values, which may be secrets. */
  propertyKeys: string[];
}
export interface PartnerDetail extends Partner {
  apiCredentials: ApiCredentialRef[];
  apiGateways: ApiGatewayAttachmentRef[];
  busGatewayRoutes: BusGatewayRouteRef[];
  recentExchanges: ExchangeRef[];
}

export type InformationTypeFormat = "Json" | "Xml";

export interface InformationType {
  id: number;
  /** Short unique identity shown across the system, e.g. PURCHASE_ORDER. Optional. */
  code?: string;
  name: string;
  format: InformationTypeFormat;
  busEnabled: boolean;
  busMessageTypeName?: string;
  /** How long an identical incoming payload counts as a duplicate. 0 = off. */
  duplicateIntervalMinutes: number;
  disregardsUnfilteredMessages: boolean;
  /** Friendly name → JSONPath/XPath, matched by routes and filters. */
  promotedProperties: { key: string; path: string }[];
  createdOn: string;
}
export interface InformationTypeRow extends InformationType {
  usedByCount: number;
}
export interface InformationTypeDetail extends InformationType {
  integrationSetups: IntegrationSetupRef[];
  busGateways: { gatewayId: number; gatewayName: string }[];
  trail: TrailEntry[];
  recentExchanges: ExchangeRef[];
}

export interface GlobalValuesSet {
  /** Caller-chosen slug; referenced as {{globals.<id>.<key>}}. */
  id: string;
  name: string;
  values: Record<string, string>;
  createdOn: string;
}
/** Alias the ported mapper code types its global-set props with. */
export type GlobalValuesSetRow = GlobalValuesSet;
export interface ValueSetUsage {
  integrationSetup: IntegrationSetupRef;
  keys: string[];
}
export interface GlobalValuesSetDetail extends GlobalValuesSet {
  usedBy: ValueSetUsage[];
}

// ——— Retry policies ———

export type RetryResultType = "Error" | "BadResult";

export type RetryMatcher =
  | { type: "contains"; value: string; caseSensitive: boolean }
  | { type: "regex"; pattern: string; flags: string }
  | { type: "exceptionType"; value: string; includeInner: boolean }
  | { type: "jsonPath"; path: string; op: "Eq" | "Neq" | "Contains" | "Exists" | "NotExists"; value?: string };

export type RetryDelay =
  | { type: "fixed"; delaySeconds: number }
  | { type: "linear"; initialSeconds: number; incrementSeconds: number }
  | { type: "exponential"; initialSeconds: number; multiplier: number; maxSeconds: number };

export interface RetryGroup {
  id: string;
  name: string;
  /** Lower runs first; the first matching group decides. */
  priority: number;
  enabled: boolean;
  appliesTo: RetryResultType[];
  /** OR logic; empty = any failure of the applicable kind. */
  matchers: RetryMatcher[];
  action: "Allow" | "Block";
  budget?: { maxAttemptsPerError: number; maxAttemptsTotal: number; delay: RetryDelay };
  notes?: string;
}

export interface RetryPolicy {
  id: number;
  name: string;
  groups: RetryGroup[];
  createdOn: string;
}
export interface RetryPolicyListRow {
  id: number;
  name: string;
  /** The list only counts groups; the full list is in the detail response. */
  groupCount: number;
  createdOn: string;
  usedByCount: number;
}
export interface RetryPolicyDetail extends RetryPolicy {
  integrations: IntegrationSetupRef[];
}

export interface RetryTestAttempt {
  attempt: number;
  shouldRetry: boolean;
  delaySeconds?: number;
  matchedGroup?: string;
  reason: string;
}

// ——— Notifiers ———

export interface Notifier {
  id: number;
  name: string;
  /** Off pauses the notifier without losing its setup. */
  enabled: boolean;
  /** Which exchange outcomes trigger a notification. */
  onFailed: boolean;
  onBadResult: boolean;
  onSuccess: boolean;
  channelId: string;
  channelProperties: Record<string, string>;
  /** Integrations this notifier watches; empty = it never fires. */
  integrationIds: number[];
  createdOn: string;
}

/** One delivery attempt from the notification history. */
export interface NotificationEntry {
  xchangeId: string;
  success: boolean;
  exception?: string;
  on: string;
}

export interface NotifierDetail extends Notifier {
  recentNotifications: NotificationEntry[];
}

// ——— Integrations (subscriptions) ———

/**
 * Backend Subscription.Type. Aggregation exists in data but is deferred in
 * this UI; Internal and ApiCall are legacy — shown and editable, never created.
 */
export type IntegrationType =
  | "Receiving"
  | "GatewayApiCall"
  | "BusGateway"
  | "Internal"
  | "ApiCall"
  | "Aggregation";

export type AdapterKind = "receiver" | "handler" | "mapper" | "validator";

/** One configurable property of an adapter, from its startup-value metadata. */
export interface AdapterProp {
  key: string;
  optional: boolean;
  default?: string;
  /** Secret values are write-only: masked after save, replaced not edited. */
  secret: boolean;
  description?: string;
}

export interface AdapterInfo {
  id: string;
  kind: AdapterKind;
  /** Friendly display name, e.g. "HTTP endpoint" for NativeHttpHandler. */
  label: string;
  /** Native adapters run in-process; others are deployed packages with versions. */
  native: boolean;
  versions: string[];
  props: AdapterProp[];
}

/**
 * Message filter over an information type's promoted properties.
 * Groups are n-ary here (friendlier to edit); the backend's binary
 * and/or tree converts losslessly both ways.
 */
export type MatchCondition = {
  op: "oneOf" | "notOneOf";
  path: string;
  values: string[];
};
export type MatchGroup = {
  op: "and" | "or";
  children: MatchNode[];
};
export type MatchNode = MatchGroup | MatchCondition;

export type Recurrence = "Hourly" | "Daily" | "Weekly" | "Monthly";

export interface Schedule {
  recurrence: Recurrence;
  /** Weekly: weekday 0–6 (Sun–Sat); Monthly: day of month 1–27; otherwise 0. */
  days: number;
  hours: number;
  minutes: number;
  /** Count the offset from the end of the period instead of the start. */
  backwards: boolean;
}

export interface Integration {
  id: number;
  name: string;
  type: IntegrationType;
  informationTypeId: number;
  /** Direct partner — legacy Internal/ApiCall (and Aggregation) only. */
  partnerId: number | null;
  /** Inverse of backend Inactive. Disabled = not scheduled, not matched. */
  enabled: boolean;
  /** Paused still accepts work but holds it for later release. */
  pausedOn: string | null;
  workGroupId: number | null;
  retryPolicyId: number | null;
  receiverId: string | null;
  receiverProperties: Record<string, string>;
  validatorId: string | null;
  validatorProperties: Record<string, string>;
  mapperId: string | null;
  mapperProperties: Record<string, string>;
  handlerId: string | null;
  handlerProperties: Record<string, string>;
  /** Legacy Internal only: which documents this integration picks up. */
  matchExpression: MatchGroup | null;
  /** Receiving (and Aggregation) only. */
  schedules: Schedule[];
  /** Feed the handler's response into another integration. */
  responseIntegrationId: number | null;
  responseMessageTypeName: string | null;
  aggregationForId: number | null;
  // — health (read-only) —
  isRunning: boolean;
  /** Receiving (and Aggregation) only — when the schedule will next fire, not when it last did. */
  nextReceiveOn: string | null;
  consecutiveFailures: number;
  lastException: string | null;
  createdOn: string;
}

export interface IntegrationRow {
  id: number;
  name: string;
  type: IntegrationType;
  informationTypeId: number;
  informationTypeCode: string;
  partners: { id: number; name: string }[];
  enabled: boolean;
  paused: boolean;
  isRunning: boolean;
  consecutiveFailures: number;
  lastException: string | null;
  scheduleSummary?: string;
  /** Receiving (and Aggregation) only — when the schedule will next fire, not when it last did. */
  nextReceiveOn: string | null;
  createdOn: string;
}

/**
 * One execution of a scheduled integration, from the scheduler's own history.
 * Kept for `RetentionDays` (~30) — older runs are purged, not archived here.
 */
export interface IntegrationRun {
  startedOn: string;
  endedOn: string | null;
  durationMs: number | null;
  /** Null while the run is still in progress. */
  success: boolean | null;
  error: string | null;
  node: string;
  /** Someone pressed Receive now rather than waiting for the schedule. */
  manual: boolean;
}

export interface IntegrationLastRun extends IntegrationRun {
  integrationId: number;
}

/**
 * Whether a scheduled integration will actually fire, straight from the scheduler.
 * Everything here can disagree with what the integration's own record says, and
 * when it does the job is silently dead rather than visibly broken.
 */
export interface ScheduleHealth {
  integrationId: number;
  scheduleCount: number;
  /** Fewer than `scheduleCount` means a schedule exists that nothing will ever fire. */
  triggerCount: number;
  state: "Normal" | "Paused" | "Blocked" | "Error" | "Complete" | "Missing";
  /** The scheduler's own next fire time, computed independently of `nextReceiveOn`. */
  nextFireOn: string | null;
  /** Flagged as running with nothing executing — the concurrency guard is skipping every run. */
  stuck: boolean;
}

export interface IntegrationDetail extends Integration {
  informationTypeCode: string;
  informationTypeName: string;
  /** Where this integration is plugged in (entry points). */
  apiGatewayAttachments: { gatewayId: number; gatewayName: string; urlName: string; partnerId: number; partnerName: string }[];
  busGatewayRoutes: { gatewayId: number; gatewayName: string; partnerId: number | null; partnerName: string | null }[];
  watchingNotifiers: { id: number; name: string }[];
  recentExchanges: ExchangeRef[];
  trail: TrailEntry[];
}

export interface WorkGroupOptions {
  rabbitMqOptions: {
    consumerSettings: {
      prefetch: number;
      priority: number;
    };
  };
}

export interface WorkGroup {
  id: number;
  name: string;
  /** Queue name suffix; combined with the id to form the real queue name. */
  busMessageName: string;
  options: WorkGroupOptions;
  createdOn: string;
}
export interface WorkGroupRow extends WorkGroup {
  usedByCount: number;
  /** Best-effort live count from the RabbitMQ management API. */
  consumerCount: number;
}
export interface WorkGroupDetail extends WorkGroup {
  integrations: IntegrationSetupRef[];
}

// ——— API gateways ———

export interface ApiGatewayAttachment {
  partnerId: number;
  partnerName: string;
  integrationId: number;
  integrationName: string;
}
export interface ApiGateway {
  id: number;
  name: string;
  urlName: string;
  createdOn: string;
}
export interface ApiGatewayRow extends ApiGateway {
  partnerCount: number;
  attachments: ApiGatewayAttachment[];
}
export interface ApiGatewayDetail extends ApiGateway {
  attachments: ApiGatewayAttachment[];
}

// ——— Bus gateways ———

export interface BusGatewayRoute {
  id: number;
  integrationId: number;
  integrationName: string;
  partnerId: number | null;
  partnerName: string | null;
  /** null = route matches every message of the gateway's type. */
  matchExpression: MatchGroup | null;
}
export interface BusGateway {
  id: number;
  name: string;
  informationTypeId: number;
  createdOn: string;
}
export interface BusGatewayRow extends BusGateway {
  informationTypeCode: string;
  routeCount: number;
  routes: BusGatewayRoute[];
}
export interface BusGatewayDetail extends BusGateway {
  informationTypeCode: string;
  informationTypeName: string;
  routes: BusGatewayRoute[];
}

// ——— Settings ———

export type SettingValueKind = "string" | "number" | "boolean" | "color";

/**
 * How a row behaves:
 * - `editable` — stored in the database, changeable here, takes effect immediately.
 * - `readonly` — an environment value, shown so you can see what this instance runs
 *   on. Read once at startup, so there's nothing to change here.
 * - `presence` — an environment value whose content stays on the server; only
 *   whether it's set is reported.
 */
export type SettingAccess = "editable" | "readonly" | "presence";

/**
 * One setting on the settings page. Only settings that take effect immediately are
 * editable, so there is no "restart required" state to represent — anything read
 * once at startup appears as `readonly` or `presence` instead.
 */
export interface SettingRow {
  /** Stable key; mirrors the config path (e.g. "Bitween.RebexLicenseKey"). */
  key: string;
  /** Grouping label owned by the backend catalog; the page derives its pills from these. */
  section: string;
  label: string;
  description: string;
  kind: SettingValueKind;
  /** The product default a reset returns to. Stored as a string per `kind`; empty for secrets. */
  defaultValue: string;
  /** The stored value, or null for a secret — those never leave the server. */
  value: string | null;
  secret: boolean;
  /** The stored value differs from the product default, so a reset would do something. */
  overridden: boolean;
  /** Whether the effective value is non-empty — the only way a secret reveals it is set. */
  hasValue: boolean;
  /**
   * Whether this row can be written. False for every environment value, and for a secret on an
   * instance with no encryption key configured — there's nowhere safe to store that one.
   */
  editable: boolean;
  access: SettingAccess;
}

// ——— Exchanges ———

/**
 * The four observable outcomes of an exchange. "badResponse" = the handler
 * delivered but the receiving system answered with a business-level error.
 */
export type ExchangeStatus = "processing" | "success" | "badResponse" | "failed";

export interface ExchangeFileRef {
  name: string;
  /** Bytes. */
  size: number;
  /** Storage key to fetch this stage's content through `getExchangeDocument`; null if no file exists. */
  key: string | null;
}

/** One exchange as the Exchanges page sees it — names pre-resolved for display. */
export interface ExchangeRow {
  id: string;
  status: ExchangeStatus;
  integrationId: number | null;
  integrationName: string | null;
  informationTypeId: number;
  informationTypeCode: string;
  partnerId: number | null;
  partnerName: string | null;
  startedOn: string;
  /** null while still processing. */
  finishedOn: string | null;
  correlationId: string | null;
  /** Set when this exchange is a retry of another one. */
  retryFor: string | null;
  /** Set when this exchange was rolled up into an aggregation exchange. */
  aggregationXchangeId: string | null;
  /** A pending auto-retry, when the retry policy scheduled one. */
  scheduledRetryOn: string | null;
  exception: string | null;
  promotedProperties: Record<string, string> | null;
  /** True when the integration has no mapper — the Mapped stage is skipped. */
  mapperSkipped: boolean;
  files: {
    input: ExchangeFileRef | null;
    mapped: ExchangeFileRef | null;
    handled: ExchangeFileRef | null;
  };
}

export interface ExchangeQuery {
  status?: ExchangeStatus;
  integrationId?: number;
  partnerId?: number;
  informationTypeId?: number;
  /** Comma/pipe/newline separated; matches id, retryFor OR aggregationXchangeId. */
  ids?: string;
  correlationId?: string;
  /** Substring match against promoted property keys and values. */
  property?: string;
  from?: string;
  to?: string;
  offset: number;
  limit: number;
}

export interface Paged<T> {
  result: T[];
  total: number;
}

// ——— Scheduled retries ———

/** A failed exchange whose retry policy scheduled an automatic retry. */
export interface ScheduledRetryRow {
  /** The exchange the retry will re-run. */
  id: string;
  /** When the retry job will pick it up. */
  on: string;
  integrationId: number | null;
  integrationName: string | null;
  informationTypeId: number;
  informationTypeCode: string;
  exception: string | null;
  /** When the failed exchange originally started. */
  startedOn: string;
  /** What the exchange carries — how a pending retry identifies itself in a list. */
  promotedProperties: Record<string, string> | null;
  /**
   * The shared retry policy the integration currently points at. Null when the
   * policy is defined inline on the integration instead, so the integration — not
   * the Retry policies list — is where to go and look.
   */
  retryPolicyId: number | null;
  retryPolicyName: string | null;
}

export interface ScheduledRetryQuery {
  integrationId?: number;
  informationTypeId?: number;
  /** Substring match against the exception text. */
  exception?: string;
  from?: string;
  to?: string;
  offset: number;
  limit: number;
}

// ——— Queue health (Ops) ———

export type QueueSeverity = "healthy" | "warning" | "critical";

export interface QueueHealthSummary {
  totalConsumers: number;
  unhealthyConsumers: number;
  disconnectedConsumers: number;
  totalQueueDepth: number;
  totalRetryBacklog: number;
  totalDeadLetterBacklog: number;
  /** Messages per second, across all queues. */
  totalIncomingRate: number;
  totalAckRate: number;
  lastUpdated: string;
}

export interface ConsumerHealth {
  name: string;
  messageName: string;
  queueName: string;
  /** Set when the consumer belongs to a work group, for drill-down. */
  workGroupId: number | null;
  totalNodes: number;
  processingCount: number;
  queueCount: number;
  retryCount: number;
  failedCount: number;
  priority: number;
  prefetch: number;
  incomingRate: number;
  ackRate: number;
  isBackpressured: boolean;
  health: QueueSeverity;
}

export interface RetryBacklogRow {
  consumerName: string;
  queueName: string;
  retryBacklog: number;
  incomingRate: number;
  ackRate: number;
  severity: QueueSeverity;
}

export interface DeadLetterRow {
  consumerName: string;
  queueName: string;
  count: number;
  lastExceptionType: string | null;
  lastExceptionMessage: string | null;
  lastFailedAt: string | null;
}

export interface QueueAlert {
  severity: "warning" | "critical";
  title: string;
  detail: string;
  queueName: string;
  on: string;
}

/** One poll = one snapshot; everything the Queue health page shows. */
export interface QueueHealthSnapshot {
  summary: QueueHealthSummary;
  consumers: ConsumerHealth[];
  retryBacklog: RetryBacklogRow[];
  deadLetters: DeadLetterRow[];
  alerts: QueueAlert[];
}

// ——— Dashboard ———

export interface DashboardData {
  today: { total: number; failed: number; processing: number };
  yesterdayTotal: number;
  /** Percentage 0–100 across the last 7 days of finished exchanges. */
  successRate7d: number;
  pendingRetries: number;
  queueAlerts: number;
  /** Last 14 days, oldest first; today is the final entry. */
  trafficByDay: { date: string; success: number; failed: number }[];
  /** Top integrations by 7-day traffic, busiest first. */
  busiest: { id: number; name: string; count: number; failed: number }[];
  latestFailures: {
    id: string;
    status: ExchangeStatus;
    integrationId: number | null;
    integrationName: string | null;
    informationTypeCode: string;
    on: string;
    exception: string | null;
  }[];
  attention: {
    failingIntegrations: { id: number; name: string; consecutiveFailures: number }[];
    pausedIntegrations: { id: number; name: string }[];
  };
}
