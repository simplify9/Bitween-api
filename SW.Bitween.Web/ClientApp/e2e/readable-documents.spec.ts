import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";
import {
  addPathRule,
  createSubscription,
  expectPreview,
  openMapper,
  preview,
  withFormats,
} from "./mapperHelpers";

/**
 * Making a document readable: laid out over lines, and coloured.
 *
 * Two halves of one job, split by whether anyone types into the box. A box you type
 * into keeps a real textarea and gets a Format button; a pane you only read gets
 * colour. Nothing gets both, because colouring text under a caret means either a
 * contenteditable or an overlay that has to track the caret exactly.
 *
 * ── Laying out a document in a box someone types into ──────────────────────
 *
 * A button rather than the exchange drawer's Raw/Formatted toggle: that shows a
 * document nobody can edit, whereas these boxes hold text belonging to whoever typed
 * it, so reflowing it is an action they take and undo reverses. The button is absent
 * when there is nothing to gain, which is `formatDocument`'s own answer and the thing
 * most worth pinning — it is what stops it offering to mangle a half-typed document.
 */

/** How a partner actually hands over a sample: one line, no spaces. */
const MINIFIED_XML =
  `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>` +
  `<shipping xmlns=""><headerValue><accountNumber>55480501</accountNumber></headerValue></shipping>` +
  `</s:Body></s:Envelope>`;

const MINIFIED_JSON = `{"order":{"customer":"Ali","line":[{"sku":"A1"}]}}`;

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("lays out a one-line XML sample, and the tree still reads it", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);
  await withFormats(page, () => page.getByLabel("From format").selectOption("xml"));

  const sample = page.getByRole("textbox", { name: "Sample source document" });
  await sample.fill(MINIFIED_XML);

  await page.getByRole("button", { name: "Format" }).click();

  // Laid out over lines, with the data untouched.
  await expect(sample).toHaveValue(/\n {2}<s:Body>/);
  await expect(sample).toHaveValue(/<accountNumber>55480501<\/accountNumber>/);

  // And it is still the same document as far as the mapping is concerned.
  await page.getByRole("button", { name: "Add a field", exact: true }).click();
  await page.getByRole("textbox", { name: "Output field name" }).last().fill("account");
  await page
    .getByLabel("Source field", { exact: true })
    .last()
    .fill("Envelope.Body.shipping.headerValue.accountNumber");
  await expectPreview(page, '"account": "55480501"');
});

test("offers nothing while there is nothing to lay out", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  const sample = page.getByRole("textbox", { name: "Sample source document" });
  const button = page.getByRole("button", { name: "Format" });

  // Empty, and half-typed: offering to reflow either one could only mangle it.
  await expect(button).toHaveCount(0);
  await sample.fill(`{"order":{"customer":`);
  await expect(button).toHaveCount(0);

  await sample.fill(MINIFIED_JSON);
  await expect(button).toBeVisible();

  // Gone again once the document is already laid out — there is no second press.
  await button.click();
  await expect(sample).toHaveValue(/\n {2}"order": \{/);
  await expect(button).toHaveCount(0);
});

test("formatting changes the layout and nothing else", async ({ page }) => {
  // The invariant that makes the button safe, and the reason it not being undoable is
  // tolerable: it inserts whitespace between tokens and touches nothing else, so the
  // document afterwards maps to exactly what it mapped to before.
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  const sample = page.getByRole("textbox", { name: "Sample source document" });
  await sample.fill(MINIFIED_JSON);

  await page.getByRole("button", { name: "Add a field", exact: true }).click();
  await page.getByRole("textbox", { name: "Output field name" }).last().fill("who");
  await page.getByLabel("Source field", { exact: true }).last().fill("order.customer");
  await expectPreview(page, '"who": "Ali"');
  const before = await preview(page).textContent();

  await page.getByRole("button", { name: "Format" }).click();
  await expect(sample).toHaveValue(/\n {2}"order": \{/);

  // Same output, from a document that now reads as several lines instead of one.
  await expectPreview(page, '"who": "Ali"');
  expect(await preview(page).textContent()).toBe(before);
});

test("lays out the sample of the output too", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page.getByRole("button", { name: "Build from a sample of the output" }).click();
  const target = page.getByRole("textbox", { name: "Sample output document" });
  await target.fill(MINIFIED_JSON);

  await page.getByRole("button", { name: "Format" }).click();
  await expect(target).toHaveValue(/\n {2}"order": \{/);
});

test("the mapped document is coloured, in whichever format it is written", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);
  await page.getByRole("textbox", { name: "Sample source document" }).fill(MINIFIED_JSON);
  await addPathRule(page, "who", "order.customer");

  await expectPreview(page, '"who": "Ali"');

  // The key and the value are separate things, and the pane says so.
  const preview = page.locator(".doc-hl");
  await expect(preview.locator(".hljs-attr").first()).toBeVisible();
  await expect(preview.locator(".hljs-string").first()).toBeVisible();

  // Switching the output to XML colours it as XML, because the mapping declares the
  // format rather than the pane guessing from the text.
  await withFormats(page, () => page.getByLabel("To format").selectOption("xml"));
  await page.getByRole("textbox", { name: "Output field name" }).first().fill("order");
  await expect(preview.locator(".hljs-name").first()).toBeVisible({ timeout: 15000 });
});

test("markup inside a document is shown, never run", async ({ page }) => {
  // The one place in the app that turns a document into HTML. A partner controls the
  // bytes, so the guarantee is worth checking through the real page and not only in
  // the unit test that pins the escaping.
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);
  await page.getByRole("textbox", { name: "Sample source document" }).fill(MINIFIED_JSON);

  await page.getByRole("button", { name: "Add a field", exact: true }).click();
  await page.getByRole("textbox", { name: "Output field name" }).last().fill("note");
  await page.getByRole("radio", { name: "Fixed" }).last().click();
  await page
    .getByRole("textbox", { name: "Fixed value" })
    .last()
    .fill("<script>window.__ran = 1</script>");

  // Visible as characters…
  await expectPreview(page, "<script>window.__ran = 1</script>");

  // …and inert: nothing was added to the document, and nothing ran.
  await expect(page.locator(".doc-hl script")).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as { __ran?: number }).__ran)).toBeUndefined();
});

test("Raw shows the bytes as they arrived, uncoloured", async ({ page }) => {
  // The Raw toggle's whole promise is that nothing has been done to the document.
  // Colour is a claim about its structure, and the pane was making that claim on both
  // sides of the toggle — including for a payload that never parsed, where the parts a
  // grammar still recognises would come out looking fine.
  await page.goto("exchanges/new");
  await page.getByRole("combobox", { name: "Pick a subscription…" }).click();
  await page.getByRole("option").first().click();
  await page.getByRole("heading", { name: "New exchange" }).click();
  await page.locator("textarea").fill(MINIFIED_JSON);
  await page.getByRole("button", { name: "Create exchange" }).click();
  await expect(page).toHaveURL(/\/exchanges\?ids=/);

  const row = page.getByRole("row").nth(1);
  await expect(row).toBeVisible({ timeout: 15000 });
  await row.locator("td").last().click();

  // Formatted is the default, and it parsed, so it is coloured.
  const pane = page.locator(".doc-hl-dark");
  await expect(pane).toContainText('"customer"', { timeout: 15000 });
  await expect(pane.locator(".hljs-attr").first()).toBeVisible();

  await page.getByRole("button", { name: "Raw" }).click();

  // The same document, one line again, and no token spans anywhere in it.
  await expect(pane).toContainText(MINIFIED_JSON);
  await expect(pane.locator("span")).toHaveCount(0);
});
