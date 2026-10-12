import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * A subscription's versions on its overview: listed, compared with now, and restored by saving the
 * version's configuration. What the server records is SW.Bitween.IntegrationTests' business
 * (SubscriptionHistoryTests).
 */

// The picker's dropdown watches its anchor's size when it closes, which jsdom cannot measure.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

const noRows = { result: [], totalCount: 0 };

type Raw = { id: number; name: string; type: string } & Record<string, unknown>;

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
    // What a subscription's overview shows below the rail - none of it matters here.
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
    // The adapters don't matter to either node - only that the delivering one has a handler.
    ...["/adapters/Catalog", "/datasources/Providers"].map((p) => http.get(apiPath(p), () => HttpResponse.json([]))),
    ...["/apigateways", "/busgateways", "/xchanges", "/documents", "/partners", "/workgroups", "/retrypolicies"].map(
      (p) => http.get(apiPath(p), () => HttpResponse.json(noRows)),
    ),
  ];
  return { handlers, saves };
}

const sub = (fields: Record<string, unknown> = {}) =>
  raw({ id: 10, name: "Acme orders", type: "BusGateway", handlerId: "NativeHttpHandler", ...fields });

const history = [
  { number: 2, savedOn: "2026-10-12T10:00:00Z", savedBy: "Rana", changed: ["handlerProperties"] },
  { number: 1, savedOn: "2026-10-11T09:00:00Z", savedBy: "Omar", changed: [] },
];

const versionOne = {
  number: 1,
  savedOn: "2026-10-11T09:00:00Z",
  configuration: {
    name: "Acme orders",
    handlerId: "NativeHttpHandler",
    handlerProperties: [
      { key: "Url", value: "https://acme.example/v1" },
      { key: "ApiKey", value: "__private__" },
    ],
  },
};

function withVersions(handlers: ReturnType<typeof backend>["handlers"]) {
  return [
    http.get(apiPath("/subscriptions/history"), () => HttpResponse.json(history)),
    http.get(apiPath("/subscriptions/revision"), () => HttpResponse.json(versionOne)),
    ...handlers,
  ];
}

const current = () =>
  sub({
    handlerProperties: [
      { key: "Url", value: "https://acme.example/v2" },
      { key: "ApiKey", value: "__private__" },
    ],
  });

describe("a subscription's versions", () => {
  it("are listed newest first, naming what each save changed", async () => {
    const { handlers } = backend([current()]);
    renderApp("/subscriptions/10", { handlers: withVersions(handlers) });

    expect(await screen.findByText("#2")).toBeVisible();
    expect(screen.getByText("Delivery settings")).toBeVisible();
    expect(screen.getByText("First version recorded")).toBeVisible();
  });

  it("compare a version with what the subscription has now", async () => {
    const { handlers } = backend([current()]);
    const { user } = renderApp("/subscriptions/10", { handlers: withVersions(handlers) });

    await user.click(await screen.findByRole("button", { name: "Compare" }));
    const dialog = await screen.findByRole("dialog");
    expect(await within(dialog).findByText(/Url: https:\/\/acme.example\/v1/)).toBeVisible();
    expect(within(dialog).getByText(/Url: https:\/\/acme.example\/v2/)).toBeVisible();
  });

  it("restore by saving the version's configuration, keeping whether it is active", async () => {
    const { handlers, saves } = backend([current()]);
    const { user } = renderApp("/subscriptions/10", { handlers: withVersions(handlers) });

    await user.click(await screen.findByRole("button", { name: "Restore" }));
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Restore" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0]).toMatchObject({
      handlerProperties: versionOne.configuration.handlerProperties,
      inactive: false,
      documentId: 3,
    });
  });
});
