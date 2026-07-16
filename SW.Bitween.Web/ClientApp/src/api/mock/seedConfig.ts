import type {
  ApiGateway,
  BusGateway,
  ExchangeDocument,
  ExchangeRef,
  GlobalValuesSet,
  InformationType,
  Integration,
  MatchGroup,
  NotificationEntry,
  Notifier,
  Partner,
  RetryPolicy,
  Setting,
  TrailEntry,
  WorkGroup,
} from "../types";

const daysAgo = (n: number) => new Date(Date.now() - n * 86_400_000).toISOString();
const minutesAgo = (n: number) => new Date(Date.now() - n * 60_000).toISOString();

/** Full credential keys, kept mock-side only; the API surface exposes prefixes. */
export interface StoredCredential {
  partnerId: number;
  name: string;
  key: string;
  createdOn: string;
}

export const SEED_PARTNERS: Partner[] = [
  {
    id: 1,
    name: "SYSTEM",
    adapterProperties: {},
    isSystem: true,
    createdOn: daysAgo(400),
  },
  {
    id: 2,
    name: "Coral Retail",
    adapterProperties: {
      storeId: "CR-114",
      callbackUrl: "https://coral-retail.example/webhooks/bitween",
    },
    isSystem: false,
    createdOn: daysAgo(320),
  },
  {
    id: 3,
    name: "SwiftShip Couriers",
    adapterProperties: {
      accountNumber: "SS-88210",
      region: "MENA",
    },
    isSystem: false,
    createdOn: daysAgo(210),
  },
  {
    id: 4,
    name: "Atlas Freight",
    adapterProperties: {},
    isSystem: false,
    createdOn: daysAgo(40),
  },
];

export const SEED_CREDENTIALS: StoredCredential[] = [
  { partnerId: 1, name: "System key", key: "7facc758283844b49cc4ffd26a75b1de", createdOn: daysAgo(400) },
  { partnerId: 2, name: "Production", key: "b31c09d41f2a4c5e8a67d90f13b8ae52", createdOn: daysAgo(310) },
  { partnerId: 2, name: "Staging", key: "0d94ee12aa374f6bb2c81f4de95c7301", createdOn: daysAgo(90) },
  { partnerId: 3, name: "Default", key: "5a7d20c3ef98442e9b10c66a84d2f7b9", createdOn: daysAgo(200) },
];

export const SEED_INFORMATION_TYPES: InformationType[] = [
  {
    id: 1001,
    code: "PURCHASE_ORDER",
    name: "Purchase order",
    format: "Json",
    busEnabled: true,
    busMessageTypeName: "purchase-order",
    duplicateIntervalMinutes: 60,
    disregardsUnfilteredMessages: false,
    promotedProperties: [
      { key: "OrderNumber", path: "$.order.id" },
      { key: "Store", path: "$.order.storeId" },
    ],
    createdOn: daysAgo(320),
  },
  {
    id: 1002,
    code: "SHIPMENT_UPDATE",
    name: "Shipment status update",
    format: "Json",
    busEnabled: true,
    busMessageTypeName: "shipment-update",
    duplicateIntervalMinutes: 0,
    disregardsUnfilteredMessages: true,
    promotedProperties: [
      { key: "TrackingNumber", path: "$.tracking.number" },
      { key: "Status", path: "$.tracking.status" },
    ],
    createdOn: daysAgo(210),
  },
  {
    id: 1003,
    code: "INVOICE",
    name: "Customer invoice",
    format: "Xml",
    busEnabled: false,
    duplicateIntervalMinutes: 1440,
    disregardsUnfilteredMessages: false,
    promotedProperties: [{ key: "InvoiceNumber", path: "//Invoice/Number" }],
    createdOn: daysAgo(180),
  },
  {
    id: 10001,
    code: "AGGREGATION_RESULT",
    name: "Aggregation result",
    format: "Json",
    busEnabled: false,
    duplicateIntervalMinutes: 0,
    disregardsUnfilteredMessages: false,
    promotedProperties: [],
    createdOn: daysAgo(400),
  },
];

