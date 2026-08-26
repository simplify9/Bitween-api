import type { AddBusRouteInput, AttachPartnerInput } from "./http/gateways";
import type {
  AdapterInfo,
  AdapterKind,
  ApiGateway,
  ApiGatewayAttachment,
  ApiGatewayDetail,
  ApiGatewayRow,
  BusGateway,
  BusGatewayDetail,
  BusGatewayRow,
  DashboardData,
  ExchangeQuery,
  ExchangeRow,
  GlobalValuesSetDetail,
  GlobalValuesSetRow,
  InformationType,
  InformationTypeDetail,
  InformationTypeRow,
  Subscription,
  SubscriptionDetail,
  SubscriptionInfo,
  SubscriptionLastRun,
  SubscriptionRow,
  SubscriptionRun,
  SubscriptionType,
  MatchGroup,
  Notifier,
  NotifierDetail,
  Paged,
  Partner,
  PartnerDetail,
  PartnerRow,
  PermissionArea,
  PermissionKey,
  QueueHealthSnapshot,
  ReceiveAttemptRow,
  ReceiveOutcome,
  RetryGroup,
  RetryAlertConfig,
  RetryAttempts,
  RetryPolicy,
  RetryPolicyDetail,
  RetryPolicyListRow,
  RetryUsageRow,
  RetryResultType,
  RetryTestAttempt,
  Role,
  Schedule,
  ScheduledRetryQuery,
  ScheduledRetryRow,
  ScheduleHealth,
  Session,
  SettingRow,
  User,
  WorkGroup,
  WorkGroupDetail,
  WorkGroupRow,
} from "./types";

/**
 * The single data-access contract the UI is written against, implemented by
 * ./http/httpClient. Components depend on this interface rather than on fetch,
 * so an endpoint's shape can change in one place.
 */
export interface ApiClient {
  // — session —
  getSession(): Promise<Session | null>;
  login(email: string, password: string): Promise<Session>;
  loginWithMicrosoft(): Promise<Session>;
  logout(): Promise<void>;

  // — self service —
  // No password-reset flow exists on the backend yet (BACKEND_WIRING_PLAN.md G3) — it needs
  // outbound mail, which Bitween doesn't have. Hidden in the UI rather than faked.
  updateProfile(changes: { displayName: string }): Promise<Session>;
  changePassword(currentPassword: string, newPassword: string): Promise<void>;

  // — members —
  listUsers(): Promise<User[]>;
  getUser(id: string): Promise<User>;
  /**
   * Creates a member outright, password and all. Bitween has no outbound mail, so there's no
   * invite link — whoever adds the member passes the first password on themselves.
   */
  createUser(input: {
    displayName: string;
    email: string;
    password: string;
    roleIds: string[];
  }): Promise<User>;
  /** Stands in for a self-service reset, which would need mail Bitween doesn't have. */
  setUserPassword(id: string, password: string): Promise<void>;
  updateUserRoles(id: string, roleIds: string[]): Promise<User>;
  /** Clears a failed-sign-in lockout early, rather than waiting it out. */
  unlockUser(id: string): Promise<User>;
  setUserDisabled(id: string, disabled: boolean): Promise<User>;
  deleteUser(id: string): Promise<void>;

  // — roles —
  /** The catalog the backend enforces, so the role matrix can never offer a grant it ignores. */
  getPermissionCatalog(): Promise<PermissionArea[]>;
  listRoles(): Promise<Role[]>;
  getRole(id: string): Promise<Role>;
  createRole(input: { name: string; description: string; permissions: PermissionKey[] }): Promise<Role>;
  updateRole(
    id: string,
    input: { name: string; description: string; permissions: PermissionKey[] },
  ): Promise<Role>;
  deleteRole(id: string): Promise<void>;

