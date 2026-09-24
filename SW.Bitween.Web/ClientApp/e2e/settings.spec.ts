import { test, expect, type Page } from "@playwright/test";
import { signInAsAdmin, signOut } from "./helpers";

const TEAL = "#0f766e";
/** A second colour, so the sign-in test stands on its own if the one above left residue. */
const INDIGO = "#4338ca";
const DEFAULT_COLOR = "#e3311d";

const brandColorVar = (page: Page) =>
  page.evaluate(() => document.documentElement.style.getPropertyValue("--color-crimson-600").trim());

/** Sections are real links — a section is a URL you can paste into a ticket — not buttons. */
const sectionLink = (page: Page, name: string) => page.getByRole("link", { name });

/** The hex box beside the colour swatch; `exact` keeps it apart from the picker itself. */
const hexInput = (page: Page) => page.getByRole("textbox", { name: "Primary color", exact: true });

async function openBrandSection(page: Page) {
  await page.goto("settings");
  await sectionLink(page, "Brand & theme").click();
}

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("brand colour: staged draft previews app-wide, saves, and resets", async ({ page }) => {
  await openBrandSection(page);
  const hex = hexInput(page);
  await expect(hex).toHaveValue(DEFAULT_COLOR);

  await hex.fill(TEAL);
  await hex.blur();
  await expect(page.getByText("Unsaved", { exact: true })).toBeVisible();
  // The draft previews immediately, before anything is saved.
  expect(await brandColorVar(page)).toBe(TEAL);

  // ...and keeps previewing on other pages, with the banner offering a way back. Navigating
  // in-app (rather than a hard load) is what a user does, and what lets the banner name the
  // section holding the change.
  await page.getByRole("link", { name: "Exchanges" }).click();
  await expect(page.getByText(/Previewing 1 unsaved setting change/)).toBeVisible();
  expect(await brandColorVar(page)).toBe(TEAL);

  await page.getByRole("button", { name: /Continue editing/ }).click();
  await expect(page).toHaveURL(/section=Brand/);
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByText("Unsaved", { exact: true })).toHaveCount(0);

  // A reload proves it persisted server-side rather than living in the draft.
  await page.reload();
  await expect(hexInput(page)).toHaveValue(TEAL);
  expect(await brandColorVar(page)).toBe(TEAL);

  await page.getByRole("button", { name: "Reset to default" }).click();
  await page.getByRole("button", { name: "Save changes" }).click();
  await page.reload();
  await expect(hexInput(page)).toHaveValue(DEFAULT_COLOR);
});

test("the sign-in page brands itself before anyone has signed in", async ({ page }) => {
  await openBrandSection(page);
  await hexInput(page).fill(INDIGO);
  await hexInput(page).blur();
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByText("Unsaved", { exact: true })).toHaveCount(0);

  await signOut(page);
  // No session here at all — the login page reads branding from the anonymous config endpoint.
  await expect.poll(() => brandColorVar(page)).toBe(INDIGO);

  await signInAsAdmin(page);
  await openBrandSection(page);
  await page.getByRole("button", { name: "Reset to default" }).click();
  await page.getByRole("button", { name: "Save changes" }).click();
  await page.reload();
  await expect(hexInput(page)).toHaveValue(DEFAULT_COLOR);
});
