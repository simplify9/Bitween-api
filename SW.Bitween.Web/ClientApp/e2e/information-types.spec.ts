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

  await page.goto("information-types/new");
  await page.fill("#nit-name", name);

  // Expand the collapsed code/format section and clear the auto-suggested code.
  await page.getByRole("button", { name: /^Code/ }).click();
  const codeInput = page.locator("#nit-code");
  await expect(codeInput).not.toHaveAttribute("required", "");
  await codeInput.fill("");

  await page.getByRole("button", { name: "Create information type" }).click();

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
