import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";

/**
 * A server that cannot answer is not a server saying "signed out".
 *
 * `getSession` used to swallow every failure and return null, which the guard reads as
 * "not signed in" — so a rate-limited or briefly unreachable backend threw people to
 * the sign-in page mid-task, with a perfectly good token still in localStorage. That is
 * also what made a whole afternoon of rate-limited test runs look like an auth problem.
 */
test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

for (const status of [429, 500, 503]) {
  test(`a ${status} from the profile call is an outage, not a sign-out`, async ({ page }) => {
    await page.route("**/api/accounts/profile", (route) =>
      route.fulfill({ status, contentType: "application/json", body: "{}" }),
    );

    await page.goto("subscriptions");

    await expect(page.getByRole("heading", { name: "Can't reach Bitween" })).toBeVisible({
      timeout: 15000,
    });
    // The distinction that matters: still signed in, so nothing asks for a password
    // and the token is left where it is.
    await expect(page).not.toHaveURL(/\/login/);
    expect(await page.evaluate(() => localStorage.getItem("access_token"))).toBeTruthy();
  });
}

test("a 401 still signs you out, because that one is the server's answer", async ({ page }) => {
  // Both the profile read and the silent refresh behind it, so there is nothing left
  // to restore the session with — which is a real, unrecoverable sign-out.
  await page.route("**/api/accounts/profile", (route) => route.fulfill({ status: 401, body: "" }));
  await page.route("**/api/accounts/login", (route) => route.fulfill({ status: 401, body: "" }));

  await page.goto("subscriptions");

  await expect(page).toHaveURL(/\/login/, { timeout: 15000 });
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
});

test("the outage screen recovers when the server comes back", async ({ page }) => {
  let failing = true;
  await page.route("**/api/accounts/profile", (route) =>
    failing ? route.fulfill({ status: 503, contentType: "application/json", body: "{}" }) : route.fallback(),
  );

  await page.goto("subscriptions");
  await expect(page.getByRole("heading", { name: "Can't reach Bitween" })).toBeVisible({
    timeout: 15000,
  });

  failing = false;
  await page.getByRole("button", { name: "Try again" }).click();

  await expect(page.getByRole("button", { name: "Account menu" })).toBeVisible({ timeout: 15000 });
});
