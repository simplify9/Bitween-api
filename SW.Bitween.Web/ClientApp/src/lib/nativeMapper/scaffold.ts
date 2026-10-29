// ─── Building rules from a sample of the output document ──────────────────────
//
// The old editor had this as "Generate from JSON": it flattened the target into
// strings like `lines[*].sku`, replaced every mapping it already had, swallowed a
// parse error with an empty catch, and left all of them unassigned.
//
// This one creates a real list for each list, reads each field's type off the
// sample, fills in the source fields it can identify, and only ever adds - on a
// document with two hundred fields the structure was never the slow part.
//
// The sample seeds the rules and is then only a note for the next reader. It is
// not the definition of the output: the rules are, which is why deleting the
// sample cannot change what the mapping produces.

import { itemScopeOf, listPaths, readablePaths, type DocumentNode } from "./documentTree";
import {
  emptyFieldRule,
  emptyListRule,
  type EditorFieldRule,
  type EditorListRule,
  type EditorRules,
  type ValueTypeName,
} from "./types";

export interface ScaffoldTally {
  /** Rules the sample added. Rules already there are never changed. */
  created: number;
  /** How many of the new rules had a source field filled in for them. */
  matched: number;
  /**
   * Set when the sample and the mapping disagree about the shape of the whole
   * document, which is a restructuring the sample does not get to do by itself.
   */
  problem: string | null;
}

/** A place in the output a rule goes, and the sample node that asked for it. */
interface Slot {
  segments: string[];
  node: DocumentNode;
}

/** Anything a field or list rule can be added to: the rules themselves, or a list. */
type Container = { fields: EditorFieldRule[]; lists: EditorListRule[] };

/**
 * Adds a rule for everything in the sample that has no rule yet.
 *
 * Mutates `rules`, so it can be handed an immer draft straight from the reducer.
 */
export function scaffoldFromTarget(
  rules: EditorRules,
  target: DocumentNode | null,
  source: DocumentNode | null,
): ScaffoldTally {
  const tally: ScaffoldTally = { created: 0, matched: 0, problem: null };

  if (!target) {
    tally.problem = "Paste a sample of the output document first.";
    return tally;
  }
  if (target.kind === "value") {
    tally.problem = "The sample is a single value, so there are no fields to build.";
    return tally;
  }

  const targetIsList = target.kind === "list";
  const hasRules = rules.fields.length > 0 || rules.lists.length > 0 || rules.root !== undefined;

  // The root is the one thing the sample cannot quietly change: switching a mapping
  // between an object and a list moves every rule in it. Say so instead.
  if (targetIsList && rules.root === undefined) {
    if (hasRules) {
      tally.problem =
        'The sample is a list, but this mapping builds an object. Turn on "The whole output is a list" first.';
      return tally;
    }
    const root = emptyListRule();
    root.over = rootListOf(source);
    rules.root = root;
    tally.created++;
  }
  if (!targetIsList && rules.root !== undefined) {
    tally.problem =
      'The sample is an object, but this mapping builds a list. Turn off "The whole output is a list" first.';
    return tally;
  }

  if (rules.root) fill(rules.root, target, itemScopeOf(source, rules.root.over ?? ""), tally);
  else fill(rules, target, source, tally);

  return tally;
}

/**
 * Whether a target names an XML namespace declaration - `@xmlns` or `@xmlns:something`.
 *
 * Only ever the last segment, because that is where an attribute sits.
 */
function isNamespaceDeclaration(segments: string[]): boolean {
  const last = segments[segments.length - 1] ?? "";
  return last === "@xmlns" || last.startsWith("@xmlns:");
}

/**
 * Adds what is missing from one container.
 *
 * `holder` is the sample node whose contents the container produces: an object's
 * members, or - for a list - the shape of one list entry. `scope` is the matching
 * half of that on the source side, and it narrows on the way down: a list's rules
 * are written against one entry, so that is what its names are matched against.
 */
function fill(
  container: Container,
  holder: DocumentNode,
  scope: DocumentNode | null,
  tally: ScaffoldTally,
) {
  const sourcePaths = readablePaths(scope);
  const sourceLists = listPaths(scope);
  const { values, lists } = slotsOf(holder.children);

  for (const slot of values) {
    if (container.fields.some((f) => join(f.target) === join(slot.segments))) continue;

    const rule = emptyFieldRule(slot.segments);
    const type = typeFromSample(slot.node.sample);
    if (type) rule.type = type;

    // An XML namespace declaration is part of what the document *is*, not something a
    // partner's data fills in - and a URI is exactly the kind of thing nobody should be
    // retyping from memory. So the sample supplies it, which is the whole reason
    // namespaces are ordinary `@xmlns` fields rather than a setting of their own.
    if (isNamespaceDeclaration(slot.segments)) {
      rule.from = { kind: "fixed", value: slot.node.sample ?? "" };
      rule.type = undefined;
      tally.matched++;
      container.fields.push(rule);
      tally.created++;
      continue;
    }

    const from = matchPath(slot.segments, sourcePaths);
    if (from) {
      rule.from = { kind: "path", path: from };
      tally.matched++;
    }

    container.fields.push(rule);
    tally.created++;
  }

  for (const slot of lists) {
    let list = container.lists.find((l) => join(l.target) === join(slot.segments));
    if (!list) {
      list = emptyListRule(slot.segments);
      const over = matchPath(slot.segments, sourceLists);
      if (over) {
        list.over = over;
        tally.matched++;
      }
      // A list of plain values says so with `item`; it has no fields of its own.
      if (slot.node.children.length === 0) list.item = emptyFieldRule();
      container.lists.push(list);
      tally.created++;
    }

    // A list already producing plain values has nowhere to put fields.
    if (list.item === undefined) fill(list, slot.node, itemScopeOf(scope, list.over ?? ""), tally);
  }
}

