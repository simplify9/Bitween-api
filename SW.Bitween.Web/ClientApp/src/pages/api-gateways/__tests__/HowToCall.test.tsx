import { screen, within } from "@testing-library/react";
import { http, HttpResponse, type JsonBodyType, type RequestHandler } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * How a partner proves who it is, on the gateway's page. That the server holds to each way is
 * ApiGatewayTests; the page is where an admin comes back to look it up, so it is spelled out
 * there rather than only in the popup that shows a new key once.
 */
const empty = { result: [], totalCount: 0 };
const none = (path: string) => http.get(apiPath(path), () => HttpResponse.json(empty));
const json = (path: string, body: JsonBodyType) => http.get(apiPath(path), () => HttpResponse.json(body));

const JWT = { method: "Jwt", issuer: "https://login.acme.example", audience: "bitween", partnerClaim: null };

const openGateway = (
  authentication: JsonBodyType = null,
  attachments: JsonBodyType[] = [],
  extra: RequestHandler[] = [],
) =>
  renderApp("/api-gateways/15", {
    handlers: [
      ...extra,
      json("/apigateways/15", {
        id: 15,
        name: "Orders",
        urlName: "orders",
        partnersCount: attachments.length,
        inactive: false,
        partners: attachments,
        authentication,
      }),
      json("/apigateways/attachments", { result: attachments, totalCount: attachments.length }),
      ...["/subscriptions", "/documents", "/partners", "/workgroups", "/retrypolicies", "/audit"].map((p) => none(p)),
    ],
  });

describe("an API gateway's page", () => {
  it("shows every way a partner can send its key", async () => {
    openGateway();

    const ways = within(await screen.findByLabelText("Ways to send the key"));

    expect(ways.getByText("partnerkey: <key>")).toBeVisible();
    expect(ways.getByText("Authorization: Bearer <key>")).toBeVisible();
    expect(ways.getByText(/username: <the key's name>\s+password: <key>/)).toBeVisible();
    // The gateway splits the username off at the first colon, so such a key name can never match.
    expect(ways.getByText("Basic auth")).toHaveAttribute("title", expect.stringContaining("colon"));
  });

  it("on a gateway taking tokens, says what the token must carry, and nothing about keys", async () => {
    openGateway({ ...JWT, partnerClaim: "client_id" });

    const token = within(await screen.findByLabelText("What the token must carry"));

    expect(token.getByText("https://login.acme.example")).toBeVisible();
    expect(token.getByText("bitween")).toBeVisible();
    expect(token.getByText("client_id")).toBeVisible();
    expect(screen.queryByLabelText("Ways to send the key")).not.toBeInTheDocument();
  });

  it("flags attached partners that no token can name", async () => {
    openGateway(JWT, [
      { partnerId: 1, subscriptionId: 9, partnerName: "Acme", subscriptionName: "Orders in", partnerLoginIdentity: "acme-orders" },
      { partnerId: 2, subscriptionId: 9, partnerName: "Globex", subscriptionName: "Orders in", partnerLoginIdentity: null },
    ]);

    const globex = (await screen.findByRole("link", { name: "Globex" })).closest("tr")!;
    expect(within(globex).getByText("Not set")).toBeVisible();
    const acme = screen.getByRole("link", { name: "Acme" }).closest("tr")!;
    expect(within(acme).getByText("acme-orders")).toBeVisible();
  });

  it("switching to tokens asks for a login server and audience, then confirms before saving", async () => {
    let sent: Record<string, unknown> | null = null;
    const { user } = openGateway(null, [], [
      http.post(apiPath("/apigateways/15"), async ({ request }) => {
        sent = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(null);
      }),
    ]);

    expect(await screen.findByText("How a partner calls it")).toBeVisible();
    expect(screen.queryByText(/with your unsaved changes/)).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText("Partners send"), "Jwt");
    // The instructions now describe a call the gateway won't take until this is saved.
    expect(screen.getByText(/with your unsaved changes/)).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Save changes" }));
    expect(sent).toBeNull();
    expect(screen.getAllByText(/A full https:\/\/ address/).length).toBeGreaterThan(0);

    await user.type(screen.getByLabelText("Login server"), "https://login.acme.example");
    await user.type(screen.getByLabelText("Audience"), "bitween");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    // Moving to tokens cuts off every partner still calling with a key.
    const confirm = within(await screen.findByRole("dialog", { name: "Change how partners authenticate?" }));
    expect(confirm.getByText(/Partners calling with API keys will get 401s/)).toBeVisible();
    await user.click(confirm.getByRole("button", { name: "Save changes" }));

    await expect.poll(() => sent).not.toBeNull();
    expect(sent!.authentication).toEqual({
      method: "Jwt",
      issuer: "https://login.acme.example",
      audience: "bitween",
      partnerClaim: "",
    });
  });
});
