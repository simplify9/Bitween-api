import type { AddBusRouteInput, AttachPartnerInput } from "./http/gateways";
import type {
  MirrorBring,
  MirrorBringSection,
  MirrorListed,
  MirrorListFilter,
  MirrorProdKinds,
  MirrorProdListAnswer,
  MirrorStatus,
  MirrorSubscriptionAnswer,
  MirrorTest,
  InviteResult,
  PasswordLinkInfo,
  AdapterInfo,
  KitInfo,
  KitInstallRequest,
  KitRun,
  Connection,
  ConnectionInput,
  AdapterKind,
  AggregationRetentionCheck,
  AggregationTarget,
  ApiGateway,
  ApiGatewayAttachment,
  AuditQuery,
  ApiGatewayDetail,
  ApiGatewayRow,
  BusGateway,
  BusGatewayDetail,
  BusGatewayRow,
  DataSourceProvider,
  GatewayAuthentication,
  GatewayFailureAnswer,
  DataSourceDetail,
  DataSourceInspectResult,
  DataSourceRow,
  DataSourceStatement,
  DataSourceStatementUsage,
  DataSourceTelemetry,
  DataSourceTestResult,
  DashboardData,
  ExchangeBreakdown,
  ExchangeQuery,
  ExchangeRow,
  ExportFilesCheck,
  BulkRetryPlan,
  BulkRetrySelection,
  RetryTree,
  GlobalValuesSetDetail,
  GlobalValuesSetRow,
  InformationType,
  InformationTypeDetail,
  InformationTypeFormat,
  InformationTypeRow,
  Subscription,
  SubscriptionDetail,
  SubscriptionInfo,
  SubscriptionLastRun,
  SubscriptionNotification,
  SubscriptionRow,
  SubscriptionRun,
  SubscriptionType,
  MatchGroup,
  NewResponseSubscriptionDraft,
  Notifier,
  NotifierDetail,
  NotificationChannelDetail,
  NotificationChannelRow,
  NotificationChannelSend,
  NotificationChannelTestResult,
  Paged,
  Partner,
  PartnerDetail,
  PartnerRow,
  PermissionArea,
  PermissionKey,
  IssuedServiceToken,
  ServiceAccount,
  QueueHealthSnapshot,
  UnattendedDeleteResult,
  DeadLetterMessage,
  ReceiveAttemptRow,
  SourcePaths,
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
  RetentionProposal,
  RetentionStatus,
  SettingRow,
  InstanceAbout,
  SubscriptionCategory,
  BackgroundWork,
  GatewayRejections,
  AdapterUploadResult,
  ClusterNodes,
  User,
  WorkGroup,
  WorkGroupDetail,
  TrailEntry,
  WorkGroupRow,
  AdapterSourceFile,
  AdapterSourceListing,
  AdapterDraft,
  AdapterDraftSummary,
  BundleInstall,
  MarketplaceInstallResult,
  MarketplacePackage,
  MarketplaceRegistration,
  MarketplaceSearch,
  RegistryCheckSummary,
  RegistryLicenceSummary,
  UsageReportPreview,
  SubscriptionRevision,
  SubscriptionRevisionDetail,
  DraftBuild,
  DraftLanguage,
  DraftPublishResult,
  DraftRun,
} from "./types";
import type { SaveResult } from "./http/dataSourceStatements";
import type {
  AdapterUsageDetail,
  AdapterUsageList,
  Insights,
  MetricsOverview,
  NodeMetrics,
  PartnerUsageList,
  SubscriptionHealthList,
  SubscriptionMetrics,
} from "./metricsTypes";

/**
 * The single data-access contract the UI is written against, implemented by
 * ./http/httpClient. Components depend on this interface rather than on fetch,
 * so an endpoint's shape can change in one place.
 */
export interface ApiClient {
  // - session -
  getSession(): Promise<Session | null>;
  login(email: string, password: string): Promise<Session>;
  loginWithMicrosoft(): Promise<Session>;
  /** The OpenID Connect provider's sign-in address to send the browser to. */
  startOidc(): Promise<string>;
  /** Finishes a single sign-on with what the provider sent the browser back with. */
  loginWithOidc(code: string, state: string): Promise<Session>;
  logout(): Promise<void>;

  // - self service -
  updateProfile(changes: { displayName: string }): Promise<Session>;
  changePassword(currentPassword: string, newPassword: string): Promise<void>;

