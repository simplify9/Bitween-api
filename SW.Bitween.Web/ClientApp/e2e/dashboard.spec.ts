import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";

test("dashboard loads with real aggregated data", async ({ page }) => {
  await signInAsAdmin(page);

  await page.goto("dashboard");
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible({ timeout: 15000 });
  await expect(page.getByText("Exchanges, last 24 hours")).toBeVisible();
  await expect(page.getByText("Success rate, 7 days")).toBeVisible();
  await expect(page.getByText("Traffic, last 48 hours")).toBeVisible();
  await expect(page.getByText("Housekeeping")).toBeVisible();
  // The page opens before its numbers arrive; check them once they have.
  await expect(page.getByText("loading…")).toHaveCount(0, { timeout: 15000 });
  await expect(page.getByText("Finding the latest failures…")).toHaveCount(0, { timeout: 15000 });
  await expect(page.getByText("Reading the retry chains…")).toHaveCount(0, { timeout: 15000 });
  // A part that couldn't be read stops loading too, and says so.
  await expect(page.getByRole("link").filter({ hasText: /^Exchanges, last 24 hours/ })).not.toContainText(
    "unavailable right now",
  );
  await expect(page.getByText("undefined")).toHaveCount(0);
  await expect(page.getByText("NaN")).toHaveCount(0);

  // "Failures to act on" counts problems rather than attempts, and has to agree with the list it
  // opens - a tile whose number changes when you click it is worse than no tile.
  // Anchored: the "Chains that keep failing" panel ends with an "All failures to act on" link,
  // which an unanchored match picks up as well.
  const tile = page.getByRole("link").filter({ hasText: /^Failures to act on/ });
  await expect(tile).toBeVisible();
  // Counted apart from the rest of the page, so it can fill in after it.
  await expect(tile).toContainText(/\d/, { timeout: 15000 });
  const count = (await tile.innerText()).match(/([\d,]+)/)?.[1];
  expect(count).toBeTruthy();

  await tile.click();
  await expect(page).toHaveURL(/status=failed&latest=1/);
  await expect(page.getByText(`of ${count}`)).toBeVisible({ timeout: 15000 });

  // The panel that says which of those failures are not getting better, however often they are
  // retried - the number on each row is how many attempts that one piece of work has taken.
  await page.goBack();
  const chains = page.getByText("Chains that keep failing");
  await expect(chains).toBeVisible();
  const rows = page.getByRole("link").filter({ hasText: /^\d+ attempts$/ });
  if ((await rows.count()) > 0) {
    await rows.first().click();
    await expect(page).toHaveURL(/\/exchanges\?ids=/);
  }
});
