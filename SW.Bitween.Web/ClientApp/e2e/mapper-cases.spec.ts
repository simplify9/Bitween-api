import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";
import { MAPPER_PARTNER } from "./global-setup";
import {
  addFixedRule,
  addList,
  addListField,
  addPathRule,
  createSubscription,
  expectPreview,
  openDetail,
  openWithSample,
  preview,
  saveAndReload,
  setSourcePath,
  suggestionsFor,
  writeMapperProperties,
} from "./mapperHelpers";

/**
 * Every shape a JSON-to-JSON mapping can take, built in the editor and run.
 *
 * The mapping engine itself is covered exhaustively by the C# unit tests — every
 * transform argument, every coercion, every filter operator. What those cannot see
 * is whether the editor can *express* each of those shapes, and whether what it
 * saves reads back as the same mapping. That is what these are for: one pass per
 * shape, through the real UI, against the real server.
 */

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

/** Chooses whose partner values the preview resolves against. */
async function previewAsTestPartner(page: import("@playwright/test").Page) {
  await page
    .getByRole("combobox", { name: "Preview as partner" })
    .selectOption({ label: `${MAPPER_PARTNER} · 2 properties` });
}

/** Adds a field at the top level, leaving it selected and unassigned. */
async function addNamedRule(page: import("@playwright/test").Page, name: string) {
  await page.getByRole("button", { name: "Add a field", exact: true }).click();
  await page.getByRole("textbox", { name: "Output field name" }).last().fill(name);
}

// ─── Transforms ───────────────────────────────────────────────────────────────

/** Every transform, its arguments, and what it makes of the sample below. */
const TRANSFORM_CASES: {
  field: string;
  path: string;
  fn: string;
  args?: [string, string][];
  expect: string;
}[] = [
  { field: "up", path: "text", fn: "upper", expect: '"up": "HELLO"' },
  { field: "down", path: "shout", fn: "lower", expect: '"down": "loud"' },
  { field: "tidy", path: "padded", fn: "trim", expect: '"tidy": "pad"' },
  {
    field: "part",
    path: "text",
    fn: "substring",
    args: [
      ["Take part of the text — Start at", "1"],
      ["Take part of the text — Length", "3"],
    ],
    expect: '"part": "ell"',
  },
  {
    field: "swapped",
    path: "text",
    fn: "replace",
    args: [
      ["Replace text — Find", "l"],
      ["Replace text — Replace with", "L"],
    ],
    expect: '"swapped": "heLLo"',
  },
  {
    field: "joined",
    path: "text",
    fn: "concat",
    args: [["Append text — Append", "!"]],
    expect: '"joined": "hello!"',
  },
  {
    field: "rounded",
    path: "n",
    fn: "round",
    args: [["Round — Decimals", "2"]],
    expect: '"rounded": 10.57',
  },
  {
    field: "times",
    path: "n",
    fn: "multiply",
    args: [["Multiply — By", "2"]],
    expect: '"times": 21.134',
  },
  {
    field: "plus",
    path: "n",
    fn: "add",
    args: [["Add — Amount", "1"]],
    expect: '"plus": 11.567',
  },
  {
    field: "when",
    path: "date",
    fn: "formatDate",
    args: [["Format a date — Format", "dd MMM yyyy"]],
    expect: '"when": "04 Mar 2026"',
  },
  {
    field: "filled",
    path: "blank",
    fn: "defaultIfEmpty",
    args: [["Use a default when empty — Default", "NONE"]],
    expect: '"filled": "NONE"',
  },
];

test("every transform has the argument boxes it needs, and produces its value", async ({
  page,
}) => {
  test.slow();

  await openWithSample(page, {
    text: "hello",
    shout: "LOUD",
    padded: "  pad  ",
    n: 10.567,
    date: "2026-03-04",
    blank: "",
  });

  for (const testCase of TRANSFORM_CASES) {
    await addPathRule(page, testCase.field, testCase.path);
    await openDetail(page, testCase.field);
    await page.getByRole("combobox", { name: "Transform" }).selectOption(testCase.fn);

    // The boxes are named from the function, so a function whose arguments the
    // editor spells differently to the server would show up right here.
    //
    // By label rather than by role: an argument box may be a plain input, or one
    // carrying a suggestion list — and an `<input list=…>` reports as a combobox, not
    // a textbox. The name is what identifies it either way.
    for (const [label, value] of testCase.args ?? []) {
      const box = page.getByLabel(label, { exact: true });
      // Some arguments are a closed list now, so the gesture depends on the control.
      if ((await box.evaluate((el) => el.tagName)) === "SELECT") await box.selectOption(value);
      else await box.fill(value);
    }

    await openDetail(page, testCase.field);
  }

  for (const testCase of TRANSFORM_CASES)
    await expect(preview(page)).toContainText(testCase.expect, { timeout: 20000 });
});

