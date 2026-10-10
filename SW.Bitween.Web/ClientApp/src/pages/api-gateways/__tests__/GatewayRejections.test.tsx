import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** A gateway's page says which calls were turned away, and what each reason means. */
const none = (path: string) => http.get(apiPath(path), () => HttpResponse.json({ result: [], totalCount: 0 }));

describe("calls turned away", () => {
  it("are counted by reason in plain words, most recent listed", async () => {
    renderApp("/api-gateways/15", {
      handlers: [
        http.get(apiPath("/apigateways/15/rejections"), () =>
          HttpResponse.json({
            node: "web-1:12",
            since: "2026-10-10T08:00:00Z",
            counts: { "not-authenticated": 3, "rate-limited": 1 },
            recent: [{ on: "2026-10-10T09:00:00Z", reason: "not-authenticated", status: 401, address: "203.0.113.7" }],
          }),
        ),
        http.get(apiPath("/apigateways/15"), () =>
          HttpResponse.json({ id: 15, name: "Orders", urlName: "orders", partnersCount: 0, inactive: false, partners: [], authentication: null, defaultKeyHeader: "partnerkey" }),
        ),
        none("/apigateways/attachments"),
        ...["/subscriptions", "/documents", "/partners", "/workgroups", "/retrypolicies", "/audit"].map((p) => none(p)),
      ],
    });

    expect(await screen.findByText("Calls turned away", undefined, { timeout: 5000 })).toBeVisible();
    expect(screen.getAllByText("Key not recognised")[0]).toBeVisible();
    expect(screen.getByText(/a typo, a revoked key, or the wrong header/)).toBeVisible();
    expect(screen.getByText("Too many calls")).toBeVisible();
    expect(screen.getByText(/203\.0\.113\.7/)).toBeVisible();
  });
});
