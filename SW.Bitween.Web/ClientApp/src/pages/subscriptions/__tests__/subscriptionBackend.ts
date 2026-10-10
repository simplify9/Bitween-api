import { http, HttpResponse } from "msw";
import { apiPath } from "../../../__tests__/support/renderApp";

/**
 * A mock backend for a subscription page: the subscription, what its page reads beside it, and
 * every save recorded. Shared by the subscription page's tests.
 */
export const noRows = { result: [], totalCount: 0 };

export type Raw = { id: number; name: string; type: string } & Record<string, unknown>;

/** GET /adapters/Catalog: a custom handler with a catalog entry. */
export const ORDERS_ADAPTER = {
  key: "acme.handlers.orders",
  native: false,
  versions: ["1.0.0", "1.1.0"],
  startupValues: {},
  currentVersion: "1.1.0",
  versionHistory: [
    { version: "1.0.0", publishedOn: "2026-09-01T00:00:00Z", publishedBy: "ci", releaseNotes: "First", withdrawn: false },
    { version: "1.1.0", publishedOn: "2026-10-01T00:00:00Z", publishedBy: "ci", releaseNotes: "Faster", withdrawn: false },
  ],
  displayName: "Acme orders",
  summary: "Sends orders to Acme.",
  publisher: "Acme Ltd",
  tags: ["orders"],
};

/** RawSubscription in src/api/http/subscriptions.ts. */
export const raw = (fields: Raw): Raw => ({
  documentId: 3,
  partnerId: null,
  aggregationForId: null,
  handlerId: null,
  mapperId: null,
  receiverId: null,
  dataSourceId: null,
  validatorId: null,
  inactive: false,
  temporary: false,
  categoryId: null,
  handlerProperties: [],
  mapperProperties: [],
  receiverProperties: [],
  validatorProperties: [],
  documentFilter: [],
  matchExpression: null,
  workGroupId: null,
  retryPolicyId: null,
  customRetryPolicy: null,
  schedules: [],
  responseSubscriptionId: null,
  responseMessageTypeName: null,
  runOnBadResponses: false,
  receiveOn: null,
  aggregateOn: null,
  pausedOn: null,
  isRunning: false,
  consecutiveFailures: 0,
  lastException: null,
  ...fields,
});


/** A mock backend holding `subjects`, recording what each save posts. */
export function backend(subjects: Raw[], adapter: Record<string, unknown> = ORDERS_ADAPTER) {
  const saves: Record<string, unknown>[] = [];
  const all = subjects;
  const handlers = [
    ...all.map((s) => http.get(apiPath(`/subscriptions/${s.id}`), () => HttpResponse.json(s))),
    ...subjects.map((s) =>
      http.post(apiPath(`/subscriptions/${s.id}`), async ({ request }) => {
        saves.push((await request.json()) as Record<string, unknown>);
        return new HttpResponse(null, { status: 204 });
      }),
    ),
    http.get(apiPath("/subscriptions"), () => HttpResponse.json({ result: all, totalCount: all.length })),
    http.get(apiPath("/documents/3"), () =>
      HttpResponse.json({
        id: 3,
        code: "SHIPMENT",
        name: "Shipment",
        documentFormat: "Json",
        busEnabled: false,
        busMessageTypeName: null,
        duplicateInterval: 0,
        disregardsUnfilteredMessages: false,
        promotedProperties: [],
        usedByCount: 1,
        retiredOn: null,
      }),
    ),
    // What a subscription's overview shows below the rail — none of it matters here.
    http.post(apiPath("/subscriptions/:id/retryusage"), () => HttpResponse.json([])),
    // Nothing has delivered yet, so a response subscription has no paths to offer.
    http.get(apiPath("/subscriptions/sourcepaths"), ({ request }) =>
      HttpResponse.json({
        subscriptionId: Number(new URL(request.url).searchParams.get("subscriptionId")),
        xchangeId: null,
        receivedOn: null,
        paths: [],
      }),
    ),
    http.get(apiPath("/audit"), () => HttpResponse.json(noRows)),
    // The adapters don't matter to either node — only that the delivering one has a handler.
    http.get(apiPath("/adapters/Catalog"), ({ request }) =>
      HttpResponse.json(new URL(request.url).searchParams.get("prefix") === "handlers" ? [adapter] : []),
    ),
    http.get(apiPath("/datasources/Providers"), () => HttpResponse.json([])),
    ...["/apigateways", "/busgateways", "/xchanges", "/documents", "/partners", "/workgroups", "/retrypolicies"].map(
      (p) => http.get(apiPath(p), () => HttpResponse.json(noRows)),
    ),
  ];
  return { handlers, saves };
}

export const sub = (fields: Record<string, unknown> = {}) =>
  raw({ id: 10, name: "Acme orders", type: "BusGateway", handlerId: "acme.handlers.orders", ...fields });

