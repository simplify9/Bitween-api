import { test, expect, type Page } from "@playwright/test";

/**
 * What the sign-in page offers is driven by the anonymous config endpoint. These tests rewrite
 * that response rather than saving the real setting: `Bitween.DisableEmailPasswordLogin` lives in
 * the database, and a test that flipped it on and then failed would leave this instance reachable
 * only through Microsoft — which the local profile has no MSAL app for. That's a locked door with
 * no key, so the flag is exercised at the boundary instead.
 */
async function withConfig(page: Page, overrides: Record<string, unknown>) {
  await page.route("**/api/settings/config", async (route) => {
    const real = await route.fetch();
    const body = await real.json();
    await route.fulfill({ json: { ...body, ...overrides } });
  });
}

const passwordField = (page: Page) => page.locator("#login-password");
const microsoftButton = (page: Page) => page.getByRole("button", { name: "Continue with Microsoft" });

test("by default the sign-in page asks for an email and password", async ({ page }) => {
  await page.goto("login");

  await expect(passwordField(page)).toBeVisible();
  await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
});

test("Microsoft-only hides the password form instead of letting it fail", async ({ page }) => {
  await withConfig(page, { disableEmailPasswordLogin: true, msalClientId: "00000000-0000-0000-0000-000000000000" });
  await page.goto("login");

  // The backend rejects email/password outright in this mode, so the form must not be offered.
  await expect(microsoftButton(page)).toBeVisible();
  await expect(passwordField(page)).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Sign in" })).toHaveCount(0);
  // With nothing above it, the divider has nothing to divide.
  await expect(page.getByText("or", { exact: true })).toHaveCount(0);
});

test("Microsoft-only with no Microsoft app configured explains itself", async ({ page }) => {
  await withConfig(page, { disableEmailPasswordLogin: true, msalClientId: null });
  await page.goto("login");

  // Both doors are shut. Saying so beats an empty card that looks like a failed page load.
  await expect(page.getByText(/Microsoft sign-in isn't configured/)).toBeVisible();
  await expect(passwordField(page)).toHaveCount(0);
  await expect(microsoftButton(page)).toHaveCount(0);
});