  // — partners —
  listPartners(): Promise<PartnerRow[]>;
  searchPartners(query: { search: string; offset: number; limit: number }): Promise<Paged<PartnerRow>>;
  getPartner(id: number): Promise<PartnerDetail>;
  /** Light fetch used by the mapper editor's test-partner selector. */
  getPartnerAdapterProperties(id: number): Promise<Record<string, string>>;
  createPartner(input: { name: string; adapterProperties?: Record<string, string> }): Promise<Partner>;
  updatePartner(
    id: number,
    changes: { name?: string; adapterProperties?: Record<string, string> },
  ): Promise<Partner>;
  deletePartner(id: number): Promise<void>;
  /** Returns the full key exactly once; afterwards only a prefix is ever shown. */
  addPartnerCredential(id: number, name: string): Promise<{ key: string }>;
  revokePartnerCredential(id: number, name: string): Promise<void>;

  // — information types —
  listInformationTypes(): Promise<InformationTypeRow[]>;
  searchInformationTypes(query: {
    search: string;
    offset: number;
    limit: number;
  }): Promise<Paged<InformationTypeRow>>;
  getInformationType(id: number): Promise<InformationTypeDetail>;
  /** Same payload as update: a new type arrives complete, promoted properties included. */
  createInformationType(
    input: Omit<InformationType, "id" | "createdOn">,
  ): Promise<InformationType>;
  updateInformationType(
    id: number,
    changes: Omit<InformationType, "id" | "createdOn">,
  ): Promise<InformationType>;
  deleteInformationType(id: number): Promise<void>;

  // — global values —
  listValueSets(): Promise<GlobalValuesSetRow[]>;
  getValueSet(id: string): Promise<GlobalValuesSetDetail>;
  createValueSet(input: {
    id: string;
    name: string;
    values: Record<string, string>;
  }): Promise<GlobalValuesSetRow>;
  updateValueSet(
    id: string,
    changes: { name: string; values: Record<string, string> },
  ): Promise<GlobalValuesSetRow>;
  deleteValueSet(id: string): Promise<void>;

  // — subscriptions (light summaries; cache aggressively) —
  listSubscriptions(): Promise<SubscriptionInfo[]>;

  // — subscriptions —
  listSubscriptionRows(): Promise<SubscriptionRow[]>;
  searchSubscriptionRows(query: {
    search: string;
    type: SubscriptionType | null;
    informationTypeId?: number | null;
    partnerId?: number | null;
    inactive?: boolean | null;
    offset: number;
    limit: number;
  }): Promise<Paged<SubscriptionRow>>;
  getSubscription(id: number): Promise<SubscriptionDetail>;
  /** One call, one transaction: the subscription exists as asked for, or not at all. */
  createSubscription(input: {
    type: SubscriptionType;
    name: string;
    informationTypeId: number;
    /** Required by the types that carry their own partner — Internal and ApiCall. */
    partnerId?: number | null;
    receiverId?: string | null;
    receiverProperties?: Record<string, string>;
    validatorId?: string | null;
    validatorProperties?: Record<string, string>;
    mapperId?: string | null;
    mapperProperties?: Record<string, string>;
    handlerId?: string | null;
    handlerProperties?: Record<string, string>;
    schedules?: Schedule[];
    retryPolicyId?: number | null;
    responseSubscriptionId?: number | null;
    responseMessageTypeName?: string | null;
    enabled?: boolean;
  }): Promise<Subscription>;
  updateSubscription(
    id: number,
    changes: Partial<
      Pick<
        Subscription,
        | "name"
        | "enabled"
        | "workGroupId"
        | "retryPolicyId"
        | "receiverId"
        | "receiverProperties"
        | "validatorId"
        | "validatorProperties"
        | "mapperId"
        | "mapperProperties"
        | "handlerId"
        | "handlerProperties"
        | "matchExpression"
        | "schedules"
        | "responseSubscriptionId"
        | "responseMessageTypeName"
      >
    >,
  ): Promise<Subscription>;
  deleteSubscription(id: number): Promise<void>;
  /** Toggles paused: paused subscriptions accept work but hold it. */
  pauseSubscription(id: number): Promise<Subscription>;
  receiveNow(id: number): Promise<Subscription>;
  /** Run history for one scheduled subscription, newest first. Empty for unscheduled types. */
  listSubscriptionRuns(id: number, limit?: number): Promise<SubscriptionRun[]>;
  searchReceiveAttempts(
    subscriptionId: number,
    query: { outcome: ReceiveOutcome | null; offset: number; limit: number },
  ): Promise<Paged<ReceiveAttemptRow>>;
  /** Newest run of every scheduled subscription — one request for a whole list. */
  listLastRuns(): Promise<SubscriptionLastRun[]>;
  /** Will these schedules actually fire? Asks the scheduler, not the subscription record. */
  listScheduleHealth(): Promise<ScheduleHealth[]>;
  listAdapters(kind: AdapterKind): Promise<AdapterInfo[]>;

