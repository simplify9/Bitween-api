import { test, expect } from "@playwright/test";

const ADMIN_EMAIL = "admin@Bitween.systems";
const ADMIN_PASSWORD = "Mtm@dmin!2";

test("dashboard loads with real aggregated data", async ({ page }) => {
  await page.goto("login");
  await page.fill("#login-email", ADMIN_EMAIL);
  await page.fill("#login-password", ADMIN_PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL((url) => !url.pathname.endsWith("/login"), { timeout: 15000 });

  await page.goto("dashboard");
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible({ timeout: 15000 });
  await expect(page.getByText("Exchanges today")).toBeVisible();
  await expect(page.getByText("Success rate (7 days)")).toBeVisible();
  await expect(page.getByText("undefined")).toHaveCount(0);
  await expect(page.getByText("NaN")).toHaveCount(0);
});
