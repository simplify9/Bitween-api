import { test, expect } from "@playwright/test";
import { pickOption, signInAsAdmin } from "./helpers";
import {
  SAMPLE,
  addList,
  addListValue,
  addPathRule,
  buildFromSample,
  createSubscription,
  openDetail,
  openMapper,
  setSourcePath,
  suggestionsFor,
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

test("a rule that cannot be applied is named rather than producing an empty field", async ({
  page,
}) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page.getByRole("textbox", { name: "Sample source document" }).fill(SAMPLE);

  await addPathRule(page, "total", "order.customer");
  await openDetail(page, "total");
  await page.getByRole("combobox", { name: "Value type" }).last().selectOption("number");

  // "Ali" is not a number. The old mapper wrote null into the field and said nothing;
  // this fails the mapping and names the rule.
  await expect(page.getByText(/could not be applied/)).toBeVisible({ timeout: 15000 });

  // Reported twice on purpose — once on the rule row that is wrong, and once in the
  // preview panel's summary of everything that failed.
  await expect(page.getByText(/cannot convert 'Ali' to number/)).toHaveCount(2);
  await expect(page.getByRole("alert").filter({ hasText: /cannot convert 'Ali'/ })).toBeVisible();
});

test("stored rules survive a switch to a list-shaped output and back", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page.getByRole("textbox", { name: "Sample source document" }).fill(SAMPLE);
  await addPathRule(page, "customerName", "order.customer");

  await page.getByRole("checkbox", { name: /The whole output is a list/ }).check();
  await expect(page.getByRole("button", { name: "Collapse the list at the root" })).toBeVisible();

  await page.getByRole("checkbox", { name: /The whole output is a list/ }).uncheck();

  // The field rules were put aside, not thrown away.
  await expect(page.getByRole("textbox", { name: "Output field name" })).toHaveValue(
    "customerName",
  );
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

test("builds the whole output from a sample of it, and matches the source fields", async ({
  page,
}) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page.getByRole("textbox", { name: "Sample source document" }).fill(SAMPLE);

  // What the partner expects. Adding this by hand is five rules; on a real document
  // it is hundreds, which is the whole point of building it from the sample.
  await buildFromSample(page, { customer: "", net: 0, line: [{ sku: "", qty: 0 }] });

  await page.getByRole("button", { name: "Build from a sample of the output" }).click();
  await expect(page.getByText(/Added 5 rules · 5 matched to a source field/)).toBeVisible();
  await page.keyboard.press("Escape");

  const names = page.getByRole("textbox", { name: "Output field name" });
  await expect(names.nth(0)).toHaveValue("customer");
  await expect(names.nth(1)).toHaveValue("net");

  await expect(page.getByRole("textbox", { name: "Output list name" })).toHaveValue("line");
  await expect(page.getByRole("combobox", { name: "Source list" })).toHaveValue("p:order.line");

  // A rule inside the list reads one entry, so its path is `sku`, not `order.line.sku`.
  const lines = page.getByRole("group", { name: "Rules for the list line" });
  await expect(lines.getByRole("combobox", { name: "Source field" }).first()).toHaveValue("sku");

  // The number came from `0` in the sample, so the output keeps the partner's type.
  await openDetail(page, "net");
  await expect(page.getByRole("combobox", { name: "Value type" })).toHaveValue("number");
  await openDetail(page, "net");

  // ── The mapping actually runs ──────────────────────────────────────────────
  const preview = page.locator("pre").first();
  await expect(preview).toContainText('"customer": "Ali"', { timeout: 15000 });
  await expect(preview).toContainText('"net": 100');
  await expect(preview).toContainText('"sku": "A1"');
  await expect(preview).toContainText('"sku": "B7"');
});

test("a list of plain values built from a sample is wired up and says so", async ({
  page,
}) => {
  // The shape that sent this round: both sides hold `[1,2,3]`, and the scaffolder
  // wires each entry to the entry itself — the right answer, which used to be shown
  // as an empty box behind a checkbox and read as nothing configured at all.
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page
    .getByRole("textbox", { name: "Sample source document" })
    .fill(JSON.stringify({ city: "errr", test: [1, 2, 3] }));
  await buildFromSample(page, { city: "", test: [1, 2, 3] });
  await page.getByRole("button", { name: "Build from a sample of the output" }).click();
  await page.keyboard.press("Escape");

  const list = page.getByRole("group", { name: "Rules for the list test" });

  // A row in the tree, not a setting behind a chevron — and it reads as an answer
  // rather than as a box waiting to be filled in.
  const value = list.getByRole("combobox", { name: "Source field" });
  await expect(value).toHaveAttribute("placeholder", "the entry itself");
  await expect(value).toHaveValue("");
  await expect(list.getByText("each entry")).toBeVisible();

  // Nothing is left unassigned, which is what the count above the tree has to agree
  // with: an empty path here is the answer, not a blank.
  await expect(page.getByText("2 rules · 2 assigned")).toBeVisible();

  // And it runs: the source values come straight through.
  await expect(page.locator("pre").first()).toHaveText(/"test":\s*\[\s*1,\s*2,\s*3\s*\]/, {
    timeout: 15000,
  });
});