export const SEED_TRAILS: Record<number, TrailEntry[]> = {
  1001: [
    { on: daysAgo(320), action: "Created", by: "Lina Haddad", byUserId: "u-lina" },
    { on: daysAgo(35), action: "Updated", by: "Omar Nasser", byUserId: "u-omar" },
  ],
  1002: [{ on: daysAgo(210), action: "Created", by: "Omar Nasser", byUserId: "u-omar" }],
  1003: [{ on: daysAgo(180), action: "Created", by: "Lina Haddad", byUserId: "u-lina" }],
  10001: [{ on: daysAgo(400), action: "Created", by: "Admin" }],
};

export const SEED_VALUE_SETS: GlobalValuesSet[] = [
  {
    id: "sap-prod",
    name: "SAP production",
    values: {
      baseUrl: "https://sap.northline.example",
      client: "100",
      companyCode: "NL01",
    },
    createdOn: daysAgo(300),
  },
  {
    id: "warehouse-defaults",
    name: "Warehouse defaults",
    values: {
      pickupLocation: "AMM-Central-01",
      timezone: "Asia/Amman",
    },
    createdOn: daysAgo(120),
  },
];

/** Common defaults so integration seeds stay readable. */
const baseIntegration = {
  partnerId: null,
  enabled: true,
  pausedOn: null,
  workGroupId: null,
  retryPolicyId: null,
  receiverId: null,
  receiverProperties: {},
  validatorId: null,
  validatorProperties: {},
  mapperId: null,
  mapperProperties: {},
  handlerId: null,
  handlerProperties: {},
  matchExpression: null,
  schedules: [],
  responseIntegrationId: null,
  responseMessageTypeName: null,
  aggregationForId: null,
  isRunning: false,
  lastReceiveOn: null,
  consecutiveFailures: 0,
  lastException: null,
} satisfies Partial<Integration>;

export const SEED_INTEGRATIONS: Integration[] = [
  {
    ...baseIntegration,
    id: 501,
    name: "Coral orders receiver",
    type: "Receiving",
    informationTypeId: 1001,
    retryPolicyId: 1,
    receiverId: "NativeHttpReceiver",
    receiverProperties: {
      url: "https://coral-retail.example/api/orders?status=new",
      authHeader: "coral-demo-token-not-real",
    },
    handlerId: "NativeHttpHandler",
    handlerProperties: {
      url: "{{globals.sap-prod.baseUrl}}/orders/import?client={{globals.sap-prod.client}}",
      method: "POST",
    },
    schedules: [{ recurrence: "Hourly", days: 0, hours: 0, minutes: 15, backwards: false }],
    lastReceiveOn: minutesAgo(48),
    createdOn: daysAgo(300),
  },
  {
    ...baseIntegration,
    id: 502,
    name: "SwiftShip tracking sync",
    type: "ApiCall",
    informationTypeId: 1002,
    partnerId: 3,
    retryPolicyId: 1,
    handlerId: "NativeHttpHandler",
    handlerProperties: {
      url: "https://api.swiftship.example/track/{{partner.accountNumber}}",
      method: "GET",
    },
    consecutiveFailures: 2,
    lastException: "HttpRequestException: 504 Gateway Timeout from api.swiftship.example",
    createdOn: daysAgo(280),
  },
  {
    ...baseIntegration,
    id: 503,
    name: "Invoice dispatch",
    type: "BusGateway",
    informationTypeId: 1003,
    workGroupId: 2,
    mapperId: "SW.Infolink.Adapters.Mappers.Liquid",
    mapperProperties: {
      template: '{"invoice": "{{ doc.Invoice.Number }}", "total": {{ doc.Invoice.Total }}}',
    },
    handlerId: "NativeHttpHandler",
    handlerProperties: {
      url: "{{partner.callbackUrl}}",
      method: "POST",
      headers: "X-Company: {{globals.sap-prod.companyCode}}",
    },
    createdOn: daysAgo(180),
  },
  {
    ...baseIntegration,
    id: 504,
    name: "Nightly PO aggregation",
    type: "Aggregation",
    informationTypeId: 10001,
    retryPolicyId: 2,
    aggregationForId: 501,
    handlerId: "NativeS3UploadHandler",
    handlerProperties: {
      bucket: "northline-agg",
      region: "me-south-1",
      accessKey: "AKIA-demo",
      secretKey: "s3-demo-secret",
      keyTemplate: "po-{{globals.warehouse-defaults.timezone}}/{date}.json",
    },
    schedules: [{ recurrence: "Daily", days: 0, hours: 2, minutes: 0, backwards: false }],
    createdOn: daysAgo(200),
  },
  {
    ...baseIntegration,
    id: 505,
    name: "Coral orders intake",
    type: "GatewayApiCall",
    informationTypeId: 1001,
    validatorId: "SW.Infolink.Adapters.Validators.JsonSchema",
    validatorProperties: { schema: '{ "required": ["order"] }' },
    handlerId: "NativeHttpHandler",
    handlerProperties: {
      url: "{{globals.sap-prod.baseUrl}}/orders/import",
      method: "POST",
      headers: "X-Store: {{partner.storeId}}",
    },
    createdOn: daysAgo(290),
  },
  {
    ...baseIntegration,
    id: 506,
    name: "SwiftShip tracking intake",
    type: "GatewayApiCall",
    informationTypeId: 1002,
    handlerId: "NativeHttpHandler",
    handlerProperties: { url: "https://tms.northline.example/api/tracking-events", method: "POST" },
    createdOn: daysAgo(200),
  },
  {
    ...baseIntegration,
    id: 507,
    name: "Coral PO forwarder",
    type: "BusGateway",
    informationTypeId: 1001,
    handlerId: "NativeS3UploadHandler",
    handlerProperties: {
      bucket: "coral-po-archive",
      region: "me-south-1",
      accessKey: "AKIA-demo",
      secretKey: "s3-demo-secret",
    },
    createdOn: daysAgo(120),
  },
];

