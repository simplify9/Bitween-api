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

  // "Failures to act on" counts problems rather than attempts, and has to agree with the list it
  // opens — a tile whose number changes when you click it is worse than no tile.
  // Anchored: the "Chains that keep failing" panel ends with an "All failures to act on" link,
  // which an unanchored match picks up as well.
  const tile = page.getByRole("link").filter({ hasText: /^Failures to act on/ });
  await expect(tile).toBeVisible();
  const count = (await tile.innerText()).match(/([\d,]+)/)?.[1];
  expect(count).toBeTruthy();

  await tile.click();
  await expect(page).toHaveURL(/status=failed&latest=1/);
  await expect(page.getByText(`of ${count}`)).toBeVisible({ timeout: 15000 });

  // The panel that says which of those failures are not getting better, however often they are
  // retried — the number on each row is how many attempts that one piece of work has taken.
  await page.goBack();
  const chains = page.getByText("Chains that keep failing");
  await expect(chains).toBeVisible();
  const rows = page.getByRole("link").filter({ hasText: /^\d+ attempts$/ });
  if ((await rows.count()) > 0) {
    await rows.first().click();
    await expect(page).toHaveURL(/\/exchanges\?ids=/);
  }
});

test("subscription health pages its rows instead of growing without bound", async ({ page }) => {
  // Fourteen unhealthy subscriptions, built from a real row so the rest of the page still resolves:
  // eleven failing, then three paused.
  await page.route("**/api/subscriptions", async (route) => {
    const res = await route.fetch();
    const body = await res.json();
    const template = body.result[0];
    body.result = Array.from({ length: 14 }, (_, i) => ({
      ...template,
      id: 900000 + i,
      name: `Health page ${i + 1}`,
      consecutiveFailures: i < 11 ? i + 1 : 0,
      pausedOn: i < 11 ? null : new Date().toISOString(),
    }));
    body.totalCount = body.result.length;
    await route.fulfill({ response: res, json: body });
  });

  await page.goto("login");
  await page.fill("#login-email", ADMIN_EMAIL);
  await page.fill("#login-password", ADMIN_PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL((url) => !url.pathname.endsWith("/login"), { timeout: 15000 });

  await page.goto("dashboard");
  const panel = page.locator("section").filter({ has: page.getByRole("heading", { name: "Subscription health" }) });
  await expect(panel.getByText("1–10 of 14")).toBeVisible({ timeout: 15000 });
  await expect(panel.getByRole("listitem")).toHaveCount(10);
  await expect(panel.getByText("Health page 1", { exact: true })).toBeVisible();

  await panel.getByRole("button", { name: "Next →" }).click();
  await expect(panel.getByText("11–14 of 14")).toBeVisible();
  await expect(panel.getByRole("listitem")).toHaveCount(4);
  await expect(panel.getByText("Health page 11", { exact: true })).toBeVisible();
  await expect(panel.getByText("Paused")).toHaveCount(3);
  await expect(panel.getByRole("button", { name: "Next →" })).toBeDisabled();
});
