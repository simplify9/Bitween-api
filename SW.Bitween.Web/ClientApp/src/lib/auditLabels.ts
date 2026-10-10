/**
 * The audit trail records the code's names: the class a row belongs to, and its properties. Shown
 * as they are, a partner's history read "SecretProperties", an information type was a "Document",
 * and a new row's change listed "Id, CreatedBy, CreatedOn". These are the names the screens use.
 */

/** What each recorded kind is called on screen. */
export const ENTITY_LABEL: Record<string, string> = {
  SubscriptionCategory: "Subscription category",
  ApiCredential: "API key",
  Document: "Information type",
  ApiGateway: "API gateway",
  ApiGatewayPartner: "Gateway attachment",
  BusGateway: "Bus gateway",
  BusGatewayRoute: "Bus gateway route",
  WorkGroup: "Work group",
  RetryPolicy: "Retry policy",
  RetryAlertOverride: "Retry alert override",
  GlobalAdapterValuesSet: "Global value set",
  Account: "Member",
  AccountRoleLink: "Member's role",
  OperatorAction: "Operation",
  AdapterDraft: "Adapter draft",
  AdapterRelease: "Adapter release",
  AdapterSourceAccess: "Adapter source read",
};

export const entityLabel = (entity: string): string => ENTITY_LABEL[entity] ?? humanize(entity);

/** Names that say something different on screen than the property does, by kind; "*" for any. */
const PROPERTY_LABEL: Record<string, Record<string, string>> = {
  "*": {
    Inactive: "Switched off",
    SecretProperties: "Which settings are secret",
    AdapterProperties: "Settings",
    DocumentId: "Information type",
    PartnerId: "Partner",
    SubscriptionId: "Subscription",
    WorkGroupId: "Work group",
    RetryPolicyId: "Retry policy",
    CategoryId: "Category",
    DataSourceId: "Data source",
  },
  Subscription: {
    HandlerId: "Delivery adapter",
    HandlerProperties: "Delivery settings",
    MapperId: "Mapping adapter",
    MapperProperties: "Mapping settings",
    ReceiverId: "Receiving adapter",
    ReceiverProperties: "Receiving settings",
    ValidatorId: "Validation adapter",
    ValidatorProperties: "Validation settings",
    DocumentFilter: "Filter",
    MatchExpression: "Match rules",
    AggregateOn: "Rolls up on",
    AggregationForId: "Rolls up",
    PausedOn: "Paused",
    IsRunning: "Running mark",
    RunningSince: "Running since",
    ResponseSubscriptionId: "Response goes to",
    ResponseMessageTypeName: "Response bus message",
    CustomRetryPolicy: "Own retry rules",
  },
  Document: {
    BusEnabled: "Published to the bus",
    BusMessageTypeName: "Bus message name",
    PromotedProperties: "Promoted properties",
    DocumentFormat: "Format",
    DuplicateInterval: "Duplicate check (minutes)",
  },
  Partner: { LoginIdentity: "Token identity" },
  Account: {
    DisplayName: "Name",
    LastSignInOn: "Signed in",
    FailedLoginCount: "Failed sign-ins",
    LockoutEnd: "Locked until",
    MustChangePassword: "Must change password",
    MicrosoftIdentity: "Microsoft account",
  },
  OperatorAction: { Target: "On", Detail: "Detail", AccountId: "By" },
  AdapterDraft: { FilesHash: "Files (hash)" },
};

/** Kept by the database for itself; not what anyone changed. */
const BOOKKEEPING = new Set(["Id", "CreatedOn", "CreatedBy", "ModifiedOn", "ModifiedBy", "OccurredOn"]);

export const isBookkeeping = (property: string): boolean => BOOKKEEPING.has(property);

export const propertyLabel = (entity: string | undefined, property: string): string =>
  (entity && PROPERTY_LABEL[entity]?.[property]) ?? PROPERTY_LABEL["*"][property] ?? humanize(property);

/** "SecretProperties" → "Secret properties", "UrlName" → "Url name". */
export function humanize(name: string): string {
  const words = name
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}
