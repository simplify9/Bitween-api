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
  /**
   * Set while the account is locked out after repeated failed sign-ins. Orthogonal to
   * `status`: a lockout is automatic and expires on its own, where disabling is an
   * admin decision that does not. Null once it has passed.
   */
  lockedUntil: string | null;
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
  /**
   * The account still has the password it was created with. The backend grants such a token
   * nothing, so `permissions` is empty and every screen would refuse — the app sends them to
   * change it instead of leaving them staring at an empty shell.
   */
  mustChangePassword: boolean;
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
export interface SubscriptionSetupRef {
  id: number;
  name: string;
  type: SubscriptionType;
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
  /** What the exchange was, in the information type's own terms. The lead column. */
  promotedProperties?: Record<string, string | null> | null;
  /** Documents produced as the exchange moved through the pipeline, for drill-down previews. */
  documents?: ExchangeDocument[];
}

/**
 * Lightweight summary of every subscription, cached client-side so pages
 * can answer "who uses this property/value/policy?" without new requests.
 * Derived server-side by scanning adapter property values for tokens.
 */
export interface SubscriptionInfo {
  id: number;
  name: string;
  type: SubscriptionType;
  /**
   * The subscription's OWN partner, which only the legacy types carry. Partners
   * linked through a gateway attachment or a bus route are NOT here — the list
   * endpoint doesn't know about them. Use `usePartnerSubscriptions()` when you
   * need the full picture.
   */
  partnerIds: number[];
  informationTypeId: number;
  workGroupId: number | null;
  retryPolicyId: number | null;
  /**
   * Its delivery step. Null means nothing is delivered — and since a response is
   * whatever the delivery hands back, null here means the two response fields below
   * can never be reached, however they are set.
   */
  handlerId: string | null;
  /** The bus message its delivery response is published as, if any. */
  responseMessageTypeName: string | null;
  /** The subscription its delivery response is handed straight to, if any. */
  responseSubscriptionId: number | null;
  /**
   * Reference tokens found in its adapter properties. Both are matched
   * case-insensitively, as the backend resolver does — compare them with
   * `referencesPartnerProp`/`referencesGlobal` rather than `===`.
   */
  partnerPropKeys: string[];
  globals: { setId: string; keys: string[] }[];
}
/** What happened to one property in one change. */
export interface AuditChange {
  property: string;
  old: unknown;
  new: unknown;
}

/**
 * One recorded change. Replaces the old per-entity trail: the backend now records every
 * configuration entity through the change tracker, including deletions, which the
 * hand-written trails never captured.
 */
export interface TrailEntry {
  id: string;
  on: string;
  action: "Added" | "Modified" | "Deleted";
  by: string;
  /** Absent for system-attributed entries with no real team member behind them. */
  byUserId?: string;
  entityName: string;
  entityKey: string;
  /**
   * Groups everything one save changed. A single edit often touches several rows — a
   * gateway and its routes — and this is what shows them as the one change they were.
   */
  correlationId: string;
  changes: AuditChange[];
}

export interface AuditQuery {
  entityName?: string;
  entityKey?: string;
  userId?: string;
  correlationId?: string;
  from?: string;
  to?: string;
  offset: number;
  limit: number;
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
  /**
   * Which property names are secrets. Their values arrive as SECRET_SENTINEL and go back
   * unchanged to mean "keep what is stored"; only a value the operator retypes is ever sent.
   */
  secretProperties: string[];
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
  /**
   * When this type was taken out of use, or null while it is still in use. A retired type keeps
   * naming the exchanges already recorded against it and is no longer offered for new work.
   */
  retiredOn: string | null;
  /**
   * Seeded by the backend and referenced by machinery rather than by configuration —
   * the Aggregation Document. Can't be edited away, retired or deleted.
   */
  isSystem: boolean;
}
export interface InformationTypeRow extends InformationType {
  usedByCount: number;
}
export interface InformationTypeDetail extends InformationType {
  subscriptionSetups: SubscriptionSetupRef[];
  busGateways: { gatewayId: number; gatewayName: string }[];
  recentExchanges: ExchangeRef[];
}

