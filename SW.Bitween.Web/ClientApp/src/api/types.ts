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
  /** False for a member invited and yet to choose a password. */
  hasPassword: boolean;
  /** Their latest invite while they have no password, expired or not; null otherwise. */
  invite: MemberInvite | null;
}

export interface MemberInvite {
  expiresOn: string;
  /** When it was emailed; null when the link was handed to whoever invited instead. */
  emailedOn: string | null;
  /** Why the email couldn't be sent, when it couldn't. */
  emailError: string | null;
}

/** What became of an invite. `link` only when it wasn't emailed, to pass on by hand - shown once. */
export interface InviteResult {
  emailed: boolean;
  emailError: string | null;
  expiresOn: string | null;
  link: string | null;
}

/** Who an invite or reset link is for. */
export interface PasswordLinkInfo {
  purpose: "Invite" | "PasswordReset";
  name: string;
  email: string;
  expiresOn: string;
}

export type ServiceTokenStatus = "active" | "expired" | "revoked";

/** A service account's token as Bitween keeps it: never the value, only its first characters. */
export interface ServiceToken {
  id: number;
  name: string;
  /** "bwt_3fK9aB2x": enough to tell tokens apart. */
  shown: string;
  createdOn: string;
  createdBy: string | null;
  expiresOn: string;
  revokedOn: string | null;
  lastUsedOn: string | null;
  lastUsedFrom: string | null;
  status: ServiceTokenStatus;
}

/** The one answer that carries a token's value: the one that issued it. */
export interface IssuedServiceToken extends ServiceToken {
  token: string;
}

/** An account for a program: signs in with tokens, never with a password or into the UI. */
export interface ServiceAccount {
  id: number;
  name: string;
  disabled: boolean;
  createdOn: string;
  lastSignInOn: string | null;
  roles: { id: number; name: string }[];
  tokens: ServiceToken[];
}

/** Just enough to name a role. Full definitions need the roles.view permission. */
export interface RoleSummary {
  id: string;
  name: string;
}

export interface Session {
  user: User;
  roles: RoleSummary[];
  /** The union of every permission the user's roles grant - resolved by the backend. */
  permissions: PermissionKey[];
  /**
   * The account still has the password it was created with. The backend grants such a token
   * nothing, so `permissions` is empty and every screen would refuse - the app sends them to
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
  /** The HTTP status, when the error came from a response. */
  status?: number;
  /** Every validation message by its code or field, when the server sent several. */
  errors?: Record<string, string[]>;
  constructor(code: string, message: string, status?: number, errors?: Record<string, string[]>) {
    super(message);
    this.code = code;
    this.status = status;
    this.errors = errors;
  }
}

/** A refusal that asking again won't change: not found, forbidden, invalid. Server and network failures may pass. */
export const isPermanentError = (error: unknown): boolean =>
  error instanceof ApiRequestError && error.status !== undefined && error.status >= 400 && error.status < 500;

/**
 * Whether a failed query is asked again. A refusal answers the same however often it is asked:
 * retrying it only held "Loading…" on screen for the ~7s of backoff before the error showed.
 * Server errors and dropped connections are retried, up to three times.
 */
export const shouldRetryQuery = (failureCount: number, error: unknown): boolean =>
  !isPermanentError(error) && failureCount < 3;

// --- Configuration entities (sub-phase 2) ---

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
   * linked through a gateway attachment or a bus route are NOT here - the list
   * endpoint doesn't know about them. Use `usePartnerSubscriptions()` when you
   * need the full picture.
   */
  partnerIds: number[];
  informationTypeId: number;
  workGroupId: number | null;
  /** A label an operator files it under; null for none. */
  categoryId: number | null;
  retryPolicyId: number | null;
  /**
   * Its delivery step. Null means nothing is delivered - and since a response is
   * whatever the delivery hands back, null here means the two response fields below
   * can never be reached, however they are set.
   */
  handlerId: string | null;
  /**
   * Every adapter it runs, one per configured slot, with the version it is pinned to (null
   * follows the current one). What the Adapters page counts usage from.
   */
  adapterUses: AdapterUse[];
  /** The bus message its delivery response is published as, if any. */
  responseMessageTypeName: string | null;
  /** The subscription its delivery response is handed straight to, if any. */
  responseSubscriptionId: number | null;
  /**
   * Reference tokens found in its adapter properties. Both are matched
   * case-insensitively, as the backend resolver does - compare them with
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
   * Groups everything one save changed. A single edit often touches several rows - a
   * gateway and its routes - and this is what shows them as the one change they were.
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
  /** Only the first characters - full keys are shown once, at creation. */
  keyPrefix: string;
  createdOn: string;
  /** When a gateway last let a call through with it, to within a few minutes; null if none has been seen. */
  lastUsedOn?: string | null;
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
  /**
   * The value a login server's tokens carry for this partner, for gateways that take tokens.
   * Only the detail endpoint returns it, so lists leave it out.
   */
  loginIdentity?: string | null;
  /** The built-in SYSTEM partner can't be renamed or deleted. */
  isSystem: boolean;
  createdOn: string;
}
export interface PartnerRow extends Partner {
  credentialCount: number;
  usedByCount: number;
  /** Property names only - the list endpoint never sends values, which may be secrets. */
  propertyKeys: string[];
}
export interface PartnerDetail extends Partner {
  apiCredentials: ApiCredentialRef[];
  apiGateways: ApiGatewayAttachmentRef[];
  busGatewayRoutes: BusGatewayRouteRef[];
  recentExchanges: ExchangeRef[];
}

export type InformationTypeFormat = "Json" | "Xml" | "Csv" | "Other";

