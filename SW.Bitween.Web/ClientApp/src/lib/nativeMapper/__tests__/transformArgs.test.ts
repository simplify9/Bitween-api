import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { TRANSFORMS } from "../types";

/**
 * The editor names each transform's arguments, and the engine reads them back by those
 * names. Nothing else ties the two together: a rename on either side still type-checks,
 * saves, and previews — the argument simply goes unread and the transform does nothing.
 *
 * So this reads the engine's source rather than a copy of its names, and fails the day
 * the two disagree.
 */
const ENGINE = readFileSync(
  new URL("../../../../../../SW.Bitween.NativeAdapters/Mapper/Transforms.cs", import.meta.url),
  "utf8",
);

/** The argument names the engine reads for one function, from the code that handles it. */
function engineArgs(fn: string): string[] {
  const special = ENGINE.match(new RegExp(`if \\(rule\\.Fn == "${fn}"\\)\\s*\\{([\\s\\S]*?)\\n\\s*\\}`));
  // Each case ends by reporting success, and labels that share a body (`case "multiply":
  // case "add":`) run on into it — so a case is everything up to its `return true`.
  const cased = ENGINE.match(new RegExp(`case "${fn}":([\\s\\S]*?)\\breturn true;`));
  const body = special?.[1] ?? cased?.[1];
  if (!body) return [];

  const read = [...body.matchAll(/Arg\w*\(rule, "(\w+)"\)/g)].map((m) => m[1]);
  // multiply and add read one operand whose name depends on the function.
  const chosen = [...body.matchAll(/\? "(\w+)" : "(\w+)"/g)].flatMap((m) => [m[1], m[2]]);
  return [...read, ...chosen];
}

describe("transform arguments", () => {
  it("are the names the engine reads", () => {
    for (const transform of TRANSFORMS) {
      const engine = engineArgs(transform.fn);
      if (transform.args.length > 0)
        expect(engine, `the engine reads no arguments for ${transform.fn}`).not.toHaveLength(0);
      for (const arg of transform.args)
        expect(engine, `${transform.fn} sends "${arg.name}"`).toContain(arg.name);
    }
  });
});
