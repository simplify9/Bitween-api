import { test, expect, type Page } from "@playwright/test";
import { API, sessionToken, signInAsAdmin } from "./helpers";

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

/**
 * The ids of `count` failed exchanges that have not been retried, newest first. Polled because a
 * retry made moments ago is still being processed, and only becomes a failure nobody has retried
 * once it has run.
 */
async function unretriedFailures(page: Page, count: number): Promise<string[]> {
  const token = await sessionToken(page);
  const query = ["StatusFilter:1:3", "LatestOnly:1:true"].map((f) => `filter=${encodeURIComponent(f)}`).join("&");
  let ids: string[] = [];
  await expect
    .poll(
      async () => {
        const res = await page.request.get(`${API}/xchanges?${query}&sort=StartedOn:2&page=0&size=${count}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        ids = ((await res.json()) as { result: { id: string }[] }).result.map((x) => x.id);
        return ids.length;
      },
      { message: `fewer than ${count} un-retried failed exchanges — did the seed project run?`, timeout: 15000 },
    )
    .toBeGreaterThanOrEqual(count);
  return ids;
}

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
  await expect(page).toHaveURL(/status=failed/);
  // Pick by content, not position: the filter re-renders the table, and an index would race it
  // and land on whichever row was showing before.
  await expect(page.getByRole("row").filter({ hasText: "Failed" }).first()).toBeVisible({ timeout: 10000 });

  // Which exchanges to retry is asked of the API rather than read off the top of the list. The
  // newest rows are whatever the last run left there: a retry still in flight, or a success
  // whose subscription has since been deleted — neither can be retried, and a bulk retry of
  // nothing but those offers no Retry button to press. The seed keeps at least three failures
  // nobody has retried yet; these are three of them.
  const [single, ...pair] = await unretriedFailures(page, 3);

  // Expand the row (click the chevron cell — other cells stop propagation)
  // and retry it from the drawer.
  await page.goto(`exchanges?ids=${single}`);
  await page.locator(`tr:has(input[aria-label="Select ${single}"])`).locator("td").last().click();
  await page.getByRole("button", { name: "Retry…" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Retry" }).click();
  await expect(page.getByText(/Retry started/)).toBeVisible({ timeout: 10000 });

  // Bulk retry a couple of specific rows (not the whole page — each retry does
  // real file I/O against storage, so keep this fast and deterministic).
  await page.goto(`exchanges?ids=${pair.join(",")}`);
  await page.getByLabel("Refresh interval").selectOption("0");
  // Row checkboxes only. "Select all on this page" also starts with "Select", and its checked
  // state is derived from every row on the page — so a refetch landing mid-click flips it back
  // and reads as a click that did nothing. A row checkbox is keyed by its own id and survives that.
  for (const id of pair) await page.getByRole("checkbox", { name: `Select ${id}`, exact: true }).check();
  await expect(page.getByText("2 selected")).toBeVisible();
  await page.getByRole("button", { name: "Retry selected…" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Retry" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0, { timeout: 15000 });

  // Manually create an exchange addressed at a subscription.
  await page.goto("exchanges/new");
  await page.getByRole("combobox", { name: "Subscription" }).click();
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
  // Queue names start with the environment's name ("v3.development.bitween…", "v3.local.bitween…"),
  // so which one to look for is asked of the same API the page reads rather than assumed.
  const token = await sessionToken(page);
  const res = await page.request.get(`${API}/ops/consumers`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(res.ok()).toBeTruthy();
  const consumers = (await res.json()) as { queueName: string }[];
  expect(consumers.length, "the broker reports no consumers at all").toBeGreaterThan(0);

  await page.goto("queue-health");
  await expect(page.getByRole("heading", { name: "Queue health" })).toBeVisible({ timeout: 15000 });
  await expect(page.getByText(consumers[0].queueName, { exact: true }).first()).toBeVisible({ timeout: 10000 });
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
