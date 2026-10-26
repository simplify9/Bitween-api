// ─── What "Generate with AI" did, in terms of the rows on screen ──────────────
//
// The server reports differences by where they are in the document - `lines[1].qty` -
// and the editor marks rows by the name the mapper reports a rule's errors under -
// `lines[].qty`. These turn one into the other, and say which rows the AI wrote
// differently from the rules it replaced.

import { outputTreeOf, type OutputNode } from "./outputTree";
import { everyFieldRule } from "./rulesReducer";
import type { EditorRules } from "./types";

/** A row of the output tree: its error key, the rule or list it draws, and whether it is a field. */
interface Row {
  key: string;
  id: string;
  isField: boolean;
  /** The rule as written, without the editor's ids - what "changed" compares. */
  signature: string;
}

const withoutIds = (_key: string, value: unknown) => (_key === "id" ? undefined : value);

function rowsOf(rules: EditorRules): Row[] {
  const rows: Row[] = [];
  const walk = (nodes: OutputNode[]) => {
    for (const node of nodes) {
      switch (node.kind) {
        case "field":
        case "item":
          rows.push({ key: node.errorKey, id: node.rule.id, isField: true, signature: JSON.stringify(node.rule, withoutIds) });
          break;
        case "list": {
          // A list's own settings, not its rows: those are rows of their own.
          const { fields: _f, lists: _l, fixed: _x, after: _a, item: _i, ...own } = node.list;
          rows.push({ key: node.errorKey, id: node.list.id, isField: false, signature: JSON.stringify(own, withoutIds) });
          walk(node.children);
          for (const entry of [...node.fixed, ...node.after]) walk(entry.children);
          break;
        }
        case "branch":
          walk(node.children);
          break;
        case "entry":
          walk(node.children);
          break;
      }
    }
  };
  walk(outputTreeOf(rules));
  return rows;
}

/**
 * The row a difference at `target` belongs to: the rule that writes that place, or
 * failing one, the nearest rule above it - a missing `customer.name` with nothing
 * writing `customer` at all is the list's business, or no row's.
 */
export function rowFor(rules: EditorRules, target: string): { key: string; id: string; isField: boolean } | null {
  if (target === "(root)") return null;
  const byKey = new Map(rowsOf(rules).map((r) => [r.key, r]));
  // A root list's rules are named "(no target)[].code"; its differences "[0].code".
  let key = target.replace(/\[\d+\]/g, "[]");
  if (key.startsWith("[")) key = `(no target)${key}`;

  for (;;) {
    const row = byKey.get(key) ?? (key.endsWith("[]") ? byKey.get(key.slice(0, -2)) : undefined);
    if (row) return { key: row.key, id: row.id, isField: row.isField };
    const cut = Math.max(key.lastIndexOf("."), key.lastIndexOf("[]"));
    if (cut <= 0) return null;
    key = key.slice(0, cut);
  }
}

/** Each row's differences, by its error key, so the row can say what is wrong with it. */
export function differencesByRow(
  rules: EditorRules,
  differences: { target: string; reason: string }[],
): Record<string, string[]> {
  const byRow: Record<string, string[]> = {};
  for (const d of differences) {
    const row = rowFor(rules, d.target);
    if (row) (byRow[row.key] ??= []).push(`${d.target}: ${d.reason}`);
  }
  return byRow;
}

/** The rows in `after` that `before` had no identical rule for: what the AI added or changed. */
export function changedRows(before: EditorRules, after: EditorRules): string[] {
  const had = new Set(rowsOf(before).map((r) => `${r.key}\u0000${r.signature}`));
  return rowsOf(after)
    .filter((r) => !had.has(`${r.key}\u0000${r.signature}`))
    .map((r) => r.key);
}

/** How many rules a mapping has, counted the way the output panel's header counts them. */
export const ruleCount = (rules: EditorRules) => everyFieldRule(rules).length;
