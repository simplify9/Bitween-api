import { describe, expect, it } from "vitest";
import { locate } from "../problems";

const paths = ["main.py", "lib/helpers.py", "src/index.ts", "adapter.json"];

describe("locate", () => {
  it("reads the place from each compiler's way of writing it", () => {
    expect(locate('File "/build/adapter/lib/helpers.py", line 12, in handle', paths)).toEqual({
      path: "lib/helpers.py",
      line: 12,
      column: 1,
    });
    expect(locate("src/index.ts(7,15): error TS2322: Type 'string'…", paths)).toEqual({
      path: "src/index.ts",
      line: 7,
      column: 15,
    });
    expect(locate("main.py:3:1: E999 SyntaxError: invalid syntax", paths)).toEqual({
      path: "main.py",
      line: 3,
      column: 1,
    });
  });

  it("only names a file that is in the draft", () => {
    expect(locate('File "/usr/lib/python3.12/json/decoder.py", line 355', paths)).toBeNull();
    expect(locate("--describe exited with 1: SyntaxError: invalid syntax", paths)).toBeNull();
  });
});
