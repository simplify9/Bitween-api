import { describe, expect, it } from "vitest";
import {
  coverageByPath,
  describeSample,
  itemScopeOf,
  itemShapeAt,
  listPaths,
  parseSample,
  readablePaths,
} from "../documentTree";

const ORDER = JSON.stringify({
  order: {
    customer: "Ali",
    net: 100,
    paid: true,
    note: null,
    address: { city: "Amman" },
    line: [
      { sku: "A1", qty: 2 },
      { sku: "B7", qty: 5 },
    ],
  },
});

const parse = (text: string) => parseSample(text, "json");

describe("reading a sample document", () => {
  it("reads nested objects", () => {
    const { root, error } = parse(ORDER);

    expect(error).toBeNull();
    expect(root?.kind).toBe("object");
    expect(readablePaths(root)).toContain("order.address.city");
  });

  it("reads every value type", () => {
    expect(readablePaths(parse(ORDER).root)).toEqual(
      expect.arrayContaining(["order.customer", "order.net", "order.paid", "order.note"]),
    );
  });

  /**
   * The difference from the old editor. It made a list behave as its own first
   * element, so `order.line.sku` appeared as something you could map — and it
   * silently meant the first line only.
   */
  it("does not offer paths inside a list as readable", () => {
    const paths = readablePaths(parse(ORDER).root);

    expect(paths).not.toContain("order.line.sku");
    expect(paths.some((p) => p.includes("line"))).toBe(false);
  });

  it("offers lists as list sources", () => {
    expect(listPaths(parse(ORDER).root)).toEqual(["order.line"]);
  });

  it("names an item's fields relative to the item", () => {
    // What a rule inside the list is written against: `sku`, not `order.line.sku`.
    expect(itemShapeAt(parse(ORDER).root, "order.line").map((n) => n.path)).toEqual(["sku", "qty"]);
  });

  it("counts the entries a list had in the sample", () => {
    const { root } = parse(ORDER);
    const line = root!.children[0].children.find((c) => c.key === "line");

    expect(line?.count).toBe(2);
  });

  /** A root array is a list source with an empty path, which is how a list says "the document". */
  it("treats a root array as a list source at the empty path", () => {
    const { root } = parse('[ { "id": 1 } ]');

    expect(root?.kind).toBe("list");
    expect(listPaths(root)).toEqual([""]);
    expect(itemShapeAt(root, "").map((n) => n.path)).toEqual(["id"]);
  });

  it("copes with an empty list", () => {
    const { root } = parse('{ "lines": [] }');

    expect(listPaths(root)).toEqual(["lines"]);
    expect(itemShapeAt(root, "lines")).toEqual([]);
  });

  it("copes with a list of plain values", () => {
    const { root } = parse('{ "tags": ["a","b"] }');

    expect(listPaths(root)).toEqual(["tags"]);
    expect(itemShapeAt(root, "tags")).toEqual([]);
  });

  it("says nothing for an empty sample", () => {
    const { root, error } = parse("   ");

    expect(root).toBeNull();
    expect(error).toBeNull();
  });

  it("reports a sample that is not JSON", () => {
    expect(parse("<order/>").error).toMatch(/not valid JSON/);
  });

  /** Honest rather than showing a JSON tree for a format we cannot read yet. */
  it("shows no tree for a format it cannot read", () => {
    const { root, error } = parseSample("UNB+UNOA:1+SENDER", "edifact");

    expect(root).toBeNull();
    expect(error).toMatch(/edifact/);
  });
});