  // - password links, from invite and reset emails; no session needed -
  /** Emails a reset link when the address is a member's. Answers the same either way. */
  forgotPassword(email: string): Promise<void>;
  readPasswordLink(token: string): Promise<PasswordLinkInfo>;
  /** Sets the password and spends the link. Returns the member's email, to sign in with. */
  usePasswordLink(token: string, password: string): Promise<{ email: string }>;

  // - service accounts -
  listServiceAccounts(): Promise<ServiceAccount[]>;
  createServiceAccount(input: { name: string; roleIds: number[] }): Promise<number>;
  updateServiceAccount(id: number, input: { name: string; roleIds: number[] }): Promise<void>;
  setServiceAccountDisabled(id: number, disabled: boolean): Promise<void>;
  deleteServiceAccount(id: number): Promise<void>;
  issueServiceToken(id: number, input: { name: string; expiresInDays: number }): Promise<IssuedServiceToken>;
  revokeServiceToken(id: number, tokenId: number): Promise<void>;

  // - members -
  listUsers(): Promise<User[]>;
  getUser(id: string): Promise<User>;
  /**
   * Creates a member. With `invite` they choose their own password from an invite (see
   * `inviteUser`); otherwise whoever adds them sets it here and passes it on.
   */
  createUser(input: {
    displayName: string;
    email: string;
    password?: string;
    invite?: boolean;
    roleIds: string[];
  }): Promise<User>;
  /** Invites a member with no password yet: emailed, or a link handed back to pass on. Replaces any earlier one. */
  inviteUser(id: string): Promise<InviteResult>;
  /** Sets someone else's password, for when they can't reset it themselves. */
  setUserPassword(id: string, password: string): Promise<void>;
  updateUserRoles(id: string, roleIds: string[]): Promise<User>;
  /** Clears a failed-sign-in lockout early, rather than waiting it out. */
  unlockUser(id: string): Promise<User>;
  setUserDisabled(id: string, disabled: boolean): Promise<User>;
  deleteUser(id: string): Promise<void>;

  // - roles -
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

  // - partners -
  listPartners(): Promise<PartnerRow[]>;
  searchPartners(query: { search: string; offset: number; limit: number }): Promise<Paged<PartnerRow>>;
  getPartner(id: number): Promise<PartnerDetail>;
  /** Light fetch used by the mapper editor's test-partner selector. */
  getPartnerAdapterProperties(id: number): Promise<Record<string, string>>;
  createPartner(input: {
    name: string;
    adapterProperties?: Record<string, string>;
    secretProperties?: string[];
    loginIdentity?: string | null;
  }): Promise<Partner>;
  updatePartner(
    id: number,
    changes: {
      name?: string;
      adapterProperties?: Record<string, string>;
      secretProperties?: string[];
      /** Left out, the stored identity is kept; null clears it. */
      loginIdentity?: string | null;
    },
  ): Promise<Partner>;
  deletePartner(id: number): Promise<void>;
  /** Returns the full key exactly once; afterwards only a prefix is ever shown. */
  addPartnerCredential(id: number, name: string): Promise<{ key: string }>;
  revokePartnerCredential(id: number, name: string): Promise<void>;

  // - information types -
  listInformationTypes(): Promise<InformationTypeRow[]>;
  searchInformationTypes(query: {
    search: string;
    format?: InformationTypeFormat | null;
    busEnabled?: boolean | null;
    offset: number;
    limit: number;
  }): Promise<Paged<InformationTypeRow>>;
  getInformationType(id: number): Promise<InformationTypeDetail>;
  /** Same payload as update: a new type arrives complete, promoted properties included. */
  // `retiredOn` and `isSystem` are absent from both on purpose: retiring is its own command
  // and being built-in is the backend's to say, so neither is a field a save can carry -
  // otherwise a form that round-trips a type could change them by accident.
  createInformationType(
    input: Omit<InformationType, "id" | "createdOn" | "retiredOn" | "isSystem">,
  ): Promise<InformationType>;
  updateInformationType(
    id: number,
    changes: Omit<InformationType, "id" | "createdOn" | "retiredOn" | "isSystem">,
  ): Promise<InformationType>;
  deleteInformationType(id: number): Promise<void>;
  /** Toggles: retires a type that is in use, restores one that is retired. */
  retireInformationType(id: number): Promise<{ retiredOn: string | null }>;

