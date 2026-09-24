import { test, expect } from "@playwright/test";
import { ADMIN_EMAIL, ADMIN_PASSWORD, signInAsAdmin } from "./helpers";

/**
 * Where signing in leaves you. Two different answers, which is the point of testing both: an
 * operator opening the app wants to see how the system is doing, but somebody who followed a
 * link — or whose token expired mid-task — wants the page they were going to, not a detour.
 */
test("signing in with nowhere to go lands on the dashboard", async ({ page }) => {
  await signInAsAdmin(page);

  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
});

test("signing in to reach a page lands on that page, not the dashboard", async ({ page }) => {
  // Arriving cold at a protected URL — a link from a colleague, or a reload after the token
  // expired. The guard sends it to login and login has to give it back.
  await page.goto("partners");
  await page.waitForURL((url) => url.pathname.endsWith("/login"));

  await page.fill("#login-email", ADMIN_EMAIL);
  await page.fill("#login-password", ADMIN_PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();

  await expect(page).toHaveURL(/\/partners$/);
});
