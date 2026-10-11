import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * The long dash (em dash, en dash, horizontal bar) is not used anywhere in Bitween: not in what
 * the UI says, not in code, comments, docs or tests. A spaced one becomes " - ", any other "-".
 * This walks the whole repository, so a new one fails here with the file and line that has it.
 */
const ROOT = join(fileURLToPath(new URL(".", import.meta.url)), "../../../..");
const SKIP = new Set(["node_modules", "bin", "obj", "wwwroot", ".git", "test-results", "playwright-report", "dist"]);
const TEXT = /\.(cs|csproj|props|targets|ts|tsx|js|mjs|cjs|json|md|yml|yaml|css|html|sh|ps1|py|txt|xml|cshtml|sql|toml)$/i;
// Built from character codes, so this file has none of them in it.
const LONG_DASH = new RegExp(`[${String.fromCharCode(0x2013, 0x2014, 0x2015)}]`);

function* files(dir: string): Generator<string> {
  for (const name of readdirSync(dir)) {
    if (SKIP.has(name)) continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) yield* files(path);
    else if (TEXT.test(name)) yield path;
  }
}

describe("the long dash", () => {
  it("appears nowhere in the repository", () => {
    const found: string[] = [];
    for (const path of files(ROOT)) {
      readFileSync(path, "utf8")
        .split("\n")
        .forEach((line, i) => {
          if (LONG_DASH.test(line)) found.push(`${relative(ROOT, path)}:${i + 1}`);
        });
    }
    expect(found).toEqual([]);
  });
});