export interface GlobalValuesSet {
  /** Caller-chosen slug; referenced as {{globals.<id>.<key>}}. */
  id: string;
  name: string;
  values: Record<string, string>;
  /** Which value names are secrets — same sentinel round trip as Partner.secretProperties. */
  secretProperties: string[];
  createdOn: string;
}
/** Alias the ported mapper code types its global-set props with. */
export type GlobalValuesSetRow = GlobalValuesSet;
export interface ValueSetUsage {
  subscriptionSetup: SubscriptionSetupRef;
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

/**
 * Whether a level of the alert hierarchy names its own destination or defers upward.
 *
 * Resolved most-specific-first per subscription and group: the pair's own override, then
 * the group, then the policy. A level that sends **replaces** the one above rather than
 * merging with it, so whichever level wins has to carry the handler and every property
 * it needs.
 */
export type RetryAlertMode = "Inherit" | "Send" | "Silent";

/** Which level of that hierarchy decided, so a wrong destination can be traced to its source. */
export type RetryAlertLevel = "SubscriptionGroup" | "Group" | "Policy";

/** A destination for budget-exhausted alerts, as configured at one level. */
export interface RetryAlertConfig {
  alertMode: RetryAlertMode;
  alertHandlerId: string | null;
  alertHandlerProperties: Record<string, string>;
}

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
  /** Where this group's budget-exhausted alert goes, for every subscription using the policy. */
  alertMode: RetryAlertMode;
  alertHandlerId: string | null;
  alertHandlerProperties: Record<string, string>;
}

export interface RetryPolicy {
  id: number;
  name: string;
  groups: RetryGroup[];
  createdOn: string;
  /** The policy-wide alert destination, inherited by every group that doesn't name its own. */
  alertHandlerId: string | null;
  alertHandlerProperties: Record<string, string>;
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
  subscriptions: SubscriptionSetupRef[];
}

/**
 * What became of a budget-exhausted alert.
 *
 * `claimedOn` is when the alert was raised — all the counter itself records. Whether it then
 * reached anyone is a separate fact that can fail, so the two are reported apart: a page showing
 * only the claim tells the reader someone was notified when nobody was.
 */
export interface RetryAlertOutcome {
  claimedOn: string;
  /** Null when the alert was claimed but no delivery attempt was ever recorded. */
  delivered: boolean | null;
  /** Why delivery failed, when it did. */
  error: string | null;
}

/**
 * The whole state of one subscription-and-group pair: how much of the group's budget that
 * subscription has spent, and where the pair's budget-exhausted alert would go.
 *
 * Budgets are counted per pair — a shared policy gives every subscription its own separate total
 * — so there is no such thing as "this policy's usage". Any single figure on a policy or a group
 * would be an aggregate matching nothing anyone can act on, which is why the pair is also what
 * resetting and overriding both address.
 */
export interface RetryUsageRow {
  subscriptionId: number;
  subscriptionName: string;
  groupId: string;
  groupName: string;
  used: number;
  total: number;
  /** Spent out: this subscription gets no further automatic retries from this group. */
  exhausted: boolean;
  /** Null when the pair has never failed — also how you know there is no counter to reset. */
  lastAttemptOn: string | null;
  /** Where the alert actually goes, or null when nothing sends for this pair. */
  resolvedHandlerId: string | null;
  resolvedHandlerProperties: Record<string, string>;
  resolvedFrom: RetryAlertLevel | null;
  /** Which level deliberately switched the alert off — a decision, as against an oversight. */
  silencedAt: RetryAlertLevel | null;
  /** This pair's own override; `Inherit` when it has none. */
  override: RetryAlertConfig;
  alert: RetryAlertOutcome | null;
}

/** One failure a group caught — what a usage row spent its budget on. */
export interface RetryAttempt {
  exchangeId: string;
  /** How deep the retry chain was, 0 being the original delivery. Null for older failures. */
  attemptNumber: number | null;
  failedOn: string;
  error: string;
  /** True while another attempt is still scheduled: the one thing here that is not history. */
  retryPending: boolean;
  /** Why no further attempt was scheduled, when the policy refused one. */
  blockedReason: string | null;
}

