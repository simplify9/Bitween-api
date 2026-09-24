import { test, expect } from "@playwright/test";
import { pickOption, signInAsAdmin } from "./helpers";
import {
  SAMPLE,
  addPathRule,
  createSubscription,
  openDetail,
  openMapper,
  setSourcePath,
  writeMapperProperties,
} from "./mapperHelpers";

/**
 * The new mapping editor, in a real browser against the real backend.
 *
 * The journeys: build a mapping, save, reload, and find exactly what was built. The
 * old editor could not do that — it saved a generated Scriban template and
 * reverse-engineered the rules back out of it on load, so subtle detail came back
 * changed and nothing said so.
 *
 * The mapping shapes themselves — every source, every transform, every kind of
 * list — are in mapper-cases.spec.ts.
 */

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("builds a mapping, previews it, saves it, and reloads exactly what was built", async ({
  page,
}) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  // ── The source document drives the field list ──────────────────────────────
  await page.getByRole("textbox", { name: "Sample source document" }).fill(SAMPLE);

  // A value is offered; a field inside a list is not, because it is only reachable
  // from a list over that list.
  await expect(page.getByRole("button", { name: /order\.customer/ })).toBeVisible();
  await expect(page.getByText("list · 2")).toBeVisible();
  await expect(page.getByRole("button", { name: /order\.line\.sku/ })).toHaveCount(0);

  // ── Rules ──────────────────────────────────────────────────────────────────
  await addPathRule(page, "customerName", "order.customer");

  await page.getByRole("button", { name: "Add a field", exact: true }).click();
  await page.getByRole("textbox", { name: "Output field name" }).last().fill("channel");
  await page.getByRole("radio", { name: "Fixed" }).last().click();
  await page.getByRole("textbox", { name: "Fixed value" }).last().fill("WEB");

  // A number target with a transform — the case that proves the arithmetic is done
  // on the server with the real value rather than baked into a template.
  await addPathRule(page, "total", "order.net");
  await openDetail(page, "total");
  await page.getByRole("combobox", { name: "Transform" }).last().selectOption("multiply");
  await page.getByRole("textbox", { name: /Multiply.*By/ }).last().fill("1.16");
  await page.getByRole("combobox", { name: "Value type" }).last().selectOption("number");
  await openDetail(page, "total");

  // ── A list with a filter ───────────────────────────────────────────────────
  await page.getByRole("button", { name: "Add a list", exact: true }).click();
  await page.getByRole("textbox", { name: "Output list name" }).fill("lines");
  await page.getByRole("combobox", { name: "Source list" }).selectOption("p:order.line");
  await page.getByRole("button", { name: "Settings for the list lines" }).click();
  await page.getByRole("checkbox", { name: "Only some entries" }).check();
  await page.getByRole("textbox", { name: "Filter field" }).fill("qty");
  await page.getByRole("combobox", { name: "Filter comparison" }).selectOption("greaterThan");
  await page.getByRole("textbox", { name: "Filter value" }).fill("0");

  // Scoped to the list's own group, so the rule lands inside the list rather than
  // at the top level.
  const lines = page.getByRole("group", { name: "Rules for the list lines" });
  await lines.getByRole("button", { name: "Add a field to lines" }).click();
  await lines.getByRole("textbox", { name: "Output field name" }).fill("code");
  await setSourcePath(lines, "sku");

  // ── The preview comes from the server ──────────────────────────────────────
  const preview = page.locator("pre").first();
  await expect(preview).toContainText('"customerName": "Ali"', { timeout: 15000 });
  await expect(preview).toContainText('"channel": "WEB"');

  // 100 × 1.16. In binary floating point this is 115.99999999999999, which is why
  // the mapper works in decimal. It arrives as 116 rather than 116.00 because a
  // whole number is written as an integer — an order quantity must not pick up a
  // decimal point the source never had.
  await expect(preview).toContainText('"total": 116,');

  // The filter dropped the entry with qty 0.
  await expect(preview).toContainText('"code": "A1"');
  await expect(preview).not.toContainText('"code": "B7"');

  // ── Save, reload, and check nothing changed ────────────────────────────────
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Saved")).toBeVisible({ timeout: 15000 });

  await page.reload();
  await expect(page.getByRole("button", { name: "Save" })).toBeVisible({ timeout: 15000 });

  const names = page.getByRole("textbox", { name: "Output field name" });
  await expect(names.nth(0)).toHaveValue("customerName");
  await expect(names.nth(1)).toHaveValue("channel");
  await expect(names.nth(2)).toHaveValue("total");

  await openDetail(page, "total");
  await expect(page.getByRole("combobox", { name: "Transform" })).toHaveValue("multiply");
  await expect(page.getByRole("combobox", { name: "Value type" })).toHaveValue("number");

  await expect(page.getByRole("textbox", { name: "Output list name" })).toHaveValue("lines");
  await expect(page.getByRole("combobox", { name: "Source list" })).toHaveValue("p:order.line");

  // The row summarises its own filter, so a mapping can be read without opening
  // anything. The controls behind the chevron agree with the summary.
  await expect(page.getByText(/qty\s*>\s*0/)).toBeVisible();
  await page.getByRole("button", { name: "Settings for the list lines" }).click();
  await expect(page.getByRole("combobox", { name: "Filter comparison" })).toHaveValue("greaterThan");

  // And the preview still produces the same document after the round trip.
  await expect(page.locator("pre").first()).toContainText('"total": 116,', { timeout: 15000 });
});

