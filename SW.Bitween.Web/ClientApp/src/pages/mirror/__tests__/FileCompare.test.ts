import { describe, expect, it } from "vitest";
import { laidOut } from "../FileCompare";

describe("laying out a file to compare", () => {
  it("indents JSON with every value as written", () => {
    const text = '{"id":9007199254740993,"price":1.0,"big":1e3,"note":"a, \\"b\\": {c}","list":[],"o":{}}';

    expect(laidOut(text, "json")).toBe(
      [
        "{",
        '  "id": 9007199254740993,',
        '  "price": 1.0,',
        '  "big": 1e3,',
        '  "note": "a, \\"b\\": {c}",',
        '  "list": [],',
        '  "o": {}',
        "}",
      ].join("\n"),
    );
  });

  it("leaves JSON that doesn't parse as it is", () => {
    expect(laidOut('{"a":', "json")).toBe('{"a":');
  });
});