  // — work groups —
  listWorkGroups(): Promise<WorkGroupRow[]>;
  searchWorkGroups(query: { search: string; offset: number; limit: number }): Promise<Paged<WorkGroupRow>>;
  getWorkGroup(id: number): Promise<WorkGroupDetail>;
  createWorkGroup(input: {
    name: string;
    busMessageName: string;
    prefetch: number;
    priority: number;
  }): Promise<WorkGroup>;
  updateWorkGroup(
    id: number,
    changes: { name: string; busMessageName: string; prefetch: number; priority: number },
  ): Promise<WorkGroup>;
  deleteWorkGroup(id: number): Promise<void>;

  // — API gateways —
  listApiGateways(): Promise<ApiGatewayRow[]>;
  searchApiGateways(query: { search: string; offset: number; limit: number }): Promise<Paged<ApiGatewayRow>>;
  getApiGateway(id: number): Promise<ApiGatewayDetail>;
  searchGatewayAttachments(
    apiGatewayId: number,
    query: { search: string; offset: number; limit: number },
  ): Promise<Paged<ApiGatewayAttachment>>;
  createApiGateway(input: { name: string; urlName: string }): Promise<ApiGateway>;
  updateApiGateway(
    id: number,
    changes: { name: string; urlName: string; inactive: boolean },
  ): Promise<ApiGateway>;
  deleteApiGateway(id: number): Promise<void>;
  /** The subscription is either an existing id or defined inline; the endpoint commits both as one. */
  attachGatewayPartner(id: number, input: AttachPartnerInput): Promise<void>;
  updateGatewayAttachment(id: number, input: { partnerId: number; subscriptionId: number }): Promise<void>;
  removeGatewayAttachment(id: number, partnerId: number): Promise<void>;

  // — bus gateways —
  listBusGateways(): Promise<BusGatewayRow[]>;
  searchBusGateways(query: {
    search: string;
    informationTypeId?: number | null;
    inactive?: boolean | null;
    offset: number;
    limit: number;
  }): Promise<Paged<BusGatewayRow>>;
  getBusGateway(id: number): Promise<BusGatewayDetail>;
  createBusGateway(input: { name: string; informationTypeId: number }): Promise<BusGateway>;
  updateBusGateway(id: number, changes: { name: string; inactive: boolean }): Promise<BusGateway>;
  deleteBusGateway(id: number): Promise<void>;
  /** The subscription is either an existing id or defined inline; the endpoint commits both as one. */
  addBusRoute(id: number, input: AddBusRouteInput): Promise<void>;
  updateBusRoute(
    id: number,
    routeId: number,
    input: { subscriptionId: number; partnerId: number | null; matchExpression: MatchGroup | null },
  ): Promise<void>;
  removeBusRoute(id: number, routeId: number): Promise<void>;

  // — retry policies —
  listRetryPolicies(): Promise<RetryPolicyListRow[]>;
  searchRetryPolicies(query: {
    search: string;
    offset: number;
    limit: number;
  }): Promise<Paged<RetryPolicyListRow>>;
  getRetryPolicy(id: number): Promise<RetryPolicyDetail>;
  createRetryPolicy(input: { name: string }): Promise<RetryPolicy>;
  updateRetryPolicy(
    id: number,
    changes: {
      name: string;
      groups: RetryGroup[];
      alertHandlerId: string | null;
      alertHandlerProperties: Record<string, string>;
    },
  ): Promise<RetryPolicy>;
  deleteRetryPolicy(id: number): Promise<void>;
  /** Dry-runs draft groups against a simulated failure over N attempts. */
  testRetryPolicy(input: {
    groups: RetryGroup[];
    resultType: RetryResultType;
    content: string;
    attempts: number;
  }): Promise<RetryTestAttempt[]>;

