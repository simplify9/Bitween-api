import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * A partner's login server identity is how a gateway taking tokens knows it. The partner update
 * copies every field it is sent, so any write that forgets the identity clears it — and the
 * partner is cut off from every such gateway with nothing on screen to say why.
 */
const empty = { result: [], totalCount: 0 };

const openPartner = (writes: Record<string, unknown>[]) =>
  renderApp("/partners/7", {
    handlers: [
      http.get(apiPath("/partners/generatekey"), () => HttpResponse.json("7f3a9c1e5b24d8")),
      http.get(apiPath("/partners/7"), () =>
        HttpResponse.json({
          name: "Acme",
          apiCredentials: [{ key: "orders-prod", value: "11111...(hidden)" }],
          adapterProperties: {},
          secretProperties: [],
          loginIdentity: "acme-orders",
        }),
      ),
      http.post(apiPath("/partners/7"), async ({ request }) => {
        writes.push((await request.json()) as Record<string, unknown>);
        return HttpResponse.json(null);
      }),
      ...["/partners", "/apigateways", "/busgateways", "/xchanges", "/subscriptions", "/audit"].map((p) =>
        http.get(apiPath(p), () => HttpResponse.json(empty)),
      ),
    ],
  });

describe("a partner's login server identity", () => {
  it("is shown, and saved with the rest of the partner", async () => {
    const writes: Record<string, unknown>[] = [];
    const { user } = openPartner(writes);

    const field = await screen.findByLabelText("Identity");
    expect(field).toHaveValue("acme-orders");

    await user.clear(field);
    await user.type(field, "  acme-v2 ");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await expect.poll(() => writes.length).toBe(1);
    expect(writes[0].loginIdentity).toBe("acme-v2");
  });

  it("survives issuing and revoking keys", async () => {
    const writes: Record<string, unknown>[] = [];
    const { user } = openPartner(writes);

    await user.click(await screen.findByRole("button", { name: "New key" }));
    await user.type(screen.getByLabelText("Key name"), "orders-test");
    await user.click(screen.getByRole("button", { name: "Generate key" }));
    await user.click(within(await screen.findByRole("dialog", { name: "API key created" })).getByRole("button", { name: "Done" }));

    await user.click(screen.getByRole("button", { name: "Revoke" }));
    await user.click(screen.getByRole("button", { name: "Revoke key" }));

    await expect.poll(() => writes.length).toBe(2);
    expect(writes.map((w) => w.loginIdentity)).toEqual(["acme-orders", "acme-orders"]);
  });
});
