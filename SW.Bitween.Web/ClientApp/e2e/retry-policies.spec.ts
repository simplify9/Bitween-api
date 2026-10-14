import { test, expect } from "@playwright/test";
import { signInAsAdmin, moreAction } from "./helpers";

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("retry policy create, add group with fixed delay, dry-run, list, delete", async ({ page }) => {
  const name = `Playwright Policy ${Date.now()}`;
  const groupName = "Timeouts";

  await page.goto("retry-policies");
  await page.getByRole("button", { name: "New retry policy" }).first().click();
  await page.fill("#nrp-name", name);
  await page.getByRole("button", { name: "Create policy" }).click();

  await expect(page).toHaveURL(/\/retry-policies\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();
  await expect(page.getByText("No groups yet")).toBeVisible();

  // A group has a page of its own, and saving it writes the policy straight away.
  await page.getByRole("button", { name: "Add group" }).click();
  await expect(page).toHaveURL(/\/retry-policies\/\d+\/groups\/new/);
  // The URL changes a moment before the page does; the policy page has a Name field of its own.
  await expect(page.getByRole("button", { name: "Add condition" })).toBeVisible();
  await page.getByLabel("Group name").fill(groupName);
  await page.getByRole("button", { name: "Add condition" }).click();
  await page.getByLabel("Text to find").fill("timeout");
  // Fixed delay in seconds - the backend stores this in milliseconds, so this
  // exercises the ms <-> seconds conversion in both directions once reloaded.
  await page.locator("#rg-delay").selectOption("fixed");
  await page.locator("#rg-d1").fill("45");
  await page.getByRole("button", { name: "Add group" }).click();

  await expect(page).toHaveURL(/\/retry-policies\/\d+$/);
  await expect(page.getByText(groupName)).toBeVisible();
  // Nothing left to save on the policy page: the group was written by its own page.
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

  // Reload forces a fresh GET - proves the group (incl. the delay unit
  // conversion) actually round-tripped through the backend correctly.
  await page.reload();
  await expect(page.getByText(groupName)).toBeVisible();
  await page.getByRole("button", { name: `Edit ${groupName}` }).click();
  await expect(page.locator("#rg-delay")).toHaveValue("fixed");
  await expect(page.locator("#rg-d1")).toHaveValue("45");
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(page).toHaveURL(/\/retry-policies\/\d+$/);

  // Dry-run against the saved (now unsaved-clean) groups.
  await page.fill("#tp-content", "System.Net.Http.HttpRequestException: timeout while connecting");
  await page.getByRole("button", { name: "Run simulation" }).click();
  // Not the breadcrumb's list: the attempts list on the page.
  const attempt = page.locator(":not(nav) > ol li").first();
  await expect(attempt).toContainText("Retries");
  await expect(attempt).toContainText("Next try in 45s");

  await page.goto("retry-policies");
  const row = page.getByRole("row", { name: new RegExp(name) });
  await expect(row).toBeVisible();
  await expect(row.locator("td").nth(1)).toHaveText("1"); // Groups column

  await row.click();
  await moreAction(page, "Delete");
  await page.getByRole("button", { name: "Delete policy" }).click();
  await expect(page).toHaveURL(/\/retry-policies$/);
  await expect(page.getByText(name)).toHaveCount(0);
});