/**
 * The source list a root list walks.
 *
 * There is no target name to match against here, so the only two safe readings are
 * "the source document is itself the list" and "the source has exactly one list".
 */
function rootListOf(source: DocumentNode | null): string {
  if (!source) return "";
  if (source.kind === "list") return ""; // empty means the document itself
  const lists = listPaths(source);
  return lists.length === 1 ? lists[0] : "";
}

/**
 * Which source path a target field reads from, or "" when nothing is clearly it.
 *
 * Three readings, each looser than the last, and every one of them has to have a
 * single answer: if two source fields both reduce to `id`, then `id` is ambiguous
 * and a guess between them is wrong half the time. Better an empty field the editor
 * counts than a mapping that looks finished and is not.
 */
function matchPath(targetSegments: string[], candidates: string[]): string {
  const path = join(targetSegments);
  if (candidates.includes(path)) return path;

  const onlyOne = (pick: (candidate: string) => boolean) => {
    const hits = candidates.filter(pick);
    return hits.length === 1 ? hits[0] : "";
  };

  const samePath = onlyOne((c) => normalise(c) === normalise(path));
  if (samePath) return samePath;

  const name = targetSegments[targetSegments.length - 1] ?? "";
  if (name === "") return "";
  return onlyOne((c) => c !== "" && normalise(leafOf(c)) === normalise(name));
}

/** Splits a sample node's members into the rules each one needs. */
function slotsOf(nodes: DocumentNode[]): { values: Slot[]; lists: Slot[] } {
  const values: Slot[] = [];
  const lists: Slot[] = [];

  const walk = (ns: DocumentNode[], prefix: string[]) => {
    for (const node of ns) {
      const segments = [...prefix, node.key];
      if (node.kind === "value") values.push({ segments, node });
      else if (node.kind === "list") lists.push({ segments, node });
      // An object is not a rule of its own: its leaves carry the whole path.
      else walk(node.children, segments);
    }
  };

  walk(nodes, []);
  return { values, lists };
}

/**
 * The type to write the field as, read off the sample.
 *
 * A null in the sample says nothing about the type, so nothing is pinned - and a
 * cast is never applied to a missing value anyway, so pinning a type here cannot
 * turn an absent optional field into a failure.
 */
function typeFromSample(sample: unknown): ValueTypeName | undefined {
  if (typeof sample === "number") return "number";
  if (typeof sample === "boolean") return "boolean";
  if (typeof sample === "string") return "string";
  return undefined;
}

const join = (segments: string[]) => segments.join(".");
const leafOf = (path: string) => path.slice(path.lastIndexOf(".") + 1);

/** `customerName`, `customer_name` and `Customer-Name` are the same name. */
const normalise = (text: string) => text.toLowerCase().replace(/[^a-z0-9]/g, "");

// ─── Filling in the source fields of rules that already exist ─────────────────

export interface MatchTally {
  /** Rules that were empty and now read a source field. */
  matched: number;
  /** Rules that were empty and stayed that way, because nothing clearly fitted. */
  unmatched: number;
}

/**
 * Points every rule that reads nothing at the source field of the same name.
 *
 * Separate from building rules out of a sample, because they answer different
 * questions: that one adds what the partner asked for, this one wires up what is
 * already there. A mapping built by hand, or one whose source sample arrived after
 * the rules did, needs only the second.
 *
 * Rules that already read something are never touched - including ones pointing at
 * a path the sample does not contain, which is a mapping this document cannot
 * confirm rather than a mistake.
 *
 * Mutates `rules`, so it can be handed an immer draft straight from the reducer.
 */
export function matchSources(rules: EditorRules, source: DocumentNode | null): MatchTally {
  const tally: MatchTally = { matched: 0, unmatched: 0 };

  const walk = (container: Container, scope: DocumentNode | null) => {
    const paths = readablePaths(scope);

    for (const rule of container.fields) {
      // Only a path read in this scope: a literal, a partner key or a value set is
      // an answer already given, and a document-scoped read was asked for on purpose.
      if (rule.from.kind !== "path" || rule.from.path) continue;

      const found = matchPath(rule.target, paths);
      if (found) {
        rule.from = { kind: "path", path: found };
        tally.matched++;
      } else {
        tally.unmatched++;
      }
    }

    for (const list of container.lists) {
      const inside = itemScopeOf(scope, list.over ?? "");
      // A written entry produces no entry of its own, so its rules read what the
      // list reads - the same rule the mapper applies when it runs them.
      for (const entry of list.fixed) walk(entry, scope);
      if (list.item === undefined) walk(list, inside);
    }
  };

  if (rules.root) {
    // Its written entries read from where the list sits, which for the root list is the
    // document itself - and they are reached here rather than by `walk`, which only sees
    // the `fixed` of lists nested inside a container.
    for (const entry of rules.root.fixed) walk(entry, source);
    if (rules.root.item === undefined)
      walk(rules.root, itemScopeOf(source, rules.root.over ?? ""));
  } else {
    walk(rules, source);
  }

  return tally;
}
