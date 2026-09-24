import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";
import {
  createSubscription,
  openMapper,
  withFormats,
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

  await withFormats(page, async () => {
    await page.getByLabel("From format").selectOption("csv");
    await page.getByLabel("source delimiter").selectOption(delimiter);
    const box = page.getByRole("checkbox", { name: "source header row" });
    if (header) await box.check();
    else await box.uncheck();
  });

  await page.getByRole("textbox", { name: "Sample source document" }).fill(sample);
  return subscriptionId;
}

/** Makes the whole output a list walking the document, which is what every row-per-row mapping is. */
async function rootListOverTheDocument(page: import("@playwright/test").Page) {
  await page.getByRole("checkbox", { name: /The whole output is a list/ }).check();
  await page.getByRole("combobox", { name: "Source list" }).selectOption("p:");
  return page.getByRole("group", { name: "Rules for the list at the root" });
}

test("counting is offered inside a list and nowhere else", async ({ page }) => {
  // Outside a list there is nothing to count, so the segment is not there to be chosen.
  await openWithCsv(page, MOVEMENTS, ",", true);

  await page.getByRole("button", { name: "Add a field", exact: true }).click();
  await expect(page.getByRole("radio", { name: "Count" })).toHaveCount(0);

  const root = await rootListOverTheDocument(page);
  await root.getByRole("button", { name: "Add a field to the root list" }).click();
  await expect(root.getByRole("radio", { name: "Count" })).toHaveCount(1);
});
