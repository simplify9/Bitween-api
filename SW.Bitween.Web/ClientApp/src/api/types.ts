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

export type UserStatus = "active" | "invited" | "disabled";

export interface User {
  id: string;
  displayName: string;
  email: string;
  phone?: string;
  roleIds: string[];
  status: UserStatus;
  /** Whether a Microsoft account is linked for SSO. */
  microsoftLinked: boolean;
  createdOn: string;
  lastActiveOn?: string;
}

export interface Invite {
  token: string;
  email: string;
  roleIds: string[];
  /** Resolved names for roleIds, so the public accept page can show them. */
  roleNames: string[];
  invitedByName: string;
  createdOn: string;
  expiresOn: string;
}

export interface Session {
  user: User;
  roles: Role[];
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
  status: "success" | "failed" | "processing";
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
   * Partners this integration runs for: its own partner (legacy types)
   * plus partners linked through gateway attachments and bus routes.
   */
  partnerIds: number[];
  informationTypeId: number;
  workGroupId: number | null;
  retryPolicyId: number | null;
  /** Keys of partner properties referenced by its adapters ({{partner.KEY}}). */
  partnerPropKeys: string[];
  /** Global value references, per set. */
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
}
export interface PartnerDetail extends Partner {
  apiCredentials: ApiCredentialRef[];
  integrationSetups: IntegrationSetupRef[];
  apiGateways: ApiGatewayAttachmentRef[];
  busGatewayRoutes: BusGatewayRouteRef[];
  recentExchanges: ExchangeRef[];
}

export type InformationTypeFormat = "Json" | "Xml";

export interface InformationType {
  id: number;
  /** Short unique identity shown across the system, e.g. PURCHASE_ORDER. */
  code: string;
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
export interface GlobalValuesSetRow extends GlobalValuesSet {
  usedByCount: number;
}
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
export interface RetryPolicyListRow extends RetryPolicy {
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

/**
 * A delivery channel = a notifier handler adapter and the configuration
 * it expects. Served by the API so new adapters appear without UI changes.
 */
export interface NotifierChannel {
  id: string;
  label: string;
  props: { key: string; label: string; placeholder?: string }[];
}

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
  lastReceiveOn: string | null;
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
  lastReceiveOn: string | null;
  createdOn: string;
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

export type SettingSection =
  | "Documents & storage"
  | "API behavior"
  | "Single sign-on (Microsoft)"
  | "Messaging"
  | "Adapters"
  | "Reliability & jobs"
  | "Security"
  | "Brand & theme";

export type SettingValueKind = "string" | "number" | "boolean" | "string[]" | "color";

export interface Setting {
  /** Stable key; mirrors the real config path (e.g. "Bitween.RebexLicenseKey"). */
  key: string;
  section: SettingSection;
  label: string;
  description: string;
  kind: SettingValueKind;
  /** Stored as a string regardless of kind; parsed for display per `kind`. */
  defaultValue: string;
  /** null = not overridden, falls back to `defaultValue`. */
  value: string | null;
  secret: boolean;
  /** Read once at process startup; changing it has no effect until the backend restarts. */
  restartRequired: boolean;
}
export interface SettingRow extends Setting {
  overridden: boolean;
}