export const SEED_WORK_GROUPS: WorkGroup[] = [
  {
    id: 1,
    name: "Priority lane",
    busMessageName: "priority-lane",
    options: { rabbitMqOptions: { consumerSettings: { prefetch: 4, priority: 10 } } },
    createdOn: daysAgo(260),
  },
  {
    id: 2,
    name: "Bulk uploads",
    busMessageName: "bulk-uploads",
    options: { rabbitMqOptions: { consumerSettings: { prefetch: 32, priority: 1 } } },
    createdOn: daysAgo(180),
  },
  {
    id: 3,
    name: "Nightly batches",
    busMessageName: "nightly-batches",
    options: { rabbitMqOptions: { consumerSettings: { prefetch: 16, priority: 3 } } },
    createdOn: daysAgo(60),
  },
];

export const SEED_INTEGRATION_TRAILS: Record<number, TrailEntry[]> = {
  501: [
    { on: daysAgo(300), action: "Created", by: "Lina Haddad", byUserId: "u-lina" },
    { on: daysAgo(20), action: "Updated", by: "Omar Nasser", byUserId: "u-omar" },
  ],
  505: [{ on: daysAgo(290), action: "Created", by: "Lina Haddad", byUserId: "u-lina" }],
};

export const SEED_RETRY_POLICIES: RetryPolicy[] = [
  {
    id: 1,
    name: "Transient failures",
    createdOn: daysAgo(150),
    groups: [
      {
        id: "g-timeouts",
        name: "Timeouts & connection drops",
        priority: 10,
        enabled: true,
        appliesTo: ["Error"],
        matchers: [
          { type: "contains", value: "timeout", caseSensitive: false },
          { type: "exceptionType", value: "HttpRequestException", includeInner: true },
        ],
        action: "Allow",
        budget: {
          maxAttemptsPerError: 3,
          maxAttemptsTotal: 10,
          delay: { type: "exponential", initialSeconds: 30, multiplier: 2, maxSeconds: 900 },
        },
        notes: "Network blips — safe to retry aggressively.",
      },
      {
        id: "g-auth",
        name: "Authentication failures",
        priority: 20,
        enabled: true,
        appliesTo: ["Error"],
        matchers: [{ type: "contains", value: "401", caseSensitive: false }],
        action: "Block",
        notes: "Retrying won't fix a bad credential — alert instead.",
      },
    ],
  },
  {
    id: 2,
    name: "Careful nightly jobs",
    createdOn: daysAgo(60),
    groups: [
      {
        id: "g-any",
        name: "Any failure",
        priority: 10,
        enabled: true,
        appliesTo: ["Error", "BadResult"],
        matchers: [],
        action: "Allow",
        budget: {
          maxAttemptsPerError: 2,
          maxAttemptsTotal: 4,
          delay: { type: "fixed", delaySeconds: 600 },
        },
      },
    ],
  },
];

