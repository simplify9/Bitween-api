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
  // Whatever the database holds: this used to pin to particular exchange ids and subscription
  // names from a seed that no longer exists, which made it a test of the fixtures, not the page.
  await expect(page.getByRole("row").nth(1)).toBeVisible({ timeout: 15000 });
  await expect(page.getByText("undefined")).toHaveCount(0);
  // Avoid the background refetch racing with row selection below.
  await page.getByLabel("Refresh interval").selectOption("0");

  // Filter down to failed exchanges only.
  await page.getByRole("button", { name: "Failed" }).click();
  // Pick by content, not position: the filter re-renders the table, and an index would race it
  // and land on whichever row was showing before.
  const row = page.getByRole("row").filter({ hasText: "Failed" }).first();
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
  // Row checkboxes only. "Select all on this page" also starts with "Select", and its checked
  // state is derived from every row on the page — so a refetch landing mid-click (the filter
  // above triggers one) flips it back and reads as a click that did nothing. A row checkbox is
  // keyed by its own id and survives that. It also keeps the bulk retry to two rows, which is
  // what this test says it wants: each retry is real file I/O.
  const rowCheckbox = page.getByRole("checkbox", { name: /^Select (?!all\b)/ });
  await expect(rowCheckbox.first()).toBeVisible({ timeout: 10000 });
  await rowCheckbox.nth(0).check();
  await rowCheckbox.nth(1).check();
  await expect(page.getByText(/\d+ selected/)).toBeVisible();
  await page.getByRole("button", { name: "Retry selected…" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Retry" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0, { timeout: 15000 });

  // Manually create an exchange addressed at a subscription.
  await page.goto("exchanges/new");
  await page.getByRole("combobox", { name: "Pick a subscription…" }).click();
  await page.getByRole("option").first().click();
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

/**
 * An exchange is retried at most once, so a retried one stops offering Retry and hands over to
 * the attempt that can be retried. Built through the UI rather than pinned to particular ids,
 * since the retry has to exist for the state to be real.
 */
test("a retried exchange shows its chain and sends you to the newest attempt", async ({ page }) => {
  test.setTimeout(60000);
  await page.goto("exchanges?status=failed");
  await expect(page.getByRole("row").nth(1)).toBeVisible({ timeout: 15000 });
  await page.getByLabel("Refresh interval").selectOption("0");

  // Whichever failed exchange has not been retried yet. Most have not, but this database
  // accumulates chains as the suite runs, and an already-retried one has no Retry button to
  // press — which is the very thing under test further down.
  let retried: string | null = null;
  const failedRows = page.getByRole("row").filter({ hasText: "Failed" });
  // The whole page, not the first few rows: this database accumulates chains as the suite runs,
  // and a run that happened to leave several retried exchanges at the top would otherwise fail
  // here before reaching what the test is about. Newest first, and every retry lands at the top
  // as a fresh un-retried leaf, so a page is far more than enough.
  const candidates = await failedRows.count();
  for (let i = 0; i < candidates && retried === null; i++) {
    const row = failedRows.nth(i);
    const label = await row.locator("input[type=checkbox]").getAttribute("aria-label");
    await row.locator("td").last().click();
    if (await page.getByRole("button", { name: "Retry…" }).isVisible()) {
      retried = label!.replace("Select ", "");
      break;
    }
    await row.locator("td").last().click(); // collapse and try the next one
  }
  expect(
    retried,
    `no un-retried failed exchange among the ${candidates} on this page`,
  ).not.toBeNull();

  await page.getByRole("button", { name: "Retry…" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Retry" }).click();
  await expect(page.getByText(/Retry started/)).toBeVisible({ timeout: 15000 });

  // Open that same exchange again: it is spent now.
  await page.goto(`exchanges?ids=${retried}`);
  const row = page.locator(`tr:has(input[aria-label="Select ${retried}"])`);
  await expect(row).toBeVisible({ timeout: 15000 });
  await row.locator("td").last().click();

  await expect(page.getByText("Already retried")).toBeVisible({ timeout: 10000 });
  await expect(page.getByRole("button", { name: "Retry…" })).toHaveCount(0);
  await expect(page.getByRole("link", { name: /Open the newest attempt/ })).toBeVisible();

  // And the chain itself, with this exchange marked in it.
  await expect(page.getByText(/Retry chain · \d+ attempts/)).toBeVisible();
  await expect(page.getByText("You are here")).toBeVisible();
});