export interface RetryAttempts {
  /**
   * Every failure this group has caught for this subscription. Failures outlive the counter,
   * which is reset, so this is not the counter's value.
   */
  total: number;
  attempts: RetryAttempt[];
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
  /** Subscriptions this notifier watches; empty = it never fires. */
  subscriptionIds: number[];
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

// ——— Subscriptions (subscriptions) ———

/**
 * Backend Subscription.Type. Internal and ApiCall are legacy — shown and
 * editable, never created.
 */

/**
 * Which file of each collected exchange an aggregation's roll-up links to: what came
 * in, what the mapper produced, or what the destination handed back.
 */
export type AggregationTarget = "Input" | "Output" | "Response";
/**
 * The editable fields of a subscription being defined inline, while whatever points
 * at it is being made. Mirrors the studio's own draft — deliberately, so the canvas
 * can hand its draft straight to the client.
 */
export interface InlineSubscriptionDraft {
  name: string;
  enabled: boolean;
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
  /** Optional here: an inline draft has no connection to bind yet. */
  dataSourceId?: number | null;
  matchExpression: MatchGroup | null;
  schedules: Schedule[];
  responseSubscriptionId: number | null;
  responseMessageTypeName: string | null;
}

export type SubscriptionType =
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

export interface Subscription {
  id: number;
  name: string;
  type: SubscriptionType;
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
  /** Optional here: an inline draft has no connection to bind yet. */
  dataSourceId: number | null;
  /** Legacy Internal only: which documents this subscription picks up. */
  matchExpression: MatchGroup | null;
  /** Receiving (and Aggregation) only. */
  schedules: Schedule[];
  /** Feed the handler's response into another subscription. */
  responseSubscriptionId: number | null;
  responseMessageTypeName: string | null;
  /**
   * Aggregation only: whose exchanges get rolled up. Fixed at creation — the backend
   * property has a private setter and the configuration applier deliberately skips it,
   * so no update can repoint a live roll-up.
   */
  aggregationForId: number | null;
  /** Aggregation only. Meaningless for every other type, where it stays "Input". */
  aggregationTarget: AggregationTarget;
  // — health (read-only) —
  isRunning: boolean;
  /** Receiving (and Aggregation) only — when the schedule will next fire, not when it last did. */
  nextReceiveOn: string | null;
  consecutiveFailures: number;
  lastException: string | null;
  createdOn: string;
}

export interface SubscriptionRow {
  id: number;
  name: string;
  type: SubscriptionType;
  informationTypeId: number;
  informationTypeCode: string;
  partners: { id: number; name: string }[];
  enabled: boolean;
  paused: boolean;
  isRunning: boolean;
  consecutiveFailures: number;
  lastException: string | null;
  scheduleSummary?: string;
  /**
   * Receiving and Aggregation only — when the schedule will next fire, not when it last
   * did. The two types keep it in different columns on the backend (`ReceiveOn` vs
   * `AggregateOn`); this is whichever one applies.
   */
  nextReceiveOn: string | null;
  /** Aggregation only: whose exchanges it rolls up. */
  aggregationForId: number | null;
  /** Aggregation only: which file of each collected exchange the roll-up links to. */
  aggregationTarget: AggregationTarget;
  createdOn: string;
}

/**
 * One execution of a scheduled subscription, from the scheduler's own history.
 * Kept for `RetentionDays` (~30) — older runs are purged, not archived here.
 */
export interface SubscriptionRun {
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

export interface SubscriptionLastRun extends SubscriptionRun {
  subscriptionId: number;
  /** Finished runs in the recent window; in-progress runs count as neither pass nor fail. */
  recentTotal: number;
  recentSucceeded: number;
}

/** One poll of a Receiving subscription's own receive step — independent of the scheduler's
 * run history, which only knows whether the method threw (it never does; failures here are
 * caught and reported this way instead). */
export type ReceiveOutcome = "Failed" | "NoNewData" | "Received";

export interface ReceiveAttemptExchange {
  id: string;
  status: ExchangeStatus;
  promotedProperties: Record<string, string | null> | null;
}

export interface ReceiveAttemptRow {
  id: number;
  startedOn: string;
  finishedOn: string;
  outcome: ReceiveOutcome;
  errorMessage: string | null;
  exchanges: ReceiveAttemptExchange[];
}

/**
 * Whether a scheduled subscription will actually fire, straight from the scheduler.
 * Everything here can disagree with what the subscription's own record says, and
 * when it does the job is silently dead rather than visibly broken.
 */
export interface ScheduleHealth {
  subscriptionId: number;
  scheduleCount: number;
  /** Fewer than `scheduleCount` means a schedule exists that nothing will ever fire. */
  triggerCount: number;
  state: "Normal" | "Paused" | "Blocked" | "Error" | "Complete" | "Missing";
  /** The scheduler's own next fire time, computed independently of `nextReceiveOn`. */
  nextFireOn: string | null;
  /** Flagged as running with nothing executing — the concurrency guard is skipping every run. */
  stuck: boolean;
}

export interface SubscriptionDetail extends Subscription {
  informationTypeCode: string;
  informationTypeName: string;
  /** Where this subscription is plugged in (entry points). */
  apiGatewayAttachments: { gatewayId: number; gatewayName: string; urlName: string; partnerId: number; partnerName: string }[];
  busGatewayRoutes: { gatewayId: number; gatewayName: string; partnerId: number | null; partnerName: string | null }[];
  watchingNotifiers: { id: number; name: string }[];
  recentExchanges: ExchangeRef[];
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
  subscriptions: SubscriptionSetupRef[];
}

// ——— API gateways ———

export interface ApiGatewayAttachment {
  partnerId: number;
  partnerName: string;
  subscriptionId: number;
  subscriptionName: string;
}
export interface ApiGateway {
  id: number;
  name: string;
  urlName: string;
  /** Off but kept, with its attachments. Partners calling it get a 503. */
  inactive: boolean;
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
  subscriptionId: number;
  subscriptionName: string;
  partnerId: number | null;
  partnerName: string | null;
  /** null = route matches every message of the gateway's type. */
  matchExpression: MatchGroup | null;
}
export interface BusGateway {
  id: number;
  name: string;
  informationTypeId: number;
  /** Off but kept, with its routes. The message stops being offered to them. */
  inactive: boolean;
  createdOn: string;

