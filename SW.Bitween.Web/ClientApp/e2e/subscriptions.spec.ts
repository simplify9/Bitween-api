import { test, expect } from "@playwright/test";
import { pickOption } from "./helpers";

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
  await page.fill("#nj-name", name);

  // The information type is a searchable picker, and the stages below it are cards that open
  // in place — there is no wizard to "Continue" through any more.
  await pickOption(page, "Information type", /Shipment order/);
  await expect(page.getByRole("combobox", { name: "Information type" })).toHaveValue(/Shipment order/);

  // Source — open by default. Receiver adapter plus its one required prop.
  await pickOption(page, "receiver adapter", "NativeHttpReceiver");
  await expect(page.getByRole("combobox", { name: "receiver adapter" })).toHaveValue("HttpReceiver");
  await page.locator("#prop-Url").fill("https://example.com/feed");
  await expect(page.locator("#prop-Url")).toHaveValue("https://example.com/feed");

  // Collapse Source before opening Delivery: only one stage is open at a time, which is what
  // keeps #prop-Url unambiguous below.
  await page.getByRole("button", { name: "Close this step" }).click();

  // Delivery — handler adapter and its required prop (transformation stays "Passes through").
  // Only one stage is open at a time, so #prop-Url is unambiguous here.
  await page.getByRole("button", { name: /^Delivery/ }).click();
  await pickOption(page, "handler adapter", "NativeHttpHandler");
  await expect(page.getByRole("combobox", { name: "handler adapter" })).toHaveValue("HttpHandler");
  await page.locator("#prop-Url").fill("https://example.com/sink");
  await expect(page.locator("#prop-Url")).toHaveValue("https://example.com/sink");

  // "Enable immediately" is checked by default.
  await page.getByRole("button", { name: "Create job" }).click();

  await expect(page).toHaveURL(/\/subscriptions\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();
  // A badge now, not a button — the switch that turns it off lives with Pause on the right.
  await expect(page.getByText("Active", { exact: true }).first()).toBeVisible();

  // Disable / enable. Distinct from pause below: this one drops the work rather than holding it,
  // and unlike the chip it replaced it applies on confirm instead of waiting for a save.
  await page.getByRole("button", { name: "Disable", exact: true }).click();
  await page
    .getByRole("dialog", { name: "Disable this subscription?" })
    .getByRole("button", { name: "Disable" })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("Disabled", { exact: true }).first()).toBeVisible();

  await page.getByRole("button", { name: "Enable", exact: true }).click();
  await page
    .getByRole("dialog", { name: "Enable this subscription?" })
    .getByRole("button", { name: "Enable" })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("Disabled", { exact: true })).toHaveCount(0);

  // Pause / resume. `exact` because the history card's "Show what changed: …, PausedOn, …"
  // button is also a substring match for "Pause" once there are edits to show.
  await page.getByRole("button", { name: "Pause", exact: true }).click();
  await page.getByRole("dialog", { name: "Pause this subscription?" }).getByRole("button", { name: "Pause" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("Paused", { exact: true }).first()).toBeVisible();

  await page.getByRole("button", { name: "Resume", exact: true }).click();
  await page.getByRole("dialog", { name: "Resume this subscription?" }).getByRole("button", { name: "Resume" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("Paused", { exact: true })).toHaveCount(0);

  // Receive now.
  await page.getByRole("button", { name: "Receive now" }).click();
  await page.getByRole("dialog", { name: "Receive now?" }).getByRole("button", { name: "Receive now" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("Next run")).toBeVisible();

  // Reload to prove the adapter config truly persisted server-side. Each stage card
  // summarises what it saved, so both the adapter and its property show without opening it.
  await page.reload();
  const source = page.getByRole("button", { name: /^Source/ });
  await expect(source).toContainText("HttpReceiver");
  await expect(source).toContainText("https://example.com/feed");
  const delivery = page.getByRole("button", { name: /^Delivery/ });
  await expect(delivery).toContainText("HttpHandler");
  await expect(delivery).toContainText("https://example.com/sink");

  // Narrow by type — the supported filter — so the row can't be paged out of sight. Deliberately
  // not the search box: a multi-word term is encoded with "+" and the backend never decodes it,
  // so searching any name containing a space returns nothing (pre-existing, see notes).
  await page.goto("subscriptions?type=Receiving");
  const row = page.getByRole("row", { name: new RegExp(name) });
  await expect(row).toBeVisible({ timeout: 15000 });
  await expect(row.getByText("undefined")).toHaveCount(0);

  // The name, not the row's centre. The whole row opens the subscription, but it also
  // carries links of its own — information type, partner — and which one sits under the
  // centre depends on how wide the columns happen to be.
  await row.getByText(name).click();
  await expect(page).toHaveURL(/\/subscriptions\/\d+$/);
  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("button", { name: "Delete subscription" }).click();
  await expect(page).toHaveURL(/\/subscriptions$/);
  await expect(page.getByText(name)).toHaveCount(0);
});
