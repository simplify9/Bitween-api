import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";
import {
  addListField,
  createSubscription,
  expectPreview,
  openMapper,
  preview,
} from "./mapperHelpers";

/**
 * Delimited text, through the real editor against the real backend.
 *
 * The point of running these end to end rather than in a unit test: the tree the editor
 * draws comes from d3-dsv in the browser, and the document the mapping actually reads
 * comes from CsvHelper on the server. Two parsers, and nothing but a test like this
 * notices when they stop agreeing — a column offered here that resolves to nothing there
 * is a mapping that looks complete and quietly writes an empty field.
 *
 * The files are the three a single client really sends, unaltered.
 */

/** Pipe, no header, three record types told apart by the first field. */
const TRACKING =
  "H|FFSTAT|1|0||||||||||202609141313|1309981|N\n" +
  "D|1309981172|OK|DELIVERY|0.100|KGM|1|||20260908FRACPKT03831|3800351262|202609141307|20260911|NTE|CDG|NTE||BRIAN MATIAS CASTRO PENA|GLOBAL LOGTICS NETWORK|||||||222998693|Clementine Sandri|\n" +
  "D|1309981174|CC|AWAITING CONSIGNEE COLLECTION|0.100|KGM|1|||E824836443|4472825486|202609141305|20260911|MRS|CDG|MRS||CHRISTOPHER GERGES|GLOBAL LOGISTIC NETWORK|||||||222998693||\n" +
  "T|9|1309981|\n";

/** Comma, with a header naming its columns. */
const MOVEMENTS =
  "ShipmentNumber,Reference,TrackingCode,Date,Time,Comment1,Comment2\n" +
  "6G61965126082,202493482,SHOR020,2026-09-14,08:29:49,,\n" +
  "8G49824171336,202340914,SHOR020,2026-09-14,08:34:34,,\n";

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

/** Opens the editor with the source side reading delimited text. */
async function openWithCsv(page: import("@playwright/test").Page, sample: string, delimiter: string, header: boolean) {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);

  await page.getByLabel("From format").selectOption("csv");
  await page.getByLabel("source delimiter").selectOption(delimiter);
  const box = page.getByRole("checkbox", { name: "source header row" });
  if (header) await box.check();
  else await box.uncheck();

  await page.getByRole("textbox", { name: "Sample source document" }).fill(sample);
  return subscriptionId;
}

/** Makes the whole output a list walking the document, which is what every row-per-row mapping is. */
async function rootListOverTheDocument(page: import("@playwright/test").Page) {
  await page.getByRole("checkbox", { name: /The whole output is a list/ }).check();
  await page.getByRole("combobox", { name: "Source list" }).selectOption("p:");
  return page.getByRole("group", { name: "Rules for the list at the root" });
}

test("a header names the columns, and the server reads the same names", async ({ page }) => {
  await openWithCsv(page, MOVEMENTS, ",", true);

  const root = await rootListOverTheDocument(page);
  await addListField(root, "the root list", "shipment", "ShipmentNumber");
  await addListField(root, "the root list", "at", "Time");

  // Produced by the server from its own reading of the file. If the two parsers disagreed
  // about where the columns are, this is where it would show.
  await expectPreview(page, '"shipment": "6G61965126082"');
  await expect(preview(page)).toContainText('"at": "08:34:34"');
});

test("the columns the editor offers are the ones the server can read", async ({ page }) => {
  await openWithCsv(page, MOVEMENTS, ",", true);

  const root = await rootListOverTheDocument(page);
  await root.getByRole("button", { name: "Add a field to the root list" }).click();
  await root.getByRole("textbox", { name: "Output field name" }).last().fill("checked");

  // Every column the source panel offers, tried against the server one at a time.
  const field = root.getByRole("combobox", { name: "Source field" }).last();
  for (const column of ["ShipmentNumber", "Reference", "TrackingCode", "Date", "Time"]) {
    await field.fill(column);
    await expect(preview(page)).not.toContainText('"checked": null', { timeout: 15000 });
  }
});

