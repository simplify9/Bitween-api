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

  // Creating happens in a dialog on the list page, not on a page of its own.
  await page.goto("work-groups");
  await page.getByRole("button", { name: "New work group" }).click();

  const dialog = page.getByRole("dialog", { name: "New work group" });
  // `exact` keeps this off "Bus message name", which also contains "Name".
  await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
  await dialog.getByRole("button", { name: "Create work group" }).click();

  await expect(page).toHaveURL(/\/work-groups\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();
  // Defaults carried over from the create dialog.
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
  // Nothing is assigned to a brand-new group, so "Used by" is an em dash. Its consumer
  // count is deliberately not asserted: declaring the group's queue makes the running
  // instance a consumer of it straight away, so "Nodes" is legitimately 1, not blank.
  await expect(row.getByRole("cell").nth(2)).toHaveText("—");

  await row.getByRole("button", { name: `Open ${name}` }).click();
  await expect(page).toHaveURL(/\/work-groups\/\d+$/);
  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("button", { name: "Delete work group" }).click();
  await expect(page).toHaveURL(/\/work-groups$/);
  await expect(page.getByText(name)).toHaveCount(0);
});