export const SEED_NOTIFIERS: Notifier[] = [
  {
    id: 1,
    name: "Ops email on failures",
    enabled: true,
    onFailed: true,
    onBadResult: true,
    onSuccess: false,
    channelId: "sendgrid",
    channelProperties: {
      apiKey: "SG.demo-key-not-real",
      from: "bitween@northline.example",
      to: "ops@northline.example",
    },
    integrationIds: [501, 502, 505],
    createdOn: daysAgo(140),
  },
  {
    id: 2,
    name: "Teams alert — invoice dispatch",
    enabled: true,
    onFailed: true,
    onBadResult: false,
    onSuccess: false,
    channelId: "msteams",
    channelProperties: {
      webhookUrl: "https://outlook.office.com/webhook/a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d/IncomingWebhook/demo",
    },
    integrationIds: [503],
    createdOn: daysAgo(35),
  },
];

/** Delivery history for the seeded notifiers, referencing seeded exchange ids. */
export const seedNotifications = (): (NotificationEntry & { notifierId: number })[] => {
  const xid = (i: number) => `0x8DD${(0x41f000 + i * 3271).toString(16).toUpperCase()}`;
  return [
    { notifierId: 1, xchangeId: xid(2), success: true, on: minutesAgo(6 + 2 * 47) },
    {
      notifierId: 1,
      xchangeId: xid(14),
      success: false,
      exception: "SendGrid responded 401 Unauthorized — check the API key.",
      on: minutesAgo(6 + 14 * 47),
    },
    { notifierId: 1, xchangeId: xid(20), success: true, on: minutesAgo(6 + 20 * 47) },
  ];
};

/** Gateways as stored: attachments/routes are raw id links; names resolve at read time. */
export type StoredApiGateway = ApiGateway & {
  attachments: { partnerId: number; integrationId: number }[];
};
export type StoredBusGateway = BusGateway & {
  routes: { id: number; integrationId: number; partnerId: number | null; matchExpression: MatchGroup | null }[];
};

export const SEED_API_GATEWAYS: StoredApiGateway[] = [
  {
    id: 1,
    name: "Orders inbound",
    urlName: "orders",
    createdOn: daysAgo(290),
    attachments: [{ partnerId: 2, integrationId: 505 }],
  },
  {
    id: 2,
    name: "Tracking inbound",
    urlName: "tracking",
    createdOn: daysAgo(200),
    attachments: [{ partnerId: 3, integrationId: 506 }],
  },
];

export const SEED_BUS_GATEWAYS: StoredBusGateway[] = [
  {
    id: 1,
    name: "ERP events",
    informationTypeId: 1001,
    createdOn: daysAgo(250),
    routes: [
      {
        id: 1,
        integrationId: 507,
        partnerId: 2,
        matchExpression: { op: "and", children: [{ op: "oneOf", path: "Store", values: ["CR-114"] }] },
      },
    ],
  },
  {
    id: 2,
    name: "Invoice events",
    informationTypeId: 1003,
    createdOn: daysAgo(150),
    routes: [{ id: 1, integrationId: 503, partnerId: 2, matchExpression: null }],
  },
];

/** Length is coprime-ish with the source mix so failures spread across types. */
const EXCHANGE_STATUSES: ExchangeRef["status"][] = [
  "success", "success", "failed", "success", "processing", "success",
];

/**
 * Small sample documents per pipeline stage (input → mapped → handled) so
 * exchange rows can expand into per-stage previews. Failed and in-flight
 * exchanges only carry the input document.
 */
