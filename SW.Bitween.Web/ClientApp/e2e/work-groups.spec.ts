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

test("work group create, edit queue settings, list, delete", async ({ page }) => {
  const name = `Playwright Workgroup ${Date.now()}`;

  await page.goto("work-groups/new");
  await page.fill("#nwg-name", name);
  await page.getByRole("button", { name: "Create work group" }).click();

  await expect(page).toHaveURL(/\/work-groups\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();
  // Defaults from the new-page form.
  await expect(page.locator("#wg-prefetch")).toHaveValue("10");
  await expect(page.locator("#wg-priority")).toHaveValue("5");

  await page.fill("#wg-prefetch", "25");
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

  // Reload forces a fresh GET — WorkGroups/Search.cs now reads the DB
  // directly (not IInfolinkCache), so this reflects the update immediately.
  await page.reload();
  await expect(page.locator("#wg-prefetch")).toHaveValue("25");

  await page.goto("work-groups");
  const row = page.getByRole("row", { name: new RegExp(name) });
  await expect(row).toBeVisible();
  // Consumers/Used by aren't wired until integrations (Batch 2), and WorkGroup
  // has no CreatedOn column on the backend — all three render as an em dash.
  await expect(row.getByText("—")).toHaveCount(3);

  await row.getByRole("button", { name: `Open ${name}` }).click();
  await expect(page).toHaveURL(/\/work-groups\/\d+$/);
  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("button", { name: "Delete work group" }).click();
  await expect(page).toHaveURL(/\/work-groups$/);
  await expect(page.getByText(name)).toHaveCount(0);
});
