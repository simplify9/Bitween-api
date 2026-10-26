import { describe, expect, it } from "vitest";
import { changedRows, differencesByRow, rowFor, ruleCount } from "../aiTally";
import { fromWire } from "../serialize";
import type { MappingRules } from "../types";

/**
 * The server says where a document differs - `shipments[0].items[1].sku` - and the editor
 * marks rows by the name the mapper gives a rule's errors - `shipments[].items[].sku`.
 */

const rules = fromWire({
  version: 1,
  sourceFormat: "json",
  targetFormat: "json",
  fields: [{ target: ["header", "batch"], from: { kind: "path", path: "b" } }],
  lists: [
    {
      over: "orders",
      target: ["shipments"],
      fields: [{ target: ["customer", "surname"], from: { kind: "path", path: "last" } }],
      lists: [
        {
          over: "lines",
          target: ["items"],
          fields: [{ target: ["sku"], from: { kind: "path", path: "sku" } }],
          lists: [
            { over: "tags", target: ["labels"], item: { target: [], from: { kind: "path", path: "name" } }, fields: [], lists: [] },
          ],
        },
      ],
    },
  ],
} as MappingRules);

describe("finding a difference's row", () => {
  it("drops the entry numbers to name the rule that writes there", () => {
    expect(rowFor(rules, "shipments[0].items[1].sku")?.key).toBe("shipments[].items[].sku");
    expect(rowFor(rules, "shipments[1].customer.surname")?.key).toBe("shipments[].customer.surname");
    expect(rowFor(rules, "header.batch")).toMatchObject({ key: "header.batch", isField: true });
  });

  it("gives a value in a list of plain values to that list", () => {
    expect(rowFor(rules, "shipments[0].items[0].labels[1]")?.key).toBe("shipments[].items[].labels");
  });

  it("falls back to the nearest rule above, and to nothing for the whole document", () => {
    // Nothing writes customer.first; the list it would sit in does.
    expect(rowFor(rules, "shipments[0].customer.first")).toMatchObject({ key: "shipments", isField: false });
    expect(rowFor(rules, "footer.total")).toBeNull();
    expect(rowFor(rules, "(root)")).toBeNull();
  });

  it("names a root list's rules the way the mapper does", () => {
    const root = fromWire({
      version: 1, sourceFormat: "json", targetFormat: "json", fields: [], lists: [],
      root: { over: "", target: [], fields: [{ target: ["code"], from: { kind: "path", path: "c" } }], lists: [] },
    } as MappingRules);
    expect(rowFor(root, "[2].code")?.key).toBe("(no target)[].code");
  });

  it("collects each row's differences", () => {
    expect(
      differencesByRow(rules, [
        { target: "shipments[0].items[0].sku", reason: "a" },
        { target: "shipments[1].items[0].sku", reason: "b" },
      ]),
    ).toEqual({ "shipments[].items[].sku": ["shipments[0].items[0].sku: a", "shipments[1].items[0].sku: b"] });
  });
});

describe("what changed", () => {
  it("lists only the rows written differently, whatever their ids", () => {
    const reloaded = fromWire(JSON.parse(JSON.stringify({
      version: 1, sourceFormat: "json", targetFormat: "json",
      fields: [{ target: ["header", "batch"], from: { kind: "path", path: "b" } }],
      lists: [{
        over: "orders", target: ["shipments"],
        fields: [{ target: ["customer", "surname"], from: { kind: "path", path: "last" }, transform: { fn: "upper" } }],
        lists: [],
      }],
    })) as MappingRules);

    expect(changedRows(rules, reloaded)).toEqual(["shipments[].customer.surname"]);
    expect(ruleCount(rules)).toBe(4);
    expect(ruleCount(reloaded)).toBe(2);
  });
});