  // - connections -
  listConnections(): Promise<Connection[]>;
  getConnection(id: number): Promise<Connection>;
  createConnection(input: ConnectionInput): Promise<Connection>;
  updateConnection(id: number, input: ConnectionInput): Promise<Connection>;
  deleteConnection(id: number): Promise<void>;
  authorizeConnection(id: number): Promise<string>;
  completeConnection(code: string, state: string): Promise<{ id: number; name: string }>;
  testConnection(id: number): Promise<Connection>;

  // - global values -
  listValueSets(): Promise<GlobalValuesSetRow[]>;
  getValueSet(id: string): Promise<GlobalValuesSetDetail>;
  createValueSet(input: {
    id: string;
    name: string;
    values: Record<string, string>;
    secretProperties?: string[];
  }): Promise<GlobalValuesSetRow>;
  updateValueSet(
    id: string,
    changes: { name: string; values: Record<string, string>; secretProperties?: string[] },
  ): Promise<GlobalValuesSetRow>;
  deleteValueSet(id: string): Promise<void>;

  // - subscriptions (light summaries; cache aggressively) -
  listSubscriptions(): Promise<SubscriptionInfo[]>;

  // - subscriptions -
  listSubscriptionRows(): Promise<SubscriptionRow[]>;
  searchSubscriptionRows(query: {
    search: string;
    type: SubscriptionType | null;
    informationTypeId?: number | null;
    partnerId?: number | null;
    categoryId?: number | null;
    inactive?: boolean | null;
    /** On a mirror: only those prod has too, or only those whose test says this. */
    mirror?: MirrorListFilter | null;
    offset: number;
    limit: number;
  }): Promise<Paged<SubscriptionRow>>;
  getSubscription(id: number): Promise<SubscriptionDetail>;
  /** One call, one transaction: the subscription exists as asked for, or not at all. */
  createSubscription(input: {
    type: SubscriptionType;
    name: string;
    informationTypeId: number;
    /** Required by the types that carry their own partner - Internal and ApiCall. */
    partnerId?: number | null;
    /** Aggregation only: whose exchanges get rolled up. Required, and fixed once created. */
    aggregationForId?: number | null;
    /** Aggregation only: which file of each collected exchange the roll-up links to. */
    aggregationTarget?: AggregationTarget;
    receiverId?: string | null;
    receiverProperties?: Record<string, string>;
    validatorId?: string | null;
    validatorProperties?: Record<string, string>;
    mapperId?: string | null;
    mapperProperties?: Record<string, string>;
    handlerId?: string | null;
    handlerProperties?: Record<string, string>;
    schedules?: Schedule[];
    /** Which lane it runs in. The API has always taken it; no create page used to ask. */
    workGroupId?: number | null;
    /** The connection its adapters go through, when one of them needs a data source. */
    dataSourceId?: number | null;
    retryPolicyId?: number | null;
    responseSubscriptionId?: number | null;
    responseMessageTypeName?: string | null;
    /** Response type only: also run when the delivery that fed it came back bad. */
    runOnBadResponses?: boolean;
    /** Who hears about its finished exchanges, and through which channel. */
    notifications?: SubscriptionNotification[];
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
        | "runOnBadResponses"
        | "notifications"
        | "aggregationTarget"
        | "autoPauseAfterFailures"
        | "expectTraffic"
        | "receiverVersion"
        | "validatorVersion"
        | "mapperVersion"
        | "handlerVersion"
      >
    > & {
      /** Create this response subscription in the same save and hand the response to it. */
      newResponseSubscription?: NewResponseSubscriptionDraft | null;
    },
  ): Promise<Subscription>;
  deleteSubscription(id: number): Promise<void>;
  /** Toggles paused: paused subscriptions accept work but hold it. */
  pauseSubscription(id: number): Promise<Subscription>;
  receiveNow(id: number): Promise<Subscription>;
  clearRunning(id: number): Promise<{ cleared: boolean }>;
  /** Runs an aggregation's roll-up now instead of waiting for its schedule. */
  aggregateNow(id: number): Promise<Subscription>;
  /** Run history for one scheduled subscription, newest first. Empty for unscheduled types. */
  listSubscriptionRuns(id: number, limit?: number): Promise<SubscriptionRun[]>;
  /** The subscription's versions, newest first. */
  listSubscriptionRevisions(id: number): Promise<SubscriptionRevision[]>;
  getSubscriptionRevision(id: number, number: number): Promise<SubscriptionRevisionDetail>;
  getSubscriptionConfiguration(id: number): Promise<Record<string, unknown>>;
  /** Saves a version's configuration as the subscription. Needs subscriptions.edit. */
  restoreSubscriptionRevision(id: number, number: number): Promise<void>;
  /** Where the subscription's receiver last got to, when it keeps a bookmark. */
  getBookmark(subscriptionId: number): Promise<{ value: string | null; updatedOn: string | null }>;
  /** Forgets it, so the receiver's next run starts from the beginning. */
  forgetBookmark(subscriptionId: number): Promise<void>;
  searchReceiveAttempts(
    subscriptionId: number,
    query: { outcome: ReceiveOutcome | null; offset: number; limit: number },
  ): Promise<Paged<ReceiveAttemptRow>>;
  /** The paths in the last document a subscription received. Needs exchanges.view. */
  getSourcePaths(subscriptionId: number): Promise<SourcePaths>;
  /** Newest run of every scheduled subscription - one request for a whole list. */
  listLastRuns(): Promise<SubscriptionLastRun[]>;
  /** Will these schedules actually fire? Asks the scheduler, not the subscription record. */
  listScheduleHealth(): Promise<ScheduleHealth[]>;
  listAdapters(kind: AdapterKind): Promise<AdapterInfo[]>;
  /** The source files a published version carries. Needs adapter-source.view. */
  getAdapterSource(adapterId: string, version: string): Promise<AdapterSourceListing>;
  /** One source file of a published version; each read is audited. Needs adapter-source.view. */
  getAdapterSourceFile(adapterId: string, version: string, path: string): Promise<AdapterSourceFile>;

  // - the marketplace (browsing needs subscriptions.view; installing needs adapter-source.operate) -
  /** What the registry set under Settings lists; `configured` is false when none is. */
  searchMarketplace(q: string, tier: string): Promise<MarketplaceSearch>;
  getMarketplacePackage(id: string): Promise<MarketplacePackage>;
  /** Verifies a registry version's signatures and publishes it here, current only when asked. */
  installFromMarketplace(id: string, version: string, current: boolean): Promise<MarketplaceInstallResult>;
  /** What the last registry check found: newer versions and revoked ones, for adapters installed from it. */
  getMarketplaceUpdates(): Promise<RegistryCheckSummary>;
  /** Asks the registry now rather than waiting for the daily check (adapter-source.operate). */
  checkMarketplace(): Promise<RegistryCheckSummary>;
  /** A kit's connectors and the parameters it asks for. */
  getKit(id: string, version: string): Promise<KitInfo>;
  /** What installing a kit would do, changing nothing. */
  planKit(request: KitInstallRequest): Promise<KitRun>;
  /** Installs a kit: its connectors, then its configuration with the parameters filled in. */
  installKit(request: KitInstallRequest): Promise<KitRun>;
  /** Registers this Bitween with the registry by a registration code (settings.edit); the token becomes a setting. */
  registerWithRegistry(code: string, displayName: string): Promise<MarketplaceRegistration>;
  /** The licences held here, as last fetched. */
  getMarketplaceLicences(): Promise<RegistryLicenceSummary>;
  /** Fetches the licences from the registry now (adapter-source.operate). */
  refreshMarketplaceLicences(): Promise<RegistryLicenceSummary>;
  /** What a day's usage report says; sends it when asked (settings.edit). */
  marketplaceUsage(day: string | null, send: boolean): Promise<UsageReportPreview>;
  /** Installs the packages a bundle zip holds, each checked as an online install is (adapter-source.operate). */
  installBundle(file: Blob, current: boolean): Promise<BundleInstall>;

  // - the adapter editor (adapter-source.edit; publishing and promoting need adapter-source.operate) -
  listAdapterDrafts(): Promise<AdapterDraftSummary[]>;
  getAdapterDraft(id: number): Promise<AdapterDraft>;
  /** A new adapter from the template; resolves to the draft's id. */
  createAdapterDraft(input: { name: string; language: DraftLanguage; kind: string; adapterId?: string }): Promise<number>;
  /** A draft of a published Python or Node version, from its source. */
  draftFromVersion(adapterId: string, version: string): Promise<number>;
  /**
   * Saves the files. With `baseHash`, the draft's hash when it was opened or last saved here: a
   * save over someone else's since then is refused with DRAFT_CHANGED. Without it, saved regardless.
   */
  saveAdapterDraft(id: number, files: Record<string, string>, baseHash?: string): Promise<AdapterDraftSummary>;
  deleteAdapterDraft(id: number): Promise<void>;
  /** Builds it and checks it against its contracts with these settings, which are never stored. */
  buildAdapterDraft(id: number, settings: Record<string, string>, buildOnly?: boolean): Promise<DraftBuild>;
  tryAdapterDraft(id: number, settings: Record<string, string>, command: string, input: string): Promise<DraftRun>;
  publishAdapterDraft(id: number, input: { version: string; releaseNotes: string; settings: Record<string, string> }): Promise<DraftPublishResult>;
  /** Makes a published version the one that runs wherever nothing is pinned. */
  promoteAdapter(adapterId: string, version: string): Promise<void>;
  /** Takes a version out of use: still listed and still run where pinned, never pinned or made current again. */
  withdrawAdapterVersion(adapterId: string, version: string): Promise<void>;
  /** Publishes a package built by bitween adapter build, any language. */
  uploadAdapterPackage(file: Blob, options: { version: string; current: boolean; releaseNotes: string }): Promise<AdapterUploadResult>;

  // - work groups -
  listWorkGroups(): Promise<WorkGroupRow[]>;
  searchWorkGroups(query: { search: string; offset: number; limit: number }): Promise<Paged<WorkGroupRow>>;
  getWorkGroup(id: number): Promise<WorkGroupDetail>;
  createWorkGroup(input: {
    name: string;
    busMessageName: string;
    prefetch: number;
    priority: number;
    concurrency: number | null;
  }): Promise<WorkGroup>;
  updateWorkGroup(
    id: number,
    changes: { name: string; busMessageName: string; prefetch: number; priority: number; concurrency: number | null },
  ): Promise<WorkGroup>;
  deleteWorkGroup(id: number): Promise<void>;

  // - API gateways -
  listApiGateways(): Promise<ApiGatewayRow[]>;
  searchApiGateways(query: {
    search: string;
    inactive?: boolean | null;
    /** On a mirror: only those prod has too, or only those whose test says this. */
    mirror?: MirrorListFilter | null;
    offset: number;
    limit: number;
  }): Promise<Paged<ApiGatewayRow>>;
  getApiGateway(id: number): Promise<ApiGatewayDetail>;
  /** Calls to the gateway turned away, and why: kept by the node that answers, since it started. */
  getGatewayRejections(id: number): Promise<GatewayRejections>;
  searchGatewayAttachments(
    apiGatewayId: number,
    query: { search: string; offset: number; limit: number },
  ): Promise<Paged<ApiGatewayAttachment>>;
  createApiGateway(input: { name: string; urlName: string }): Promise<ApiGateway>;
  /** Leave `authentication` out to keep what the gateway has - pausing it must not change who can call. */
  updateApiGateway(
    id: number,
    changes: {
      name: string;
      urlName: string;
      inactive: boolean;
      authentication?: GatewayAuthentication;
      failureAnswer?: GatewayFailureAnswer;
    },
  ): Promise<ApiGateway>;
  deleteApiGateway(id: number): Promise<void>;
  /** The subscription is either an existing id or defined inline; the endpoint commits both as one. */
  attachGatewayPartner(id: number, input: AttachPartnerInput): Promise<void>;
  updateGatewayAttachment(id: number, input: { partnerId: number; subscriptionId: number }): Promise<void>;
  removeGatewayAttachment(id: number, partnerId: number): Promise<void>;

  // - bus gateways -
  listBusGateways(): Promise<BusGatewayRow[]>;
  searchBusGateways(query: {
    search: string;
    informationTypeId?: number | null;
    inactive?: boolean | null;
    /** On a mirror: only those prod has too, or only those whose test says this. */
    mirror?: MirrorListFilter | null;
    offset: number;
    limit: number;
  }): Promise<Paged<BusGatewayRow>>;
  getBusGateway(id: number): Promise<BusGatewayDetail>;
  createBusGateway(input: { name: string; informationTypeId: number }): Promise<BusGateway>;
  updateBusGateway(
    id: number,
    changes: {
      name: string;
      inactive: boolean;
      /** Omit to leave the source alone; null moves the gateway back onto the internal bus. */
      dataSourceId?: number | null;
      endpoint?: string | null;
    },
  ): Promise<BusGateway>;
  deleteBusGateway(id: number): Promise<void>;

  // --- Data sources ---
  /** What Bitween can connect to, and what each provider accepts. */
  listDataSourceProviders(): Promise<DataSourceProvider[]>;
  listDataSources(): Promise<DataSourceRow[]>;
  searchDataSources(query: {
    search: string;
    offset: number;
    limit: number;
  }): Promise<Paged<DataSourceRow>>;
  getDataSource(id: number): Promise<DataSourceDetail>;
  createDataSource(input: {
    name: string;
    adapterId: string;
    properties: Record<string, string>;
    secretProperties: string[];
    /** Broker, Relational, Document, ObjectStore or Http - the provider declares it. */
    kind?: string;
  }): Promise<{ id: number }>;
  updateDataSource(
    id: number,
    changes: {
      name: string;
      adapterId: string;
      kind: string;
      properties: Record<string, string>;
      secretProperties: string[];
      inactive: boolean;
      placement: string;
      deduplicationWindowDays: number;
      softMemoryLimitMb: number;
      hardMemoryLimitMb: number;
      cpuPercentLimit: number;
      cpuLimitSamples: number;
    },
  ): Promise<void>;
  deleteDataSource(id: number): Promise<void>;
  testDataSource(id: number): Promise<DataSourceTestResult>;
  getDataSourceTelemetry(id: number): Promise<DataSourceTelemetry>;
  /** Relays a read-only command (Discover, GetStats) to the adapter actually serving traffic. */
  inspectDataSource(
    id: number,
    command: string,
    /** Discover's filters: objectType, schema, nameLike, includeColumns, skip, take. */
    args?: Record<string, string>,
  ): Promise<DataSourceInspectResult>;

  // --- data source statements: the SQL a relational data source may run ---
  listDataSourceStatements(dataSourceId: number): Promise<DataSourceStatement[]>;
  createDataSourceStatement(
    dataSourceId: number,
    input: {
      name: string;
      sql: string;
      description?: string | null;
      workGroupId?: number | null;
      /** Only for a statement a receiver polls with - see DataSourceStatement. */
      cursorColumn?: string | null;
      keyColumn?: string | null;
    },
  ): Promise<SaveResult>;
  updateDataSourceStatement(
    id: number,
    changes: {
      name: string;
      sql: string;
      description?: string | null;
      workGroupId?: number | null;
      inactive: boolean;
      cursorColumn?: string | null;
      keyColumn?: string | null;
    },
  ): Promise<SaveResult>;
  deleteDataSourceStatement(id: number): Promise<void>;
  getDataSourceStatementUsage(id: number): Promise<DataSourceStatementUsage>;
  /** The subscription is either an existing id or defined inline; the endpoint commits both as one. */
  addBusRoute(id: number, input: AddBusRouteInput): Promise<void>;
  updateBusRoute(
    id: number,
    routeId: number,
    input: { subscriptionId: number; partnerId: number | null; matchExpression: MatchGroup | null },
  ): Promise<void>;
  removeBusRoute(id: number, routeId: number): Promise<void>;

  // - retry policies -
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
      alertChannelId: number | null;
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
   * inline `CustomRetryPolicy` - those carry no policy id for the policy-scoped report to address,
   * yet still spend budget and can sit stopped with no counter anyone can see.
   */
  getSubscriptionRetryUsage(subscriptionId: number): Promise<RetryUsageRow[]>;
  /** The failures one group caught for one subscription - what its spent budget went on. */
  getRetryAttempts(policyId: number, pair: { subscriptionId: number; groupId: string }): Promise<RetryAttempts>;
  /** Hands a spent budget back so the group retries again. Omit a field to reset across it. */
  resetRetryUsage(policyId: number, pair?: { subscriptionId?: number; groupId?: string }): Promise<void>;
  /** Reset by subscription, for the inline-policy case the policy-scoped reset cannot reach. */
  resetSubscriptionRetryUsage(subscriptionId: number, groupId?: string): Promise<void>;
  /** Sets, changes or clears where one pair's alert goes - the most specific level. */
  saveRetryAlertOverride(
    policyId: number,
    input: { subscriptionId: number; groupId: string } & RetryAlertConfig,
  ): Promise<void>;

  // - prod mirror -
  getMirror(): Promise<MirrorStatus>;
  getMirrorProdKinds(): Promise<MirrorProdKinds>;
  getMirrorProd(section: MirrorBringSection, type?: string): Promise<MirrorProdListAnswer>;
  bringFromProd(section: MirrorBringSection, keys: string[], apply: boolean): Promise<MirrorBring>;
  getMirrorSubscription(id: number): Promise<MirrorSubscriptionAnswer>;
  getMirrorTest(id: number): Promise<MirrorTest>;
  takeMirrorTest(id: number): Promise<MirrorTest>;
  runMirrorTest(id: number): Promise<MirrorTest>;
  clearMirrorTest(id: number): Promise<MirrorTest>;
  getMirrorListed(section: MirrorBringSection): Promise<MirrorListed>;
  testMirrorMany(section: MirrorBringSection, ids: number[]): Promise<{ subscriptions: string[] }>;
  stopMirrorTests(): Promise<{ dropped: number }>;
  mirrorFilledIn(section: string, key: string): Promise<{ closed: number }>;

  // - settings -
  listSettings(): Promise<SettingRow[]>;
  getAbout(): Promise<InstanceAbout>;

  // - subscription categories -
  listCategories(): Promise<SubscriptionCategory[]>;
  createCategory(input: { code: string; description: string }): Promise<number>;
  updateCategory(id: number, input: { code: string; description: string }): Promise<void>;
  /** Refused while any subscription is filed under it. */
  deleteCategory(id: number): Promise<void>;
  getBackgroundWork(): Promise<BackgroundWork>;
  getClusterNodes(): Promise<ClusterNodes>;
  /** How long exchanges and their files are kept under the saved settings. `refresh` re-reads the bucket's rules. */
  getRetention(refresh?: boolean): Promise<RetentionStatus>;
  /** The same, for settings not saved yet. Changes nothing. */
  previewRetention(proposal: RetentionProposal): Promise<RetentionStatus>;
  /** Whether exchanges would be removed before an aggregation on these schedules rolls them up. Changes nothing. */
  checkAggregationRetention(schedules: Schedule[]): Promise<AggregationRetentionCheck>;
  /** `value: null` resets the setting back to its default. */
  updateSetting(key: string, value: string | null): Promise<void>;

  // - notifiers -
  // No backend delete/test-send endpoint exists yet (BACKEND_WIRING_PLAN.md G8) - hidden in the UI.
  // Channel choices come from listAdapters("handler") - same catalog as any other handler slot.
  searchNotifiers(query: { search: string; offset: number; limit: number }): Promise<Paged<Notifier>>;
  getNotifier(id: number): Promise<NotifierDetail>;
  createNotifier(input: { name: string }): Promise<Notifier>;
  updateNotifier(id: number, changes: Omit<Notifier, "id" | "createdOn">): Promise<Notifier>;
  deleteNotifier(id: number): Promise<void>;

  // - notification channels -
  listNotificationChannels(): Promise<NotificationChannelRow[]>;
  /** Id and name only; readable by anyone signed in, for the pickers. */
  lookupNotificationChannels(): Promise<{ id: number; name: string }[]>;
  getNotificationChannel(id: number): Promise<NotificationChannelDetail>;
  createNotificationChannel(input: {
    name: string;
    handlerId: string;
    handlerProperties: Record<string, string>;
  }): Promise<number>;
  updateNotificationChannel(
    id: number,
    input: { name: string; handlerId: string; handlerProperties: Record<string, string> },
  ): Promise<void>;
  deleteNotificationChannel(id: number): Promise<void>;
  /** Sends a made-up notification through the channel as saved. */
  testNotificationChannel(id: number): Promise<NotificationChannelTestResult>;
  /** Newest first. `failedOnly` keeps the sends that didn't go through. */
  listNotificationChannelSends(
    id: number,
    query: { failedOnly: boolean; offset: number; limit: number },
  ): Promise<Paged<NotificationChannelSend>>;

  // - exchanges -
  searchExchanges(query: ExchangeQuery): Promise<Paged<ExchangeRow>>;
  getExchangeBreakdown(query: ExchangeQuery): Promise<ExchangeBreakdown>;

  // - audit trail -
  /** Who changed what, and when. Filters narrow it; with none set it is the whole trail. */
  searchAudit(query: AuditQuery): Promise<Paged<TrailEntry>>;
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
  bulkRetryExchanges(selection: BulkRetrySelection, opts: { reset: boolean }): Promise<BulkRetryPlan>;
  previewBulkRetry(selection: BulkRetrySelection, opts: { reset: boolean }): Promise<BulkRetryPlan>;
  /** The selection's input, mapped and handled files as one zip, a folder per exchange. */
  exportExchangeFiles(selection: BulkRetrySelection): Promise<{ blob: Blob; fileName: string }>;
  /** How many of the selection's exchanges the export would come without files for. Changes nothing. */
  checkExchangeFilesExport(selection: BulkRetrySelection): Promise<ExportFilesCheck>;
  getRetryTree(id: string): Promise<RetryTree>;
  /** Manually injects a payload, addressed at a subscription or an information type. */
  createExchange(input: {
    target: "subscription" | "informationType";
    subscriptionId?: number;
    informationTypeId?: number;
    data: string;
  }): Promise<{ id: string }>;

  // - scheduled retries -
  searchScheduledRetries(query: ScheduledRetryQuery): Promise<Paged<ScheduledRetryRow>>;
  /** Executes a pending auto-retry immediately instead of waiting for its slot. */
  runScheduledRetryNow(id: string): Promise<void>;

  // - queue health -
  getQueueHealth(): Promise<QueueHealthSnapshot>;
  /** Deletes unattended lanes (all of each one's queues) and whatever they hold. */
  deleteUnattendedQueues(queueNames: string[]): Promise<UnattendedDeleteResult>;
  /** The oldest messages in a dead-letter queue, left where they are. */
  browseDeadLetters(queueName: string, count?: number): Promise<DeadLetterMessage[]>;
  /** Sends a dead-letter queue's messages back to be tried again; resolves to how many went. */
  requeueDeadLetters(queueName: string): Promise<number>;

  // - dashboard -
  getDashboard(): Promise<DashboardData>;

  // Metrics, read from the hourly rows the server keeps.
  getMetricsOverview(): Promise<MetricsOverview>;
  getAdapterUsage(days: number): Promise<AdapterUsageList>;
  getAdapterUsageDetail(id: string, days: number): Promise<AdapterUsageDetail>;
  getSubscriptionHealth(days: number): Promise<SubscriptionHealthList>;
  getSubscriptionMetrics(id: number, days: number): Promise<SubscriptionMetrics>;
  getPartnerUsage(days: number): Promise<PartnerUsageList>;
  getNodeMetrics(): Promise<NodeMetrics>;
  getInsights(): Promise<Insights>;

  // - mappers -
  /** Executes a Scriban template against sample input, injecting the partner's adapter properties and global value sets exactly as the runtime mapper does. */
  previewMapping(input: {
    scribanTemplate: string;
    inputJson: string;
    partnerId?: number | null;
  }): Promise<{ outputJson: string | null; error: string | null }>;

  /**
   * Maps a sample document with unsaved rules, through the same read/map/write the exchange
   * pipeline runs - so what the editor shows is what a partner would receive.
   *
   * `ruleErrors` carries one entry per rule that could not be applied, keyed by its target path,
   * so the editor can mark the rows that are wrong. `error` is set instead when the mapping could
   * not be attempted at all, which is a problem with the document or the rules rather than with
   * any one row.
   */
  previewMappingRules(input: {
    mappingRules: string;
    sourceDocument: string;
    partnerId?: number | null;
  }): Promise<{
    outputDocument: string | null;
    contentType: string | null;
    ruleErrors: { target: string; reason: string }[];
    error: string | null;
  }>;

  /**
   * Asks the AI provider under Settings, AI for rules that turn the source sample into the target
   * sample. The server previews them before answering, so `matchesTarget` and `differences` say
   * how close they came. `refine` fixes `mappingRules` instead of writing new ones; either way,
   * their formats are kept. `instructions` go to the model as they are. Partner and global
   * values go by name only, never by value.
   */
  suggestMappingRules(
    input: {
      mappingRules: string;
      sourceDocument: string;
      targetDocument: string;
      partnerId?: number | null;
      refine?: boolean;
      instructions?: string;
    },
    signal?: AbortSignal,
  ): Promise<{
    mappingRules: string;
    differences: { target: string; reason: string }[];
    matchesTarget: boolean;
    model: string | null;
  }>;
}
