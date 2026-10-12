import { describe, expect, it } from "vitest";
import { parseSample } from "../documentTree";
import { fromWire, toWire } from "../serialize";
import { emptyRules, type FixedWidthOptions } from "../types";

/**
 * Fixed-width and Excel in the editor: the sample shows the same fields the server reads, and the
 * settings survive a save. How the server reads and writes them is the backend's business
 * (FixedWidthFormatTests, ExcelFormatTests).
 */
const layout: FixedWidthOptions = {
  columns: [
    { name: "type", start: 1, length: 1, align: "left", pad: " " },
    { name: "reference", start: 2, length: 8, align: "left", pad: " " },
    { name: "amount", start: 10, length: 7, align: "right", pad: "0" },
  ],
  skipLines: 1,
  trim: true,
};

describe("a fixed-width sample", () => {
  it("is cut at the layout's columns, after the lines it skips", () => {
    const { root, error } = parseSample("HEADER\nDSO-1    0001250\n", "fixedwidth", "source", undefined, { fixedWidth: layout });
    expect(error).toBeNull();
    expect(root?.kind).toBe("list");
    expect(root?.children.map((c) => [c.key, c.sample])).toEqual([
      ["type", "D"],
      ["reference", "SO-1"],
      ["amount", "0001250"],
    ]);
  });

  it("asks for the columns when there are none", () => {
    expect(parseSample("DSO-1", "fixedwidth", "source", undefined, { fixedWidth: { columns: [] } }).error).toMatch(/columns/);
  });
});

describe("an Excel sample", () => {
  it("is cells pasted from Excel, read as tab-separated text", () => {
    const { root } = parseSample("Reference\tAmount\n041800\t12.5\n", "excel", "source", undefined, { excel: { hasHeader: true } });
    expect(root?.children.map((c) => [c.key, c.sample])).toEqual([
      ["Reference", "041800"],
      ["Amount", "12.5"],
    ]);
  });
});

describe("format settings", () => {
  it("are written for the side that uses them and read back the same", () => {
    const rules = {
      ...emptyRules(),
      sourceFormat: "fixedwidth" as const,
      targetFormat: "excel" as const,
      sourceFixedWidth: layout,
      targetExcel: { sheet: "Orders", hasHeader: false },
    };
    const wire = toWire(rules);
    expect(wire.sourceFixedWidth).toEqual(layout);
    expect(wire.targetExcel).toEqual({ sheet: "Orders", hasHeader: false });
    expect(wire.sourceExcel).toBeUndefined();

    const back = fromWire(wire);
    expect(back.sourceFixedWidth).toEqual(layout);
    expect(back.targetExcel).toEqual({ sheet: "Orders", hasHeader: false });
  });
});
