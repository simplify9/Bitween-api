import { test, expect, type Page } from "@playwright/test";
import { AdminApi, stamp } from "./api";
import { pickOption, signInAsAdmin } from "./helpers";
import { SEED } from "./seed-data";

/**
 * Editing a subscription that already exists — the path every change after the first goes down,
 * and the one the create specs never reach. Each test saves through the page, reloads it, and
 * then asks the API what was stored, so a value the page only appeared to keep is caught.
 */

/** Published by tools/e2e.sh: a custom handler with versions 1.0.0 and 2.0.0 (current). */
const CUSTOM_HANDLER = "e2e.samplehandler";
const CUSTOM_HANDLER_LABEL = "Echo handler (e2e)";

interface RawSubscription {
  name: string;
  handlerId: string | null;
  handlerVersion: string | null;
  handlerProperties: Record<string, string> | { key: string; value: string }[];
}

/** Handler properties as a plain record, whichever shape the API answered with. */
const handlerProps = (s: RawSubscription): Record<string, string> =>
  Array.isArray(s.handlerProperties)
    ? Object.fromEntries(s.handlerProperties.map((p) => [p.key, p.value]))
    : s.handlerProperties;

async function deliverySubscription(api: AdminApi, name: string, { live = false } = {}) {
  const documentId = await api.idOf("documents", SEED.informationType);
  const partnerId = await api.idOf("partners", SEED.partner);
  return api.post<number>("/subscriptions", {
    name,
    documentId,
    type: "ApiCall",
    partnerId,
    handlerId: "NativeHttpHandler",
    handlerProperties: [{ key: "Url", value: "https://example.com/before" }],
    receiverProperties: [],
    validatorProperties: [],
    mapperProperties: [],
    documentFilter: [],
    // Disabled unless a test sends it something: an enabled one would be live.
    inactive: !live,
  });
}

/** Opens the Delivery step, unless it is already open — a reload keeps the open step (?stage=). */
async function openDelivery(page: Page) {
  const card = page.getByRole("button", { name: /^Delivery/ });
  if ((await card.getAttribute("aria-current")) !== "true") await card.click();
  await expect(card).toHaveAttribute("aria-current", "true");
}
const saveChanges = async (page: Page) => {
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);
};

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("editing an existing subscription's name and delivery settings: the page shows them after a reload, the API stored them, and a secret setting stays masked and is kept until the URL changes", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  const name = `Playwright Edit ${stamp()}`;
  const id = await deliverySubscription(api, name);
  const renamed = `${name} renamed`;

  await page.goto(`subscriptions/${id}`);
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await page.getByRole("textbox", { name: "Name", exact: true }).fill(renamed);

  await openDelivery(page);
  await expect(page.locator("#prop-Url")).toHaveValue("https://example.com/before");
  await page.locator("#prop-Url").fill("https://example.com/after");
  await page.locator("#prop-Verb").fill("put");
  await page.locator("#prop-ContentType").fill("application/xml");
  // A secret setting: written here, and never shown or handed back again.
  await page.locator("#prop-Headers").fill("X-Api-Key=playwright-secret");
  await saveChanges(page);

  await page.reload();
  await expect(page.getByRole("textbox", { name: "Name", exact: true })).toHaveValue(renamed);
  // The stage card summarises what is saved without opening it.
  await expect(page.getByRole("button", { name: /^Delivery/ })).toContainText("https://example.com/after");
  await openDelivery(page);
  await expect(page.locator("#prop-Url")).toHaveValue("https://example.com/after");
  await expect(page.locator("#prop-Verb")).toHaveValue("put");
  await expect(page.locator("#prop-ContentType")).toHaveValue("application/xml");
  await expect(page.locator("#prop-Headers")).toHaveCount(0);
  await expect(page.getByText("playwright-secret")).toHaveCount(0);

  const raw = await api.get<RawSubscription>(`/subscriptions/${id}`);
  expect(raw.name).toBe(renamed);
  expect(handlerProps(raw)).toMatchObject({
    Url: "https://example.com/after",
    Verb: "put",
    ContentType: "application/xml",
  });
  expect(handlerProps(raw).Headers, "the API echoed a secret setting back").not.toBe("X-Api-Key=playwright-secret");
  expect(handlerProps(raw).Headers, "the secret setting was not stored").toBeTruthy();

  // A second edit, to a value already saved once: what is kept is the latest, not the first.
  await page.locator("#prop-Verb").fill("patch");
  await saveChanges(page);
  const again = handlerProps(await api.get<RawSubscription>(`/subscriptions/${id}`));
  expect(again.Verb).toBe("patch");
  // Saved again with the secret still masked on the page: it is kept, not blanked.
  expect(again.Headers).toBe(handlerProps(raw).Headers);

  // Except when where it is sent changes. A kept secret would go to the new address without the
  // editor ever having seen it (AdapterSecretProperties.MayKeepStoredSecrets), so it is dropped and
  // has to be typed in again.
  await page.locator("#prop-Url").fill("https://example.com/elsewhere");
  await saveChanges(page);
  const moved = handlerProps(await api.get<RawSubscription>(`/subscriptions/${id}`));
  expect(moved.Url).toBe("https://example.com/elsewhere");
  expect(moved.Headers).toBeUndefined();

  await api.delete(`/subscriptions/${id}`);
});

