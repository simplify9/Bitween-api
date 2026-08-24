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

test("scheduled job create, adapters, pause/resume, receive now, list, delete", async ({ page }) => {
  const name = `Playwright Job ${Date.now()}`;

  await page.goto("scheduled-jobs/new");
  await page.fill("#sjw-name", name);
  await page.getByRole("button", { name: "order" }).click();
  await page.getByRole("button", { name: "Continue" }).click();

  // Source & schedule step — receiver adapter + its one required prop.
  await page.getByLabel("receiver adapter").click();
  await page.getByRole("option", { name: "NativeHttpReceiver" }).click();
  await page.keyboard.press("Escape"); // close the combobox popover, it doesn't auto-dismiss
  await expect(page.getByLabel("receiver adapter")).toHaveValue("NativeHttpReceiver");
  await page.locator("#prop-Url").fill("https://example.com/feed");
  await page.getByRole("button", { name: "Continue" }).click();

  // Pipeline step — handler adapter + its required prop (mapper stays "None").
  await page.getByLabel("handler adapter").click();
  await page.getByRole("option", { name: "NativeHttpHandler" }).click();
  await expect(page.getByLabel("handler adapter")).toHaveValue("NativeHttpHandler");
  await expect(page.getByRole("listbox")).toHaveCount(0, { timeout: 10000 });
  await page.locator("#prop-Url").fill("https://example.com/sink");
  await page.getByRole("button", { name: "Continue" }).click();

  // Review step — "Enable immediately" is checked by default.
  await page.getByRole("button", { name: "Create scheduled job" }).click();

  await expect(page).toHaveURL(/\/subscriptions\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();
  await expect(page.getByRole("button", { name: "Active" })).toBeVisible();

  // Pause / resume.
  await page.getByRole("button", { name: "Pause" }).click();
  await page.getByRole("dialog", { name: "Pause this integration?" }).getByRole("button", { name: "Pause" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("Paused", { exact: true }).first()).toBeVisible();

  await page.getByRole("button", { name: "Resume" }).click();
  await page.getByRole("dialog", { name: "Resume this integration?" }).getByRole("button", { name: "Resume" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("Paused", { exact: true })).toHaveCount(0);

  // Receive now.
  await page.getByRole("button", { name: "Receive now" }).click();
  await page.getByRole("dialog", { name: "Receive now?" }).getByRole("button", { name: "Receive now" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("Next run")).toBeVisible();

  // Reload to prove the adapter config truly persisted server-side.
  await page.reload();
  await expect(page.getByLabel("receiver adapter")).toHaveValue("NativeHttpReceiver");
  await expect(page.locator("#prop-Url").first()).toHaveValue("https://example.com/feed");
  await expect(page.getByLabel("handler adapter")).toHaveValue("NativeHttpHandler");

  await page.goto("subscriptions?types=scheduled-jobs");
  const row = page.getByRole("row", { name: new RegExp(name) });
  await expect(row).toBeVisible({ timeout: 15000 });
  await expect(row.getByText("undefined")).toHaveCount(0);

  await row.getByRole("button", { name: `Open ${name}` }).click();
  await expect(page).toHaveURL(/\/subscriptions\/\d+$/);
  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("button", { name: "Delete integration" }).click();
  await expect(page).toHaveURL(/\/subscriptions$/);
  await expect(page.getByText(name)).toHaveCount(0);
});