test("choosing the new mapper offers its editor, and the old mapper keeps its own", async ({
  page,
}) => {
  // The old mapper is listed only while a subscription somewhere still uses it, so this
  // test has to put one on it before it can pick it. A separate subscription rather than
  // the one under test: pinning that one would give it a mapper already, and the first
  // thing asserted below is that it has none.
  await writeMapperProperties(await createSubscription(page), "NativeJSONMapper", {
    ScribanTemplate: "{}",
  });

  const subscriptionId = await createSubscription(page);

  await page.goto(`subscriptions/${subscriptionId}`);
  await page.getByRole("button", { name: /^Transformation/ }).click();

  const link = page.getByRole("link", { name: /Open the visual mapping editor/ });

  // Nothing chosen yet, so there is no mapping to open.
  await expect(link).toHaveCount(0);

  await pickOption(page, "mapper adapter", "NativeMapper");
  await expect(link).toBeVisible();
  await expect(link).toHaveAttribute(
    "href",
    new RegExp(`/subscriptions/${subscriptionId}/mapper\\?mapper=NativeMapper$`),
  );

  // Both mappers are named by the same check, so adding the new one cannot quietly
  // take the editor away from the one running subscriptions still use.
  //
  // Choosing an option leaves focus on the picker's input, and the list opens on a
  // focus change — so picking twice from the same picker needs the focus released
  // first. A person never hits this: their next click lands long after focus has
  // settled, and the chevron toggles the list either way.
  await page.getByRole("combobox", { name: "mapper adapter" }).blur();
  await pickOption(page, "mapper adapter", "NativeJSONMapper");
  await expect(link).toBeVisible();
});


test("the editor opens for the mapper you picked, not the one that is saved", async ({ page }) => {
  // The link used to carry only the subscription, so the editor asked the server which
  // mapper it used and got the one being replaced. Choosing a mapper and opening its
  // editor gave you the other one — in both directions — until you saved first, with
  // nothing on screen to say why.
  const subscriptionId = await createSubscription(page);
  await writeMapperProperties(subscriptionId, "NativeJSONMapper", { ScribanTemplate: "{}" });

  await page.goto(`subscriptions/${subscriptionId}`);
  await page.getByRole("button", { name: /^Transformation/ }).click();

  const link = page.getByRole("link", { name: /Open the visual mapping editor/ });

  // Saved as the old mapper, picking the new one: the new editor, no save in between.
  await pickOption(page, "mapper adapter", "NativeMapper");
  await link.click();
  await expect(
    page.getByRole("button", { name: "What this mapping reads and writes" }),
  ).toBeVisible({ timeout: 15000 });

  // And back the other way, which is the same bug reversed.
  await page.goto(`subscriptions/${subscriptionId}/mapper?mapper=NativeJSONMapper`);
  await expect(page.getByRole("button", { name: "Visual" })).toBeVisible({ timeout: 15000 });

  // A mapper nobody has an editor for is ignored rather than opening one on a guess.
  await page.goto(`subscriptions/${subscriptionId}/mapper?mapper=SomethingElse`);
  await expect(page.getByRole("button", { name: "Visual" })).toBeVisible({ timeout: 15000 });
});

test("saving over the mapping the other mapper already has asks first", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await writeMapperProperties(subscriptionId, "NativeJSONMapper", {
    ScribanTemplate: '{ "ref": "{{ order.ref }}" }',
  });

  await page.goto(`subscriptions/${subscriptionId}/mapper?mapper=NativeMapper`);
  await page.getByRole("textbox", { name: "Sample source document" }).fill(SAMPLE);
  await addPathRule(page, "customer", "order.customer");

  // Saving here switches the mapper as well as storing the rules, so the template
  // someone wrote in the other editor goes — and nothing else holds a copy of it.
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText(/Replace the mapping this subscription already has/)).toBeVisible();

  // Cancelling leaves the stored mapping exactly where it was.
  await page.getByRole("button", { name: "Cancel" }).click();
  await page.goto(`subscriptions/${subscriptionId}/mapper?mapper=NativeJSONMapper`);
  await expect(page.getByRole("button", { name: "Visual" })).toBeVisible({ timeout: 15000 });

  // Going through with it does switch, and there is no second question next time.
  await page.goto(`subscriptions/${subscriptionId}/mapper?mapper=NativeMapper`);
  await page.getByRole("textbox", { name: "Sample source document" }).fill(SAMPLE);
  await addPathRule(page, "customer", "order.customer");
  await page.getByRole("button", { name: "Save" }).click();
  await page.getByRole("button", { name: "Replace the mapping" }).click();
  await expect(page.getByText("Saved")).toBeVisible({ timeout: 15000 });
});

test("dragging a source field onto a rule wires it up", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page.getByRole("textbox", { name: "Sample source document" }).fill(SAMPLE);

  await page.getByRole("button", { name: "Add a field", exact: true }).click();
  await page.getByRole("textbox", { name: "Output field name" }).fill("customerName");

  const path = page.getByRole("combobox", { name: "Source field" });
  await expect(path).toHaveValue("");

  await page
    .getByRole("button", { name: "order.customer" })
    .dragTo(page.getByRole("textbox", { name: "Output field name" }));

  await expect(path).toHaveValue("order.customer");
  await expect(page.locator("pre").first()).toContainText('"customerName": "Ali"', {
    timeout: 15000,
  });
});
