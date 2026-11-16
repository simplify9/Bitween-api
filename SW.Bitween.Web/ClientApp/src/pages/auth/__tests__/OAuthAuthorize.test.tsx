import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** Where an AI app sends the browser to connect it to /mcp: asks first, then hands the server's answer back. */
const CHALLENGE = "b".repeat(43);
const query = (extra = "") =>
  `/oauth/authorize?response_type=code&client_id=bwc_app&redirect_uri=${encodeURIComponent("https://app.example.com/cb")}` +
  `&code_challenge=${CHALLENGE}&code_challenge_method=S256&state=s1&resource=${encodeURIComponent("https://bitween.example.com/mcp")}${extra}`;

function server(client: object | { status: number; message: string } = { name: "Claude", redirectHost: "app.example.com", onlyThisComputer: false }) {
  const grants: Array<Record<string, unknown>> = [];
  const handlers = [
    http.get(apiPath("/oauth/client"), () =>
      "status" in client
        ? HttpResponse.json({ message: client.message }, { status: client.status })
        : HttpResponse.json(client),
    ),
    http.post(apiPath("/oauth/grant"), async ({ request }) => {
      const body = (await request.json()) as Record<string, unknown>;
      grants.push(body);
      return HttpResponse.json({ redirectTo: body.approve ? "https://app.example.com/cb?code=c1&state=s1" : "https://app.example.com/cb?error=access_denied&state=s1" });
    }),
  ];
  return { grants, handlers };
}

describe("connecting an AI app", () => {
  const assign = vi.fn();
  const original = window.location;

  afterEach(() => {
    Object.defineProperty(window, "location", { configurable: true, value: original });
    assign.mockReset();
  });

  const stubLocation = () =>
    Object.defineProperty(window, "location", { configurable: true, value: { ...original, assign } });

  it("names the app and where it goes back to, and only on Allow sends the member back with a code", async () => {
    const { grants, handlers } = server();
    const { user } = renderApp(query(), { handlers });

    expect(await screen.findByRole("heading", { name: "Connect Claude" })).toBeVisible();
    expect(screen.getByText("app.example.com")).toBeVisible();
    expect(grants).toHaveLength(0);
    stubLocation();

    await user.click(screen.getByRole("button", { name: "Allow" }));

    await vi.waitFor(() => expect(assign).toHaveBeenCalledWith("https://app.example.com/cb?code=c1&state=s1"));
    expect(grants[0]).toMatchObject({
      clientId: "bwc_app",
      redirectUri: "https://app.example.com/cb",
      codeChallenge: CHALLENGE,
      codeChallengeMethod: "S256",
      state: "s1",
      resource: "https://bitween.example.com/mcp",
      approve: true,
    });
  });

  it("sends the member back declined on Cancel", async () => {
    const { grants, handlers } = server();
    const { user } = renderApp(query(), { handlers });
    stubLocation();

    await user.click(await screen.findByRole("button", { name: "Cancel" }));

    await vi.waitFor(() => expect(assign).toHaveBeenCalledWith("https://app.example.com/cb?error=access_denied&state=s1"));
    expect(grants[0]).toMatchObject({ approve: false });
  });

  it("warns when the app goes back to a program on this computer", async () => {
    const { handlers } = server({ name: "Claude Code", redirectHost: "127.0.0.1:53682", onlyThisComputer: true });
    renderApp(query(), { handlers });

    expect(await screen.findByText(/sends you back to a program on this computer/)).toBeVisible();
  });

  it("shows why an app can't be connected, and offers nothing to approve", async () => {
    const { handlers } = server({ status: 400, message: "This app asked to send you somewhere it didn't register, so it can't be signed in." });
    renderApp(query(), { handlers });

    expect(await screen.findByText(/somewhere it didn't register/)).toBeVisible();
    expect(screen.queryByRole("button", { name: "Allow" })).toBeNull();
  });

  it("refuses a request without PKCE", async () => {
    const { handlers } = server();
    renderApp(query().replace("code_challenge_method=S256", "code_challenge_method=plain"), { handlers });

    expect(await screen.findByText(/isn't a sign-in request Bitween can answer/)).toBeVisible();
  });
});
