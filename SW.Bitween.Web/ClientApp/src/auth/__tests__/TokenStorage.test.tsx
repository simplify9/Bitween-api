import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { getToken, SIGNED_IN_KEY, TOKEN_KEY } from "../../api/http/request";
import { apiPath, profile, renderApp } from "../../__tests__/support/renderApp";

/**
 * The Jwt lives in memory, not in localStorage, where any script that ever ran on the page could
 * read it. A reload therefore starts without one and gets it back from the HttpOnly refresh
 * cookie — but only when this browser held a session, so the sign-in page doesn't spend the
 * sign-in rate limit probing for one.
 */
const empty = () => HttpResponse.json({ result: [], totalCount: 0 });
const partnersPage = ["/partners", "/subscriptions", "/apigateways", "/busgateways"].map((path) =>
  http.get(apiPath(path), empty),
);

describe("where the Jwt is kept", () => {
  it("is never written to localStorage", async () => {
    renderApp("/partners", { handlers: partnersPage });
    await screen.findByRole("button", { name: "Account menu" });

    expect(getToken()).toBe("test-token");
    expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
    for (let i = 0; i < localStorage.length; i++)
      expect(localStorage.getItem(localStorage.key(i)!)).not.toContain("test-token");
  });

  it("comes back from the refresh cookie after a reload", async () => {
    // A reload: no Jwt in memory, but this browser was signed in.
    localStorage.setItem(SIGNED_IN_KEY, "1");
    let refreshed = false;
    renderApp("/partners", {
      as: null,
      handlers: [
        ...partnersPage,
        profile(),
        http.post(apiPath("/accounts/login"), () => {
          refreshed = true;
          return HttpResponse.json({ jwt: "from-cookie" });
        }),
      ],
    });

    expect(await screen.findByRole("button", { name: "Account menu" })).toBeVisible();
    expect(refreshed).toBe(true);
    expect(getToken()).toBe("from-cookie");
  });

  it("does not probe for a session on a first visit", async () => {
    let probed = false;
    renderApp("/partners", {
      as: null,
      handlers: [
        http.post(apiPath("/accounts/login"), () => {
          probed = true;
          return new HttpResponse(null, { status: 400 });
        }),
      ],
    });

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeVisible();
    expect(probed).toBe(false);
  });
});
