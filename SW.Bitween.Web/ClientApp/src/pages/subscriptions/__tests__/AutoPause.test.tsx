import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Auto-pause on a subscription's overview: what it shows, what a save sends, and that a save
 * touching something else leaves it as it was. When the server pauses is
 * SW.Bitween.IntegrationTests' business (AutoPauseTests).
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
    ...["/adapters/Catalog", "/datasources/Providers"].map((p) => http.get(apiPath(p), () => HttpResponse.json([]))),
    ...["/apigateways", "/busgateways", "/xchanges", "/documents", "/partners", "/workgroups", "/retrypolicies"].map(
      (p) => http.get(apiPath(p), () => HttpResponse.json(noRows)),
    ),
  ];
  return { handlers, saves };
}

const sub = (fields: Record<string, unknown> = {}) =>
  raw({ id: 10, name: "Acme orders", type: "BusGateway", handlerId: "NativeHttpHandler", ...fields });

describe("auto-pause", () => {
  it("is off until a number is entered, and saves the number", async () => {
    const { handlers, saves } = backend([sub()]);
    const { user } = renderApp("/subscriptions/10", { handlers });

    const field = await screen.findByRole("spinbutton", { name: /Pause after this many failed deliveries/ });
    expect(field).toHaveValue(null);

    await user.type(field, "5");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].autoPauseAfterFailures).toBe(5);
  });

  it("survives a save that changes something else", async () => {
    const { handlers, saves } = backend([sub({ autoPauseAfterFailures: 3 })]);
    const { user } = renderApp("/subscriptions/10", { handlers });

    await user.click(await screen.findByRole("button", { name: "Disable" }));
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Disable" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].inactive).toBe(true);
    expect(saves[0].autoPauseAfterFailures).toBe(3);
  });

  it("says when the subscription paused itself", async () => {
    const { handlers } = backend([
      sub({
        autoPauseAfterFailures: 3,
        pausedOn: "2026-10-08T09:00:00Z",
        pausedAutomatically: true,
        consecutiveFailures: 3,
      }),
    ]);
    renderApp("/subscriptions/10", { handlers });

    expect(await screen.findByText(/Paused automatically on .* after 3 failed deliveries in a row/)).toBeVisible();
  });
});
