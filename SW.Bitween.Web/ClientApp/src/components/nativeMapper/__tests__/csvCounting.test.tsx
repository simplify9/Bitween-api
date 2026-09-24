import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { fill, mapperBackend, openEditor, withFormats } from "./editorHarness";

/**
 * Delimited text in the editor.
 *
 * Whether the tree drawn here and the document the server reads agree is pinned on each side
 * rather than end to end: the browser's reading in src/lib/nativeMapper/__tests__/
 * csvSampleTree.test.ts, the server's in C# CsvFormatTests and CsvMappingTests.
 */

/** Comma, with a header naming its columns. From a real client, unaltered. */
const MOVEMENTS =
  "ShipmentNumber,Reference,TrackingCode,Date,Time,Comment1,Comment2\n" +
  "6G61965126082,202493482,SHOR020,2026-09-14,08:29:49,,\n" +
  "8G49824171336,202340914,SHOR020,2026-09-14,08:34:34,,\n";

describe("delimited text", () => {
  it("offers counting inside a list and nowhere else", async () => {
    const { user } = await openEditor(mapperBackend());
    await withFormats(user, async () => {
      await user.selectOptions(screen.getByLabelText("From format"), "csv");
      await user.selectOptions(screen.getByLabelText("source delimiter"), ",");
      const header = screen.getByRole("checkbox", { name: "source header row" });
      if (!(header as HTMLInputElement).checked) await user.click(header);
    });
    await fill(user, screen.getByRole("textbox", { name: "Sample source document" }), MOVEMENTS);

    // Outside a list there is nothing to count, so the segment is not there to be chosen.
    await user.click(screen.getByRole("button", { name: "Add a field" }));
    expect(screen.queryByRole("radio", { name: "Count" })).not.toBeInTheDocument();

    // Made a list walking the document, which is what every row-per-row mapping is.
    await user.click(screen.getByRole("checkbox", { name: /The whole output is a list/ }));
    await user.selectOptions(screen.getByRole("combobox", { name: "Source list" }), "p:");
    const root = screen.getByRole("group", { name: "Rules for the list at the root" });

    await user.click(within(root).getByRole("button", { name: "Add a field to the root list" }));
    expect(within(root).getAllByRole("radio", { name: "Count" })).toHaveLength(1);
  });
});
