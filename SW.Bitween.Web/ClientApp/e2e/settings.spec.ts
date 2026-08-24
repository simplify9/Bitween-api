import { test, expect, type Page } from "@playwright/test";
import { signInAsAdmin, signOut, startsWith } from "./helpers";

const TEAL = "#0f766e";
/** A second colour, so the sign-in test stands on its own if the one above left residue. */
const INDIGO = "#4338ca";
const DEFAULT_COLOR = "#e3311d";
const DEFAULT_CRON = "0 * * * * ?";

const brandColorVar = (page: Page) =>
  page.evaluate(() => document.documentElement.style.getPropertyValue("--color-crimson-600").trim());

/** The hex box beside the colour swatch; `exact` keeps it apart from the picker itself. */
const hexInput = (page: Page) => page.getByRole("textbox", { name: "Primary color", exact: true });

async function openBrandSection(page: Page) {
  await page.goto("settings");
  await page.getByRole("button", { name: "Brand & theme" }).click();
}

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("sections come from the backend catalog, with no restart-required rows", async ({ page }) => {
  await page.goto("settings");

  for (const section of [
    "Documents & storage",
    "API behavior",
    "Single sign-on (Microsoft)",
    "Adapters",
    "Reliability & jobs",
    "Messaging",
    "Database",
    "Security",
    "Brand & theme",
  ])
    await expect(page.getByRole("button", { name: section })).toBeVisible();

  // Nothing carries a restart badge: a setting that couldn't take effect immediately is shown
  // as an environment value instead of being offered as an edit that needs a restart to land.
  await expect(page.getByText("Restart", { exact: true })).toHaveCount(0);
});

test("environment settings are shown but not offered as edits", async ({ page }) => {
  await page.goto("settings");
  await page.getByRole("button", { name: "Database" }).click();

  // A read-only row renders its value as text — there's no control carrying its label…
  await expect(page.getByText("Use Azure managed identity")).toBeVisible();
  await expect(page.getByText("Off", { exact: true })).toBeVisible();
  await expect(
    page.getByRole("textbox", { name: "Use Azure managed identity", exact: true }),
  ).toHaveCount(0);
  await expect(page.getByRole("checkbox")).toHaveCount(0);

  // …and a presence row reports only whether a value is set, never the value itself.
  await expect(page.getByText("Not set", { exact: true })).toBeVisible();
  await expect(
    page.getByRole("textbox", { name: "Managed identity client ID", exact: true }),
  ).toHaveCount(0);

  // Neither kind can be reset, because neither is stored.
  await expect(page.getByRole("button", { name: "Reset to default" })).toHaveCount(0);
  await expect(page.getByText("Environment").first()).toBeVisible();
});

test("Microsoft-only sign-in is an editable setting, not an environment value", async ({ page }) => {
  await page.goto("settings");
  await page.getByRole("button", { name: "Single sign-on (Microsoft)" }).click();

  // It applies per request — the Login handler and the config endpoint both read it live — so it
  // belongs in the catalog as an edit rather than a read-only environment row.
  const toggle = page.getByRole("checkbox", { name: startsWith("Off") });
  await expect(toggle).toBeVisible();
  await expect(toggle).not.toBeChecked();
  await expect(page.getByText("Microsoft sign-in only")).toBeVisible();
});

test("the retry schedule is editable and rejects an invalid cron", async ({ page }) => {
  await page.goto("settings");
  await page.getByRole("button", { name: "Reliability & jobs" }).click();

  const cron = page.getByRole("textbox", { name: "Retry poll schedule", exact: true });
  await expect(cron).toHaveValue(DEFAULT_CRON);

  // The backend validates the expression before storing it, because a bad one would break the
  // startup job seeding — so a rejected save leaves the draft dirty rather than silently passing.
  await cron.fill("not a cron");
  await cron.blur();
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByText(/not a valid cron expression/)).toBeVisible();

  await page.getByRole("button", { name: "Discard" }).click();
  await expect(cron).toHaveValue(DEFAULT_CRON);
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

test("a secret's value never reaches the browser", async ({ page }) => {
  const payloads: string[] = [];
  page.on("response", async (res) => {
    if (res.url().endsWith("/api/settings")) payloads.push(await res.text());
  });

  await page.goto("settings");
  await page.getByRole("button", { name: "Adapters" }).click();

  // The local backend configures a Rebex key, so the row shows as set — masked, with the
  // adapter-config "Replace" affordance rather than the value itself.
  await expect(page.getByText("••••••••")).toBeVisible();
  await expect(page.getByRole("button", { name: "Replace" })).toBeVisible();

  expect(payloads.length).toBeGreaterThan(0);
  const rebex = JSON.parse(payloads[0]).find(
    (r: { key: string }) => r.key === "Bitween.RebexLicenseKey",
  );
  expect(rebex.secret).toBe(true);
  expect(rebex.value).toBeNull();
  expect(rebex.defaultValue).toBe("");
  expect(rebex.hasValue).toBe(true);
  // Editable because this instance has an encryption key configured; without one the row comes
  // back read-only instead.
  expect(rebex.editable).toBe(true);
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
