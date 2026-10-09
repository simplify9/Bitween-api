import type { Extension } from "@codemirror/state";
import { StreamLanguage } from "@codemirror/language";
import { javascript } from "@codemirror/lang-javascript";
import { python } from "@codemirror/lang-python";
import { json } from "@codemirror/lang-json";
import { xml } from "@codemirror/lang-xml";
import { csharp } from "@codemirror/legacy-modes/mode/clike";
import { go } from "@codemirror/legacy-modes/mode/go";
import { rust } from "@codemirror/legacy-modes/mode/rust";
import { shell } from "@codemirror/legacy-modes/mode/shell";
import { toml } from "@codemirror/legacy-modes/mode/toml";
import { yaml } from "@codemirror/legacy-modes/mode/yaml";
import type { AdapterSourceListing } from "../api";

/** Highlighting by file name; anything unknown is shown as plain text. */
export function languageFor(path: string): Extension | null {
  const name = path.toLowerCase();
  const ext = name.slice(name.lastIndexOf(".") + 1);
  switch (ext) {
    case "cs":
      return StreamLanguage.define(csharp);
    case "py":
      return python();
    case "js":
    case "mjs":
    case "cjs":
      return javascript();
    case "jsx":
      return javascript({ jsx: true });
    case "ts":
    case "mts":
    case "cts":
      return javascript({ typescript: true });
    case "tsx":
      return javascript({ typescript: true, jsx: true });
    case "json":
      return json();
    case "xml":
    case "csproj":
    case "fsproj":
    case "props":
    case "targets":
    case "config":
      return xml();
    case "go":
      return StreamLanguage.define(go);
    case "rs":
      return StreamLanguage.define(rust);
    case "sh":
      return StreamLanguage.define(shell);
    case "toml":
      return StreamLanguage.define(toml);
    case "yml":
    case "yaml":
      return StreamLanguage.define(yaml);
    default:
      return null;
  }
}

export type FileChange = "added" | "removed" | "changed" | "unchanged";

/** Every path in either version, with how it differs — told apart by hash, without reading a file. */
export function compareListings(
  shown: AdapterSourceListing,
  base: AdapterSourceListing | null,
): { path: string; change: FileChange | null }[] {
  if (!base) return shown.files.map((f) => ({ path: f.path, change: null }));
  const before = new Map(base.files.map((f) => [f.path, f.sha256]));
  const after = new Map(shown.files.map((f) => [f.path, f.sha256]));
  const paths = [...new Set([...after.keys(), ...before.keys()])].sort();
  return paths.map((path) => ({
    path,
    change: !before.has(path)
      ? "added"
      : !after.has(path)
        ? "removed"
        : before.get(path) === after.get(path)
          ? "unchanged"
          : "changed",
  }));
}