// ─── Lookup tables ────────────────────────────────────────────────────────────

test("a lookup runs after the transform, not before it", async ({ page }) => {
  await openWithSample(page, { country: "jo" });

  await addPathRule(page, "countryName", "country");
  await openDetail(page, "countryName");

  await page.getByRole("combobox", { name: "Transform" }).selectOption("upper");
  await page.getByRole("checkbox", { name: "Substitute values from a table" }).check();
  await page.getByRole("button", { name: "Add incoming value" }).click();
  // Keyed on the *transformed* value: "jo" uppercased is "JO", and it is "JO" the
  // table is asked about. A table keyed on "jo" would miss.
  await page.getByRole("textbox", { name: "Incoming value 1" }).fill("JO");
  await page.getByRole("textbox", { name: "Becomes 1" }).fill("Jordan");

  await expectPreview(page, '"countryName": "Jordan"');
});

// ─── Lists ────────────────────────────────────────────────────────────────────

test("an entry written into a list may read the document, the partner, and hold a list", async ({
  page,
}) => {
  await openWithSample(page, {
    order: { ref: "ORD-9", line: [{ sku: "A1", tag: [{ code: "cold" }] }] },
  });
  await previewAsTestPartner(page);

  await addList(page, "lines", "order.line");
  const lines = page.getByRole("group", { name: "Rules for the list lines" });
  await addListField(lines, "lines", "sku", "sku");

  // A header entry the partner expects. It is built from ordinary rules, so unlike
  // the old mapper's literal JSON it can read anything a walked entry can.
  await lines.getByRole("button", { name: "Add an entry to lines" }).click();
  const entry = page.getByRole("group", { name: "Rules for entry 1" });

  await entry.getByRole("button", { name: "Add a field to entry 1" }).click();
  await entry.getByRole("textbox", { name: "Output field name" }).last().fill("sku");
  await entry.getByRole("radio", { name: "Fixed" }).last().click();
  await entry.getByRole("textbox", { name: "Fixed value" }).last().fill("HEADER");

  await entry.getByRole("button", { name: "Add a field to entry 1" }).click();
  await entry.getByRole("textbox", { name: "Output field name" }).last().fill("ref");
  await setSourcePath(entry, "order.ref");

  await entry.getByRole("button", { name: "Add a field to entry 1" }).click();
  await entry.getByRole("textbox", { name: "Output field name" }).last().fill("warehouse");
  await entry.getByRole("radio", { name: "Partner" }).last().click();
  await entry.getByRole("combobox", { name: "Partner property key" }).last().fill("WarehouseCode");

  await expectPreview(page, '"sku": "HEADER"');
  await expect(preview(page)).toContainText('"ref": "ORD-9"');
  await expect(preview(page)).toContainText('"warehouse": "WH-7"');
  // Written first, then one per walked entry.
  await expect(preview(page)).toHaveText(/HEADER[\s\S]*"sku": "A1"/);

  // A written entry reads from where the list sits, not from an entry that was
  // never walked — so `order.ref` resolves rather than being relative to a line.
  await saveAndReload(page);
  await expect(page.getByRole("group", { name: "Rules for entry 1" })).toBeVisible();
});

// ─── The editor itself ────────────────────────────────────────────────────────

test("removing a field, a list, and a written entry", async ({ page }) => {
  await openWithSample(page);

  await addPathRule(page, "customer", "order.customer");
  await addList(page, "lines", "order.line");
  const lines = page.getByRole("group", { name: "Rules for the list lines" });
  await addListField(lines, "lines", "code", "sku");
  await lines.getByRole("button", { name: "Add an entry to lines" }).click();

  await expectPreview(page, '"code": "A1"');

  await page.getByRole("button", { name: "Remove entry 1" }).click();
  await expect(page.getByRole("group", { name: "Entry 1" })).toHaveCount(0);

  await page.getByRole("button", { name: "Remove the rule for code" }).click();
  await page.getByRole("button", { name: "Remove the list lines" }).click();
  await page.getByRole("button", { name: "Remove the rule for customer" }).click();

  await expect(page.getByText("No rules yet.")).toBeVisible();
  // Removing every rule is a mapping that produces an empty document, not a failure.
  await expectPreview(page, "{}");
});