const SAMPLE_DOCS: Record<number, (i: number) => { input: string; mapped?: string; handled?: string }> = {
  1001: (i) => ({
    input: JSON.stringify(
      { order: { id: `PO-1${String(i).padStart(3, "0")}`, storeId: "CR-114", lines: [{ sku: "SKU-2231", qty: 3 + (i % 4) }], total: 118.4 + i } },
      null,
      2,
    ),
    mapped: JSON.stringify(
      { OrderHeader: { DocNum: `PO-1${String(i).padStart(3, "0")}`, SoldTo: "CR-114", Currency: "JOD" }, Items: [{ Material: "SKU-2231", Quantity: 3 + (i % 4) }] },
      null,
      2,
    ),
    handled: JSON.stringify({ sapDocumentNumber: `45000${1200 + i}`, status: "CREATED" }, null, 2),
  }),
  1002: (i) => ({
    input: JSON.stringify(
      { tracking: { number: `SS-7${String(i).padStart(4, "0")}`, status: i % 3 === 0 ? "OUT_FOR_DELIVERY" : "IN_TRANSIT", eta: "2026-07-14T09:00:00Z" } },
      null,
      2,
    ),
    mapped: JSON.stringify(
      { shipment: { reference: `SS-7${String(i).padStart(4, "0")}`, state: i % 3 === 0 ? "out-for-delivery" : "in-transit", expectedOn: "2026-07-14" } },
      null,
      2,
    ),
    handled: JSON.stringify({ acknowledged: true, storeNotified: i % 3 === 0 }, null, 2),
  }),
  1003: (i) => ({
    input: `<Invoice>\n  <Number>INV-9${String(i).padStart(3, "0")}</Number>\n  <CompanyCode>NL01</CompanyCode>\n  <Total currency="JOD">${(340 + i * 7).toFixed(2)}</Total>\n</Invoice>`,
    mapped: JSON.stringify(
      { invoice: { number: `INV-9${String(i).padStart(3, "0")}`, companyCode: "NL01", total: Number((340 + i * 7).toFixed(2)), currency: "JOD" } },
      null,
      2,
    ),
    handled: JSON.stringify({ delivered: true, channel: "sftp", file: `INV-9${String(i).padStart(3, "0")}.xml` }, null, 2),
  }),
  10001: (i) => ({
    input: JSON.stringify({ files: [`https://files.northline.example/agg/${i}-a.json`, `https://files.northline.example/agg/${i}-b.json`] }, null, 2),
    handled: JSON.stringify({ forwarded: true, fileCount: 2 }, null, 2),
  }),
};

const stageDocuments = (
  docs: { input: string; mapped?: string; handled?: string },
  status: ExchangeRef["status"],
): ExchangeDocument[] => {
  if (status !== "success") return [{ stage: "Input", content: docs.input }];
  return [
    { stage: "Input" as const, content: docs.input },
    ...(docs.mapped ? [{ stage: "Mapped" as const, content: docs.mapped }] : []),
    ...(docs.handled ? [{ stage: "Handled" as const, content: docs.handled }] : []),
  ];
};

export type SeedExchange = ExchangeRef & {
  partnerId: number | null;
  informationTypeId: number;
  integrationId: number;
};

/** Deterministic-ish recent traffic across partners, types and integrations. */
export const seedExchanges = (): SeedExchange[] => {
  const mix = [
    { partnerId: 2, informationTypeId: 1001, integrationId: 505 },
    { partnerId: 2, informationTypeId: 1003, integrationId: 503 },
    { partnerId: 3, informationTypeId: 1002, integrationId: 502 },
    { partnerId: null, informationTypeId: 10001, integrationId: 504 },
    { partnerId: null, informationTypeId: 1001, integrationId: 501 },
  ];
  return Array.from({ length: 24 }, (_, i) => {
    const source = mix[i % mix.length];
    const status = EXCHANGE_STATUSES[i % EXCHANGE_STATUSES.length];
    const docs = SAMPLE_DOCS[source.informationTypeId]?.(i);
    return {
      id: `0x8DD${(0x41f000 + i * 3271).toString(16).toUpperCase()}`,
      partnerId: source.partnerId,
      informationTypeId: source.informationTypeId,
      integrationId: source.integrationId,
      partnerName: undefined,
      informationTypeCode: "",
      status,
      on: minutesAgo(8 + i * 47),
      documents: docs ? stageDocuments(docs, status) : undefined,
    };
  });
};

