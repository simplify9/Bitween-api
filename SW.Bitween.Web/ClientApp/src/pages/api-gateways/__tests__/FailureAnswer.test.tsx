import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** What a sync caller is told when a delivery fails is read back from the gateway, not assumed. */
const none = (path: string) => http.get(apiPath(path), () => HttpResponse.json({ result: [], totalCount: 0 }));

describe("sync callers are told", () => {
  it("shows the gateway's saved answer and keeps it on an unrelated save", async () => {
    let saved: Record<string, unknown> | undefined;
    const { user } = renderApp("/api-gateways/15", {
      handlers: [
        http.get(apiPath("/apigateways/15/rejections"), () =>
          HttpResponse.json({ node: "web-1:12", since: "2026-10-10T08:00:00Z", counts: {}, recent: [] }),
        ),
        http.get(apiPath("/apigateways/15"), () =>
          HttpResponse.json({ id: 15, name: "Orders", urlName: "orders", partnersCount: 0, inactive: false, partners: [], authentication: null, defaultKeyHeader: "partnerkey", failureAnswer: "Accurate" }),
        ),
        http.post(apiPath("/apigateways/15"), async ({ request }) => {
          saved = (await request.json()) as Record<string, unknown>;
          return new HttpResponse(null, { status: 204 });
        }),
        none("/apigateways/attachments"),
        ...["/subscriptions", "/documents", "/partners", "/workgroups", "/retrypolicies", "/audit"].map((p) => none(p)),
      ],
    });

    const answer = await screen.findByRole("combobox", { name: "Sync callers are told" }, { timeout: 5000 });
    expect(answer).toHaveValue("Accurate");

    const name = screen.getByRole("textbox", { name: "Name" });
    await user.clear(name);
    await user.type(name, "Orders inbound");
    await user.click(screen.getByRole("button", { name: /^Save/ }));

    await expect.poll(() => saved?.failureAnswer).toBe("Accurate");
  });
});