test("a list's value takes a type and a transform like any other rule", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);
  await page
    .getByRole("textbox", { name: "Sample source document" })
    .fill(JSON.stringify({ price: [10, 20] }));

  const list = await addList(page, "totals", "price");
  await addListValue(list, "totals", "");

  // The row carries the whole rule, which is the point of it being a row: the value
  // each entry produces can be multiplied and typed exactly like a named field.
  await list.getByRole("button", { name: "Details for each entry" }).click();
  await list.getByRole("combobox", { name: "Transform" }).selectOption("multiply");
  await list.getByRole("textbox", { name: /Multiply.*By/ }).fill("2");
  await list.getByRole("combobox", { name: "Value type" }).selectOption("number");

  await expect(page.locator("pre").first()).toHaveText(/"totals":\s*\[\s*20,\s*40\s*\]/, {
    timeout: 15000,
  });
});

test("a list inside a list offers the entry's own lists, not the document's", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  // `tags` sits inside an entry of `order.line`, and there is a decoy `tags` at the
  // top of the document that the inner list must not reach for.
  await page.getByRole("textbox", { name: "Sample source document" }).fill(
    JSON.stringify(
      {
        tags: [{ code: "DECOY" }],
        order: {
          line: [
            { sku: "A1", tags: [{ code: "fragile" }, { code: "boxed" }] },
            { sku: "B7", tags: [{ code: "cold" }] },
          ],
        },
      },
      null,
      2,
    ),
  );

  await buildFromSample(page, { line: [{ sku: "", tags: [{ code: "" }] }] });

  // The outer list is named from the document; the inner one from one entry of it.
  // Offering `order.line.tags` here was a real bug: the mapper resolves a nested
  // list against the entry, so that path names nothing at all.
  const lists = page.getByRole("combobox", { name: "Source list" });
  await expect(lists.nth(0)).toHaveValue("p:order.line");
  await expect(lists.nth(1)).toHaveValue("p:tags");
  // Only the entry's own lists, plus the choice to walk nothing at all.
  await expect(lists.nth(1).locator("option")).toHaveText([
    "— just the entries below —",
    "tags",
  ]);

  // And it runs: two entries, each with its own tags, and no sign of the decoy.
  const preview = page.locator("pre").first();
  await expect(preview).toContainText('"code": "fragile"', { timeout: 15000 });
  await expect(preview).toContainText('"code": "boxed"');
  await expect(preview).toContainText('"code": "cold"');
  await expect(preview).not.toContainText("DECOY");
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

test("a lookup table substitutes values, and says what happens to a miss", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page.getByRole("textbox", { name: "Sample source document" }).fill(
    JSON.stringify({ country: "JO", other: "XX" }, null, 2),
  );

  await addPathRule(page, "countryName", "country");
  await addPathRule(page, "otherName", "other");

  // ── The table applies ──────────────────────────────────────────────────────
  await openDetail(page, "countryName");
  await page.getByRole("checkbox", { name: "Substitute values from a table" }).check();
  await page.getByRole("button", { name: "Add incoming value" }).click();
  await page.getByRole("textbox", { name: "Incoming value 1" }).fill("JO");
  await page.getByRole("textbox", { name: "Becomes 1" }).fill("Jordan");

  await expect(page.locator("pre").first()).toContainText('"countryName": "Jordan"', {
    timeout: 15000,
  });

  // ── A miss is empty unless the rule says otherwise ─────────────────────────
  await expect(page.getByText("Otherwise the field is left empty.")).toBeVisible();
  await page.getByRole("checkbox", { name: /Use a fallback/ }).check();
  await page.getByRole("textbox", { name: "Lookup fallback" }).fill("Unknown");

  await expect(page.locator("pre").first()).toContainText('"otherName": "XX"');
  await expect(page.locator("pre").first()).toContainText('"countryName": "Jordan"');
});