test("undo puts back a rule that was removed", async ({ page }) => {
  await openWithSample(page);

  await addPathRule(page, "customer", "order.customer");
  await expectPreview(page, '"customer": "Ali"');

  await page.getByRole("button", { name: "Remove the rule for customer" }).click();
  await expect(page.getByRole("textbox", { name: "Output field name" })).toHaveCount(0);

  await page.getByRole("button", { name: "Undo" }).click();
  await expect(page.getByRole("textbox", { name: "Output field name" })).toHaveValue("customer");

  await page.getByRole("button", { name: "Redo" }).click();
  await expect(page.getByRole("textbox", { name: "Output field name" })).toHaveCount(0);
});

test("searching the output keeps the branches above a match", async ({ page }) => {
  await openWithSample(page, { c: "Amman", k: "JO", n: "Ali" });

  await addPathRule(page, "billing.city", "c");
  await addPathRule(page, "billing.country", "k");
  await addPathRule(page, "name", "n");

  await page.getByRole("textbox", { name: "Search output fields" }).fill("city");

  // The match is reachable, which means the object above it survives too.
  await expect(page.getByRole("group", { name: "Fields inside billing" })).toBeVisible();
  const names = page.getByRole("textbox", { name: "Output field name" });
  await expect(names).toHaveCount(1);
  await expect(names).toHaveValue("city");

  // Searching does not change the mapping — only what is shown of it.
  await expect(preview(page)).toContainText('"name": "Ali"');

  await page.getByRole("textbox", { name: "Search output fields" }).fill("");
  await expect(page.getByRole("textbox", { name: "Output field name" })).toHaveCount(3);
});

test("a list folds away without losing what is inside it", async ({ page }) => {
  await openWithSample(page);

  await addList(page, "lines", "order.line");
  const lines = page.getByRole("group", { name: "Rules for the list lines" });
  await addListField(lines, "lines", "code", "sku");
  await expectPreview(page, '"code": "A1"');

  await page.getByRole("button", { name: "Collapse the list lines" }).click();
  await expect(page.getByRole("group", { name: "Rules for the list lines" })).toHaveCount(0);
  // Folded, not removed: the mapping still produces the same document.
  await expect(preview(page)).toContainText('"code": "A1"');

  await page.getByRole("button", { name: "Expand the list lines" }).click();
  await expect(
    page.getByRole("group", { name: "Rules for the list lines" }).getByRole("textbox", {
      name: "Output field name",
    }),
  ).toHaveValue("code");
});

// ─── Stored rules the editor must not open ────────────────────────────────────

const REFUSED = [
  {
    what: "are not readable at all",
    rules: "{ this is not json",
    says: /could not be read/,
  },
  {
    what: "come from a newer version of Bitween",
    rules: JSON.stringify({ version: 99, fields: [], lists: [] }),
    says: /version 99/,
  },
  {
    what: "were saved before lists were renamed",
    rules: JSON.stringify({ version: 1, fields: [], loops: [{ over: "x", target: ["y"] }] }),
    says: /before lists were renamed/,
  },
];