describe("describing a sample value", () => {
  it("quotes text and leaves other types bare", () => {
    expect(describeSample("Ali")).toBe('"Ali"');
    expect(describeSample(42)).toBe("42");
    expect(describeSample(true)).toBe("true");
    expect(describeSample(null)).toBe("null");
  });

  it("shortens long text so it fits on a line", () => {
    expect(describeSample("x".repeat(50))).toMatch(/…"$/);
  });
});

// ─── The scope inside a list ──────────────────────────────────────────────────

describe("itemScopeOf", () => {
  const doc = parseSample(
    '{ "order": { "line": [{ "sku": "A1", "tags": [{ "code": "fragile" }] }] } }',
    "json",
  ).root;

  it("hands back one entry's shape, named relative to the entry", () => {
    const scope = itemScopeOf(doc, "order.line");

    expect(readablePaths(scope)).toEqual(["sku"]);
    expect(listPaths(scope)).toEqual(["tags"]);
  });

  /** Which is what lets a list inside a list resolve its own list. */
  it("narrows again from that entry", () => {
    const inner = itemScopeOf(itemScopeOf(doc, "order.line"), "tags");

    expect(readablePaths(inner)).toEqual(["code"]);
  });

  it("is empty for a path that holds no list", () => {
    expect(readablePaths(itemScopeOf(doc, "order"))).toEqual([]);
    expect(readablePaths(itemScopeOf(null, "anything"))).toEqual([]);
  });

  /** An empty path means the document itself, for a source that is a bare array. */
  it("reads the document itself when the path is empty", () => {
    const array = parseSample('[{ "code": "A" }]', "json").root;

    expect(readablePaths(itemScopeOf(array, ""))).toEqual(["code"]);
  });
});

describe("findNode stays inside one namespace", () => {
  it("does not answer a top-level path with a node from inside a list", () => {
    // `tag` at the top of the document, and a `tag` inside an entry of `line`. A list's
    // children are named relative to one entry, so the two are different namespaces that
    // happen to share a spelling.
    const root = parseSample(
      JSON.stringify({
        line: [{ sku: "A1", tag: [{ code: "inner" }] }],
        tag: [{ code: "outer" }],
      }),
      "json",
    ).root!;

    // The one meant is the top-level list, whose entries carry `code: "outer"`.
    expect(itemShapeAt(root, "tag").map((n) => n.sample)).toEqual(["outer"]);

    // And the nested one is reachable from the entry scope, where it belongs.
    const entry = itemScopeOf(root, "line");
    expect(itemShapeAt(entry, "tag").map((n) => n.sample)).toEqual(["inner"]);
  });
});

describe("coverageByPath", () => {
  it("counts what is read under each object and list", () => {
    const root = parseSample(
      JSON.stringify({
        order: {
          customer: "Ali",
          net: 100,
          line: [{ sku: "A1", qty: 2 }],
        },
      }),
      "json",
    ).root!;

    // A rule inside a list reads `sku`, which is how the list's own contents are named
    // here — so both halves agree on what "read" means.
    const coverage = coverageByPath(root, new Set(["order.customer", "sku"]));

    expect(coverage.get("order")).toEqual({ mapped: 2, total: 4 });
    expect(coverage.get("order.line")).toEqual({ mapped: 1, total: 2 });
  });

  it("has nothing to say about an empty document", () => {
    expect(coverageByPath(null, new Set()).size).toBe(0);
  });
});

describe("a list whose entries are not all the same shape", () => {
  // Same defect as the XML tree had, in the same function: an optional field present
  // in a later entry has to be offered, or the editor cannot map a field the document
  // plainly contains.
  const MIXED = JSON.stringify({
    line: [
      { sku: "A1", note: null },
      { sku: "B7", discount: 5, note: "gift" },
    ],
  });

  it("offers a field only a later entry has", () => {
    expect(itemShapeAt(parseSample(MIXED, "json").root, "line").map((c) => c.key)).toEqual([
      "sku",
      "note",
      "discount",
    ]);
  });

  it("shows a sample from an entry that has one", () => {
    // `note` is null in the first entry. Showing "null" beside the field would say the
    // field holds nothing, when the next entry shows exactly what it holds.
    const note = itemShapeAt(parseSample(MIXED, "json").root, "line").find(
      (c) => c.key === "note",
    );

    expect(describeSample(note?.sample)).toBe('"gift"');
  });

  it("leaves a list of uniform entries exactly as it was", () => {
    expect(itemShapeAt(parseSample(ORDER, "json").root, "order.line").map((c) => c.key)).toEqual([
      "sku",
      "qty",
    ]);
  });
});
