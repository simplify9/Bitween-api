import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiPath, profile, renderApp } from "../../../__tests__/support/renderApp";

/** Single sign-on with an OpenID Connect provider: out to the provider, and back through /oidc-callback. */
describe("single sign-on with an OpenID Connect provider", () => {
  const assign = vi.fn();
  const original = window.location;

  afterEach(() => {
    Object.defineProperty(window, "location", { configurable: true, value: original });
    assign.mockReset();
    sessionStorage.clear();
  });

  const stubLocation = () =>
    Object.defineProperty(window, "location", { configurable: true, value: { ...original, assign } });

  it("offers the provider by name and sends the browser to the address the server gives", async () => {
    stubLocation();
    const { user } = renderApp("/login", {
      as: null,
      config: { oidcEnabled: true, oidcDisplayName: "Acme ID" },
      handlers: [
        http.post(apiPath("/accounts/oidcstart"), () => HttpResponse.json({ url: "https://id.acme.test/auth?state=s" })),
      ],
    });

    await user.click(await screen.findByRole("button", { name: "Continue with Acme ID" }));
    await waitFor(() => expect(assign).toHaveBeenCalledWith("https://id.acme.test/auth?state=s"));
    // The password form is still there: single sign-on is offered beside it, not instead of it.
    expect(screen.getByRole("button", { name: "Sign in" })).toBeVisible();
  });

  it("is called single sign-on without a name, and stands alone when passwords are off", async () => {
    renderApp("/login", { as: null, config: { oidcEnabled: true, disableEmailPasswordLogin: true } });

    expect(await screen.findByRole("button", { name: "Continue with single sign-on" })).toBeVisible();
    expect(screen.queryByRole("button", { name: "Sign in" })).not.toBeInTheDocument();
    expect(screen.queryByText(/isn't configured/)).not.toBeInTheDocument();
  });

  it("isn't offered when no provider is configured", async () => {
    renderApp("/login", { as: null });
    expect(await screen.findByRole("button", { name: "Sign in" })).toBeVisible();
    expect(screen.queryByRole("button", { name: /single sign-on/ })).not.toBeInTheDocument();
  });

  it("hands the code back to the server once and goes where sign-in was asked to go", async () => {
    sessionStorage.setItem("bitween_oidc_return", "/change-password");
    const sent: Array<Record<string, string>> = [];
    const { router } = renderApp("/oidc-callback?code=the-code&state=the-state", {
      as: null,
      handlers: [
        http.post(apiPath("/accounts/login"), async ({ request }) => {
          sent.push((await request.json()) as Record<string, string>);
          return HttpResponse.json({ jwt: "signed-in" });
        }),
        profile(),
      ],
    });

    await waitFor(() => expect(router.state.location.pathname).toBe("/change-password"));
    expect(sent.filter((body) => body.OidcCode)).toEqual([{ OidcCode: "the-code", OidcState: "the-state" }]);
  });

  it("says what the provider or the server refused, with a way back", async () => {
    renderApp("/oidc-callback?error=access_denied&error_description=The+user+cancelled", { as: null });
    expect(await screen.findByText("The provider didn't sign you in: The user cancelled")).toBeVisible();
    expect(screen.getByRole("link", { name: "Back to sign in" })).toHaveAttribute("href", "/login");
  });

  it("shows the server's reason when the sign-in is refused", async () => {
    renderApp("/oidc-callback?code=c&state=s", {
      as: null,
      handlers: [
        http.post(apiPath("/accounts/login"), () =>
          HttpResponse.json({ message: "Your account is not registered in the system." }, { status: 400 }),
        ),
      ],
    });
    expect(await screen.findByText(/not registered in the system/)).toBeVisible();
  });
});
