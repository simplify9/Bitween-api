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

test("API gateway: create, attach partner, create integration detour, edit attachment, detach, delete", async ({
  page,
}) => {
  const name = `Playwright API GW ${Date.now()}`;

  await page.goto("api-gateways/new");
  await page.fill("#nag-name", name);
  await page.getByRole("button", { name: "Create gateway" }).click();
  await expect(page).toHaveURL(/\/api-gateways\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();

  // Attach a partner, detouring to create the required GatewayApiCall integration inline.
  await page.getByRole("button", { name: "Attach partner" }).click();
  await expect(page).toHaveURL(/\/api-gateways\/\d+\/attach$/);

  await page.getByRole("button", { name: "acme" }).click();
  await page.getByRole("button", { name: "Continue" }).click();

  const integrationName = `Playwright GW Integration ${Date.now()}`;
  await page.getByRole("link", { name: "New integration" }).click();
  await expect(page).toHaveURL(/\/subscriptions\/new\?type=GatewayApiCall/);
  await page.fill("#ni-name", integrationName);
  await page.getByRole("button", { name: "test doc" }).click();
  await page.getByLabel("handler adapter").click();
  await page.getByRole("option", { name: "NativeHttpHandler" }).click();
  await expect(page.getByRole("listbox")).toHaveCount(0, { timeout: 10000 });
  await page.locator("#prop-Url").fill("https://example.com/sink");
  await page.getByRole("button", { name: "Create integration" }).click();

  // This page itself renders a ReturnBanner with a "Continue" button before
  // the mutation resolves (inherited from the detour link) — wait for the
  // create to actually land on the new integration's own page first, or the
  // click races and hits that stale button instead.
  await expect(page).toHaveURL(/\/subscriptions\/\d+\?/);
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page).toHaveURL(/\/api-gateways\/\d+\/attach$/);
  await expect(page.getByText(integrationName)).toBeVisible();
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("button", { name: "Attach partner" }).click();

  await expect(page).toHaveURL(/\/api-gateways\/\d+$/);
  await expect(page.getByText("acme").first()).toBeVisible();
  await expect(page.getByText(integrationName)).toBeVisible();

  // Edit the attachment — exercises the remove-then-add path (backend's
  // updatepartner can't mutate a composite-key column in place).
  await page.getByRole("button", { name: "Edit attachment for acme" }).click();
  await expect(page).toHaveURL(/\/api-gateways\/\d+\/attachments\/\d+$/);
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page).toHaveURL(/\/api-gateways\/\d+$/);
  await expect(page.getByText(integrationName)).toBeVisible();

  // Detach.
  await page.getByRole("button", { name: "Detach acme" }).click();
  await page
    .getByRole("dialog", { name: "Detach this partner?" })
    .getByRole("button", { name: "Detach partner" })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("No partners attached")).toBeVisible();

  // Delete (no attachments left — exercises the plain path; cascade-delete
  // path is covered separately since seed data already has an attached gateway).
  await page.getByRole("button", { name: "Delete" }).click();
  await page
    .getByRole("dialog", { name: "Delete this API gateway?" })
    .getByRole("button", { name: "Delete gateway" })
    .click();
  // ApiGatewayPage navigates to /api-gateways, which the router redirects to
  // the unified integrations list.
  await expect(page).toHaveURL(/\/subscriptions\?types=api-gateways$/);
});

test("Bus gateway: create, add route with match expression, edit route, remove, delete", async ({ page }) => {
  const name = `Playwright Bus GW ${Date.now()}`;

  await page.goto("bus-gateways/new");
  await page.fill("#nbg-name", name);
  await page.getByRole("button", { name: "test-hh" }).click();
  await page.getByRole("button", { name: "Create gateway" }).click();
  await expect(page).toHaveURL(/\/bus-gateways\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();

  await page.getByRole("button", { name: "Add route" }).click();
  await expect(page).toHaveURL(/\/bus-gateways\/\d+\/add-route$/);

  // Filter step — leave the match expression empty (null = matches everything).
  await page.getByRole("button", { name: "Continue" }).click();
  // Partner step — no partner.
  await page.getByRole("button", { name: "No partner" }).click();
  await page.getByRole("button", { name: "Continue" }).click();

  // Integration step — detour to create the required BusGateway integration.
  const integrationName = `Playwright Bus Integration ${Date.now()}`;
  await page.getByRole("link", { name: "New integration" }).click();
  await expect(page).toHaveURL(/\/subscriptions\/new\?type=BusGateway/);
  await page.fill("#ni-name", integrationName);
  await page.getByLabel("handler adapter").click();
  await page.getByRole("option", { name: "NativeHttpHandler" }).click();
  await expect(page.getByRole("listbox")).toHaveCount(0, { timeout: 10000 });
  await page.locator("#prop-Url").fill("https://example.com/sink");
  await page.getByRole("button", { name: "Create integration" }).click();

  // Wait for the create to actually land (see the comment in the API gateway
  // test above) before clicking the ReturnBanner's "Continue".
  await expect(page).toHaveURL(/\/subscriptions\/\d+\?/);
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page).toHaveURL(/\/bus-gateways\/\d+\/add-route$/);
  await expect(page.getByText(integrationName)).toBeVisible();
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("button", { name: "Add route" }).click();

  await expect(page).toHaveURL(/\/bus-gateways\/\d+$/);
  await expect(page.getByText(integrationName)).toBeVisible();

  // Edit the route (no-op save exercises the round trip of a null match expression).
  await page.getByRole("button", { name: /Edit route \d+/ }).click();
  await expect(page).toHaveURL(/\/bus-gateways\/\d+\/routes\/\d+$/);
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page).toHaveURL(/\/bus-gateways\/\d+$/);

  // Remove the route.
  await page.getByRole("button", { name: /Remove route \d+/ }).click();
  await page
    .getByRole("dialog", { name: "Remove this route?" })
    .getByRole("button", { name: "Remove route" })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByText("No routes")).toBeVisible();

  await page.getByRole("button", { name: "Delete" }).click();
  await page
    .getByRole("dialog", { name: "Delete this bus gateway?" })
    .getByRole("button", { name: "Delete gateway" })
    .click();
  await expect(page).toHaveURL(/\/subscriptions\?types=bus-gateways$/);
});
