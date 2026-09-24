import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { TOKEN_KEY } from "../../api";
import { apiPath, renderApp } from "../../__tests__/support/renderApp";

/**
 * A server that cannot answer is not a server saying "signed out".
 *
 * `getSession` used to swallow every failure and return null, which the guard reads as
 * "not signed in" — so a rate-limited or briefly unreachable backend threw people to
 * the sign-in page mid-task, with a perfectly good token still in localStorage. That is
 * also what made a whole afternoon of rate-limited test runs look like an auth problem.
 */
const outage = () => screen.findByRole("heading", { name: "Can't reach Bitween" });

/** What the subscriptions page reads once it is let in: nothing configured yet. */
const empty = () => HttpResponse.json({ result: [], totalCount: 0 });
const subscriptionsPage = ["/subscriptions", "/documents", "/partners", "/apigateways", "/busgateways"].map(
  (path) => http.get(apiPath(path), empty),
);

describe("a session read the server could not answer", () => {
  it.each([429, 500, 503])("treats a %i from the profile call as an outage, not a sign-out", async (status) => {
    const { router } = renderApp("/subscriptions", {
      handlers: [http.get(apiPath("/accounts/profile"), () => HttpResponse.json({}, { status }))],
    });

    expect(await outage()).toBeVisible();
    // The distinction that matters: still signed in, so nothing asks for a password
    // and the token is left where it is.
    expect(router.state.location.pathname).toBe("/subscriptions");
    expect(localStorage.getItem(TOKEN_KEY)).toBeTruthy();
  });

  it("still signs you out on a 401, because that one is the server's answer", async () => {
    // Both the profile read and the silent refresh behind it, so there is nothing left
    // to restore the session with — which is a real, unrecoverable sign-out.
    const { router } = renderApp("/subscriptions", {
      handlers: [
        http.get(apiPath("/accounts/profile"), () => new HttpResponse(null, { status: 401 })),
        http.post(apiPath("/accounts/login"), () => new HttpResponse(null, { status: 401 })),
      ],
    });

    await waitFor(() => expect(router.state.location.pathname).toBe("/login"));
    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeVisible();
  });

  it("recovers from the outage screen when the server comes back", async () => {
    let failing = true;
    const { user } = renderApp("/subscriptions", {
      handlers: [
        // Once it stops failing it falls through to the ordinary profile.
        http.get(apiPath("/accounts/profile"), () => (failing ? HttpResponse.json({}, { status: 503 }) : undefined)),
        ...subscriptionsPage,
      ],
    });
    expect(await outage()).toBeVisible();

    failing = false;
    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByRole("button", { name: "Account menu" })).toBeVisible();
  });
});