export interface InformationType {
  id: number;
  /** Short unique identity shown across the system, e.g. PURCHASE_ORDER. Optional. */
  code?: string;
  name: string;
  format: InformationTypeFormat;
  busEnabled: boolean;
  busMessageTypeName?: string;
  /** How long an identical incoming payload from the same partner counts as a duplicate. 0 = off. */
  duplicateIntervalMinutes: number;
  /** Drop duplicates instead of letting them through tagged duplicate-of. */
  dropDuplicates: boolean;
  /** Duplicates in the last 7 days, and how many were dropped. Read with one type only. */
  duplicatesLastWeek?: number;
  duplicatesDroppedLastWeek?: number;
  disregardsUnfilteredMessages: boolean;
  /** Friendly name → JSONPath/XPath, matched by routes and filters. */
  promotedProperties: { key: string; path: string }[];
  /** JSON Schema for a JSON type, XSD for an XML one; null checks nothing. */
  validationSchema: string | null;
  createdOn: string;
  /**
   * When this type was taken out of use, or null while it is still in use. A retired type keeps
   * naming the exchanges already recorded against it and is no longer offered for new work.
   */
  retiredOn: string | null;
  /**
   * Seeded by the backend and referenced by machinery rather than by configuration -
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
  /** Which value names are secrets - same sentinel round trip as Partner.secretProperties. */
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

// --- Retry policies ---

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
 * the group, then the policy. A level that sends **replaces** the one above: whichever
 * level wins names the one notification channel the alert goes through.
 */
export type RetryAlertMode = "Inherit" | "Send" | "Silent";

/** Which level of that hierarchy decided, so a wrong destination can be traced to its source. */
export type RetryAlertLevel = "SubscriptionGroup" | "Group" | "Policy";

/** A destination for budget-exhausted alerts, as configured at one level. */
export interface RetryAlertConfig {
  alertMode: RetryAlertMode;
  alertChannelId: number | null;
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
  /** `maxAttemptsTotal: null` is no group-wide limit - only the per-message cap applies. */
  budget?: { maxAttemptsPerError: number; maxAttemptsTotal: number | null; delay: RetryDelay };
  notes?: string;
  /** Where this group's budget-exhausted alert goes, for every subscription using the policy. */
  alertMode: RetryAlertMode;
  alertChannelId: number | null;
}

export interface RetryPolicy {
  id: number;
  name: string;
  groups: RetryGroup[];
  createdOn: string;
  /** The policy-wide alert channel, inherited by every group that doesn't name its own. */
  alertChannelId: number | null;
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
 * `claimedOn` is when the alert was raised - all the counter itself records. Whether it then
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
 * Budgets are counted per pair - a shared policy gives every subscription its own separate total
 * - so there is no such thing as "this policy's usage". Any single figure on a policy or a group
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
  /** Null when the pair has never failed - also how you know there is no counter to reset. */
  lastAttemptOn: string | null;
  /** The channel the alert actually goes through, or null when nothing sends for this pair. */
  resolvedChannelId: number | null;
  resolvedFrom: RetryAlertLevel | null;
  /** Which level deliberately switched the alert off - a decision, as against an oversight. */
  silencedAt: RetryAlertLevel | null;
  /** This pair's own override; `Inherit` when it has none. */
  override: RetryAlertConfig;
  alert: RetryAlertOutcome | null;
}

/** One failure a group caught - what a usage row spent its budget on. */
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

// --- Notifiers ---

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

/**
 * Somewhere Bitween can send a notification: a handler adapter (built in or custom) and
 * everything it needs to deliver. Set up once in Settings; every place that notifies picks one.
 */
export interface NotificationChannel {
  id: number;
  name: string;
  handlerId: string;
  handlerProperties: Record<string, string>;
}

export type NotificationChannelUseKind = "Subscription" | "RetryPolicy" | "RetryGroup" | "RetryAlertOverride";

/** One place that sends through a channel. */
export interface NotificationChannelUse {
  kind: NotificationChannelUseKind;
  subscriptionId: number | null;
  retryPolicyId: number | null;
  name: string;
}

export interface NotificationChannelRow {
  id: number;
  name: string;
  handlerId: string;
  usedBy: NotificationChannelUse[];
}

export interface NotificationChannelDetail extends NotificationChannel {
  usedBy: NotificationChannelUse[];
}

/** What the channel page's test button got back: sent, or the reason it wasn't. */
export interface NotificationChannelTestResult {
  sent: boolean;
  error: string | null;
}

/** One delivery attempt through a channel, from the notification history. */
export interface NotificationChannelSend {
  id: number;
  xchangeId: string;
  success: boolean;
  /** Not sent: the quiet window held it back, and the next message counts it. */
  heldBack: boolean;
  exception: string | null;
  on: string;
  /** "Retry budget alert" or "Auto-pause alert" for an alert; the channel's name for a subscription's own notification. */
  sentFor: string;
  /** Null once the exchange it was about has been removed. */
  subscriptionId: number | null;
  subscriptionName: string | null;
}

/** When a subscription's finished exchanges are reported, and through which channel. */
export interface SubscriptionNotification {
  channelId: number;
  onFailure: boolean;
  onBadResult: boolean;
  onSuccess: boolean;
  /** The subscription went quiet against what it expects, and again when exchanges are back. */
  onQuiet: boolean;
}

// --- Subscriptions (subscriptions) ---

/**
 * Backend Subscription.Type. Internal and ApiCall are legacy - shown and
 * editable, never created.
 */

/**
 * Which file of each collected exchange an aggregation's roll-up links to: what came
 * in, what the mapper produced, or what the destination handed back.
 */
export type AggregationTarget = "Input" | "Output" | "Response";
/**
 * The editable fields of a subscription being defined inline, while whatever points
 * at it is being made. Mirrors the studio's own draft - deliberately, so the canvas
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
  /** Response type only. */
  runOnBadResponses?: boolean;
  /**
   * A response subscription to create in the same save and hand the response to - instead
   * of `responseSubscriptionId`, never with it.
   */
  newResponseSubscription?: NewResponseSubscriptionDraft | null;
}

/** A response subscription being defined alongside the subscription that feeds it. */
export interface NewResponseSubscriptionDraft extends InlineSubscriptionDraft {
  /** What the responses it runs on are - nothing imposes it, unlike a bus gateway's own. */
  informationTypeId: number;
  runOnBadResponses: boolean;
}

export type SubscriptionType =
  | "Receiving"
  | "GatewayApiCall"
  | "BusGateway"
  | "Internal"
  | "ApiCall"
  | "Aggregation"
  | "Response";

export type AdapterKind = "receiver" | "handler" | "mapper" | "validator";

/** One configurable property of an adapter, from its startup-value metadata. */
export interface AdapterProp {
  key: string;
  optional: boolean;
  default?: string;
  /** Secret values are write-only: masked after save, replaced not edited. */
  secret: boolean;
  description?: string;
  /**
   * A manifest's own name and section for the property. Read when the server sends them; today's
   * servers don't (the startup-value shape has no room for them), so the label is humanised from
   * `key` and the section guessed from it.
   */
  displayName?: string;
  group?: string;
}

export interface AdapterInfo {
  id: string;
  kind: AdapterKind;
  /** Friendly display name, e.g. "HTTP endpoint" for NativeHttpHandler. */
  label: string;
  /** Native adapters run in-process; others are deployed packages with versions. */
  native: boolean;
  /** Versions that can be pinned, oldest first. */
  versions: string[];
  props: AdapterProp[];
  /** From the adapter's manifest; empty for a package published before manifests. */
  summary?: string;
  description?: string;
  publisher?: string;
  /** A data: URI. */
  icon?: string;
  tags: string[];
  /** The version that runs when a subscription pins none; null when the catalog doesn't say. */
  currentVersion: string | null;
  versionHistory: AdapterVersion[];
}

export interface AdapterUse {
  kind: AdapterKind;
  adapterId: string;
  version: string | null;
}

export interface AdapterVersion {
  version: string;
  publishedOn: string | null;
  publishedBy: string | null;
  releaseNotes: string | null;
  withdrawn: boolean;
  /** The package carries its source, which can be read and compared (adapter-source.view). */
  hasSource: boolean;
  /** dotnet, python, node, exec; null when its manifest doesn't say. */
  runtime: string | null;
}

/** The files a published version's source holds. Published versions never change, so neither does this. */
export interface AdapterSourceListing {
  adapterId: string;
  version: string;
  language: string | null;
  runtime: string | null;
  buildCommand: string | null;
  lockfiles: string[];
  /** Relative to the source folder, sorted, each with its SHA-256 - enough to tell what two versions differ in. */
  files: { path: string; sha256: string }[];
}