test("discarding edits to an existing subscription puts the saved values back and stores nothing", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  const name = `Playwright Discard ${stamp()}`;
  const id = await deliverySubscription(api, name);

  await page.goto(`subscriptions/${id}`);
  await openDelivery(page);
  await page.locator("#prop-Url").fill("https://example.com/not-kept");
  await page.getByRole("button", { name: "Discard" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);
  await expect(page.locator("#prop-Url")).toHaveValue("https://example.com/before");

  expect(handlerProps(await api.get<RawSubscription>(`/subscriptions/${id}`)).Url).toBe("https://example.com/before");

  await api.delete(`/subscriptions/${id}`);
});

test("pinning a subscription's custom handler to v1.0.0: the picker offers the published versions, the pin survives a reload, an exchange runs through it, and unpinning follows current again", async ({
  page,
  request,
}) => {
  test.setTimeout(90_000);
  const api = await AdminApi.signIn(request);
  test.skip(
    !(await api.hasAdapter("handlers", CUSTOM_HANDLER)),
    `${CUSTOM_HANDLER} is not published here — tools/e2e.sh publishes it; another instance needs it published to test pinning`,
  );
  const name = `Playwright Pinned ${stamp()}`;
  const id = await deliverySubscription(api, name, { live: true });

  await page.goto(`subscriptions/${id}`);
  await openDelivery(page);
  // The built-in picker lists the native adapters; custom ones are a step further in.
  await pickOption(page, "handler adapter", /^Custom adapter/);
  await pickOption(page, "Custom handler adapter", new RegExp(CUSTOM_HANDLER_LABEL.replace(/[()]/g, "\\$&")));
  await expect(page.getByText("Custom · v2.0.0")).toBeVisible();

  const version = page.getByRole("combobox", { name: "Adapter version" });
  await expect(version.locator("option")).toHaveText(["Follow current (v2.0.0)", "v2.0.0", "v1.0.0"]);
  await expect(version).toHaveValue("");
  await version.selectOption("1.0.0");
  await saveChanges(page);

  await page.reload();
  await openDelivery(page);
  await expect(page.getByRole("combobox", { name: "Adapter version" })).toHaveValue("1.0.0");
  const pinned = await api.get<RawSubscription>(`/subscriptions/${id}`);
  expect(pinned.handlerId).toBe(CUSTOM_HANDLER);
  expect(pinned.handlerVersion).toBe("1.0.0");

  // The Adapters page counts the pin against the version.
  await page.goto("adapters");
  const custom = page.getByRole("region", { name: /^Custom/ });
  await custom.getByRole("button", { name: new RegExp(CUSTOM_HANDLER_LABEL.replace(/[()]/g, "\\$&")) }).click();
  const v1 = custom.getByRole("row").filter({ hasText: "v1.0.0" });
  await expect(v1.getByRole("cell").nth(2)).toHaveText("1");
  await expect(custom.getByRole("row").filter({ hasText: "v2.0.0" })).toContainText("Current");

  // The pinned package really runs: an exchange sent to the subscription is delivered by it.
  await api.sendExchange(id, { shipment: "pinned" });
  await expect
    .poll(async () => (await api.exchangesOf(id))[0]?.status ?? null, {
      message: "the exchange sent through the pinned handler never finished",
      timeout: 60_000,
    })
    .toBe(true);

  // Unpinning: back to following whatever is current.
  await page.goto(`subscriptions/${id}`);
  await openDelivery(page);
  await page.getByRole("combobox", { name: "Adapter version" }).selectOption("");
  await saveChanges(page);
  expect((await api.get<RawSubscription>(`/subscriptions/${id}`)).handlerVersion).toBeNull();

  await api.delete(`/subscriptions/${id}`);
});

test("a native handler on an existing subscription offers no version to pin", async ({ page, request }) => {
  const api = await AdminApi.signIn(request);
  const id = await deliverySubscription(api, `Playwright Native ${stamp()}`);

  await page.goto(`subscriptions/${id}`);
  await openDelivery(page);
  await expect(page.getByText("Runs in-process")).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Adapter version" })).toHaveCount(0);

  await api.delete(`/subscriptions/${id}`);
});
