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
