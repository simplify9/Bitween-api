import { screen, within } from "@testing-library/react";
import { http, HttpResponse, type JsonBodyType } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Where our team sees that a partner maps a subscription their own way, made in the partner portal:
 * on the partner's page, and beside the partner on each gateway that reaches it.
 */
const empty = { result: [], totalCount: 0 };
const json = (path: string, body: JsonBodyType) => http.get(apiPath(path), () => HttpResponse.json(body));
const none = (...paths: string[]) => paths.map((p) => json(p, empty));

describe("a partner's own mappings, as our team sees them", () => {
  it("are listed on the partner's page, each opening its subscription", async () => {
    renderApp("/partners/7", {
      handlers: [
        json("/partners/7", { name: "Acme", apiCredentials: [], adapterProperties: {}, secretProperties: [], changesNeedApproval: true }),
        json("/partners/7/ownmappings", [
          { subscriptionId: 21, subscriptionName: "Acme webhook", changedOn: "2026-11-25T10:00:00Z", changedBy: "Pat", inUse: true, waiting: true },
        ]),
        ...none("/partners", "/apigateways", "/busgateways", "/xchanges", "/subscriptions", "/audit"),
      ],
    });

    const panel = (await screen.findByRole("heading", { name: "Their own mappings" })).closest("section")!;
    expect(within(panel).getByRole("link", { name: "Acme webhook" })).toHaveAttribute("href", "/subscriptions/21");
    expect(within(panel).getByText("Waiting")).toBeVisible();
  });

  it("are marked beside the partner on an API gateway", async () => {
    const attachments = [
      { partnerId: 7, partnerName: "Acme", subscriptionId: 21, subscriptionName: "Acme webhook", ownMapping: true },
      { partnerId: 8, partnerName: "Beta", subscriptionId: 21, subscriptionName: "Acme webhook", ownMapping: false },
    ];
    renderApp("/api-gateways/15", {
      handlers: [
        json("/apigateways/15", { id: 15, name: "Orders", urlName: "orders", partnersCount: 2, inactive: false, partners: attachments, authentication: null, defaultKeyHeader: "partnerkey" }),
        json("/apigateways/attachments", { result: attachments, totalCount: 2 }),
        json("/apigateways/15/rejections", { node: "web-1:12", since: "2026-10-10T08:00:00Z", counts: {}, recent: [] }),
        ...none("/subscriptions", "/documents", "/partners", "/workgroups", "/retrypolicies", "/audit"),
      ],
    });

    const acme = (await screen.findByRole("link", { name: "Acme" })).closest("tr")!;
    expect(within(acme).getByText("Own mapping")).toBeVisible();
    const beta = screen.getByRole("link", { name: "Beta" }).closest("tr")!;
    expect(within(beta).queryByText("Own mapping")).not.toBeInTheDocument();
  });
});
