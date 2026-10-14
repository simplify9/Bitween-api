import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";

/**
 * What a 390px phone gets on the screens people open when paged. Nothing scrolled sideways
 * before either; it was bought by squeezing twelve columns until every name wrapped one syllable
 * to a line and Queue health ran to nine metres. These hold the other half of the contract: the
 * lists are cards, and the page still never scrolls sideways.
 */

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await signInAsAdmin(page);
});

const CARD_PAGES = ["queue-health", "scheduled-jobs", "subscriptions", "team/members"];

test("operate lists are cards on a phone, and nothing scrolls sideways", async ({ page }) => {
  for (const slug of CARD_PAGES) {
    await page.goto(slug);
    await page.waitForTimeout(1500);
    // Queue health keeps a short table for queues nobody reads; it sits in its own scroll box.
    const tables = await page.locator("main table").count();
    if (slug !== "queue-health") expect(tables, `${slug} still renders a table`).toBe(0);
    const sideways = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    expect(sideways, `${slug} scrolls sideways`).toBeLessThanOrEqual(1);
  }
});

test("the top bar's buttons are big enough for a thumb", async ({ page }) => {
  await page.goto("dashboard");
  for (const name of ["Open navigation", "Search"]) {
    const box = await page.getByRole("button", { name, exact: true }).boundingBox();
    expect(box!.width, name).toBeGreaterThanOrEqual(44);
    expect(box!.height, name).toBeGreaterThanOrEqual(44);
  }
});