  /** Spent budget and alert routing for every subscription-and-group pair under this policy. */
  getRetryUsage(policyId: number): Promise<RetryUsageRow[]>;
  /**
   * The same report for one subscription, which is the only way to reach one whose policy is an
   * inline `CustomRetryPolicy` — those carry no policy id for the policy-scoped report to address,
   * yet still spend budget and can sit stopped with no counter anyone can see.
   */
  getSubscriptionRetryUsage(subscriptionId: number): Promise<RetryUsageRow[]>;
  /** The failures one group caught for one subscription — what its spent budget went on. */
  getRetryAttempts(policyId: number, pair: { subscriptionId: number; groupId: string }): Promise<RetryAttempts>;
  /** Hands a spent budget back so the group retries again. Omit a field to reset across it. */
  resetRetryUsage(policyId: number, pair?: { subscriptionId?: number; groupId?: string }): Promise<void>;
  /** Reset by subscription, for the inline-policy case the policy-scoped reset cannot reach. */
  resetSubscriptionRetryUsage(subscriptionId: number, groupId?: string): Promise<void>;
  /** Sets, changes or clears where one pair's alert goes — the most specific level. */
  saveRetryAlertOverride(
    policyId: number,
    input: { subscriptionId: number; groupId: string } & RetryAlertConfig,
  ): Promise<void>;

  // — settings —
  listSettings(): Promise<SettingRow[]>;
  /** `value: null` resets the setting back to its default. */
  updateSetting(key: string, value: string | null): Promise<void>;

  // — notifiers —
  // No backend delete/test-send endpoint exists yet (BACKEND_WIRING_PLAN.md G8) — hidden in the UI.
  // Channel choices come from listAdapters("handler") — same catalog as any other handler slot.
  searchNotifiers(query: { search: string; offset: number; limit: number }): Promise<Paged<Notifier>>;
  getNotifier(id: number): Promise<NotifierDetail>;
  createNotifier(input: { name: string }): Promise<Notifier>;
  updateNotifier(id: number, changes: Omit<Notifier, "id" | "createdOn">): Promise<Notifier>;
  deleteNotifier(id: number): Promise<void>;

  // — exchanges —
  searchExchanges(query: ExchangeQuery): Promise<Paged<ExchangeRow>>;
  /** Fetches a stage document's raw text content by its storage key (`ExchangeFileRef.key`). */
  getExchangeDocument(key: string): Promise<string>;
  /**
   * Re-runs an exchange from its input file. `reset` re-resolves adapter
   * properties from the subscription's current configuration instead of the
   * values captured when the exchange first ran. Fails with
   * AUTO_RETRY_SCHEDULED when an auto-retry is already pending.
   */
  retryExchange(id: string, opts: { reset: boolean }): Promise<{ id: string }>;
  /** Retries many; exchanges with a pending auto-retry are skipped, not failed. */
  bulkRetryExchanges(ids: string[], opts: { reset: boolean }): Promise<{ retried: number; skipped: number }>;
  /** Manually injects a payload, addressed at a subscription or an information type. */
  createExchange(input: {
    target: "subscription" | "informationType";
    subscriptionId?: number;
    informationTypeId?: number;
    data: string;
  }): Promise<{ id: string }>;

  // — scheduled retries —
  searchScheduledRetries(query: ScheduledRetryQuery): Promise<Paged<ScheduledRetryRow>>;
  /** Executes a pending auto-retry immediately instead of waiting for its slot. */
  runScheduledRetryNow(id: string): Promise<void>;

  // — queue health —
  getQueueHealth(): Promise<QueueHealthSnapshot>;

  // — dashboard —
  getDashboard(): Promise<DashboardData>;

  // — mappers —
  /** Executes a Scriban template against sample input, injecting the partner's adapter properties and global value sets exactly as the runtime mapper does. */
  previewMapping(input: {
    scribanTemplate: string;
    inputJson: string;
    partnerId?: number | null;
  }): Promise<{ outputJson: string | null; error: string | null }>;
}