export type DraftLanguage = "python" | "node" | "typescript";

export interface AdapterDraftSummary {
  id: number;
  adapterId: string;
  language: DraftLanguage;
  kind: string | null;
  /** The version it started from or was last published as; null for an adapter not yet published. */
  baseVersion: string | null;
  filesHash: string;
  createdOn: string;
  createdBy: string | null;
  modifiedOn: string | null;
  modifiedBy: string | null;
}

export interface AdapterDraft extends AdapterDraftSummary {
  files: Record<string, string>;
}

export interface DraftCheck {
  name: string;
  outcome: "Passed" | "Failed" | "Skipped";
  detail: string | null;
}

/** What building a draft found: its problems, or what it is and how its contract checks went. */
export interface DraftBuild {
  succeeded: boolean;
  conforms: boolean;
  problems: string[];
  warnings: string[];
  checks: DraftCheck[];
  settings: { name: string; description: string | null; required: boolean; secret: boolean; default: string | null; type: string }[] | null;
  commands: string[] | null;
  lifecycle: string | null;
  kinds: string[] | null;
}

export interface DraftRun {
  succeeded: boolean;
  output: string | null;
  error: string | null;
  problems: string[];
}

export interface DraftPublishResult {
  published: boolean;
  version?: string;
  build: DraftBuild;
}

export interface AdapterSourceFile {
  path: string;
  sha256: string;
  size: number;
  /** Not text, or too large to show; `content` is then null. */
  binary: boolean;
  content: string | null;
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
  /** Weekly: weekday 0-6 (Sun-Sat); Monthly: day of month 1-27; otherwise 0. */
  days: number;
  hours: number;
  minutes: number;
  /** Count the offset from the end of the period instead of the start. */
  backwards: boolean;
}

/** The expectation that a subscription sees traffic regularly, checked by a server job. */
export interface ExpectTraffic {
  /** Quiet once no exchange arrives for this long inside the window; at least 5. */
  everyMinutes: number;
  /** "Sun".."Sat"; null or empty is every day. */
  days: string[] | null;
  /** "HH:mm" in `timeZone`, both or neither; no overnight windows, so `to` is later than `from`. */
  from: string | null;
  to: string | null;
  /** IANA id such as "Asia/Riyadh"; null is UTC. */
  timeZone: string | null;
}

export interface Subscription {
  id: number;
  name: string;
  type: SubscriptionType;
  informationTypeId: number;
  /** Direct partner - legacy Internal/ApiCall (and Aggregation) only. */
  partnerId: number | null;
  /** Inverse of backend Inactive. Disabled = not scheduled, not matched. */
  enabled: boolean;
  /** Paused still accepts work but holds it for later release. */
  pausedOn: string | null;
  /** Pinned adapter versions, one per slot; null follows the current version. */
  receiverVersion: string | null;
  validatorVersion: string | null;
  mapperVersion: string | null;
  handlerVersion: string | null;
  /** Paused by `autoPauseAfterFailures`, not by a person. */
  pausedAutomatically: boolean;
  /** Pause after this many failed deliveries in a row; null never pauses. */
  autoPauseAfterFailures: number | null;
  /** Traffic it should see regularly; null expects nothing. */
  expectTraffic: ExpectTraffic | null;
  /** When the server found it quiet against `expectTraffic`; null while it is not. Read-only. */
  quietSince: string | null;
  workGroupId: number | null;
  /** A label an operator files it under; null for none. */
  categoryId: number | null;
  retryPolicyId: number | null;
  /**
   * Rule groups of a retry policy written on the subscription itself, which only the API sets;
   * null when it has none. The UI keeps it through a save unless a named policy is chosen.
   */
  customRetryGroups?: number | null;
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
  /**
   * The response subscription the handler's response is handed to. Only a Response-type
   * subscription can be chosen; a legacy Internal/ApiCall one already saved is kept.
   */
  responseSubscriptionId: number | null;
  responseMessageTypeName: string | null;
  /** Response type only: also run when the delivery that fed it came back bad. */
  runOnBadResponses: boolean;
  /** Which finished exchanges are reported, and through which notification channel. */
  notifications: SubscriptionNotification[];
  /**
   * Aggregation only: whose exchanges get rolled up. Fixed at creation - the backend
   * property has a private setter and the configuration applier deliberately skips it,
   * so no update can repoint a live roll-up.
   */
  aggregationForId: number | null;
  /** Aggregation only. Meaningless for every other type, where it stays "Input". */
  aggregationTarget: AggregationTarget;
  // - health (read-only) -
  isRunning: boolean;
  /** Receiving (and Aggregation) only - when the schedule will next fire, not when it last did. */
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
   * Receiving and Aggregation only - when the schedule will next fire, not when it last
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
 * Kept for `RetentionDays` (~30) - older runs are purged, not archived here.
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

/** One poll of a Receiving subscription's own receive step - independent of the scheduler's
 * run history, which only knows whether the method threw (it never does; failures here are
 * caught and reported this way instead). */
export type ReceiveOutcome = "Failed" | "NoNewData" | "Received";

export interface ReceiveAttemptExchange {
  id: string;
  status: ExchangeStatus;
  promotedProperties: Record<string, string | null> | null;
}

/**
 * The paths in the last document a subscription received: what a response or bus gateway
 * subscription it feeds can read as `{{source.PATH}}` and Original mapping rules.
 */
export interface SourcePaths {
  subscriptionId: number;
  /** The exchange that received it. Null when the subscription hasn't run successfully yet. */
  xchangeId: string | null;
  receivedOn: string | null;
  /** Empty when the document has no paths, or its file is gone. */
  paths: { path: string; example: string }[];
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
  /** Flagged as running with nothing executing - the concurrency guard is skipping every run. */
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
      /** Parallel exchanges per node; null takes the prefetch. */
      concurrency: number | null;
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

// --- API gateways ---

export interface ApiGatewayAttachment {
  partnerId: number;
  partnerName: string;
  subscriptionId: number;
  subscriptionName: string;
  /** null when the partner has none - it can't call a gateway that takes tokens. */
  partnerLoginIdentity: string | null;
}
/** How a gateway's callers prove who they are: the gateway chooses, not the caller. */
export type GatewayAuthMethod = "PartnerKey" | "Jwt";
export interface GatewayAuthentication {
  method: GatewayAuthMethod;
  /** PartnerKey: the header keys are sent in on this gateway; empty uses the system-wide one. */
  keyHeader: string;
  /** Jwt: the login server, exactly as its tokens name it in `iss`. */
  issuer: string;
  /** Jwt: the `aud` a token must carry, so tokens meant for other systems are refused. */
  audience: string;
  /** Jwt: the claim holding the partner's login server identity; empty means `sub`. */
  partnerClaim: string;
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
  authentication: GatewayAuthentication;
  /** The system-wide key header (Settings → API behavior), used while `keyHeader` is empty. */
  defaultKeyHeader: string;
  /** What a sync call is told when its delivery fails. Legacy: 400 always. Accurate: 202, 422 or 502. */
  failureAnswer?: GatewayFailureAnswer;
}
export type GatewayFailureAnswer = "Legacy" | "Accurate";

// --- Bus gateways ---

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
   * Where the messages come from. Null is Bitween's own internal bus - the only behaviour that
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

// --- Data sources ---

/**
 * A connection to something outside Bitween - today a broker.
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
  /** Auto, Exclusive or PerNode. Auto means Exclusive for a broker and PerNode otherwise. */
  placement: string;
  inactive: boolean;
  /**
   * How long a message's deduplication key is remembered. Has to exceed the widest redelivery
   * window this broker can produce. Zero turns deduplication off.
   */
  deduplicationWindowDays: number;

