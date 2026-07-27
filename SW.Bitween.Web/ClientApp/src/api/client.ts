import type {
  AdapterInfo,
  AdapterKind,
  ApiGateway,
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
  InformationTypeFormat,
  InformationTypeRow,
  Integration,
  IntegrationDetail,
  IntegrationInfo,
  IntegrationRow,
  IntegrationType,
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
  RetryGroup,
  RetryPolicy,
  RetryPolicyDetail,
  RetryPolicyListRow,
  RetryResultType,
  RetryTestAttempt,
  Role,
  Schedule,
  ScheduledRetryQuery,
  ScheduledRetryRow,
  Session,
  SettingRow,
  User,
  WorkGroup,
  WorkGroupDetail,
  WorkGroupRow,
} from "./types";

/**
 * The single data-access contract the UI is written against.
 * The mock implementation lives in ./mock; a real HTTP client can
 * replace it later without touching any component.
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
  getPartner(id: number): Promise<PartnerDetail>;
  /** Light fetch used by the mapper editor's test-partner selector. */
  getPartnerAdapterProperties(id: number): Promise<Record<string, string>>;
  createPartner(input: { name: string }): Promise<Partner>;
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
  getInformationType(id: number): Promise<InformationTypeDetail>;
  createInformationType(input: {
    name: string;
    code: string;
    format: InformationTypeFormat;
    busEnabled?: boolean;
    busMessageTypeName?: string;
  }): Promise<InformationType>;
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

  // — integrations (light summaries; cache aggressively) —
  listIntegrations(): Promise<IntegrationInfo[]>;

  // — integrations —
  listIntegrationRows(): Promise<IntegrationRow[]>;
  getIntegration(id: number): Promise<IntegrationDetail>;
  /** Only Receiving / GatewayApiCall / BusGateway — always via the entry-point wizards. */
  createIntegration(input: {
    type: IntegrationType;
    name: string;
    informationTypeId: number;
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
    enabled?: boolean;
  }): Promise<Integration>;
  updateIntegration(
    id: number,
    changes: Partial<
      Pick<
        Integration,
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
        | "responseIntegrationId"
        | "responseMessageTypeName"
      >
    >,
  ): Promise<Integration>;
  deleteIntegration(id: number): Promise<void>;
  /** Toggles paused: paused integrations accept work but hold it. */
  pauseIntegration(id: number): Promise<Integration>;
  receiveNow(id: number): Promise<Integration>;
  listAdapters(kind: AdapterKind): Promise<AdapterInfo[]>;

  // — work groups —
  listWorkGroups(): Promise<WorkGroupRow[]>;
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
  getApiGateway(id: number): Promise<ApiGatewayDetail>;
  createApiGateway(input: { name: string; urlName: string }): Promise<ApiGateway>;
  updateApiGateway(id: number, changes: { name: string; urlName: string }): Promise<ApiGateway>;
  deleteApiGateway(id: number): Promise<void>;
  attachGatewayPartner(id: number, input: { partnerId: number; integrationId: number }): Promise<void>;
  updateGatewayAttachment(id: number, input: { partnerId: number; integrationId: number }): Promise<void>;
  removeGatewayAttachment(id: number, partnerId: number): Promise<void>;

  // — bus gateways —
  listBusGateways(): Promise<BusGatewayRow[]>;
  getBusGateway(id: number): Promise<BusGatewayDetail>;
  createBusGateway(input: { name: string; informationTypeId: number }): Promise<BusGateway>;
  updateBusGateway(id: number, changes: { name: string }): Promise<BusGateway>;
  deleteBusGateway(id: number): Promise<void>;
  addBusRoute(
    id: number,
    input: { integrationId: number; partnerId: number | null; matchExpression: MatchGroup | null },
  ): Promise<void>;
  updateBusRoute(
    id: number,
    routeId: number,
    input: { integrationId: number; partnerId: number | null; matchExpression: MatchGroup | null },
  ): Promise<void>;
  removeBusRoute(id: number, routeId: number): Promise<void>;

  // — retry policies —
  listRetryPolicies(): Promise<RetryPolicyListRow[]>;
  getRetryPolicy(id: number): Promise<RetryPolicyDetail>;
  createRetryPolicy(input: { name: string }): Promise<RetryPolicy>;
  updateRetryPolicy(id: number, changes: { name: string; groups: RetryGroup[] }): Promise<RetryPolicy>;
  deleteRetryPolicy(id: number): Promise<void>;
  /** Dry-runs draft groups against a simulated failure over N attempts. */
  testRetryPolicy(input: {
    groups: RetryGroup[];
    resultType: RetryResultType;
    content: string;
    attempts: number;
  }): Promise<RetryTestAttempt[]>;

  // — settings —
  listSettings(): Promise<SettingRow[]>;
  /** `value: null` resets the setting back to its default. */
  updateSetting(key: string, value: string | null): Promise<SettingRow>;

  // — notifiers —
  // No backend delete/test-send endpoint exists yet (BACKEND_WIRING_PLAN.md G8) — hidden in the UI.
  // Channel choices come from listAdapters("handler") — same catalog as any other handler slot.
  listNotifiers(): Promise<Notifier[]>;
  getNotifier(id: number): Promise<NotifierDetail>;
  createNotifier(input: { name: string }): Promise<Notifier>;
  updateNotifier(id: number, changes: Omit<Notifier, "id" | "createdOn">): Promise<Notifier>;

  // — exchanges —
  searchExchanges(query: ExchangeQuery): Promise<Paged<ExchangeRow>>;
  /** Fetches a stage document's raw text content by its storage key (`ExchangeFileRef.key`). */
  getExchangeDocument(key: string): Promise<string>;
  /**
   * Re-runs an exchange from its input file. `reset` re-resolves adapter
   * properties from the integration's current configuration instead of the
   * values captured when the exchange first ran. Fails with
   * AUTO_RETRY_SCHEDULED when an auto-retry is already pending.
   */
  retryExchange(id: string, opts: { reset: boolean }): Promise<{ id: string }>;
  /** Retries many; exchanges with a pending auto-retry are skipped, not failed. */
  bulkRetryExchanges(ids: string[], opts: { reset: boolean }): Promise<{ retried: number; skipped: number }>;
  /** Manually injects a payload, addressed at an integration or an information type. */
  createExchange(input: {
    target: "integration" | "informationType";
    integrationId?: number;
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
