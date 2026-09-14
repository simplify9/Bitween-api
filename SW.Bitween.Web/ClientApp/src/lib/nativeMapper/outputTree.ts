// ─── The output side as a tree, derived from the rules ───────────────────────
//
// The rules stay one flat list of targets like ["billing", "city"]. This turns
// them into the tree the editor draws, and that direction matters: the tree is a
// view of the rules, never a second copy of them. The old editor had it the other
// way round — its tree was rebuilt from pasted JSON and *was* the truth — which is
// why deleting the sample there could change what the mapping produced.
//
// Nothing here decides what the output contains. It only decides how it is shown.

import { targetPathOf } from "./rulesReducer";
import type {
  EditorFieldRule,
  EditorListEntry,
  EditorListRule,
  EditorRules,
  RuleId,
} from "./types";

export interface OutputFieldNode {
  kind: "field";
  /** Stable across renders: the rule's own id. */
  key: string;
  /** The last segment only — the indentation carries the rest of the path. */
  name: string;
  /** The dotted target within its container, which is what search matches on. */
  path: string;
  rule: EditorFieldRule;
  /** What the mapper reports this rule's errors under. */
  errorKey: string;
}

export interface OutputListNode {
  kind: "list";
  key: string;
  name: string;
  path: string;
  /**
   * Whether this is the mapping's root list — the whole output being a list.
   *
   * It has to be said rather than inferred from an empty target, because a list
   * the user has only just added has no name yet either, and calling that the
   * root left it with no name box to type into.
   */
  isRoot: boolean;
  list: EditorListRule;
  errorKey: string;
  /** Empty for a list of plain values, which carries one item rule instead. */
  children: OutputNode[];
  /** Entries written into the list, before whatever walking a source produces. */
  fixed: OutputEntryNode[];
  /** Entries written after them — where a trailer carrying a record count goes. */
  after: OutputEntryNode[];
}

/** One entry written into a list rather than produced by walking a source list. */
export interface OutputEntryNode {
  kind: "entry";
  key: string;
  /** Counting from one, because it is shown to a reader. */
  position: number;
  /**
   * Which end of the list it is written at.
   *
   * Shown, not just stored: both ends number from one, so a list with a header and a trailer
   * would otherwise have two rows called "entry 1" and two buttons that add to them.
   */
  placement: "fixed" | "after";
  entry: EditorListEntry;
  errorKey: string;
  /** Empty when the entry is a single value rather than an object. */
  children: OutputNode[];
}

/**
 * The single value each walked entry of a list of plain values produces.
 *
 * A node rather than a setting on the list, because that is what it is: one rule,
 * with a source, a transform and a type, exactly like a field. The only thing it
 * lacks is a name — the entries of `["A1","B7"]` have nowhere to be named — so it
 * is the one row whose name is fixed text rather than a box.
 */
export interface OutputItemNode {
  kind: "item";
  key: string;
  /** The list whose walked entries this produces. */
  listId: RuleId;
  rule: EditorFieldRule;
  errorKey: string;
}

export interface OutputBranchNode {
  kind: "branch";
  key: string;
  /** An object that exists only because rules are nested under it. */
  name: string;
  children: OutputNode[];
}

export type OutputNode =
  | OutputFieldNode
  | OutputListNode
  | OutputBranchNode
  | OutputEntryNode
  | OutputItemNode;

/** The two nodes drawn as an ordinary rule row: a named field, and a list's value. */
export type OutputRowNode = OutputFieldNode | OutputItemNode;

/**
 * How a rule's errors are named, mirroring DocumentMapper.Describe.
 *
 * Worth keeping in one place: a root list's own target is empty, so it describes
 * as "(no target)" and its fields come back as "(no target)[].code". Computing
 * this per component got that case wrong, and a failing rule inside a root list
 * never lit up.
 */
export function describeTarget(prefix: string, target: string[]): string {
  const joined = targetPathOf(target);
  return prefix === "" ? joined : `${prefix}[].${joined}`;
}

export function outputTreeOf(rules: EditorRules): OutputNode[] {
  if (rules.root) return [listNode(rules.root, "", true)];
  return grouped(entriesOf(rules.fields, rules.lists, ""));
}

interface Entry {
  /** Segments still to be folded into branches. */
  segments: string[];
  node: OutputNode;
}

