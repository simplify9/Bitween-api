import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * A page that couldn't load says so. The partners list used to show "No partners yet" when the
 * request failed, and a partner that couldn't be read "no longer exists", so an outage or a
 * missing permission read as data that was gone.
 */
const LOADED = { timeout: 5000 };
const failing = (status: number, body: string | Record<string, unknown> = "Something broke") => () => HttpResponse.json(body, { status });
const others = [
  http.get(apiPath("/subscriptions"), () => HttpResponse.json({ result: [], totalCount: 0 })),
  http.get(apiPath("/apigateways"), () => HttpResponse.json({ result: [], totalCount: 0 })),
  http.get(apiPath("/busgateways"), () => HttpResponse.json({ result: [], totalCount: 0 })),
  http.get(apiPath("/xchanges"), () => HttpResponse.json({ result: [], totalCount: 0 })),
];

describe("a page that couldn't load", () => {
  it("says the list couldn't be loaded rather than that it is empty, and offers to try again", async () => {
    renderApp("/partners", { handlers: [http.get(apiPath("/partners"), failing(500)), ...others] });

    expect(await screen.findByText("Couldn't load partners", undefined, LOADED)).toBeVisible();
    expect(screen.getByRole("button", { name: "Try again" })).toBeVisible();
    expect(screen.queryByText("No partners yet")).not.toBeInTheDocument();
  });

  it("says when the list is refused for want of a permission", async () => {
    renderApp("/partners", { handlers: [http.get(apiPath("/partners"), failing(403, { title: "Forbidden", status: 403 })), ...others] });

    expect(await screen.findByText("You can't see partners", undefined, LOADED)).toBeVisible();
  });

  it("says a record is gone only when the server says it isn't there", async () => {
    renderApp("/partners/7", {
      handlers: [http.get(apiPath("/partners/7"), failing(404, "Not found")), http.get(apiPath("/partners"), failing(404, "Not found")), ...others],
    });
    expect(await screen.findByText("This partner no longer exists", undefined, LOADED)).toBeVisible();
  });

  it("says a record couldn't be loaded when the server failed", async () => {
    renderApp("/partners/7", {
      handlers: [http.get(apiPath("/partners/7"), failing(500)), http.get(apiPath("/partners"), failing(500)), ...others],
    });
    expect(await screen.findByText("Couldn't load this partner", undefined, LOADED)).toBeVisible();
    expect(screen.queryByText("This partner no longer exists")).not.toBeInTheDocument();
  });
});
