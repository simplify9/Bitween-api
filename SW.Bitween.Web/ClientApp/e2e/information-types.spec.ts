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

test("information type Code is optional end to end", async ({ page }) => {
  const name = `Playwright No Code ${Date.now()}`;

  // Creating happens in a dialog on the list page, not on a page of its own.
  await page.goto("information-types");
  await page.getByRole("button", { name: "New information type" }).click();

  const dialog = page.getByRole("dialog", { name: "New information type" });
  await dialog.getByRole("textbox", { name: "Name" }).fill(name);
  // Code starts empty and stays optional — nothing to clear, and it doesn't block creating.
  await expect(dialog.getByRole("textbox", { name: "Code" })).toHaveValue("");

  await dialog.getByRole("button", { name: "Create information type" }).click();

  // Should navigate straight to the detail page — no validation block on empty code.
  await expect(page).toHaveURL(/\/information-types\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();
  await expect(page.locator("#it-code")).toHaveValue("");
  // The identity badge next to the heading falls back to the name when there's no code.
  await expect(page.getByRole("heading", { name }).locator("code")).toHaveText(name);

  // The list page's Code column falls back to the name too, not a blank/dash.
  await page.goto("information-types");
  const row = page.getByRole("row", { name: new RegExp(name) });
  await expect(row.locator("code")).toHaveText(name);

  // Cleanup.
  await row.click();
  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("button", { name: "Delete information type" }).click();
  await expect(page).toHaveURL(/\/information-types$/);
  await expect(page.getByText(name)).toHaveCount(0);
});
