import { describe, expect, it } from "vitest";
import {
  describeSample,
  itemShapeAt,
  listPaths,
  parseSample,
  readablePaths,
} from "../documentTree";
import { defaultCsvOptions, type CsvOptions } from "../types";

/**
 * The source tree for a delimited sample.
 *
 * These expectations are deliberately the same ones `CsvFormatReadTests` asserts on the
 * server, because this tree is what the editor offers and that reader is what the mapping
 * actually gets. A path shown here that resolves to nothing there is the worst kind of
 * bug: the mapping looks right and quietly writes nothing. The end-to-end guard is the
 * Playwright test that maps a column and reads the previewed output back off the server.
 */

/** The client's comma file, which names its columns. */
const MOVEMENTS =
  "ShipmentNumber,Reference,TrackingCode,Date,Time,Comment1,Comment2\n" +
  "6G61965126082,202493482,SHOR020,2026-09-14,08:29:49,,\n" +
  "8G49824171336,202340914,SHOR020,2026-09-14,08:34:34,,\n";

/** The client's pipe file: three record types, no header. */
const TRACKING =
  "H|FFSTAT|1|0||||||||||202609141313|1309981|N\n" +
  "D|1309981172|OK|DELIVERY|0.100|KGM|1|||20260908FRACPKT03831|3800351262|202609141307|20260911|NTE|CDG|NTE||BRIAN MATIAS CASTRO PENA|GLOBAL LOGTICS NETWORK|||||||222998693|Clementine Sandri|\n" +
  "T|9|1309981|\n";

/** The client's semicolon file: no header, blank lines between blocks, an accented name. */
const TRACK =
  "track;ZT6090386150DE;CDGSF1;SHTW001;20260914;123938;CDG;M+;;;TS;;;BEAUTRAIT Raphaël;ZT6090386150DE;;041800\n" +
  "\n" +
  "\n" +
  "track;ZT6090386205DE;CDGSF1;SHTW001;20260914;123938;CDG;M+;;;TS;;;BEAUTRAIT Raphaël;ZT6090386205DE;;041800\n";

const options = (over: Partial<CsvOptions> = {}): CsvOptions => ({
  ...defaultCsvOptions(),
  ...over,
});

const tree = (text: string, over: Partial<CsvOptions> = {}) =>
  parseSample(text, "csv", "source", options(over));