  /** Soft ceiling in MB - crossing it recycles the adapter between messages. 0 = host default. */
  softMemoryLimitMb: number;
  /** Hard ceiling in MB - becomes the adapter's GC heap hard limit. 0 = host default. */
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
  /** How many subscriptions run on its connection. */
  subscriptionCount: number;
}

export interface DataSourceDetail extends DataSourceRow {
  /** Connection settings. Secret values arrive as the sentinel, never in clear. */
  properties: Record<string, string>;
  /** Which of those names hold credentials. */
  secretProperties: string[];
}

/**
 * What the connection is doing right now, read from the adapter's heartbeat rather than from the
 * data source row - which only carries what the last reconcile wrote back.
 */
/**
 * One named piece of SQL a data source may run.
 *
 * A record rather than a field on the data source, so that writing a query and changing the
 * database credentials are different permissions - see the backend entity for why SQL cannot live
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
  /** Handler, Mapper or Receiver - which adapter slot names it. */
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

  /** Whatever the adapter reports - per-queue depth, counters, prefetch. Untyped by design. */
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
  /** The adapter's own JSON, as text - Bitween does not model any broker's topology. */
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

// --- Settings ---

export type SettingValueKind = "string" | "number" | "boolean" | "color";

/**
 * How a row behaves:
 * - `editable` - stored in the database, changeable here, takes effect immediately.
 * - `readonly` - an environment value, shown so you can see what this instance runs
 *   on. Read once at startup, so there's nothing to change here.
 * - `presence` - an environment value whose content stays on the server; only
 *   whether it's set is reported.
 */
export type SettingAccess = "editable" | "readonly" | "presence";

/**
 * One setting on the settings page. Only settings that take effect immediately are
 * editable, so there is no "restart required" state to represent - anything read
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
  /** The stored value, or null for a secret - those never leave the server. */
  value: string | null;
  secret: boolean;
  /** The only values a string setting takes, shown as a dropdown. Absent for free text. */
  choices?: string[] | null;
  /** The stored value differs from the product default, so a reset would do something. */
  overridden: boolean;
  /** Whether the effective value is non-empty - the only way a secret reveals it is set. */
  hasValue: boolean;
  /**
   * Whether this row can be written. False for every environment value, and for a secret on an
   * instance with no encryption key configured - there's nowhere safe to store that one.
   */
  editable: boolean;
  access: SettingAccess;
}

// --- Retention ---

/** Retention settings to preview before saving; anything left out keeps its current value. */
export interface RetentionProposal {
  documentPrefix?: string;
  exchangeRetentionDays?: number;
  archiveExchanges?: boolean;
  exchangeRetentionCron?: string;
}

export interface StorageRule {
  id: string | null;
  prefix: string;
  days: number;
  enabled: boolean;
}

/** How long the bucket keeps files under one prefix: `days` null when no rule deletes them by age. */
export interface PrefixRetention {
  prefix: string;
  days: number | null;
  rulePrefix: string | null;
}

export interface RetentionNotice {
  level: "warning" | "info";
  code: string;
  message: string;
}

/** What the retention settings do, now or as proposed. */
export interface RetentionStatus {
  storage: { provider: string | null; bucket: string | null; problem: string | null; rules: StorageRule[] };
  files: PrefixRetention;
  /** Where exchanges from before prefixes were recorded keep their files, when that differs. */
  legacyFiles: PrefixRetention | null;
  archive: PrefixRetention;
  exchanges: {
    retentionDays: number;
    archive: boolean;
    cron: string;
    nextRun: string | null;
    dueNow: number | null;
    withoutFiles: number | null;
    /** Counts stop here; a count equal to it means "at least". */
    countCap: number;
    oldest: string | null;
  };
  notices: RetentionNotice[];
}

/** Whether retention removes exchanges before an aggregation with given schedules collects them. */
export interface AggregationRetentionCheck {
  /** Null when it won't. */
  warning: string | null;
}

// --- Exchanges ---

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

/** One exchange as the Exchanges page sees it - names pre-resolved for display. */
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
   * retry, so this is also what makes it un-retryable - the newest attempt in the chain is the
   * one to act on.
   */
  hasRetry: boolean;
  /** Set when this exchange was rolled up into an aggregation exchange. */
  aggregationXchangeId: string | null;
  /** A pending auto-retry, when the retry policy scheduled one. */
  scheduledRetryOn: string | null;
  exception: string | null;
  /**
   * Something to know about a successful exchange: a delivery that went through while its output
   * or response file could not be stored, one line per file. null for nothing.
   */
  warning: string | null;
  promotedProperties: Record<string, string | null> | null;
  /**
   * The exchange whose delivery's response started this one. Only a response or bus gateway
   * subscription's exchange has one.
   */
  sourceXchangeId: string | null;
  /** What it read from the input of `sourceXchangeId`, the original document, by path. */
  sourceValues: Record<string, string | null> | null;
  /** True when the subscription has no mapper - the Mapped stage is skipped. */
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
  /** What the payload promoted - how an attempt is named, the same as in the exchange list. */
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
 * Which exchanges a bulk retry or a files export is about: the rows someone ticked, or a whole
 * filter's worth minus the ones they unticked.
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
  /** The exchange the skip is about - the one selected, or the attempt that stood in for it. */
  id: string;
  reason: string;
}

/** What a files export would hold, asked before it downloads. */
export interface ExportFilesCheck {
  count: number;
  /** Exchanges older than storage keeps their files: the zip won't have them. */
  withoutFiles: number;
  /** The days storage keeps files, when one rule covers all of those; otherwise null. */
  keptDays: number | null;
}

/** What a bulk retry will do, or (returned by the retry itself) what it did. */
export interface BulkRetryPlan {
  selected: number;
  willRetry: number;
  /** The most one retry request carries out; a bigger selection goes in rounds of this many. */
  limit: number;
  /** The most a preview works out. */
  maxSelection: number;
  /** True when the selection is past `maxSelection` (or, for a retry, past `limit`); nothing else is filled in. */
  overLimit: boolean;
  /** The attempts that will run - what the rounds send back as ids when `selected` is past `limit`. */
  targets: string[];
  substituted: RetrySubstitution[];
  skipped: RetrySkip[];
  /**
   * Promoted properties for every exchange the plan names, keyed by id, so they can be named the
   * way the exchange list names them. Absent for exchanges that promote nothing.
   */
  properties: Record<string, Record<string, string | null> | undefined>;
}

