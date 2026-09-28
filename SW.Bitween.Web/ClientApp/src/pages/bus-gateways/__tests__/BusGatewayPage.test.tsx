import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const noRows = { result: [], totalCount: 0 };

/** RawBusGateway in src/api/http/gateways.ts — no routes, which is all this needs. */
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

describe("a bus gateway's page", () => {
  it("starts over when another gateway is opened from it", async () => {
    const { router } = renderApp("/bus-gateways/1", {
      handlers: [
        http.get(apiPath("/busgateways/1"), () => HttpResponse.json(gateway(1, "Label distribution"))),
        http.get(apiPath("/busgateways/2"), () => HttpResponse.json(gateway(2, "Tracking fan-out"))),
        ...["/adapters/Catalog", "/datasources/Providers"].map((p) =>
          http.get(apiPath(p), () => HttpResponse.json([])),
        ),
        ...["/busgateways", "/documents", "/partners", "/subscriptions"].map((p) =>
          http.get(apiPath(p), () => HttpResponse.json(noRows)),
        ),
      ],
    });

    expect(await screen.findByDisplayValue("Label distribution")).toBeVisible();

    // What a listener card on the canvas does when the listener is on another gateway.
    await router.navigate("/bus-gateways/2");

    // Its own name, not the one just left read back as an unsaved rename of this one.
    expect(await screen.findByDisplayValue("Tracking fan-out")).toBeVisible();
    await waitFor(() => expect(screen.queryByDisplayValue("Label distribution")).not.toBeInTheDocument());
    expect(screen.queryByText(/^Unsaved/)).not.toBeInTheDocument();
  });
});
