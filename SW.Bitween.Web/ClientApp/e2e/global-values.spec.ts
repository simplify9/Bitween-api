import { test, expect } from "@playwright/test";

const ADMIN_EMAIL = "admin@Bitween.systems";
const ADMIN_PASSWORD = "Mtm@dmin!2";

test.beforeEach(async ({ page }) => {
  await page.goto("login");
  await page.fill("#login-email", ADMIN_EMAIL);
  await page.fill("#login-password", ADMIN_PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL((url) => !url.pathname.endsWith("/login"), { timeout: 15000 });
});

test("global value set create, edit values, list, delete", async ({ page }) => {
  const stamp = Date.now();
  const name = `Playwright Values ${stamp}`;

  await page.goto("global-values");
  await page.getByRole("button", { name: "New value set" }).click();
  await page.fill("#nvs-name", name);
  await page.getByRole("button", { name: "Create value set" }).click();

  await expect(page).toHaveURL(/\/global-values\/[a-z0-9-]+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();

  await page.getByRole("button", { name: "Add key" }).click();
  await page.getByLabel("Key 1").fill("baseUrl");
  await page.getByLabel("Value 1").fill("https://example.com");
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

  // Reload forces a fresh GET — proves the write actually persisted server-side.
  await page.reload();
  await expect(page.getByLabel("Key 1")).toHaveValue("baseUrl");
  await expect(page.getByLabel("Value 1")).toHaveValue("https://example.com");

  await page.goto("global-values");
  const row = page.getByRole("row", { name: new RegExp(name) });
  await expect(row).toBeVisible();
  await expect(row.locator("td").nth(2)).toHaveText("1"); // Values column

  await row.click();
  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("button", { name: "Delete value set" }).click();
  await expect(page).toHaveURL(/\/global-values$/);
  await expect(page.getByText(name)).toHaveCount(0);
});
