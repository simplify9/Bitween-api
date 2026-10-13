import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const connection = (over: Record<string, unknown> = {}) => ({
  id: 4,
  name: "salesforce",
  grant: "AuthorizationCode",
  authorizeUrl: "https://login.test/authorize",
  tokenUrl: "https://login.test/token",
  clientId: "bitween",
  clientSecret: "__private__",
  scopes: "api refresh_token",
  authorizeParameters: null,
  tokenAuth: "post",
  username: null,
  password: null,
  status: "NeedsAuthorizing",
  lastError: null,
  accessTokenExpiresOn: null,
  hasRefreshToken: false,
  authorizedBy: null,
  authorizedOn: null,
  reference: "{{connections.salesforce.accessToken}}",
  ...over,
});

const noHistory = http.get(apiPath("/audit"), () => HttpResponse.json({ result: [], totalCount: 0 }));

describe("connections", () => {
  const assign = vi.fn();
  const original = window.location;
  afterEach(() => {
    Object.defineProperty(window, "location", { configurable: true, value: original });
    assign.mockReset();
  });
  const stubLocation = () =>
    Object.defineProperty(window, "location", { configurable: true, value: { ...original, assign } });

  it("lists each connection with its status and the reference settings write", async () => {
    renderApp("/connections", {
      handlers: [
        http.get(apiPath("/connections"), () =>
          HttpResponse.json({ result: [connection(), connection({ id: 5, name: "servicenow", grant: "ClientCredentials", status: "Connected", reference: "{{connections.servicenow.accessToken}}" })], totalCount: 2 }),
        ),
      ],
    });
    expect(await screen.findByText("salesforce")).toBeVisible();
    expect(screen.getByText("Needs authorizing")).toBeVisible();
    expect(screen.getByText("Connected")).toBeVisible();
    expect(screen.getByText("{{connections.servicenow.accessToken}}")).toBeVisible();
  });

  it("creates one with the fields its grant needs", async () => {
    const sent: unknown[] = [];
    const { user, router } = renderApp("/connections?new=1", {
      handlers: [
        http.get(apiPath("/connections"), () => HttpResponse.json({ result: [], totalCount: 0 })),
        http.post(apiPath("/connections"), async ({ request }) => {
          sent.push(await request.json());
          return HttpResponse.json(connection({ id: 9, name: "sn", grant: "ClientCredentials", status: "New" }));
        }),
        http.get(apiPath("/connections/9"), () => HttpResponse.json(connection({ id: 9, name: "sn", grant: "ClientCredentials", status: "New" }))),
        noHistory,
      ],
    });

    await user.type(await screen.findByLabelText("Name"), "sn");
    await user.selectOptions(screen.getByLabelText("How it gets tokens"), "ClientCredentials");
    // Approving once needs the provider's page; client credentials don't.
    expect(screen.queryByLabelText("Authorize URL")).not.toBeInTheDocument();
    await user.type(screen.getByLabelText("Token URL"), "https://id.test/token");
    await user.type(screen.getByLabelText("Client ID"), "app");
    await user.type(screen.getByLabelText("Client secret"), "s3cret");
    await user.click(screen.getByRole("button", { name: "Create connection" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/connections/9"));
    expect(sent[0]).toMatchObject({ name: "sn", grant: "ClientCredentials", tokenUrl: "https://id.test/token", clientId: "app", clientSecret: "s3cret" });
  });

  it("keeps a saved secret when its field is left empty, and sends the browser to approve", async () => {
    stubLocation();
    const saved: Array<Record<string, unknown>> = [];
    const { user } = renderApp("/connections/4", {
      handlers: [
        http.get(apiPath("/connections/4"), () => HttpResponse.json(connection())),
        http.post(apiPath("/connections/4"), async ({ request }) => {
          saved.push((await request.json()) as Record<string, unknown>);
          return HttpResponse.json(connection());
        }),
        http.post(apiPath("/connections/4/authorize"), () => HttpResponse.json({ url: "https://login.test/authorize?state=x" })),
        noHistory,
      ],
    });

    const secret = await screen.findByLabelText("Client secret");
    expect(secret).toHaveValue("");
    expect(secret).toHaveAttribute("placeholder", "Saved - type to replace it");
    await user.clear(screen.getByLabelText("Scopes"));
    await user.type(screen.getByLabelText("Scopes"), "api");
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(saved).toHaveLength(1));
    expect(saved[0]).toMatchObject({ scopes: "api", clientSecret: "__private__" });

    await user.click(screen.getByRole("button", { name: "Authorize" }));
    await waitFor(() => expect(assign).toHaveBeenCalledWith("https://login.test/authorize?state=x"));
  });

  it("shows why a connection failed", async () => {
    renderApp("/connections/4", {
      handlers: [
        http.get(apiPath("/connections/4"), () => HttpResponse.json(connection({ status: "Error", lastError: "the provider answered 401: invalid_client" }))),
        noHistory,
      ],
    });
    expect(await screen.findByText("the provider answered 401: invalid_client")).toBeVisible();
  });

  it("finishes an approval once and goes back to the connection", async () => {
    const sent: unknown[] = [];
    const { router } = renderApp("/oauth-callback?code=c1&state=s1", {
      handlers: [
        http.post(apiPath("/connections/complete"), async ({ request }) => {
          sent.push(await request.json());
          return HttpResponse.json({ id: 4, name: "salesforce" });
        }),
        http.get(apiPath("/connections/4"), () => HttpResponse.json(connection({ status: "Connected", hasRefreshToken: true }))),
        noHistory,
      ],
    });
    await waitFor(() => expect(router.state.location.pathname).toBe("/connections/4"));
    expect(sent).toEqual([{ code: "c1", state: "s1" }]);
  });

  it("says what the provider refused", async () => {
    renderApp("/oauth-callback?error=access_denied", {});
    expect(await screen.findByText("The provider didn't approve the connection: access_denied")).toBeVisible();
  });
});
