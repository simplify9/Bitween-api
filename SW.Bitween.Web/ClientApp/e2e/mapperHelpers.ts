import { expect, request, type APIRequestContext, type Locator, type Page } from "@playwright/test";
import { ADMIN_EMAIL, ADMIN_PASSWORD, pickOption } from "./helpers";

/**
 * Driving the new mapping editor.
 *
 * Shared by both mapper specs so a change to the editor's chrome is one edit here
 * rather than one per test — the specs then read as the mapping they are about.
 */

const API = "https://localhost:7155/api";

/** A source document with a value, a number, and a list with one entry to filter out. */
export const SAMPLE = JSON.stringify(
  {
    order: {
      customer: "Ali",
      net: 100,
      line: [
        { sku: "A1", qty: 2 },
        { sku: "B7", qty: 0 },
      ],
    },
  },
  null,
  2,
);

/** Creates a subscription with no mapper, which opens in the new editor. */
export async function createSubscription(page: Page): Promise<string> {
  const name = `Playwright Mapper ${Date.now()}`;

  await page.goto("scheduled-jobs/new");
  await page.fill("#nj-name", name);
  await pickOption(page, "Information type", /Shipment order/);

  await pickOption(page, "receiver adapter", "NativeHttpReceiver");
  await page.locator("#prop-Url").fill("https://example.com/feed");
  await page.getByRole("button", { name: "Close this step" }).click();

  await page.getByRole("button", { name: /^Delivery/ }).click();
  await pickOption(page, "handler adapter", "NativeHttpHandler");
  await page.locator("#prop-Url").fill("https://example.com/post");

  await page.getByRole("button", { name: /^(Create|Save)/ }).last().click();
  await page.waitForURL(/\/subscriptions\/\d+/, { timeout: 15000 });

  return page.url().match(/\/subscriptions\/(\d+)/)![1];
}

export async function openMapper(page: Page, subscriptionId: string) {
  await page.goto(`subscriptions/${subscriptionId}/mapper`);
  await expect(page.getByRole("button", { name: "Save" })).toBeVisible({ timeout: 15000 });
}

/**
 * Runs `set` with the format panel open, and closes it afterwards.
 *
 * What the mapping reads and writes sits behind a summary chip rather than in the
 * toolbar row, so a test that changes a format has to open the panel first — and the
 * panel covers the rules underneath, so it has to be closed before touching them.
 */
export async function withFormats(page: Page, set: () => Promise<unknown>) {
  const chip = page.getByRole("button", { name: "What this mapping reads and writes" });
  const panel = page.getByLabel("From format");

  // Waited for on both sides because the chip toggles: acting before the panel has
  // opened, or opening again before the last one has gone, closes it instead.
  await chip.click();
  await expect(panel).toBeVisible({ timeout: 15000 });
  await set();
  await page.keyboard.press("Escape");
  await expect(panel).toBeHidden({ timeout: 15000 });
}

/**
 * Points the last-added rule within `scope` at a source path.
 *
 * The box takes a typed path and offers the sample's paths as suggestions, so a
 * test says what a person would type. `from` picks which scope the path is read
 * against, and that control only exists inside a list.
 */
export async function setSourcePath(
  scope: Locator | Page,
  path: string,
  from: "entry" | "document" = "entry",
) {
  if (from === "document")
    await scope.getByRole("combobox", { name: "Read from" }).last().selectOption("doc");
  await scope.getByRole("combobox", { name: "Source field" }).last().fill(path);
}

/**
 * What a suggest box offers, which is a hint and not a limit.
 *
 * Focused first: only the box being typed in carries a suggestion list, so that a big
 * mapping does not put every row's copy of every path into the page at once.
 */
export async function suggestionsFor(page: Page, field: Locator): Promise<string[]> {
  await field.focus();
  const listId = await field.getAttribute("list");
  if (!listId) return [];
  return page
    .locator(`datalist[id="${listId}"] option`)
    .evaluateAll((options) => options.map((o) => (o as HTMLOptionElement).value));
}

/** Adds a field at the top level and points it at a path. */
export async function addPathRule(page: Page, name: string, path: string) {
  await page.getByRole("button", { name: "Add a field", exact: true }).click();
  await page.getByRole("textbox", { name: "Output field name" }).last().fill(name);
  await setSourcePath(page, path);
}

/** Opens a row's detail panel, which is where the transform, type and lookup live. */
export async function openDetail(page: Page, name: string) {
  await page.getByRole("button", { name: `Details for ${name}` }).click();
}

/** The mapped document, which the server produces. */
export const preview = (page: Page): Locator => page.locator("pre").first();

/** Waits for the preview to settle on the given text. */
export async function expectPreview(page: Page, text: string | RegExp) {
  await expect(preview(page)).toContainText(text, { timeout: 15000 });
}

// ─── Reaching past the editor ─────────────────────────────────────────────────

let api: APIRequestContext | null = null;
let token = "";

async function adminApi(): Promise<APIRequestContext> {
  if (api) return api;
  api = await request.newContext({ ignoreHTTPSErrors: true });
  const login = await api.post(`${API}/accounts/login`, {
    data: { Username: ADMIN_EMAIL, Password: ADMIN_PASSWORD },
  });
  // Without this the token becomes the string "undefined", and every later call fails
  // as "could not read subscription <id>" — which blames the subscription, not the login.
  if (!login.ok())
    throw new Error(`could not sign in as ${ADMIN_EMAIL}: ${login.status()} ${await login.text()}`);
  token = (await login.json()).jwt;
  return api;
}

/**
 * Writes a subscription's mapper properties directly.
 *
 * Only for the cases the editor is supposed to refuse to open: rules that are not
 * JSON, rules from a newer version, rules saved before a rename. None of those can
 * be produced through the editor, and they are exactly the ones where opening blank
 * and letting someone save over the top would destroy a working mapping.
 */
export async function writeMapperProperties(
  subscriptionId: string,
  mapperId: string,
  properties: Record<string, string>,
) {
  const client = await adminApi();
  const auth = { Authorization: `Bearer ${token}` };

  const current = await client.get(`${API}/subscriptions/${subscriptionId}`, { headers: auth });
  if (!current.ok()) throw new Error(`could not read subscription ${subscriptionId}`);
  const raw = await current.json();

  // Posting back what came out, with only the mapper changed. The update endpoint
  // takes the whole row, so anything dropped here would be cleared.
  const res = await client.post(`${API}/subscriptions/${subscriptionId}`, {
    headers: auth,
    data: {
      ...raw,
      mapperId,
      mapperProperties: Object.entries(properties).map(([key, value]) => ({ key, value })),
    },
  });
  if (!res.ok()) throw new Error(`could not write mapper properties: ${await res.text()}`);
}
