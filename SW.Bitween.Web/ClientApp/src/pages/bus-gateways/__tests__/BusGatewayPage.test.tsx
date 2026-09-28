import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const noRows = { result: [], totalCount: 0 };

// jsdom has neither, and the canvas a route opens on uses both.
Element.prototype.scrollTo ??= () => {};
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

/** RawBusGateway in src/api/http/gateways.ts, with no routes until a test gives it some. */
const gateway = (id: number, name: string) => ({
  id,
  name,
  documentId: 3,
  documentName: "Shipment",
  routesCount: 0,
  inactive: false,
  routes: [],
  dataSourceId: null,
});

const handlers = (routes: unknown[] = []) => [
  http.get(apiPath("/busgateways/1"), () =>
    HttpResponse.json({ ...gateway(1, "Label distribution"), routes, routesCount: routes.length }),
  ),
  http.get(apiPath("/busgateways/2"), () => HttpResponse.json(gateway(2, "Tracking fan-out"))),
  ...["/adapters/Catalog", "/datasources/Providers"].map((p) => http.get(apiPath(p), () => HttpResponse.json([]))),
  ...["/busgateways", "/documents", "/partners", "/subscriptions"].map((p) =>
    http.get(apiPath(p), () => HttpResponse.json(noRows)),
  ),
];

describe("a bus gateway's page", () => {
  it("offers a new subscription only to a new route", async () => {
    const saved = { id: 5, subscriptionId: 9, subscriptionName: "Label print", partnerId: null, partnerName: null, matchExpression: null };
    const { router } = renderApp("/bus-gateways/1?route=new&node=route", {
      handlers: [
        ...handlers([saved]),
        // The saved route's subscription; this test is about the route, not what it runs.
        http.get(apiPath("/subscriptions/9"), () => new HttpResponse(null, { status: 404 })),
        ...["/apigateways", "/xchanges"].map((p) => http.get(apiPath(p), () => HttpResponse.json(noRows))),
      ],
    });
    expect(await screen.findByRole("button", { name: /New subscription/ })).toBeVisible();

    // Updating a route takes an existing subscription; a new one here could never be saved.
    await router.navigate("/bus-gateways/1?route=5&node=route");
    await waitFor(() => expect(screen.queryByRole("button", { name: /New subscription/ })).not.toBeInTheDocument());
    expect(screen.getByRole("combobox", { name: "Subscription" })).toBeVisible();
  });

  it("starts over when another gateway is opened from it", async () => {
    const { router } = renderApp("/bus-gateways/1", { handlers: handlers() });

    expect(await screen.findByDisplayValue("Label distribution")).toBeVisible();

    // What a listener card on the canvas does when the listener is on another gateway.
    await router.navigate("/bus-gateways/2");

    // Its own name, not the one just left read back as an unsaved rename of this one.
    expect(await screen.findByDisplayValue("Tracking fan-out")).toBeVisible();
    await waitFor(() => expect(screen.queryByDisplayValue("Label distribution")).not.toBeInTheDocument());
    expect(screen.queryByText(/^Unsaved/)).not.toBeInTheDocument();
  });
});
