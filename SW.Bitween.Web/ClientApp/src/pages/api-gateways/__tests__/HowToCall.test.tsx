import { screen, within } from "@testing-library/react";
import { http, HttpResponse, type JsonBodyType } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * A gateway takes a partner's key three ways; that the server holds to this is ApiGatewayTests.
 * The gateway's page is where an admin comes back to look up how a partner should call it, so all
 * three are spelled out there rather than only in the popup that shows a new key once.
 */
const empty = { result: [], totalCount: 0 };
const none = (path: string) => http.get(apiPath(path), () => HttpResponse.json(empty));
const json = (path: string, body: JsonBodyType) => http.get(apiPath(path), () => HttpResponse.json(body));

describe("an API gateway's page", () => {
  it("shows every way a partner can send its key", async () => {
    renderApp("/api-gateways/15", {
      handlers: [
        json("/apigateways/15", { id: 15, name: "Orders", urlName: "orders", partnersCount: 0, inactive: false, partners: [] }),
        none("/apigateways/attachments"),
        ...["/subscriptions", "/documents", "/partners", "/workgroups", "/retrypolicies", "/audit"].map((p) => none(p)),
      ],
    });

    const ways = within(await screen.findByLabelText("Ways to send the key"));

    expect(ways.getByText("partnerkey: <key>")).toBeVisible();
    expect(ways.getByText("Authorization: Bearer <key>")).toBeVisible();
    expect(ways.getByText(/username: <the key's name>\s+password: <key>/)).toBeVisible();
    // The gateway splits the username off at the first colon, so such a key name can never match.
    expect(ways.getByText("Basic auth")).toHaveAttribute("title", expect.stringContaining("colon"));
  });
});
