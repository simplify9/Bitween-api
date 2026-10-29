import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** A gateway's page says which calls were turned away, and what each reason means. */
const none = (path: string) => http.get(apiPath(path), () => HttpResponse.json({ result: [], totalCount: 0 }));

describe("calls turned away", () => {
  it("are counted by reason in plain words, the most recent grouped by caller", async () => {
    renderApp("/api-gateways/15", {
      handlers: [
        http.get(apiPath("/apigateways/15/rejections"), () =>
          HttpResponse.json({
            node: "web-1:12",
            since: "2026-10-10T08:00:00Z",
            counts: { "not-authenticated": 3, "rate-limited": 1 },
            recent: [
              { on: "2026-10-10T09:00:00Z", reason: "not-authenticated", status: 401, address: "203.0.113.7" },
              { on: "2026-10-10T08:55:00Z", reason: "not-authenticated", status: 401, address: "203.0.113.7" },
              { on: "2026-10-10T08:50:00Z", reason: "not-authenticated", status: 401, address: "203.0.113.7" },
              { on: "2026-10-10T08:58:00Z", reason: "rate-limited", status: 429, address: "198.51.100.2" },
            ],
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
    expect(screen.getAllByText("Too many calls")[0]).toBeVisible();
    // One row per reason and caller, counted, rather than the same line three times.
    const recent = within(screen.getByRole("heading", { name: "Most recent" }).parentElement!);
    expect(recent.getAllByRole("listitem")).toHaveLength(2);
    expect(recent.getByText(/from 203\.0\.113\.7/)).toBeVisible();
    expect(recent.getByText("3")).toBeVisible();
    expect(recent.getByText(/^first /)).toBeVisible();
  });
});
