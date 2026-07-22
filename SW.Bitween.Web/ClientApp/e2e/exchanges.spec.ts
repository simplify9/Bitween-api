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

test("exchanges list, filter, retry, bulk retry, create", async ({ page }) => {
  test.setTimeout(45000);
  await page.goto("exchanges");
  await expect(page.getByRole("row", { name: /azureBlob test sub/ }).first()).toBeVisible({ timeout: 15000 });
  await expect(page.getByText("undefined")).toHaveCount(0);
  // Avoid the background refetch racing with row selection below.
  await page.getByLabel("Refresh interval").selectOption("0");

  // Filter down to failed exchanges only.
  await page.getByRole("button", { name: "Failed" }).click();
  const row = page.getByRole("row", { name: "36b3e2b1003048ff8dec1573b2f752c5" });
  await expect(row).toBeVisible({ timeout: 10000 });

  // Expand the row (click the chevron cell — other cells stop propagation)
  // and retry it from the drawer.
  await row.locator("td").last().click();
  await page.getByRole("button", { name: "Retry…" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Retry" }).click();
  await expect(page.getByText(/Retry started/)).toBeVisible({ timeout: 10000 });

  // Bulk retry a couple of specific rows (not the whole page — each retry does
  // real file I/O against storage, so keep this fast and deterministic).
  await page.getByRole("button", { name: "All" }).click();
  await expect(page.getByRole("checkbox", { name: "Select 18dfd10c4b764b53aea339eedb98de18" })).toBeVisible({
    timeout: 10000,
  });
  await page.getByRole("checkbox", { name: "Select 18dfd10c4b764b53aea339eedb98de18" }).check();
  await page.getByRole("checkbox", { name: "Select a1b088fffe9e4993ac75736576a826cb" }).check();
  await expect(page.getByText(/\d+ selected/)).toBeVisible();
  await page.getByRole("button", { name: "Retry selected…" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Retry" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0, { timeout: 15000 });

  // Manually create an exchange addressed at an integration.
  await page.goto("exchanges/new");
  await page.getByRole("combobox", { name: "Pick an integration…" }).click();
  await page.getByRole("option", { name: "s3 test sub" }).click();
  // Dismiss the dropdown panel via an outside click (it sits above the panel's
  // anchor point, so it can't itself be covered) rather than Escape, which
  // doesn't close this Headless UI combobox instance.
  await page.getByRole("heading", { name: "New exchange" }).click();
  await expect(page.getByRole("listbox")).toHaveCount(0);
  await page.locator("textarea").fill('{"test": true}');
  await page.getByRole("button", { name: "Create exchange" }).click();
  await expect(page).toHaveURL(/\/exchanges\?ids=/);
  await expect(page.getByRole("row")).toHaveCount(2, { timeout: 10000 }); // header + the one new row
});

test("scheduled retries page loads", async ({ page }) => {
  await page.goto("scheduled-retries");
  await expect(page.getByRole("heading", { name: "Scheduled retries" })).toBeVisible({ timeout: 15000 });
  await expect(page.getByText("undefined")).toHaveCount(0);
});

test("queue health page loads with live consumer data", async ({ page }) => {
  await page.goto("queue-health");
  await expect(page.getByRole("heading", { name: "Queue health" })).toBeVisible({ timeout: 15000 });
  await expect(page.getByText("v3.local.bitween").first()).toBeVisible({ timeout: 10000 });
  await expect(page.getByText("undefined")).toHaveCount(0);
});