  /**
   * Where the messages come from. Null is Bitween's own internal bus — the only behaviour that
   * existed before data sources, and still the default, so no gateway changes meaning. Set it and
   * this gateway is fed by a broker outside Bitween instead.
   */
  dataSourceId: number | null;
  dataSourceName: string | null;
  /** Health of that connection, so a broker that has gone away shows on the gateway itself. */
  dataSourceState: string | null;
  /** Which queue, topic or SQS URL on that data source feeds this gateway. */
  endpoint: string | null;
}

// ——— Data sources ———

/**
 * A connection to something outside Bitween — today a broker.
 *
 * It holds the connection and nothing else: what Bitween does with the messages belongs to the bus
 * gateway pointing at it, which is why one data source can feed many gateways the way one broker
 * connection serves many queues.
 */
export interface DataSource {
  id: number;
  name: string;
  /** The adapter that speaks this protocol, e.g. bitween.bus.rabbitmq. */
  adapterId: string;
  kind: string;
  inactive: boolean;
  /**
   * How long a message's deduplication key is remembered. Has to exceed the widest redelivery
   * window this broker can produce. Zero turns deduplication off.
   */
  deduplicationWindowDays: number;

  /** Soft ceiling in MB — crossing it recycles the adapter between messages. 0 = host default. */
  softMemoryLimitMb: number;
  /** Hard ceiling in MB — becomes the adapter's GC heap hard limit. 0 = host default. */
  hardMemoryLimitMb: number;

  /**
   * Sustained CPU ceiling as a share of the WHOLE node, not of one core. One pegged core on a
   * sixteen-core node is about 6%. 0 = host default.
   */
  cpuPercentLimit: number;
  /** Consecutive heartbeats above the ceiling before it trips. 0 = host default. */
  cpuLimitSamples: number;

