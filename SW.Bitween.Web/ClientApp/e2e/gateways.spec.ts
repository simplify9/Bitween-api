import { test, expect } from "@playwright/test";
import { pickOption, signInAsAdmin } from "./helpers";
import { SEED } from "./seed-data";

/** A seeded partner, used for the attachment this test makes and then removes. */
const PARTNER = SEED.partner;

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("API gateway: create, attach partner, create subscription detour, edit attachment, detach, delete", async ({
  page,
}) => {
  const name = `Playwright API GW ${Date.now()}`;

  await page.goto("api-gateways/new");
  await page.fill("#nag-name", name);
  await page.getByRole("button", { name: "Create gateway" }).click();
  await expect(page).toHaveURL(/\/api-gateways\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();

  // Attach a partner, detouring to create the required GatewayApiCall subscription. The
  // detour is a nested route off the attach page, and both pickers are comboboxes now —
  // there is no wizard to "Continue" through.
  await page.getByRole("button", { name: "Attach partner" }).click();
  await expect(page).toHaveURL(/\/api-gateways\/\d+\/attach$/);

  await pickOption(page, "Partner", new RegExp(PARTNER));
  await expect(page.getByRole("combobox", { name: "Partner" })).toHaveValue(PARTNER);

  const subscriptionName = `Playwright GW Subscription ${Date.now()}`;
  await page.getByRole("button", { name: "New subscription" }).click();
  // The picked partner rides along as a query param through the detour.
  await expect(page).toHaveURL(/\/api-gateways\/\d+\/attach\/new-subscription/);
  await page.fill("#ngi-name", subscriptionName);
  await pickOption(page, "Information type", new RegExp(SEED.informationType));
  await expect(page.getByRole("combobox", { name: "Information type" })).toHaveValue(new RegExp(SEED.informationType));
  await pickOption(page, "handler adapter", "NativeHttpHandler");
  await page.locator("#prop-Url").fill("https://example.com/sink");
  await expect(page.locator("#prop-Url")).toHaveValue("https://example.com/sink");
  await page.getByRole("button", { name: "Create subscription" }).click();

  // Back on the attach form with the new subscription already chosen.
  await expect(page).toHaveURL(/\/api-gateways\/\d+\/attach(\?|$)/);
  // It comes back selected in the picker, so it is the combobox's value, not page text.
  await expect(page.getByRole("combobox", { name: "Subscription" })).toHaveValue(subscriptionName);
  await page.getByRole("button", { name: "Attach partner" }).click();

  await expect(page).toHaveURL(/\/api-gateways\/\d+$/);
  await expect(page.getByText(PARTNER).first()).toBeVisible();
  await expect(page.getByText(subscriptionName).first()).toBeVisible();

  // Edit the attachment — exercises the remove-then-add path (backend's
  // updatepartner can't mutate a composite-key column in place).
  await page.getByRole("button", { name: `Edit attachment for ${PARTNER}` }).click();
  await expect(page).toHaveURL(/\/api-gateways\/\d+\/attachments\/\d+$/);
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page).toHaveURL(/\/api-gateways\/\d+$/);
  await expect(page.getByText(subscriptionName)).toBeVisible();

  // Detach.
  await page.getByRole("button", { name: `Detach ${PARTNER}` }).click();
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
  // API gateways have their own list page now, which is where deleting one lands.
  await expect(page).toHaveURL(/\/api-gateways$/);
});

test("Bus gateway: create, add route with match expression, edit route, remove, delete", async ({ page }) => {
  const name = `Playwright Bus GW ${Date.now()}`;

  await page.goto("bus-gateways/new");
  await page.fill("#nbg-name", name);
  // Bus-enabled types only, and this one is the one no seeded gateway already listens for.
  await pickOption(page, "Information type", new RegExp(SEED.busInformationType));
  await expect(page.getByRole("combobox", { name: "Information type" })).toHaveValue(new RegExp(SEED.busInformationType));
  await page.getByRole("button", { name: "Create gateway" }).click();
  await expect(page).toHaveURL(/\/bus-gateways\/\d+$/);
  await expect(page.getByRole("heading", { name })).toBeVisible();

  // Routes are built on the gateway's own canvas now — no separate add-route page, and no
  // wizard: the route, its subscription and that subscription's delivery are all edited in
  // place, and "Create route" saves the lot.
  // An empty gateway offers both "Add a route" in the header and "Add the first route"
  // in the empty canvas; either does the same thing.
  await page.getByRole("button", { name: /^Add (a|the first) route/ }).first().click();
  await expect(page).toHaveURL(/\/bus-gateways\/\d+\?route=new/);

  // A new route opens on its own node, which is where its filter lives. Filters match on the
  // information type's promoted properties, which the seed gives this type.
  const { path } = SEED.busFilterProperty;
  await page.getByRole("button", { name: "Add a filter" }).click();
  await page.getByRole("combobox", { name: "Property" }).selectOption(path);
  await page.getByRole("textbox", { name: "Values" }).fill("DHL");

  // No partner: the route runs without {{partner.…}} values.
  const subscriptionName = `Playwright Bus Subscription ${Date.now()}`;
  await page.getByRole("button", { name: "New subscription" }).click();
  await page.fill("#bs-int-name", subscriptionName);

  // Its delivery is a node on the same canvas.
  await page.getByRole("button", { name: /^Delivery/ }).click();
  await pickOption(page, "handler adapter", "NativeHttpHandler");
  await page.locator("#prop-Url").fill("https://example.com/sink");
  await expect(page.locator("#prop-Url")).toHaveValue("https://example.com/sink");

  await page.getByRole("button", { name: "Create route" }).click();
  await expect(page).toHaveURL(/\/bus-gateways\/\d+\?.*route=\d+/);

  // Reload to prove the route round-tripped, filter and all. The route list names each route by
  // its filter, so that is where the saved expression shows.
  await page.reload();
  await expect(page.getByText(subscriptionName).first()).toBeVisible();
  await expect(page.getByText(`${path} is DHL`).first()).toBeVisible();

  // Edit the route: widen the filter to a second value. This goes through the route update
  // rather than the create, which is the path that has to carry the expression over intact.
  await page.getByRole("button", { name: /^Matches when/ }).first().click();
  await expect(page.getByRole("textbox", { name: "Values" })).toHaveValue("DHL");
  await page.getByRole("textbox", { name: "Values" }).fill("DHL, UPS");
  await expect(page.getByText(/Unsaved: the route \(filter\)/)).toBeVisible();
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByText(/^Unsaved:/)).toHaveCount(0);

  await page.reload();
  await expect(page.getByText(`${path} is one of DHL, UPS`).first()).toBeVisible();
  await expect(page.getByText(`${path} is DHL`, { exact: true })).toHaveCount(0);

  // Remove the route.
  await page.getByRole("button", { name: "Remove route" }).click();
  await page
    .getByRole("dialog", { name: "Remove this route?" })
    .getByRole("button", { name: "Remove route" })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  // The route list and the main panel both say this, so name the panel's heading: it shows only
  // when no route is selected, which is the thing actually worth proving here — that removing
  // the last route doesn't leave the page still pointing at it.
  await expect(page.getByRole("heading", { name: /^No routes —/ })).toBeVisible();

  await page.getByRole("button", { name: "Delete" }).click();
  await page
    .getByRole("dialog", { name: "Delete this bus gateway?" })
    .getByRole("button", { name: "Delete gateway" })
    .click();
  // Bus gateways have their own list page now, which is where deleting one lands.
  await expect(page).toHaveURL(/\/bus-gateways$/);
});