describe("the tree for a delimited sample", () => {
  it("is a list, so a rule walks the document itself", () => {
    // The same shape a JSON bare array produces, which is why this format needed nothing
    // from the mapper: `over: ""` already means "the document is the list".
    const root = tree(MOVEMENTS).root;

    expect(root?.kind).toBe("list");
    expect(root?.count).toBe(2);
    expect(listPaths(root)).toEqual([""]);
  });

  it("offers no path at the top, because every field belongs to a row", () => {
    // Offering `ShipmentNumber` outside the list would offer a mapping that resolves to
    // nothing: the document is a list, and a list is something a list walks.
    expect(readablePaths(tree(MOVEMENTS).root)).toEqual([]);
  });

  it("names the columns from the header", () => {
    expect(itemShapeAt(tree(MOVEMENTS).root, "").map((c) => c.key)).toEqual([
      "ShipmentNumber",
      "Reference",
      "TrackingCode",
      "Date",
      "Time",
      "Comment1",
      "Comment2",
    ]);
  });

  it("shows what a column holds", () => {
    const columns = itemShapeAt(tree(MOVEMENTS).root, "");
    const at = (key: string) => columns.find((c) => c.key === key)?.sample;

    expect(at("ShipmentNumber")).toBe("6G61965126082");
    expect(describeSample(at("TrackingCode"))).toBe('"SHOR020"');
  });

  it("numbers the fields when nothing names them", () => {
    const columns = itemShapeAt(tree(TRACKING, { delimiter: "|", hasHeader: false }).root, "");

    expect(columns.slice(0, 3).map((c) => c.key)).toEqual(["1", "2", "3"]);
    expect(columns.find((c) => c.key === "18")?.sample).toBe("BRIAN MATIAS CASTRO PENA");
  });

  it("offers every field any record type has", () => {
    // The header record has 16 fields, a detail row 28 and the trailer 4. Reading only the
    // first row would leave two thirds of the file unmappable.
    const root = tree(TRACKING, { delimiter: "|", hasHeader: false }).root;

    expect(root?.count).toBe(3);
    expect(itemShapeAt(root, "")).toHaveLength(28);
  });

  it("takes a sample from a row that has something in it", () => {
    // Field 5 is empty on the header record and carries the weight on a detail row. Showing
    // the first row's blank would say the column holds nothing, when the rows underneath
    // show exactly what it holds.
    const columns = itemShapeAt(tree(TRACKING, { delimiter: "|", hasHeader: false }).root, "");

    expect(columns.find((c) => c.key === "5")?.sample).toBe("0.100");

    // And a field the first row does fill keeps that row's value, because the first row is
    // as real as any other — this file has no header to skip.
    expect(columns.find((c) => c.key === "2")?.sample).toBe("FFSTAT");
  });

  it("skips the blank lines between blocks of records", () => {
    const root = tree(TRACK, { delimiter: ";", hasHeader: false }).root;

    expect(root?.count).toBe(2);
  });

  it("keeps an accented name and a leading zero exactly as written", () => {
    const columns = itemShapeAt(tree(TRACK, { delimiter: ";", hasHeader: false }).root, "");

    expect(columns.find((c) => c.key === "14")?.sample).toBe("BEAUTRAIT Raphaël");
    expect(columns.find((c) => c.key === "17")?.sample).toBe("041800");
  });

  it("reads a value that holds the delimiter as one field", () => {
    const columns = itemShapeAt(tree('sku,address,qty\nA1,"Flat 3, Rainbow St",2').root, "");

    expect(columns.find((c) => c.key === "address")?.sample).toBe("Flat 3, Rainbow St");
    expect(columns.find((c) => c.key === "qty")?.sample).toBe("2");
  });

  it("reads a value that holds a line break as one row", () => {
    const root = tree('sku,address\nA1,"Flat 3\nRainbow Street\nAmman"\n').root;

    expect(root?.count).toBe(1);
    expect(itemShapeAt(root, "").find((c) => c.key === "address")?.sample).toBe(
      "Flat 3\nRainbow Street\nAmman",
    );
  });

  it("gives a repeated column name one of its own", () => {
    const columns = itemShapeAt(tree("code,code,qty\nA1,B7,2").root, "");

    expect(columns.map((c) => c.key)).toEqual(["code", "code_2", "qty"]);
  });

  it("moves a fallback name a header already took", () => {
    // The blank second column wants its position, `2`, which the first column is called. Sharing
    // it would leave the second column with no path of its own for a rule to read.
    const columns = itemShapeAt(tree("2,,qty\na,b,7").root, "");

    expect(columns.map((c) => c.key)).toEqual(["2", "2_2", "qty"]);
  });

  it("names a surplus field without colliding either", () => {
    const columns = itemShapeAt(tree("sku,3\nA1,x,surplus").root, "");

    expect(columns.map((c) => c.key)).toEqual(["sku", "3", "3_2"]);
  });

  it("names a column the same way on every row", () => {
    // Naming runs once for the file rather than per row, so a narrow row followed by a wide one
    // cannot end up calling the same column two different things.
    const columns = itemShapeAt(tree("sku\nA1\nB7,extra").root, "");

    expect(columns.map((c) => c.key)).toEqual(["sku", "2"]);
  });

  it("draws a tree even when the stored options are incomplete", () => {
    // Rules can be hand-written or saved by an older build. Reading the delimiter off `{}` used
    // to throw while the editor was drawing, which is a blank screen rather than a message.
    const parsed = parseSample("a,b\n1,2", "csv", "source", {} as CsvOptions);

    expect(parsed.error).toBeNull();
    expect(itemShapeAt(parsed.root, "").map((c) => c.key)).toEqual(["a", "b"]);
  });

  it("drops the byte-order mark Excel writes", () => {
    // Left in place it becomes part of the first column's name, and every rule reading
    // that column resolves to nothing while the name on screen looks exactly right.
    const columns = itemShapeAt(tree("﻿" + MOVEMENTS).root, "");

    expect(columns[0].key).toBe("ShipmentNumber");
  });

  it("shows a header-only file as a list of no rows", () => {
    const root = tree("ShipmentNumber,Reference\n").root;

    expect(root?.kind).toBe("list");
    expect(root?.count).toBe(0);
  });

  it("says so rather than guessing at a delimiter it cannot show", () => {
    // The wire format allows a longer delimiter because the server's parser does, so such a
    // mapping still runs — it just cannot be drawn here, and a wrong tree would be worse.
    const { root, error } = tree(MOVEMENTS, { delimiter: "||" });

    expect(root).toBeNull();
    expect(error).toMatch(/cannot be shown/);
  });

  it("has nothing to show for an empty sample", () => {
    expect(tree("   ")).toEqual({ root: null, error: null });
  });
});