test("a rule inside a list can read a value from the top of the document", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page.getByRole("textbox", { name: "Sample source document" }).fill(SAMPLE);

  await page.getByRole("button", { name: "Add a list", exact: true }).click();
  await page.getByRole("textbox", { name: "Output list name" }).fill("lines");
  await page.getByRole("combobox", { name: "Source list" }).selectOption("p:order.line");

  const lines = page.getByRole("group", { name: "Rules for the list lines" });
  await lines.getByRole("button", { name: "Add a field to lines" }).click();
  await lines.getByRole("textbox", { name: "Output field name" }).fill("code");

  // Inside a list, "sku" alone is ambiguous — it could be the line's or the
  // document's — so the scope is its own control, and the suggestions follow it.
  const field = lines.getByRole("combobox", { name: "Source field" });
  const scope = lines.getByRole("combobox", { name: "Read from" });

  await expect(scope).toHaveValue("entry");
  expect(await suggestionsFor(page, field)).toContain("sku");

  await scope.selectOption("doc");
  expect(await suggestionsFor(page, field)).toContain("order.customer");
  await scope.selectOption("entry");

  await setSourcePath(lines, "sku");

  await lines.getByRole("button", { name: "Add a field to lines" }).click();
  await lines.getByRole("textbox", { name: "Output field name" }).last().fill("customer");
  await setSourcePath(lines, "order.customer", "document");

  // Every line carries the order's customer, which a path read on the entry cannot do.
  const preview = page.locator("pre").first();
  await expect(preview).toContainText('"code": "A1"', { timeout: 15000 });
  await expect(preview).toContainText('"customer": "Ali"');

  // And it survives the round trip as a document-scoped read, not an entry one.
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Saved")).toBeVisible({ timeout: 15000 });
  await page.reload();
  await expect(page.getByRole("button", { name: "Save" })).toBeVisible({ timeout: 15000 });

  const reloaded = page.getByRole("group", { name: "Rules for the list lines" });
  await expect(reloaded.getByRole("combobox", { name: "Source field" }).nth(0)).toHaveValue("sku");
  await expect(reloaded.getByRole("combobox", { name: "Read from" }).nth(0)).toHaveValue("entry");
  await expect(reloaded.getByRole("combobox", { name: "Source field" }).nth(1)).toHaveValue(
    "order.customer",
  );
  await expect(reloaded.getByRole("combobox", { name: "Read from" }).nth(1)).toHaveValue("doc");
});

test("a list can carry entries written into it, before the ones it walks", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page.getByRole("textbox", { name: "Sample source document" }).fill(SAMPLE);

  await page.getByRole("button", { name: "Add a list", exact: true }).click();
  await page.getByRole("textbox", { name: "Output list name" }).fill("lines");
  await page.getByRole("combobox", { name: "Source list" }).selectOption("p:order.line");

  const lines = page.getByRole("group", { name: "Rules for the list lines" });
  await lines.getByRole("button", { name: "Add a field to lines" }).click();
  await lines.getByRole("textbox", { name: "Output field name" }).fill("sku");
  await setSourcePath(lines, "sku");

  // ── A header line the partner expects ──────────────────────────────────────
  await lines.getByRole("button", { name: "Add an entry to lines" }).click();

  const entry = page.getByRole("group", { name: "Rules for entry 1" });
  await entry.getByRole("button", { name: "Add a field to entry 1" }).click();
  await entry.getByRole("textbox", { name: "Output field name" }).fill("sku");
  await entry.getByRole("radio", { name: "Fixed" }).click();
  await entry.getByRole("textbox", { name: "Fixed value" }).fill("HEADER");

  // Written entries come first, then one per entry of the source list — the order
  // the previous mapper produced for the same configuration.
  const preview = page.locator("pre").first();
  await expect(preview).toContainText('"sku": "HEADER"', { timeout: 15000 });
  await expect(preview).toHaveText(/HEADER[\s\S]*A1[\s\S]*B7/);

  // ── And it survives the round trip ─────────────────────────────────────────
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Saved")).toBeVisible({ timeout: 15000 });
  await page.reload();
  await expect(page.getByRole("button", { name: "Save" })).toBeVisible({ timeout: 15000 });

  await expect(page.getByRole("group", { name: "Rules for entry 1" })).toBeVisible();
  await expect(page.locator("pre").first()).toHaveText(/HEADER[\s\S]*A1/, { timeout: 15000 });
});

test("a list of values with a slot per rule, walking nothing", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page.getByRole("textbox", { name: "Sample source document" }).fill(SAMPLE);

  await page.getByRole("button", { name: "Add a list", exact: true }).click();
  await page.getByRole("textbox", { name: "Output list name" }).fill("codes");

  // Nothing to walk, so the list is exactly what is written into it. This is what
  // the old mapper called a primitive array.
  await page.getByRole("combobox", { name: "Source list" }).selectOption("none");

  // What the list holds is decided by what is put in it, not by a setting: the first
  // slot says these are plain values, and every entry after it follows.
  await page.getByRole("button", { name: "Add a value to codes" }).click();
  await page.getByRole("button", { name: "Add an entry to codes" }).click();

  const first = page.getByRole("group", { name: "Entry 1" });
  const second = page.getByRole("group", { name: "Entry 2" });

  await setSourcePath(first, "order.customer");
  await second.getByRole("radio", { name: "Fixed" }).click();
  await second.getByRole("textbox", { name: "Fixed value" }).fill("WEB");

  await expect(page.locator("pre").first()).toContainText('"codes"', { timeout: 15000 });
  await expect(page.locator("pre").first()).toHaveText(/"codes":\s*\[\s*"Ali",\s*"WEB"\s*\]/);
});