function listNode(list: EditorListRule, prefix: string, isRoot = false): OutputListNode {
  const errorKey = describeTarget(prefix, list.target);
  return {
    kind: "list",
    key: list.id,
    name: lastSegment(list.target),
    path: list.target.join("."),
    isRoot,
    list,
    errorKey,
    children: list.item
      ? [
          {
            kind: "item" as const,
            key: list.item.id,
            listId: list.id,
            rule: list.item,
            errorKey,
          },
        ]
      : grouped(entriesOf(list.fields, list.lists, errorKey)),
    // A fixed entry's rules are reported under the list, exactly as a walked
    // entry's are — the mapper hands both the same target.
    fixed: list.fixed.map((entry, at) => entryNode(entry, at, errorKey, "fixed")),
    after: (list.after ?? []).map((entry, at) => entryNode(entry, at, errorKey, "after")),
  };
}

function entryNode(
  entry: EditorListEntry,
  at: number,
  errorKey: string,
  placement: "fixed" | "after",
): OutputEntryNode {
  return {
    kind: "entry",
    key: entry.id,
    position: at + 1,
    placement,
    entry,
    errorKey,
    children: entry.item ? [] : grouped(entriesOf(entry.fields, entry.lists, errorKey)),
  };
}

/**
 * One container's rules, in the order the mapper writes them: every field, then
 * every list. That is what MapInto does, so it is the key order the partner sees —
 * and the tree has to show that rather than an order of its own.
 */
function entriesOf(
  fields: EditorFieldRule[],
  lists: EditorListRule[],
  prefix: string,
): Entry[] {
  return [
    ...fields.map((rule) => ({
      segments: rule.target,
      node: {
        kind: "field" as const,
        key: rule.id,
        name: lastSegment(rule.target),
        path: rule.target.join("."),
        rule,
        errorKey: describeTarget(prefix, rule.target),
      },
    })),
    ...lists.map((list) => ({ segments: list.target, node: listNode(list, prefix) })),
  ];
}

/**
 * Folds multi-segment targets into branches.
 *
 * A branch appears where its first member sits, and members keep their order — so
 * the tree reads in the same order as the document the mapper writes. Sorting the
 * names would look tidier and would be a lie.
 */
function grouped(entries: Entry[]): OutputNode[] {
  const out: OutputNode[] = [];
  const branches = new Map<string, { node: OutputBranchNode; members: Entry[] }>();

  for (const entry of entries) {
    // Nothing left to fold: a direct child of this container. An unnamed rule the
    // user has just added lands here too, so a new row is visible immediately.
    if (entry.segments.length <= 1) {
      out.push(entry.node);
      continue;
    }

    const [head, ...rest] = entry.segments;
    let branch = branches.get(head);
    if (!branch) {
      branch = { node: { kind: "branch", key: head, name: head, children: [] }, members: [] };
      branches.set(head, branch);
      out.push(branch.node);
    }
    branch.members.push({ segments: rest, node: entry.node });
  }

  for (const branch of branches.values()) branch.node.children = grouped(branch.members);
  return out;
}

const lastSegment = (target: string[]) => target[target.length - 1] ?? "";

/**
 * The tree with only what matches `search` left in it, keeping the branches above
 * a match so a hit deep in an object is still reachable.
 *
 * A branch or list that matches by name keeps all of its contents: having found
 * `billing`, you want to see what is in it.
 */
export function filterTree(nodes: OutputNode[], search: string): OutputNode[] {
  const needle = search.trim().toLowerCase();
  if (needle === "") return nodes;

  const matches = (text: string) => text.toLowerCase().includes(needle);

  const keep = (nodes: OutputNode[]): OutputNode[] => {
    const out: OutputNode[] = [];

    for (const node of nodes) {
      switch (node.kind) {
        case "field":
          if (matches(node.path)) out.push(node);
          break;

        case "branch": {
          if (matches(node.name)) {
            out.push(node);
            break;
          }
          const children = keep(node.children);
          if (children.length > 0) out.push({ ...node, children });
          break;
        }

        case "list": {
          if (matches(node.path)) {
            out.push(node);
            break;
          }
          // Both halves of a list are searched: what each entry produces, and the
          // entries written into it.
          const children = keep(node.children);
          const fixed = keep(node.fixed) as OutputEntryNode[];
          const after = keep(node.after) as OutputEntryNode[];
          if (children.length > 0 || fixed.length > 0 || after.length > 0)
            out.push({ ...node, children, fixed, after });
          break;
        }

        // Neither an item nor an entry has a name to match on. An item is only ever
        // reached by its list matching, which keeps its children whole; an entry can
        // still hold named rules, so it survives for those.
        case "item":
          break;

        case "entry": {
          const children = keep(node.children);
          if (children.length > 0) out.push({ ...node, children });
          break;
        }
      }
    }

    return out;
  };

  return keep(nodes);
}