test("three record types in one file are separated by the list's own filter", async ({ page }) => {
  await openWithCsv(page, TRACKING, "|", false);

  const root = await rootListOverTheDocument(page);

  // The claim the whole plan rests on: a file holding an H record, D records and a T record
  // needs no feature of its own. Field 1 says which kind of line this is.
  await page.getByRole("button", { name: "Settings for the list at the root" }).click();
  await page.getByRole("checkbox", { name: "Only some entries" }).check();
  await page.getByRole("textbox", { name: "Filter field" }).fill("1");
  await page.getByRole("combobox", { name: "Filter comparison" }).selectOption("equal");
  await page.getByRole("textbox", { name: "Filter value" }).fill("D");
  await page.getByRole("button", { name: "Settings for the list at the root" }).click();

  await addListField(root, "the root list", "tracking", "2");
  await addListField(root, "the root list", "status", "3");

  await expectPreview(page, '"tracking": "1309981172"');
  await expect(preview(page)).toContainText('"status": "CC"');

  // The header and the trailer are gone, and nothing had to be told they existed.
  await expect(preview(page)).not.toContainText("FFSTAT");
  await expect(preview(page)).not.toContainText('"tracking": "9"');
});

test("a leading zero and a trailing scale survive the round trip to the server", async ({
  page,
}) => {
  await openWithCsv(page, TRACKING, "|", false);

  const root = await rootListOverTheDocument(page);
  await addListField(root, "the root list", "weight", "5");
  await addListField(root, "the root list", "account", "26");

  // 0.100 as 0.1, or an account reference losing a character, is a file the partner
  // rejects with nothing anywhere to say why.
  await expectPreview(page, '"weight": "0.100"');
  await expect(preview(page)).toContainText('"account": "222998693"');
});

test("writing a delimited file takes its delimiter and header from the target side", async ({
  page,
}) => {
  await openWithCsv(page, TRACKING, "|", false);

  await page.getByLabel("To format").selectOption("csv");
  await page.getByLabel("target delimiter").selectOption(";");
  await page.getByRole("checkbox", { name: "target header row" }).check();

  const root = await rootListOverTheDocument(page);
  await page.getByRole("button", { name: "Settings for the list at the root" }).click();
  await page.getByRole("checkbox", { name: "Only some entries" }).check();
  await page.getByRole("textbox", { name: "Filter field" }).fill("1");
  await page.getByRole("textbox", { name: "Filter value" }).fill("D");
  await page.getByRole("button", { name: "Settings for the list at the root" }).click();

  await addListField(root, "the root list", "Tracking", "2");
  await addListField(root, "the root list", "Status", "3");

  await expectPreview(page, "Tracking;Status");
  await expect(preview(page)).toContainText("1309981172;OK");
  await expect(preview(page)).toContainText("1309981174;CC");
});

test("a nested rule becomes a dotted column", async ({ page }) => {
  await openWithCsv(page, MOVEMENTS, ",", true);
  await page.getByLabel("To format").selectOption("csv");

  const root = await rootListOverTheDocument(page);
  // A row is flat, so the nesting has to land somewhere. The name box splits on dots, so
  // this is the only way a column of this name can exist at all.
  await addListField(root, "the root list", "shipment.number", "ShipmentNumber");

  await expectPreview(page, "shipment.number");
  await expect(preview(page)).toContainText("6G61965126082");
});

test("a shape a row cannot hold is refused with a reason", async ({ page }) => {
  await openWithCsv(page, MOVEMENTS, ",", true);
  await page.getByLabel("To format").selectOption("csv");

  // A list inside a row. There is no cell that holds one, and inventing a way to fit it —
  // joining the entries, taking the first — would lose data without a word.
  const root = await rootListOverTheDocument(page);
  await addListField(root, "the root list", "shipment", "ShipmentNumber");
  await root.getByRole("button", { name: "Add a list to the root list" }).click();
  await root.getByRole("textbox", { name: "Output list name" }).last().fill("tags");

  await expect(page.getByText(/cannot hold one|cannot itself be a list/)).toBeVisible({
    timeout: 15000,
  });
});