/** The exchange filters that can be asked the other way round: "is not", "does not contain", "is missing". */
export type NegatableExchangeFilter =
  | "subscription"
  | "partner"
  | "informationType"
  | "property"
  | "error"
  | "retry"
  | "scheduled"
  | "aggregated";

/** One promoted-property filter. With a key, the text is looked for in that key's value; without one, in any. */
export interface ExchangePropertyFilter {
  key: string | null;
  /** Null with a key asks only whether the exchange promoted it. */
  text: string | null;
  negated: boolean;
}

export interface ExchangeQuery {
  status?: ExchangeStatus;
  /** One is "is", several are "is any of" - or, turned round by `not`, "is not" and "is none of". */
  subscriptionIds?: number[];
  partnerIds?: number[];
  informationTypeIds?: number[];
  /**
   * The filters turned round. A turned-round filter keeps the exchanges with no value at all:
   * "Subscription is not A" keeps the ones with no subscription.
   */
  not?: NegatableExchangeFilter[];
  /** Comma/pipe/newline separated; matches id, retryFor OR aggregationXchangeId. */
  ids?: string;
  /**
   * The exchanges one run of a scheduled job created. The server reads the run's own list of
   * them, so this says the same thing as spelling every id into `ids` - at any size, and
   * without putting them all in the URL.
   */
  receiveAttemptId?: number;
  correlationId?: string;
  /**
   * Only the newest attempt of each retry chain. A chain is one piece of work however many
   * attempts it took, so this turns a list of failures into a list of things still to deal with.
   */
  latest?: boolean;
  /** Promoted properties, any number of them, all of which have to hold. */
  properties?: ExchangePropertyFilter[];
  /** Case-insensitive substring of the exception text. */
  errorContains?: string;
  /** Only exchanges that are a retry of another one. */
  isRetry?: boolean;
  /** Only exchanges with an automatic retry booked. */
  scheduledRetry?: boolean;
  /** Only exchanges rolled up into an aggregation. */
  aggregated?: boolean;
  from?: string;
  to?: string;
  offset: number;
  limit: number;
}

/** One slice of the exchange volume histogram: how many started in it, by status. */
export interface ExchangeVolumeBucket {
  /** UTC start of the slice. */
  start: string;
  processing: number;
  success: number;
  badResponse: number;
  failed: number;
}

/**
 * How the exchanges a filter matches break down: a count per status, ignoring any status filter,
 * and, when the filter has a start time, how they spread over that range.
 */
export interface ExchangeBreakdown {
  /** Each capped like the list total: a value past the cap means "more than". */
  counts: Record<ExchangeStatus, number>;
  buckets: ExchangeVolumeBucket[];
  /** The width of one bucket; 0 when there are none. */
  bucketMinutes: number;
}

export interface Paged<T> {
  result: T[];
  total: number;
}

// --- Scheduled retries ---

