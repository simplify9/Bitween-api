import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Handing a delivery's response to a response subscription, from both ends: the Response step
 * of the subscription that delivers, and the Trigger node of the response subscription itself.
 * Which targets the server accepts is SW.Bitween.IntegrationTests' business
 * (ResponseSubscriptionTests); this is what the page offers and what it sends.
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

const RESPONSE = raw({ id: 20, name: "Store shipment labels", type: "Response" });
const LEGACY = raw({ id: 30, name: "Old internal intake", type: "Internal", partnerId: 7 });

/** A mock backend holding `subjects`, recording what each save posts. */
function backend(subjects: Raw[]) {
  const saves: Record<string, unknown>[] = [];
  const all = [...subjects, RESPONSE, LEGACY].filter(
    (s, i, list) => list.findIndex((x) => x.id === s.id) === i,
  );
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
    http.get(apiPath("/audit"), () => HttpResponse.json(noRows)),
    // The adapters don't matter to either node — only that the delivering one has a handler.
    ...["/adapters/Catalog", "/datasources/Providers"].map((p) => http.get(apiPath(p), () => HttpResponse.json([]))),
    ...["/apigateways", "/busgateways", "/xchanges", "/documents", "/partners", "/workgroups", "/retrypolicies"].map(
      (p) => http.get(apiPath(p), () => HttpResponse.json(noRows)),
    ),
  ];
  return { handlers, saves };
}

describe("a subscription's Response step", () => {
  const delivering = raw({ id: 10, name: "Acme orders", type: "BusGateway", handlerId: "NativeHttpHandler" });

  it("offers only response subscriptions, and saves the one picked", async () => {
    const { handlers, saves } = backend([delivering]);
    const { user } = renderApp("/subscriptions/10?stage=response", { handlers });

    await user.click(await screen.findByRole("combobox", { name: "Hand the response to" }));
    const options = screen.getAllByRole("option").map((o) => o.textContent);
    expect(options).toContainEqual(expect.stringContaining("Store shipment labels"));
    expect(options).not.toContainEqual(expect.stringContaining("Old internal intake"));

    await user.click(screen.getByRole("option", { name: /Store shipment labels/ }));
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].responseSubscriptionId).toBe(20);
  });

  it("keeps a legacy target already saved, and says it can't be picked again", async () => {
    const { handlers } = backend([{ ...delivering, responseSubscriptionId: 30 }]);
    renderApp("/subscriptions/10?stage=response", { handlers });

    expect(await screen.findByRole("combobox", { name: "Hand the response to" })).toHaveValue(
      "Old internal intake",
    );
    expect(screen.getByText(/A legacy subscription\. It keeps getting the response/)).toBeVisible();
  });
});

describe("a response subscription's Trigger node", () => {
  it("lists what feeds it, and saves whether a bad response counts", async () => {
    const feeder = raw({ id: 11, name: "Acme labels", type: "BusGateway", handlerId: "NativeHttpHandler", responseSubscriptionId: 20 });
    const { handlers, saves } = backend([RESPONSE, feeder]);
    const { user } = renderApp("/subscriptions/20?stage=trigger", { handlers });

    const feeders = await screen.findByRole("table");
    expect(within(feeders).getByRole("link", { name: "Acme labels" })).toHaveAttribute("href", "/subscriptions/11");

    await user.click(screen.getByRole("checkbox", { name: /Also run on a bad response/ }));
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].runOnBadResponses).toBe(true);
  });
});

describe("creating a response subscription from a Response step", () => {
  const delivering = raw({ id: 10, name: "Acme orders", type: "BusGateway", handlerId: "NativeHttpHandler" });

  it("keeps the page's unsaved edits while away, and brings them back with the new one picked", async () => {
    const { handlers } = backend([delivering]);
    const { user, router } = renderApp("/subscriptions/10?stage=response", { handlers });

    const title = await screen.findByRole("textbox", { name: "Name" });
    await user.clear(title);
    await user.type(title, "Renamed before leaving");
    await user.click(screen.getByRole("button", { name: "New response subscription" }));

    // Its own full page, told where to come back to.
    expect(router.state.location.pathname).toBe("/response-subscriptions/new");
    const back = new URLSearchParams(router.state.location.search).get("return")!;
    expect(back).toBe("/subscriptions/10?stage=response&draftKept=1");
    expect(await screen.findByRole("heading", { name: "New response subscription" })).toBeVisible();

    // What its Create does once the subscription exists.
    await router.navigate(`${back}&pickedResponse=20`);

    expect(await screen.findByRole("combobox", { name: "Hand the response to" })).toHaveValue(
      "Store shipment labels",
    );
    expect(screen.getByRole("textbox", { name: "Name" })).toHaveValue("Renamed before leaving");
    expect(screen.getByText("Unsaved changes")).toBeVisible();
    // The marks come off, so a refresh opens the saved subscription rather than restoring again.
    await waitFor(() => expect(router.state.location.search).toBe("?stage=response"));
  });
});

describe("following a response chain", () => {
  const delivering = raw({
    id: 10,
    name: "Acme orders",
    type: "BusGateway",
    handlerId: "NativeHttpHandler",
    responseSubscriptionId: 20,
  });

  it("opens the response subscription from the Response step, and names what feeds it there", async () => {
    const { handlers } = backend([delivering]);
    const { user, router } = renderApp("/subscriptions/10?stage=response", { handlers });

    await user.click(await screen.findByRole("button", { name: "Open" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/subscriptions/20"));
    // A fresh page, not the one just left with its id swapped.
    expect(await screen.findByRole("textbox", { name: "Name" })).toHaveValue("Store shipment labels");
    expect(screen.queryByText("Unsaved changes")).not.toBeInTheDocument();
    // And the way back up.
    expect(screen.getByRole("link", { name: "Acme orders" })).toHaveAttribute("href", "/subscriptions/10");
  });

  it("saves unsaved changes first when asked to open it", async () => {
    const { handlers, saves } = backend([delivering]);
    const { user, router } = renderApp("/subscriptions/10?stage=response", { handlers });

    const title = await screen.findByRole("textbox", { name: "Name" });
    await user.clear(title);
    await user.type(title, "Renamed first");
    await user.click(screen.getByRole("button", { name: "Open" }));

    const dialog = await screen.findByRole("dialog", { name: "Save your changes first?" });
    await user.click(within(dialog).getByRole("button", { name: "Save and open" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/subscriptions/20"));
    expect(saves).toHaveLength(1);
    expect(saves[0].name).toBe("Renamed first");
  });
});

