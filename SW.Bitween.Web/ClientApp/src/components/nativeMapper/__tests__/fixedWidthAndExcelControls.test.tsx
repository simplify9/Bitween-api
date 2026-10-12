import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { fill, mapperBackend, openEditor, withFormats } from "./editorHarness";

/**
 * Fixed-width and Excel in the editor's format settings: what is set there reaches the server
 * with the rules, and the sample shows the fields the layout names. How the server reads either
 * is C#'s business (FixedWidthFormatTests, ExcelFormatTests).
 */
describe("fixed-width settings", () => {
  it("are a table of columns that goes to the server with the rules, and cuts the sample", async () => {
    const backend = mapperBackend();
    const { user } = await openEditor(backend);
    await withFormats(user, async () => {
      await user.selectOptions(screen.getByLabelText("From format"), "fixedwidth");
      await user.click(screen.getByRole("button", { name: "+ Column" }));
      await fill(user, screen.getByLabelText("source column 1 name"), "type");
      await fill(user, screen.getByLabelText("source column 1 length"), "1");
      await user.click(screen.getByRole("button", { name: "+ Column" }));
      await fill(user, screen.getByLabelText("source column 2 name"), "reference");
      await fill(user, screen.getByLabelText("source column 2 length"), "8");
    });
    await fill(user, screen.getByRole("textbox", { name: "Sample source document" }), "DSO-1    \n");

    expect(await screen.findByText("reference")).toBeVisible();
    await waitFor(() =>
      expect(backend.previews.at(-1)?.rules.sourceFixedWidth?.columns).toMatchObject([
        { name: "type", start: 1, length: 1 },
        { name: "reference", start: 2, length: 8 },
      ]),
    );
  });
});

describe("Excel settings", () => {
  it("send the sheet and say the workbook travels as base64", async () => {
    const backend = mapperBackend();
    const { user } = await openEditor(backend);
    await withFormats(user, async () => {
      await user.selectOptions(screen.getByLabelText("From format"), "excel");
      await fill(user, screen.getByLabelText("source sheet"), "Orders");
      expect(screen.getByText(/ResponseEncoding to base64/)).toBeVisible();
    });
    await fill(user, screen.getByRole("textbox", { name: "Sample source document" }), "Reference\tAmount\n041800\t12.5\n");

    expect(await screen.findByText("Reference")).toBeVisible();
    await waitFor(() => expect(backend.previews.at(-1)?.rules.sourceExcel).toEqual({ sheet: "Orders", hasHeader: true }));
  });
});
