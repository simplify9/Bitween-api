import { screen } from "@testing-library/react";
import type { UserEvent } from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { TOKEN_KEY } from "../../../api";
import { ADMIN, apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * The screen an account with the published default password is held on.
 *
 * Changing the password used to leave you on it: the server ends every session the account holds,
 * and the token you held was issued while the change was still owed, so it grants nothing. The
 * form stayed put, and typing another address opened the app with an empty sidebar.
 */
const NEW_PASSWORD = "Chosen-By-Me-9!";

/** The server's side: the profile follows the token the request carries, as the real one does. */
function server({ signInFails = false } = {}) {
  const calls = { signIn: null as Record<string, string> | null };
  const handlers = [
    http.get(apiPath("/accounts/profile"), ({ request }) => {
      const fresh = request.headers.get("Authorization") === "Bearer fresh-token";
      return HttpResponse.json({
        ...ADMIN,
        role: "Admin",
        disabled: false,
        createdOn: "2026-01-01T00:00:00Z",
        roles: [{ id: 1, name: "Administrator" }],
        // Nothing, under either token here: an empty list lands on the profile page, which asks the
        // API for nothing else. What tells the two tokens apart is the flag.
        permissions: [],
        mustChangePassword: !fresh,
      });
    }),
    http.post(apiPath("/accounts/changePassword"), () => HttpResponse.json(null)),
    http.post(apiPath("/accounts/login"), async ({ request }) => {
      calls.signIn = (await request.json()) as Record<string, string>;
      return signInFails
        ? HttpResponse.json({ SWException: ["Try again later."] }, { status: 500 })
        : HttpResponse.json({ jwt: "fresh-token" });
    }),
    http.post(apiPath("/accounts/logout"), () => HttpResponse.json({})),
    http.get(apiPath("/permissions"), () => HttpResponse.json([])),
  ];
  return { calls, handlers };
}

const choose = async (user: UserEvent) => {
  await user.type(await screen.findByLabelText("Current password"), "Mtm@dmin!2");
  await user.type(screen.getByLabelText("New password"), NEW_PASSWORD);
  await user.type(screen.getByLabelText("Confirm new password"), NEW_PASSWORD);
  await user.click(screen.getByRole("button", { name: "Set password" }));
};

describe("the forced password change", () => {
  it("signs back in with the new password and opens the app", async () => {
    const { calls, handlers } = server();
    const { user, router } = renderApp("/change-password", { handlers });

    await choose(user);

    expect(await screen.findByRole("heading", { name: "Your profile" })).toBeVisible();
    expect(router.state.location.pathname).toBe("/profile");
    expect(calls.signIn).toEqual({ Username: ADMIN.email, Password: NEW_PASSWORD });
    expect(localStorage.getItem(TOKEN_KEY)).toBe("fresh-token");
  });

  it("sends you to sign in when signing back in fails", async () => {
    const { handlers } = server({ signInFails: true });
    const { user } = renderApp("/change-password", { handlers });

    await choose(user);

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeVisible();
  });
});