/** A failed exchange whose retry policy scheduled an automatic retry. */
export interface ScheduledRetryRow {
  /** The exchange the retry will re-run. */
  id: string;
  /** When the retry job will pick it up. */
  on: string;
  /** Kept because its retry budget is spent: runs once the subscription next succeeds, `on` means nothing. */
  held: boolean;
  subscriptionId: number | null;
  subscriptionName: string | null;
  informationTypeId: number;
  informationTypeCode: string;
  exception: string | null;
  /** When the failed exchange originally started. */
  startedOn: string;
  /** What the exchange carries - how a pending retry identifies itself in a list. */
  promotedProperties: Record<string, string | null> | null;
  /**
   * The shared retry policy the subscription currently points at. Null when the
   * policy is defined inline on the subscription instead, so the subscription - not
   * the Retry policies list - is where to go and look.
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

// --- Queue health (Ops) ---

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
 * `WorkGroup.GetBusMessageName()` - the formula that builds the name in the
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
  /** How many of the lane's messages run at once on a node: parallel exchanges, capped by prefetch. */
  concurrency: number;
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
 * A queue RabbitMQ has that nothing here reads: a paused information type's, or one left
 * behind by an older version of Bitween. Every other view on this page is built from what
 * the running process declares - so these are invisible everywhere else.
 */
export interface UnattendedQueue {
  queueName: string;
  messages: number;
  retryMessages: number;
  deadMessages: number;
  /** Main plus whichever of its retry/dead queues still exist. */
  queues: number;
  /**
   * Consumers the broker reports on these queues, from any instance. Above zero means
   * something still reads it, so it can't be deleted yet.
   */
  consumers: number;
  /** Set when this is the queue of an information type paused on the bus. */
  informationTypeId: number | null;
}

/** What a delete of unattended lanes did: a lane that couldn't go is skipped, not fatal. */
/** One message in a dead-letter queue, read without taking it off. */
export interface DeadLetterMessage {
  position: number;
  body: string;
  /** The body was longer than the server sends, and this is its start. */
  bodyCut: boolean;
  lastException: string | null;
  correlationId: string | null;
  routingKey: string | null;
  exceptionHistory: string[];
}

export interface UnattendedDeleteResult {
  deleted: string[];
  skipped: { queueName: string; reason: string }[];
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

// --- Dashboard ---

/** One chain that has been retried repeatedly and is still failing. */
export interface FailingChain {
  /** The newest attempt - where this chain got to, and the one a retry continues from. */
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
  /** Exchanges started in the last 24 hours and not finished yet. */
  inFlight: number;
  pendingRetries: number;
  /** Null when the RabbitMQ management API is unavailable - never counts as zero. */
  queueAlerts: number | null;
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
 * One connection setting, as the adapter itself declares it. The UI keeps no list of its own -
 * see DataSourceProviderCatalog on the server for why.
 */
export interface DataSourceProviderSetting {
  name: string;
  /** string, number or boolean - enough to pick an input, nothing more. */
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
   * only read from a Broker - a provider that holds a database session is a data source too, but
   * it has no queue to consume.
   */
  kind: string;
  description: string | null;
  settings: DataSourceProviderSetting[];
}

/** GET /settings/about: this Bitween and how it is set up, as the node that answered sees it. */
export interface InstanceAbout {
  version: string;
  build: string | null;
  node: { name: string; startedOn: string; runtime: string; os: string; dataSources: boolean };
  database: string;
  storage: { provider: string; adapterPath: string; documentPrefix: string };
  broker: { queuePrefix: string; managementConfigured: boolean };
  health: { name: string; status: string; durationMs: number; description: string | null; error: string | null }[];
  adapters: {
    runtimes: { name: string; available: boolean; version: string | null; reason: string | null }[];
    pip: { available: boolean; version: string | null; reason: string | null };
    npm: { available: boolean; version: string | null; reason: string | null };
    commandTimeoutSeconds: number;
    editor: { dependencies: boolean; memoryMb: number; cpuCores: number };
  };
  limits: {
    signInPerMinute: number;
    requestsPerMinute: number;
    fileLinksPerMinute: number;
    maxResponseWaitSeconds: number;
    maxRetryChainDepth: number;
    staleRunAfterMinutes: number;
    notifierQuietMinutes: number;
  };
  network: { publicUrl: string | null; blockPrivateNetworkAddresses: boolean; trustedProxies: number; exposeApiDocs: boolean; corsOrigins: string[] };
  retention: { receiveAttemptRetentionDays: number; receiveAttemptCleanupCron: string; inboundMessagePruneCron: string };
  telemetry: { openTelemetry: boolean };
}

/** GET /cluster/nodes: the nodes, as their heartbeats describe them. */
export interface ClusterNodes {
  answeredBy: string;
  goneAfterSeconds: number;
  nodes: {
    name: string;
    host: string;
    startedOn: string;
    lastSeenOn: string;
    online: boolean;
    version: string;
    dataSources: boolean;
    runtimes: string[];
    leases: { resource: string; term: number; acquiredOn: string }[];
    holdsDataSources: { id: number; name: string }[];
  }[];
}

/** POST /adapters/packages: what was published. */
export interface AdapterUploadResult {
  adapterId: string;
  version: string;
  current: boolean;
  sha256: string;
}

/** GET /apigateways/{id}/rejections: calls to the gateway turned away, on the node that answered. */
export interface GatewayRejections {
  node: string;
  since: string;
  counts: Record<string, number>;
  /** Every node's refusals over the last week, by reason. Absent from servers before metrics. */
  lastSevenDays?: Record<string, number>;
  recent: { on: string; reason: string; status: number; address: string | null }[];
}

/** GET /ops/background: Bitween's own system jobs, and the outbox to the broker. */
export interface BackgroundWork {
  jobs: {
    name: string;
    does: string;
    cron: string | null;
    scheduled: boolean;
    state: "Normal" | "Paused" | "Error" | "NotScheduled";
    lastRanOn: string | null;
    nextRunOn: string | null;
    runningNow: boolean;
  }[];
  outbox: { pending: number; oldestPendingOn: string | null; failing: number; lastError: string | null; publishedLastHour: number };
}

/** A label subscriptions are filed under, to find them by. */
export interface SubscriptionCategory {
  id: number;
  code: string;
  description: string | null;
  createdOn: string;
}

// - the marketplace (a Bitween Registry, when one is set under Settings) -

export type RegistryTier = "Official" | "Verified" | "Community";

export interface MarketplaceCard {
  id: string;
  displayName: string;
  summary: string | null;
  publisher: string;
  publisherName: string;
  tier: RegistryTier;
  pricing: "Free" | "Paid";
  latest: string;
  runtime: string | null;
  kinds: string | null;
  categories: string[];
  iconDataUri: string | null;
  downloads: number;
  updatedOn: string;
}

/** What this Bitween already has of a registry package. */
export interface MarketplaceInstalled {
  installed: boolean;
  /** Installed from a registry; false for an adapter of the same name published here. */
  fromRegistry: boolean;
  current: string | null;
  versions: string[];
}

export interface MarketplaceSearch {
  configured: boolean;
  /** Turned off under Settings; otherwise not configured means no registry address. */
  disabled?: boolean;
  registry?: string;
  result?: { card: MarketplaceCard; installed: MarketplaceInstalled }[];
  totalCount?: number;
}

export interface MarketplaceVersion {
  version: string;
  status: "Published" | "Withdrawn" | "Revoked" | string;
  size: number;
  minBitweenVersion: string | null;
  signedOn: string | null;
  revokeReason: string | null;
}

export interface MarketplacePackage {
  package: {
    card: MarketplaceCard;
    description: string | null;
    homepage: string | null;
    license: string | null;
    versions: MarketplaceVersion[];
  };
  /** The newest version's manifest, for its settings. */
  latest: { properties?: { name: string; displayName?: string; description?: string }[] } | null;
  installed: MarketplaceInstalled;
}

/** A version newer than any installed here, with what its publisher says changed. */
export interface RegistryNewerVersion {
  version: string;
  releaseNotes: string | null;
}

/** An installed version the registry has revoked, from its signed revocation list. */
export interface RegistryRevokedVersion {
  version: string;
  reason: string | null;
  revokedOn: string | null;
}

/** What the last registry check found for one adapter installed from it. */
export interface RegistryAdapterCheck {
  adapterId: string;
  /** Current here now. */
  current: string | null;
  latest: string | null;
  updateAvailable: boolean;
  /** Newest first. */
  newer: RegistryNewerVersion[];
  revoked: RegistryRevokedVersion[];
  /** False when the registry no longer lists it. */
  listed: boolean;
  checkedOn: string;
}

/** The last registry check, run daily and on Check now. */
export interface RegistryCheckSummary {
  configured: boolean;
  checkedOn: string | null;
  /** Why the last check didn't finish; the flags from the one before stay. */
  error: string | null;
  adapters: RegistryAdapterCheck[];
}

export type LicenceState = "None" | "NotYet" | "Live" | "Grace" | "Expired";

/** One licence this Bitween holds, as the registry signed it and as it stands now. */
export interface RegistryLicenceRow {
  licenceId: number;
  customer: string;
  /** Package ids or namespace patterns, comma separated. */
  packages: string;
  /** Instance ids, or * for any. */
  instances: string;
  starts: string;
  ends: string;
  graceDays: number;
  state: LicenceState;
}

/** The licences held here: fetched daily and on demand, checked against the trusted keys. */
export interface RegistryLicenceSummary {
  registered: boolean;
  instanceId: string | null;
  refreshedOn: string | null;
  /** Why the last fetch didn't keep everything; what was held before stays. */
  error: string | null;
  licences: RegistryLicenceRow[];
}

export interface MarketplaceRegistration {
  instanceId: string;
  displayName: string | null;
  customerName: string;
  licences: RegistryLicenceSummary;
}

export interface RegistryUsageLine {
  packageId: string;
  version: string;
  subscriptions: number;
  calls: number;
  failures: number;
}

/** One day's usage report, as it would be or was sent. */
export interface UsageReportPreview {
  day: string;
  lines: RegistryUsageLine[];
}

/** What installing a bundle did, package by package. */
export interface BundleItem {
  id: string;
  version: string;
  outcome: "Installed" | "Skipped" | "Refused";
  message: string;
  sha256: string | null;
}

export interface BundleInstall {
  registry: string | null;
  madeOn: string | null;
  items: BundleItem[];
}

/** A kit's parameter, as its install form asks for it. */
export interface KitParameter {
  name: string;
  description: string | null;
  required: boolean;
  secret: boolean;
  default: string | null;
  example: string | null;
}

export interface KitInfo {
  id: string;
  version: string;
  displayName: string;
  summary: string | null;
  description: string | null;
  connectors: { id: string; version: string }[];
  parameters: KitParameter[];
}

/** What planning or installing a kit came to, as bitween kit install prints it. */
export interface KitRun {
  name: string;
  ready: boolean;
  applied: boolean;
  lines: string[];
  problems: string[];
  warnings: string[];
  changes: number;
}

export interface KitInstallRequest {
  id: string;
  version: string;
  name?: string;
  parameters: Record<string, string>;
}

export interface MarketplaceInstallResult {
  adapterId: string;
  version: string;
  current: boolean;
  sha256: string;
  publisher: string;
}

// - a subscription's versions -

/** One save in a subscription's history: who, when, and which settings it changed from the one before. */
export interface SubscriptionRevision {
  number: number;
  savedOn: string;
  savedBy: string | null;
  /** Setting names as the API spells them, e.g. handlerProperties. Empty for the first. */
  changed: string[];
}

/** A revision's configuration, in the subscription's own wire shape. Secrets are __private__. */
export interface SubscriptionRevisionDetail {
  number: number;
  savedOn: string;
  configuration: Record<string, unknown>;
}

/** How a connection gets its tokens. */
export type ConnectionGrant = "ClientCredentials" | "AuthorizationCode" | "Password";

export type ConnectionStatus = "New" | "Connected" | "NeedsAuthorizing" | "Error";

/** Credentials Bitween keeps fresh for adapters. Secrets come back as `__private__` when set. */
export interface Connection {
  id: number;
  name: string;
  grant: ConnectionGrant;
  authorizeUrl: string | null;
  tokenUrl: string | null;
  clientId: string | null;
  clientSecret: string | null;
  scopes: string | null;
  authorizeParameters: string | null;
  tokenAuth: string | null;
  username: string | null;
  password: string | null;
  status: ConnectionStatus;
  lastError: string | null;
  accessTokenExpiresOn: string | null;
  hasRefreshToken: boolean;
  authorizedBy: string | null;
  authorizedOn: string | null;
  /** What a setting writes to use it. */
  reference: string;
}

/** A secret left as `__private__`, or not sent, keeps the stored one. */
export interface ConnectionInput {
  name: string;
  grant: ConnectionGrant;
  authorizeUrl?: string | null;
  tokenUrl?: string | null;
  clientId?: string | null;
  clientSecret?: string | null;
  scopes?: string | null;
  authorizeParameters?: string | null;
  tokenAuth?: string | null;
  username?: string | null;
  password?: string | null;
}

// ── Prod mirror ──

/** Whether this Bitween is a mirror and how it reaches prod - known without calling prod. */
export interface MirrorStatus {
  /** Set by the deployment: only a mirror can be copied onto. */
  target: boolean;
  sourceUrl: string | null;
  signIn: "token" | "member" | null;
  configured: boolean;
}

/** inSync, differs, missing; waiting for an adapter staging lacks; extra: only on staging. */
export type MirrorItemState = "inSync" | "differs" | "missing" | "waiting" | "extra";

/**
 * One of prod's items and how staging's compares, with where each is: its id on staging and on
 * prod, and a subscription's type, which decides its page. A SQL statement's ids are its data
 * source's, the page it is on.
 */
export interface MirrorItem {
  key: string;
  state: MirrorItemState;
  fields: string[];
  reason: string | null;
  /** The last copy tried it and Bitween turned it down: copying again won't change that. */
  refused: boolean;
  stagingId: string | null;
  prodId: string | null;
  type: string | null;
}

/**
 * A line of what is left to do, or worth knowing. needsYou: only a person can do it. fill: an address or
 * secret staging has no value for yet, to fill in with staging's own. faked: what came faked. copy,
 * notCovered and onPurpose: how staging stands against prod.
 */
export interface MirrorNote {
  id: string;
  group: "needsYou" | "fill" | "faked" | "copy" | "notCovered" | "onPurpose";
  text: string;
  /** The item it is about, to open it. */
  section: string | null;
  key: string | null;
  /** Where it is done: adapters-installed or adapters-marketplace. */
  action: string | null;
  /** The item on staging and on prod, to open it on each; null where a side doesn't have it yet. */
  stagingId?: string | null;
  prodId?: string | null;
  /** For a subscription: its type, which decides its page. */
  type?: string | null;
  /** For a fill-in: the field to fill in, as its page names it. */
  field?: string | null;
  /** For a fill-in: what kind of item it is in, in words. */
  noun?: string | null;
}

/** One of prod's exchange files as staging keeps it: faked, or not kept and why. */
export interface MirrorSampleFile {
  /** Null when it isn't kept. */
  key: string | null;
  /** Its name, faked. */
  name: string | null;
  /** json, xml, html, csv, xlsx or text when kept; what it is when it isn't: pdf, zip, edi, binary, large... */
  format: string | null;
  size: number;
  notCopied: string | null;
  /** Opens the faked file on staging. */
  url: string | null;
}

/** One of prod's exchanges, faked, and how it ended on prod. */
export interface MirrorSample {
  prodExchangeId: string;
  startedOn: string;
  outcome: "success" | "badResponse" | "failed";
  /** The partner's HTTP status, when prod's error names one. */
  status: number | null;
  /** The kinds of error prod's error names, outermost first. Its message isn't kept: it can hold prod's data. */
  errorTypes: string[];
  input: MirrorSampleFile | null;
  output: MirrorSampleFile | null;
  response: MirrorSampleFile | null;
  /** The partner it ran as, by name. */
  partner: string | null;
  /** The handler it ran, as prod recorded it: the adapter, and its version where one was pinned. */
  handler: string | null;
  mapper: string | null;
  /** Its subscription, by name; null for a document being routed. */
  subscription: string | null;
  /** The type of what came in, by name. */
  informationType: string | null;
  /** The exchange on prod whose answer or message started it; null for the one that came in. */
  parent: string | null;
  /** For an aggregation: a few of the files it rolled up, faked. */
  rolled?: MirrorSampleFile[] | null;
}

/**
 * One thing that came in on prod and every exchange it led to: a document routed, a partner's call
 * through a gateway, a file a receiver read, an aggregation, then the answers those handed on.
 */
export interface MirrorFamily {
  /** Its first exchange's id on prod. */
  id: string;
  /** How it came in on prod. */
  entry: "document" | "gateway" | "api" | "receiver" | "aggregation" | "direct";
  /** What it came in through, by name: the information type, the gateway, the receiving subscription. */
  door: string | null;
  /** The partner who called, for a gateway or the partner API. */
  partner: string | null;
  /** Its exchanges in the order they started on prod; the first is the one that came in. */
  members: MirrorSample[];
  /** How many more exchanges prod's family had than the sample keeps. */
  cut: number;
  /** Prod doesn't say which subscriptions a document of it went to: staging's are run, but not counted as more than prod's. */
  unlinked?: boolean;
}

/**
 * One line of a rehearsed exchange's compare. different and check count towards the verdict; info is a
 * difference that explains, such as the upgrade itself; faked is the same as prod's with faked values.
 */
export interface MirrorDiff {
  /** The part of the exchange it is about; entry and family are a family's own: how it came in, and what it led to. */
  part: "result" | "output" | "answer" | "calls" | "input" | "used" | "entry" | "family";
  name: string;
  level: "different" | "check" | "info" | "faked" | "same";
  /** Prod's side, as staging can show it: faked. Null where only staging's side says anything. */
  prod: string | null;
  staging: string | null;
  note: string | null;
  fields: MirrorFieldDiff[] | null;
  /** For a setup item: where it opens on staging. */
  section: string | null;
  stagingId: string | null;
  type: string | null;
}

export interface MirrorFieldDiff {
  path: string;
  level: "different" | "check" | "info" | "same";
  prod: string | null;
  staging: string | null;
}

/** One exchange of a rehearsed family: prod's, the twin staging ran, and how the two compare. */
export interface MirrorRun {
  /** Null for one staging ran that prod's family didn't. */
  prodExchangeId: string | null;
  /** Its subscription, or the information type of a document being routed. */
  subscription: string;
  /** Null when it didn't run. */
  stagingExchangeId: string | null;
  ranOn: string;
  /** How it ended on staging; null when it didn't run or finish. */
  outcome: "success" | "badResponse" | "failed" | null;
  status: number | null;
  errorTypes: string[];
  verdict: "same" | "check" | "different" | null;
  /** Why it didn't run, or didn't finish. */
  notRun: string | null;
  output: MirrorSampleFile | null;
  response: MirrorSampleFile | null;
  diffs: MirrorDiff[];
}

/** One of prod's families, sent in on staging the way it came in on prod, and every exchange staging ran for it. */
export interface MirrorFamilyRun {
  familyId: string;
  /** The correlation id it ran under on staging; null when it didn't run. */
  correlation: string | null;
  ranOn: string;
  /** The worst of its exchanges and how it came in; null when it didn't run. */
  verdict: "same" | "check" | "different" | null;
  notRun: string | null;
  /** The family's own lines: how it came in, the release, and anything prod's family had more of. */
  diffs: MirrorDiff[];
  members: MirrorRun[];
}

/** The kinds a page brings from prod. */
export type MirrorBringSection = "subscriptions" | "apiGateways" | "busGateways";

/** How many of prod's items there are of each kind a page lists, read from prod's lists alone. */
export interface MirrorProdKinds {
  /** By subscription type, and every one of them under "all". */
  subscriptions: Record<string, number>;
  apiGateways: number;
  busGateways: number;
  /** Why prod couldn't be read; pages still offer to bring, and the list says why. */
  error: string | null;
}

/** An item's traffic on prod over the window. */
export interface MirrorProdCounts {
  total: number;
  failed: number;
  lastOn: string | null;
  /** One count a day, oldest first. */
  daily: number[];
}

/** One of prod's items as a page offers it: its traffic, and whether staging has it. */
export interface MirrorProdItem {
  key: string;
  prodId: string | null;
  type: string | null;
  /** What it is for, in a few words: a subscription's partner and information type, a gateway's partners. */
  detail: string;
  state: MirrorItemState;
  stagingId: string | null;
  reason: string | null;
  traffic: MirrorProdCounts | null;
}

/** Prod's items of one kind, busiest first. */
export interface MirrorProdList {
  section: MirrorBringSection;
  noun: string;
  plural: string;
  /** The window the traffic covers; null when prod can't say, and trafficNote says why. */
  days: number | null;
  trafficNote: string | null;
  items: MirrorProdItem[];
}

export type MirrorProdListAnswer = MirrorProdList | { error: string };

/**
 * What happens to one item a bring touches: new (staging gets prod's, faked), kept (staging has it
 * and keeps its own), adds (staging has it and gets the partner or route that leads to what comes),
 * waits (it can't come yet: reason says why), install (an adapter from the marketplace).
 */
export type MirrorPieceState = "new" | "kept" | "adds" | "waits" | "install";

export interface MirrorPiece {
  section: string;
  noun: string;
  key: string;
  state: MirrorPieceState;
  /** Why it comes: picked, or what needs it. */
  why: string;
  reason: string | null;
  stagingId: string | null;
  type: string | null;
}

/**
 * One of staging's subscriptions beside prod's of the same name: it and what it needs, each the same,
 * faked, different field by field or not on staging, then the adapters they run; what bringing it
 * would do; and what only a person can do first.
 */
export interface MirrorSubscriptionView {
  key: string;
  /** Whether prod has a subscription of this name: without one there is nothing to compare or test with. */
  onProd: boolean;
  prodId: string | null;
  prodUrl: string;
  used: MirrorDiff[];
  toBring: MirrorPiece[];
  needsYou: MirrorNote[];
  /**
   * The adapters it and what it needs run that don't come with Bitween: one may call addresses written in
   * its code, which staging can't see, so a test asks first.
   */
  customAdapters?: string[] | null;
}

export type MirrorSubscriptionAnswer = MirrorSubscriptionView | { error: string };

/** A run of a subscription's test whose exchanges are still on staging. */
export interface MirrorTestRun {
  correlation: string;
  startedOn: string;
}

/** How a subscription's test last ran, kept after what it made is cleared. */
export interface MirrorTestVerdict {
  /** The worst of its arrivals; one that didn't run counts as one to check. */
  verdict: "same" | "check" | "different";
  ranOn: string;
  same: number;
  check: number;
  different: number;
  notRun: number;
}

/** One of staging's items that prod has too, and how its test last ran. */
export interface MirrorListedItem {
  id: number;
  key: string;
  /** It, or a subscription a gateway leads to, runs an adapter that doesn't come with Bitween. */
  custom: boolean;
  /** A subscription's type; null for a gateway. */
  type: SubscriptionType | null;
  /** noTraffic when prod had none of it to take; null when it hasn't been tested. A gateway's is the worst of the tested subscriptions it leads to. */
  verdict: MirrorTestVerdict["verdict"] | "noTraffic" | null;
  ranOn: string | null;
  /** How many of the subscriptions it is, or a gateway leads to, have been tested, of `of`. */
  tested: number;
  of: number;
}

/** Staging's items of one kind that prod has too: what a list shows beside its rows. */
export interface MirrorListed {
  items: MirrorListedItem[];
  /** The subscription whose test is under way, by name. */
  testing: string | null;
  /** Those waiting their turn after it. */
  waiting: string[];
  /** Why prod can't be read, when it can't. */
  error: string | null;
}

/** A list's filter on how its items stand against prod. */
export type MirrorListFilter = "fromProd" | "untested" | "same" | "check" | "different" | "noTraffic";

/**
 * A subscription's test with prod's traffic: prod's latest arrivals it takes part in, each sent in on
 * staging the way it came in on prod, and how they last ran.
 */
export interface MirrorTest {
  /** queued: it waits its turn after `busy`. */
  state: "idle" | "taking" | "running" | "queued" | "failed";
  /** Other work of the mirror under way, which this test waits for, in words: The test of “Push labels”. */
  busy: string | null;
  done: number;
  total: number;
  error: string | null;
  takenOn: string | null;
  /** Why prod had none to take. */
  reason: string | null;
  families: MirrorFamily[];
  ranOn: string | null;
  runs: MirrorFamilyRun[];
  setupError: string | null;
  made: MirrorTestRun[];
  /** How many exchanges the test's runs have left on staging. */
  exchanges: number;
  /** How the arrivals last ran, kept after a clear; null until they run. */
  last: MirrorTestVerdict | null;
}

/** What bringing the picked items does, or did. */
export interface MirrorBring {
  applied: boolean;
  pieces: MirrorPiece[];
  /** What only a person can do first: upload an adapter, say. */
  needsYou: MirrorNote[];
  failed: { section: string; noun: string; key: string; error: string }[];
}