  // Health, written back by the supervisor from the adapter's heartbeat.
  lastKnownState: string | null;
  lastHeartbeatOn: string | null;
  lastException: string | null;
  consecutiveFailures: number;
  /** Which node holds this connection, and at which fencing term. */
  ownedByNode: string | null;
}

export interface DataSourceRow extends DataSource {
  /** How many bus gateways read from it. Deleting is refused while any do. */
  gatewayCount: number;
}

export interface DataSourceDetail extends DataSourceRow {
  /** Connection settings. Secret values arrive as the sentinel, never in clear. */
  properties: Record<string, string>;
  /** Which of those names hold credentials. */
  secretProperties: string[];
}

/**
 * What the connection is doing right now, read from the adapter's heartbeat rather than from the
 * data source row — which only carries what the last reconcile wrote back.
 */
/**
 * One named piece of SQL a data source may run.
 *
 * A record rather than a field on the data source, so that writing a query and changing the
 * database credentials are different permissions — see the backend entity for why SQL cannot live
 * on the subscription instead.
 */
export interface DataSourceStatement {
  id: number;
  dataSourceId: number;
  name: string;
  sql: string;
  description: string | null;
  workGroupId: number | null;
  workGroupName: string | null;
  inactive: boolean;
  /**
   * Only for a statement a receiver polls with: which column carries the cursor, and which
   * identifies the row. They describe what this query returns, so they belong to the statement
   * rather than to each subscription reading it.
   */
  cursorColumn: string | null;
  keyColumn: string | null;
  /** How many subscriptions name it. Zero is the number that says it is safe to delete. */
  usageCount: number;
  createdOn: string;
  createdBy: string | null;
  modifiedOn: string | null;
  modifiedBy: string | null;
}

export interface DataSourceStatementUsage {
  statementId: number;
  name: string;
  usedBy: DataSourceStatementUsageEntry[];
}

export interface DataSourceStatementUsageEntry {
  subscriptionId: number;
  subscriptionName: string;
  /** Handler, Mapper or Receiver — which adapter slot names it. */
  role: string;
  /** query, execute or call. */
  operation: string;
  inactive: boolean;
}

export interface DataSourceTelemetry {
  /** False when this node is not the one holding the connection. Not a fault: it is exclusive. */
  runningHere: boolean;
  ownedByNode: string | null;

  connected: boolean;
  state: string | null;
  lastMessageOn: string | null;
  inFlight: number;
  lastError: string | null;

  /** Whatever the adapter reports — per-queue depth, counters, prefetch. Untyped by design. */
  details: Record<string, string>;

  // Host-observed: these keep working when the adapter is wedged, which is when they matter.
  processId: number | null;
  workingSetBytes: number;
  cpuPercent: number;
  threadCount: number;
  uptime: string;
  restartCount: number;
  missedHeartbeats: number;
  quarantined: boolean;
  lastHeartbeatOn: string | null;

