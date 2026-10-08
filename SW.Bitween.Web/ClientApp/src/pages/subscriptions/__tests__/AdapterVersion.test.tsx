import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Pinning a delivery adapter to a published version: what the Delivery step offers, what a save
 * sends, and that a save touching something else keeps the pin. What the server accepts is
 * SW.Bitween.IntegrationTests' business (AdapterCatalogTests).
 */

// The picker's dropdown watches its anchor's size when it closes, which jsdom cannot measure.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

const noRows = { result: [], totalCount: 0 };

type Raw = { id: number; name: string; type: string } & Record<string, unknown>;

/** GET /adapters/Catalog: a custom handler with a catalog entry. */
const ORDERS_ADAPTER = {
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
const raw = (fields: Raw): Raw => ({
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
function backend(subjects: Raw[]) {
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
      HttpResponse.json(new URL(request.url).searchParams.get("prefix") === "handlers" ? [ORDERS_ADAPTER] : []),
    ),
    http.get(apiPath("/datasources/Providers"), () => HttpResponse.json([])),
    ...["/apigateways", "/busgateways", "/xchanges", "/documents", "/partners", "/workgroups", "/retrypolicies"].map(
      (p) => http.get(apiPath(p), () => HttpResponse.json(noRows)),
    ),
  ];
  return { handlers, saves };
}

const sub = (fields: Record<string, unknown> = {}) =>
  raw({ id: 10, name: "Acme orders", type: "BusGateway", handlerId: "acme.handlers.orders", ...fields });

describe("pinning an adapter version", () => {
  it("shows what the adapter says about itself and follows the current version by default", async () => {
    const { handlers } = backend([sub()]);
    renderApp("/subscriptions/10?stage=delivery", { handlers });

    expect(await screen.findByText(/Sends orders to Acme\./)).toBeVisible();
    expect(screen.getByText(/by Acme Ltd/)).toBeVisible();
    expect(screen.getByRole("combobox", { name: "Adapter version" })).toHaveDisplayValue("Follow current (v1.1.0)");
  });

  it("saves the version picked", async () => {
    const { handlers, saves } = backend([sub()]);
    const { user } = renderApp("/subscriptions/10?stage=delivery", { handlers });

    await user.selectOptions(await screen.findByRole("combobox", { name: "Adapter version" }), "1.0.0");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].handlerVersion).toBe("1.0.0");
    expect(saves[0].handlerId).toBe("acme.handlers.orders");
  });

  it("keeps the pin through a save that changes something else", async () => {
    const { handlers, saves } = backend([sub({ handlerVersion: "1.0.0" })]);
    const { user } = renderApp("/subscriptions/10", { handlers });

    await user.click(await screen.findByRole("button", { name: "Disable" }));
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Disable" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].handlerVersion).toBe("1.0.0");
  });
});