test("rules the editor cannot read refuse to open rather than starting blank", async ({ page }) => {
  const subscriptionId = await createSubscription(page);

  for (const { what, rules, says } of REFUSED) {
    await writeMapperProperties(subscriptionId, "NativeMapper", { MappingRules: rules });

    await page.goto(`subscriptions/${subscriptionId}/mapper`);

    // Opening blank and letting someone press Save would replace a working mapping
    // with nothing, which is worse than refusing to open.
    await expect(page.getByText(says), what).toBeVisible({ timeout: 15000 });
    await expect(page.getByRole("button", { name: "Save" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Back to the subscription" })).toBeVisible();
  }
});

test("a stored date format the dropdown never offered still shows what is saved", async ({
  page,
}) => {
  const subscriptionId = await createSubscription(page);

  // The engine formats with any .NET pattern, so a saved mapping can hold one this
  // closed list does not offer — set through the API, or offered here under a label
  // that has since changed. A select with no matching option shows nothing selected,
  // which reads as "no format chosen".
  await writeMapperProperties(subscriptionId, "NativeMapper", {
    MappingRules: JSON.stringify({
      version: 1,
      sourceFormat: "json",
      targetFormat: "json",
      fields: [
        {
          target: ["shipped"],
          from: { kind: "path", path: "order.date" },
          transform: { fn: "formatDate", format: "d MMMM" },
        },
      ],
      lists: [],
    }),
  });

  await page.goto(`subscriptions/${subscriptionId}/mapper`);
  await expect(page.getByRole("button", { name: "Save" })).toBeVisible({ timeout: 15000 });
  await openDetail(page, "shipped");

  await expect(page.getByLabel("Format a date — Format")).toHaveValue("d MMMM");

  // And saving the mapping for some unrelated reason must not quietly replace it.
  await addFixedRule(page, "channel", "web");
  await saveAndReload(page);
  await openDetail(page, "shipped");
  await expect(page.getByLabel("Format a date — Format")).toHaveValue("d MMMM");
});

// ─── The toolbar, and reading a big mapping ───────────────────────────────────

test("clicking a row shows what is behind its chevron", async ({ page }) => {
  await openWithSample(page);

  await addPathRule(page, "total", "order.net");

  // The transform and the type live behind the chevron, and finding the chevron was
  // the whole complaint: the row itself is the obvious thing to click.
  await expect(page.getByRole("combobox", { name: "Transform" })).toHaveCount(0);

  await page.getByRole("textbox", { name: "Output field name" }).click();
  await expect(page.getByRole("combobox", { name: "Transform" })).toHaveCount(0);

  // Clicking the row's own space, rather than a control in it.
  await page.getByText("←", { exact: true }).first().click();
  await expect(page.getByRole("combobox", { name: "Transform" })).toBeVisible();

  await page.getByText("←", { exact: true }).first().click();
  await expect(page.getByRole("combobox", { name: "Transform" })).toHaveCount(0);
});

test("hiding the preview gives the rules the whole width", async ({ page }) => {
  await openWithSample(page);
  await addPathRule(page, "customer", "order.customer");
  await expectPreview(page, '"customer": "Ali"');

  await page.getByRole("button", { name: "Hide the preview" }).click();
  await expect(page.getByText("— what a partner would receive")).toHaveCount(0);

  // Hidden, not switched off: the rules are untouched and it comes back as it was.
  await page.getByRole("button", { name: "Show the preview" }).click();
  await expectPreview(page, '"customer": "Ali"');
});

test("the partner key box offers the keys the previewed partner actually has", async ({ page }) => {
  await openWithSample(page, { order: { customer: "Ali" } });

  await addNamedRule(page, "warehouse");
  await page.getByRole("radio", { name: "Partner" }).last().click();

  const key = page.getByRole("combobox", { name: "Partner property key" });

  // With no partner chosen there is nothing to suggest, and the box is still a box:
  // the mapping runs against whichever partner the exchange belongs to, not this one.
  expect(await suggestionsFor(page, key)).toEqual([]);

  await previewAsTestPartner(page);
  // Fetched for the chosen partner, so the box fills in a moment rather than at once.
  await expect
    .poll(() => suggestionsFor(page, key))
    .toEqual(expect.arrayContaining(["WarehouseCode", "SenderId"]));

  await key.fill("WarehouseCode");
  await expectPreview(page, '"warehouse": "WH-7"');
  await expect(page.getByText("⚠")).toHaveCount(0);

  // A key that partner does not have is flagged rather than refused.
  await key.fill("NotAProperty");
  await expect(page.getByText("⚠")).toBeVisible();
});

// ─── The source side of a big document ────────────────────────────────────────

test("the source tree shows the fields inside a list, named as a rule names them", async ({
  page,
}) => {
  await openWithSample(page);

  // `sku` sits inside `order.line`, and a rule in a list over that list reads it as
  // `sku`. Hiding these meant the only way to see what was in a list was to read the
  // sample somewhere else.
  await expect(page.getByRole("button", { name: "sku", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "qty", exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Collapse order.line" }).click();
  await expect(page.getByRole("button", { name: "sku", exact: true })).toHaveCount(0);

  await page.getByRole("button", { name: "Expand order.line" }).click();
  await expect(page.getByRole("button", { name: "sku", exact: true })).toBeVisible();
});

test("undo, redo and save from the keyboard", async ({ page }) => {
  await openWithSample(page);

  await addPathRule(page, "customer", "order.customer");
  const source = page.getByRole("combobox", { name: "Source field" });
  await expect(source).toHaveValue("order.customer");

  // Focus is in a box after typing, and Ctrl+Z there belongs to the box. Clicking
  // the panel's own space takes it back.
  await page.getByText("Output", { exact: true }).click();

  // One step is one change, so this undoes pointing the rule somewhere — not the
  // whole rule, which was three changes ago.
  await page.keyboard.press("ControlOrMeta+z");
  await expect(source).toHaveValue("");

  await page.keyboard.press("ControlOrMeta+y");
  await expect(source).toHaveValue("order.customer");

  await page.keyboard.press("ControlOrMeta+z");
  await page.keyboard.press("ControlOrMeta+Shift+z");
  await expect(source).toHaveValue("order.customer");

  await page.keyboard.press("ControlOrMeta+s");
  await expect(page.getByText("Saved")).toBeVisible({ timeout: 15000 });
});

test("Ctrl+Z inside a box undoes the typing, not the mapping", async ({ page }) => {
  await openWithSample(page);
  await addPathRule(page, "customer", "order.customer");

  const name = page.getByRole("textbox", { name: "Output field name" });
  await name.click();
  await name.press("ControlOrMeta+z");

  // The rule is still there. The old editor took this key in both cases, so fixing a
  // mistyped name meant undoing a change somewhere else entirely.
  await expect(name).toHaveCount(1);
});

test("a checkbox in a settings panel can be ticked by its text", async ({ page }) => {
  await openWithSample(page);

  const lines = await addList(page, "lines", "order.line");
  await addListField(lines, "lines", "qty", "qty");
  await page.getByRole("button", { name: "Settings for the list lines" }).click();

  // Clicking the words, not the box — which is what anyone does, and what a test
  // using .check() never exercises, because that clicks the input directly. The row
  // click that opens these settings used to swallow it: the box ticked and the panel
  // folded away in the same tick, so it looked like the click did nothing.
  await page.getByText("Only some entries").click();

  await expect(page.getByRole("textbox", { name: "Filter field" })).toBeVisible();
  await page.getByRole("textbox", { name: "Filter field" }).fill("qty");
  await page.getByRole("combobox", { name: "Filter comparison" }).selectOption("greaterThan");
  await page.getByRole("textbox", { name: "Filter value" }).fill("0");

  // The entry with qty 0 is gone, which is the whole point of the checkbox.
  await expectPreview(page, '"qty": 2');
  await expect(preview(page)).not.toContainText('"qty": 0');
});

test("a checkbox in a rule's detail can be ticked by its text", async ({ page }) => {
  await openWithSample(page, { country: "JO" });

  await addPathRule(page, "countryName", "country");
  await openDetail(page, "countryName");

  await page.getByText("Substitute values from a table").click();
  await expect(page.getByRole("button", { name: "Add incoming value" })).toBeVisible();

  await page.getByRole("button", { name: "Add incoming value" }).click();
  await page.getByRole("textbox", { name: "Incoming value 1" }).fill("JO");
  await page.getByRole("textbox", { name: "Becomes 1" }).fill("Jordan");

  await expectPreview(page, '"countryName": "Jordan"');
});

test("a date that could be read two ways has to say which", async ({ page }) => {
  // A real CargoNet shipping date: the 4th of September, French style.
  await openWithSample(page, { order: { shippingdate: "04.09.2026" } });

  await addPathRule(page, "shipDate", "order.shippingdate");
  await openDetail(page, "shipDate");
  await page.getByRole("combobox", { name: "Transform" }).selectOption("formatDate");

  // One control on the row, and it is a closed list: what the date should look like on
  // the way out, shown as the date itself rather than as yyyy-MM-dd letters.
  const format = page.getByRole("combobox", { name: "Format a date — Format" });
  await expect(format.locator("option")).toContainText(["Format…", "2026-09-04", "04/09/2026"]);
  await format.selectOption("yyyy-MM-dd");

  // Refused rather than guessed. The invariant parser reads this as the 9th of April
  // perfectly happily, which would date a shipment five months out with nothing said.
  await expect(page.getByText(/could not read '04\.09\.2026'/).first()).toBeVisible({
    timeout: 15000,
  });

  // Answered once for the document, under the sample it describes — a partner writes
  // dates one way throughout, so this is not a per-rule question.
  const dates = page.getByRole("combobox", { name: "Dates in the incoming document" });
  await expect(dates.locator("option")).toHaveText([
    "Year first — 2026-09-04",
    "Day first — 04.09.2026",
    "Month first — 09.04.2026",
  ]);

  await dates.selectOption("dayFirst");
  await expectPreview(page, '"shipDate": "2026-09-04"');

  // And the other way round, from the same characters.
  await dates.selectOption("monthFirst");
  await expectPreview(page, '"shipDate": "2026-04-09"');

  // It is part of the mapping, so it comes back with it.
  await saveAndReload(page);
  await expect(page.getByRole("combobox", { name: "Dates in the incoming document" })).toHaveValue(
    "monthFirst",
  );
});