  commands: string[];
}

/** What a read-only command relayed to the running adapter came back with. */
export interface DataSourceInspectResult {
  ran: boolean;
  command: string | null;
  /** The adapter's own JSON, as text — Bitween does not model any broker's topology. */
  result: string | null;
  error: string | null;
}

export interface DataSourceTestStage {
  name: string;
  succeeded: boolean;
  detail: string | null;
}

export interface DataSourceTestResult {
  succeeded: boolean;
  error: string | null;
  stages: DataSourceTestStage[];
}

/** Secret values come back as this. Saving it again keeps whatever is stored. */
export const SECRET_SENTINEL = "__private__";
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
  subscriptionId: number | null;
  subscriptionName: string | null;
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
  /**
   * True when a retry has already been made from this exchange. An exchange gets at most one
   * retry, so this is also what makes it un-retryable — the newest attempt in the chain is the
   * one to act on.
   */
  hasRetry: boolean;
  /** Set when this exchange was rolled up into an aggregation exchange. */
  aggregationXchangeId: string | null;
  /** A pending auto-retry, when the retry policy scheduled one. */
  scheduledRetryOn: string | null;
  exception: string | null;
  promotedProperties: Record<string, string | null> | null;
  /** True when the subscription has no mapper — the Mapped stage is skipped. */
  mapperSkipped: boolean;
  files: {
    input: ExchangeFileRef | null;
    mapped: ExchangeFileRef | null;
    handled: ExchangeFileRef | null;
  };
}

/** One attempt in a retry chain. */
export interface RetryTreeNode {
  id: string;
  /** null on the original attempt. */
  retryFor: string | null;
  /** What the payload promoted — how an attempt is named, the same as in the exchange list. */
  promotedProperties: Record<string, string | null> | null;
  startedOn: string;
  finishedOn: string | null;
  status: ExchangeStatus;
  exception: string | null;
  /** True when a person asked for this attempt rather than a retry policy. */
  manualRetry: boolean;
  scheduledRetryOn: string | null;
  retryBlockedReason: string | null;
}

/** Every attempt related to one exchange, oldest first. */
export interface RetryTree {
  rootId: string;
  attempts: RetryTreeNode[];
  /** True when the chain is longer than the server would walk. */
  truncated: boolean;
}

/**
 * Which exchanges a bulk retry is about: the rows someone ticked, or a whole filter's worth
 * minus the ones they unticked.
 */
export type BulkRetrySelection =
  | { ids: string[] }
  /** Everything the list's current filters match, minus the rows unticked after selecting all. */
  | { matching: ExchangeQuery; excludeIds: string[] };

/** A selection that had already been retried, and the later attempt standing in for it. */
export interface RetrySubstitution {
  selectedId: string;
  retryId: string;
}

export interface RetrySkip {
  /** The exchange the skip is about — the one selected, or the attempt that stood in for it. */
  id: string;
  reason: string;
}

/** What a bulk retry will do, or (returned by the retry itself) what it did. */
export interface BulkRetryPlan {
  selected: number;
  willRetry: number;
  limit: number;
  /** True when the selection is past `limit`; nothing else is filled in. */
  overLimit: boolean;
  substituted: RetrySubstitution[];
  skipped: RetrySkip[];
  /**
   * Promoted properties for every exchange the plan names, keyed by id, so they can be named the
   * way the exchange list names them. Absent for exchanges that promote nothing.
   */
  properties: Record<string, Record<string, string | null> | undefined>;
}

export interface ExchangeQuery {
  status?: ExchangeStatus;
  subscriptionId?: number;
  partnerId?: number;
  informationTypeId?: number;
  /** Comma/pipe/newline separated; matches id, retryFor OR aggregationXchangeId. */
  ids?: string;
  /**
   * The exchanges one run of a scheduled job created. The server reads the run's own list of
   * them, so this says the same thing as spelling every id into `ids` — at any size, and
   * without putting them all in the URL.
   */
  receiveAttemptId?: number;
  correlationId?: string;
  /**
   * Only the newest attempt of each retry chain. A chain is one piece of work however many
   * attempts it took, so this turns a list of failures into a list of things still to deal with.
   */
  latest?: boolean;
  /** Substring match against promoted property keys and values. */
  property?: string;
  /**
   * Narrows `property` to one promoted key. Set on its own it asks "has this key at
   * all", which is worth being able to ask.
   */
  propertyKey?: string;
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
  subscriptionId: number | null;
  subscriptionName: string | null;
  informationTypeId: number;
  informationTypeCode: string;
  exception: string | null;
  /** When the failed exchange originally started. */
  startedOn: string;
  /** What the exchange carries — how a pending retry identifies itself in a list. */
  promotedProperties: Record<string, string | null> | null;
  /**
   * The shared retry policy the subscription currently points at. Null when the
   * policy is defined inline on the subscription instead, so the subscription — not
   * the Retry policies list — is where to go and look.
   */
  retryPolicyId: number | null;
  retryPolicyName: string | null;
}

export interface ScheduledRetryQuery {
  subscriptionId?: number;
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

/**
 * What a queue is for. Resolved by `Ops/LaneResolver`, which sits next to
 * `WorkGroup.GetBusMessageName()` — the formula that builds the name in the
 * first place. The wording for each lane is this app's, in `QueueHealthPage`.
 */
export type QueueLane = "FrontDoor" | "Work" | "Notifications" | "Legacy" | "Control";

export interface ConsumerHealth {
  name: string;
  messageName: string;
  queueName: string;
  lane: QueueLane;
  /** The name of the thing this lane belongs to; the raw message name if it no longer resolves. */
  title: string;
  /** Set when the lane belongs to a work group, for drill-down. */
  workGroupId: number | null;
  /** Set on a front door, for drill-down to the information type it listens for. */
  informationTypeId: number | null;
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
  /** The lane's operator-facing name, resolved from the consumer rows. */
  title: string;
  queueName: string;
  retryBacklog: number;
  incomingRate: number;
  ackRate: number;
  severity: QueueSeverity;
}

export interface DeadLetterRow {
  consumerName: string;
  title: string;
  queueName: string;
  count: number;
  lastExceptionType: string | null;
  lastExceptionMessage: string | null;
  lastFailedAt: string | null;
}

/**
 * A queue RabbitMQ has that nothing here reads. Deleting or renaming a work group
 * leaves its queues behind, and every other view on this page is built from what the
 * running process declares — so these are invisible everywhere else.
 */
export interface UnattendedQueue {
  queueName: string;
  messages: number;
  retryMessages: number;
  deadMessages: number;
  /** Main plus whichever of its retry/dead queues still exist. */
  queues: number;
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
  unattended: UnattendedQueue[];
  alerts: QueueAlert[];
}

// ——— Dashboard ———

/** One chain that has been retried repeatedly and is still failing. */
export interface FailingChain {
  /** The newest attempt — where this chain got to, and the one a retry continues from. */
  id: string;
  /** How many attempts it has taken, counting the original as one. */
  attempts: number;
  subscriptionId: number | null;
  subscriptionName: string | null;
  informationTypeCode: string | null;
  startedOn: string;
  exception: string | null;
}

export interface DashboardData {
  /** Worst first. Empty when nothing has been retried more than once and left failing. */
  failingChains: FailingChain[];
  /**
   * Retries started in the last 7 days: how many have finished, and how many of those worked.
   * Says whether retrying achieves anything here, which is what makes a long chain either bad
   * luck or a configuration problem.
   */
  retriesLast7Days: { finished: number; succeeded: number };
  /**
   * Failures that are the newest attempt of their chain. Counts problems rather than attempts: a
   * chain retried nine times is one of these, not nine. Subject to the search's count cap, so a
   * value above it means "at least this many".
   */
  failuresToActOn: number;
  today: { total: number; failed: number; processing: number };
  yesterdayTotal: number;
  /** Percentage 0–100 across the last 7 days of finished exchanges. */
  successRate7d: number;
  pendingRetries: number;
  /** Null when the RabbitMQ management API is unavailable — never counts as zero. */
  queueAlerts: number | null;
  /** Last 14 days, oldest first; today is the final entry. */
  trafficByDay: { date: string; success: number; failed: number }[];
  /** Top subscriptions by 7-day traffic, busiest first. */
  busiest: { id: number; name: string; count: number; failed: number }[];
  latestFailures: {
    id: string;
    status: ExchangeStatus;
    subscriptionId: number | null;
    subscriptionName: string | null;
    informationTypeCode: string;
    on: string;
    exception: string | null;
  }[];
  attention: {
    failingSubscriptions: { id: number; name: string; consecutiveFailures: number }[];
    pausedSubscriptions: { id: number; name: string }[];
  };
}

/**
 * One connection setting, as the adapter itself declares it. The UI keeps no list of its own —
 * see DataSourceProviderCatalog on the server for why.
 */
export interface DataSourceProviderSetting {
  name: string;
  /** string, number or boolean — enough to pick an input, nothing more. */
  type: "string" | "number" | "boolean";
  hint: string | null;
  /** What a new data source starts with. Null means start it empty. */
  default: string | null;
  /** When present, the only legal values: rendered as a menu instead of a text box. */
  allowedValues: string[] | null;
  secret: boolean;
  required: boolean;
}

export interface DataSourceProvider {
  adapterId: string;
  label: string;
  /**
   * What it connects to: Broker, Relational, Document, ObjectStore or Http. A bus gateway can
   * only read from a Broker — a provider that holds a database session is a data source too, but
   * it has no queue to consume.
   */
  kind: string;
  description: string | null;
  settings: DataSourceProviderSetting[];
}
