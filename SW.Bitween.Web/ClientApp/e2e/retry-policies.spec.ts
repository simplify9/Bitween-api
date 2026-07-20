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

test("retry policy create, add group with fixed delay, dry-run, list, delete", async ({ page }) => {
  const name = `Playwright Policy ${Date.now()}`;
  const groupName = "Timeouts";

  await page.goto("retry-policies");
  await page.getByRole("button", { name: "New retry policy" }).click();
  await page.fill("#nrp-name", name);
  await page.getByRole("button", { name: "Create policy" }).click();

  await expect(page).toHaveURL(/\/retry-policies\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();
  await expect(page.getByText("No groups yet")).toBeVisible();

  await page.getByRole("button", { name: "Add group" }).click();
  const dialog = page.getByRole("dialog", { name: "New group" });
  await dialog.locator("#rg-name").fill(groupName);
  await dialog.getByRole("button", { name: "Add condition" }).click();
  await dialog.getByLabel("Text to find").fill("timeout");
  // Fixed delay in seconds — the backend stores this in milliseconds, so this
  // exercises the ms <-> seconds conversion in both directions once reloaded.
  await dialog.locator("#rg-delay").selectOption("fixed");
  await dialog.locator("#rg-d1").fill("45");
  await dialog.getByRole("button", { name: "Add group" }).click();

  await expect(page.getByText(groupName)).toBeVisible();
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

  // Reload forces a fresh GET — proves the group (incl. the delay unit
  // conversion) actually round-tripped through the backend correctly.
  await page.reload();
  await expect(page.getByText(groupName)).toBeVisible();
  await page.getByRole("button", { name: `Edit ${groupName}` }).click();
  await expect(page.locator("#rg-delay")).toHaveValue("fixed");
  await expect(page.locator("#rg-d1")).toHaveValue("45");
  await page.getByRole("button", { name: "Cancel" }).click();

  // Dry-run against the saved (now unsaved-clean) groups.
  await page.fill("#tp-content", "System.Net.Http.HttpRequestException: timeout while connecting");
  await page.getByRole("button", { name: "Run simulation" }).click();
  const attempt = page.locator("ol li").first();
  await expect(attempt).toContainText("Retries");
  await expect(attempt).toContainText("Next try in 45s");

  await page.goto("retry-policies");
  const row = page.getByRole("row", { name: new RegExp(name) });
  await expect(row).toBeVisible();
  await expect(row.locator("td").nth(1)).toHaveText("1"); // Groups column

  await row.click();
  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("button", { name: "Delete policy" }).click();
  await expect(page).toHaveURL(/\/retry-policies$/);
  await expect(page.getByText(name)).toHaveCount(0);
});