/** Adds a field inside one of a list's written entries, pointed at a fixed value. */
async function addEntryField(
  scope: import("@playwright/test").Locator,
  entry: string,
  name: string,
  value: string,
) {
  await scope.getByRole("button", { name: `Add a field to ${entry}` }).click();
  await scope.getByRole("textbox", { name: "Output field name" }).last().fill(name);
  await scope.getByRole("radio", { name: "Fixed" }).last().click();
  await scope.getByRole("textbox", { name: "Fixed value" }).last().fill(value);
}

test("the carrier's whole file can be produced, trailer count and all", async ({ page }) => {
  // The other direction, and the one that needed two features of its own: a file like the
  // client's ends with a record carrying how many records came before it.
  await openWithCsv(page, TRACKING, "|", false);

  await page.getByLabel("To format").selectOption("csv");
  await page.getByLabel("target delimiter").selectOption("|");
  await page.getByRole("checkbox", { name: "target header row" }).uncheck();

  const root = await rootListOverTheDocument(page);
  await page.getByRole("button", { name: "Settings for the list at the root" }).click();
  await page.getByRole("checkbox", { name: "Only some entries" }).check();
  await page.getByRole("textbox", { name: "Filter field" }).fill("1");
  await page.getByRole("textbox", { name: "Filter value" }).fill("D");
  await page.getByRole("button", { name: "Settings for the list at the root" }).click();

  // The header line, written before anything is walked.
  await root.getByRole("button", { name: "Add an entry to the root list" }).click();
  const first = page.getByRole("group", { name: "Rules for entry 1" });
  await addEntryField(first, "entry 1", "1", "H");
  await addEntryField(first, "entry 1", "2", "FFSTAT");

  // One line per shipment.
  await addListField(root, "the root list", "1", "1");
  await addListField(root, "the root list", "2", "2");
  await addListField(root, "the root list", "3", "3");

  // And the trailer, which is the only place a rule can see what the list ended up holding.
  await root.getByRole("button", { name: "Add a closing entry to the root list" }).click();
  const closing = page.getByRole("group", { name: "Rules for closing entry 1" });
  await addEntryField(closing, "closing entry 1", "1", "T");
  await closing.getByRole("button", { name: "Add a field to closing entry 1" }).click();
  await closing.getByRole("textbox", { name: "Output field name" }).last().fill("2");
  await closing.getByRole("radio", { name: "Count" }).last().click();

  await expectPreview(page, "H|FFSTAT");
  await expect(preview(page)).toContainText("D|1309981172|OK");
  await expect(preview(page)).toContainText("D|1309981174|CC");

  // Two shipments, so the trailer says 2 — the header line above it is not a record, and
  // adding one later cannot move the number.
  await expect(preview(page)).toContainText("T|2");
});

test("counting is offered inside a list and nowhere else", async ({ page }) => {
  // Outside a list there is nothing to count, so the segment is not there to be chosen.
  await openWithCsv(page, MOVEMENTS, ",", true);

  await page.getByRole("button", { name: "Add a field", exact: true }).click();
  await expect(page.getByRole("radio", { name: "Count" })).toHaveCount(0);

  const root = await rootListOverTheDocument(page);
  await root.getByRole("button", { name: "Add a field to the root list" }).click();
  await expect(root.getByRole("radio", { name: "Count" })).toHaveCount(1);
});

test("a file can be marked so Excel opens accented names correctly", async ({ page }) => {
  await openWithCsv(page, MOVEMENTS, ",", true);
  await page.getByLabel("To format").selectOption("csv");

  const root = await rootListOverTheDocument(page);
  await addListField(root, "the root list", "shipment", "ShipmentNumber");
  await expectPreview(page, "shipment");

  // The mark itself is invisible, so what is checked is that asking for it changes the
  // document the server produced rather than that anything looks different.
  const before = await preview(page).textContent();
  await page.getByRole("checkbox", { name: "write a byte-order mark" }).check();
  await expect
    .poll(async () => (await preview(page).textContent())?.charCodeAt(0), { timeout: 15000 })
    .toBe(0xfeff);
  expect(before?.charCodeAt(0)).not.toBe(0xfeff);
});