export const SEED_SETTINGS: Setting[] = [
  // ——— Documents & storage ———
  {
    key: "Bitween.DocumentPrefix",
    section: "Documents & storage",
    label: "Document path prefix",
    description:
      "Add a custom prefix here if you want generated exchange documents stored under a different folder in the storage bucket — useful for keeping environments or tenants in separate trees.",
    kind: "string",
    defaultValue: "temp30/Bitweendocs",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Bitween.AreXChangeFilesPrivate",
    section: "Documents & storage",
    label: "Keep exchange files private",
    description:
      "Turn this on if you want generated exchange files kept private and served through short-lived signed links instead of public URLs.",
    kind: "boolean",
    defaultValue: "false",
    value: null,
    secret: false,
    restartRequired: false,
  },

  // ——— API behavior ———
  {
    key: "Bitween.ApiCallSubscriptionResponseAcceptedStatusCode",
    section: "API behavior",
    label: "Accepted response status code",
    description:
      "Change this if a partner's API expects a different HTTP status code (instead of 202 Accepted) when their request has been queued for async processing.",
    kind: "number",
    defaultValue: "202",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Bitween.JwtExpiryMinutes",
    section: "API behavior",
    label: "Sign-in session length (minutes)",
    description:
      "Shorten this for tighter session security, or lengthen it if teammates are being signed out more often than you'd like.",
    kind: "number",
    defaultValue: "60",
    value: "120",
    secret: false,
    restartRequired: false,
  },

  // ——— Single sign-on (Microsoft) ———
  {
    key: "Bitween.MsalClientId",
    section: "Single sign-on (Microsoft)",
    label: "Azure AD client ID",
    description:
      "Add this — together with the tenant ID and redirect URI below — if you want to let teammates sign in with a Microsoft account. All three are required for Microsoft sign-in to turn on.",
    kind: "string",
    defaultValue: "",
    value: null,
    secret: true,
    restartRequired: false,
  },
  {
    key: "Bitween.MsalTenantId",
    section: "Single sign-on (Microsoft)",
    label: "Azure AD tenant ID",
    description: "The Azure AD tenant Microsoft sign-in is restricted to. Required alongside the client ID and redirect URI.",
    kind: "string",
    defaultValue: "",
    value: null,
    secret: true,
    restartRequired: false,
  },
  {
    key: "Bitween.MsalRedirectUri",
    section: "Single sign-on (Microsoft)",
    label: "Azure AD redirect URI",
    description:
      "The URL Azure AD sends users back to after signing in — must match the redirect URI registered on the Azure AD app. Required alongside the client ID and tenant ID.",
    kind: "string",
    defaultValue: "",
    value: null,
    secret: true,
    restartRequired: false,
  },

  // ——— Messaging ———
  {
    key: "Bitween.BusDefaultQueuePrefetch",
    section: "Messaging",
    label: "Default queue prefetch",
    description:
      "Raise this if consumers are sitting idle while messages queue up; lower it if one busy queue is starving the others of a fair share.",
    kind: "number",
    defaultValue: "12",
    value: "24",
    secret: false,
    restartRequired: true,
  },

  // ——— Adapters ———
  {
    key: "Bitween.RebexLicenseKey",
    section: "Adapters",
    label: "Rebex POP3 license key",
    description:
      "Add this if you want to use the native Rebex POP3 receiver adapter for partners that deliver over POP3 — without a key, that adapter isn't offered when picking a receiver.",
    kind: "string",
    defaultValue: "",
    value: null,
    secret: true,
    restartRequired: true,
  },

  // ——— Reliability & jobs ———
  {
    key: "Bitween.ServerlessCommandTimeout",
    section: "Reliability & jobs",
    label: "Serverless command timeout (seconds)",
    description:
      "Increase this if long-running serverless commands are being cancelled before they finish; decrease it to fail fast on commands that hang.",
    kind: "number",
    defaultValue: "300",
    value: null,
    secret: false,
    restartRequired: true,
  },
  {
    key: "Bitween.ConsumeLegacyEventMessages",
    section: "Reliability & jobs",
    label: "Consume legacy event messages",
    description:
      "Keep this on while some publishers still emit the legacy (pre-migration) event shape. Turn it off once every publisher has moved to the current format.",
    kind: "boolean",
    defaultValue: "false",
    value: null,
    secret: false,
    restartRequired: true,
  },
  {
    key: "Bitween.RetryJobCron",
    section: "Reliability & jobs",
    label: "Auto-retry poll schedule (cron)",
    description:
      "Change this cron expression if you want the auto-retry job to check for due exchanges more or less often than once a minute.",
    kind: "string",
    defaultValue: "0 * * * * ?",
    value: null,
    secret: false,
    restartRequired: true,
  },

  // ——— Security ———
  {
    key: "Bitween.CorsOrigins",
    section: "Security",
    label: "Allowed CORS origins",
    description:
      "List the browser origins (e.g. https://your-app.com) that should be allowed to call the API with cookies attached. Add one if you're building a browser app that needs cookie-based auth against this API; leave empty to allow any origin without credentials.",
    kind: "string[]",
    defaultValue: "",
    value: null,
    secret: false,
    restartRequired: true,
  },

  // ——— Brand & theme ———
  {
    key: "Theme.PrimaryColor",
    section: "Brand & theme",
    label: "Primary color",
    description:
      "Re-brands the whole app's accent color — buttons, links, active nav, focus rings — instantly, without waiting on a deploy.",
    kind: "color",
    defaultValue: "#e3311d",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.CompanyName",
    section: "Brand & theme",
    label: "Company name",
    description: "Shown in the footer and used in a few page titles.",
    kind: "string",
    defaultValue: "Simplify9",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.TabTitle",
    section: "Brand & theme",
    label: "Browser tab title",
    description: "What shows in the browser tab.",
    kind: "string",
    defaultValue: "Bitween",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.TabIcon",
    section: "Brand & theme",
    label: "Favicon URL",
    description: "The icon shown in the browser tab. Paste a URL to an .ico, .svg or .png.",
    kind: "string",
    defaultValue: "/favicon.ico",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.LoginLogo",
    section: "Brand & theme",
    label: "Sign-in page logo",
    description: "The logo shown above the sign-in form.",
    kind: "string",
    defaultValue: "/Graphics/s9.png",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.BitweenLogo",
    section: "Brand & theme",
    label: "Sidebar logo",
    description: "The full logo shown at the top of the sidebar.",
    kind: "string",
    defaultValue: "/Graphics/BitweenFull.svg",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.BitweenIcon",
    section: "Brand & theme",
    label: "Collapsed sidebar icon",
    description: "The compact icon shown when the sidebar is collapsed to icons only.",
    kind: "string",
    defaultValue: "/Graphics/BitweenIcon.png",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.BitweenHeaderIcon",
    section: "Brand & theme",
    label: "Mobile header icon",
    description: "Icon variant used in the mobile top bar.",
    kind: "string",
    defaultValue: "/Graphics/BitweenIcon.svg",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.BitweenText",
    section: "Brand & theme",
    label: "Sign-in page blurb",
    description: "The marketing description shown beside the sign-in form.",
    kind: "string",
    defaultValue:
      "is all-in-one solution to solving integration with third parties, automating workflows with exchanges coming from all forms of requests, ranging from internal messages to files dumped on a server.",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.LinkedinLink",
    section: "Brand & theme",
    label: "LinkedIn link",
    description: "Add this if you want a LinkedIn link in the footer — leave blank to hide it.",
    kind: "string",
    defaultValue: "https://www.linkedin.com/company/simplify9",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.GithubLink",
    section: "Brand & theme",
    label: "GitHub link",
    description: "Add this if you want a GitHub link in the footer — leave blank to hide it.",
    kind: "string",
    defaultValue: "https://github.com/simplify9",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.WebsiteLink",
    section: "Brand & theme",
    label: "Website link",
    description: "Add this if you want a company website link in the footer — leave blank to hide it.",
    kind: "string",
    defaultValue: "https://www.simplify9.com/",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.AllRightsReserved",
    section: "Brand & theme",
    label: "Copyright notice",
    description: "The copyright notice text shown in the footer.",
    kind: "string",
    defaultValue: "All Rights Reserved.",
    value: null,
    secret: false,
    restartRequired: false,
  },
  {
    key: "Theme.CopyRightsIcon",
    section: "Brand & theme",
    label: "Copyright symbol",
    description: "The symbol shown before the copyright notice.",
    kind: "string",
    defaultValue: "©",
    value: null,
    secret: false,
    restartRequired: false,
  },
];
