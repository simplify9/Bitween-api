// ─── The shape of a sample document, as the mapper sees it ───────────────────
//
// Deliberately not the old editor's buildTree. That one makes a list behave as its
// own first element, so `order.line.sku` shows up as a leaf you can map — which
// silently meant "the first line's sku" and had no way to say anything else. The
// new mapper does not do that: a path stops at a list, and a list is something a
// list walks. The tree has to show the same thing, or the editor would offer paths
// that resolve to null.

import { dsvFormat } from "d3-dsv";
import { defaultCsvOptions, type CsvOptions } from "./types";

export type DocumentNodeKind = "value" | "object" | "list";

export interface DocumentNode {
  /** The key, as it appears in the document. */
  key: string;
  /** Dot-separated path from the root. Empty for the root itself. */
  path: string;
  kind: DocumentNodeKind;
  /** A sample of the value, for `value` nodes. */
  sample?: unknown;
  /** How many entries the list had in the sample. */
  count?: number;
  /**
   * For a list, the shape of its items — the paths inside are relative to the item,
   * because that is what a list's field rules are written against.
   */
  children: DocumentNode[];
}

/**
 * Which end of the mapping a sample is for.
 *
 * It changes what an XML tree keeps. Reading a partner's document, a prefix is theirs
 * to change between one message and the next, so paths match on the local name and
 * declarations are machinery. Writing one for them, the prefixes and declarations are
 * the document — their parser may well insist on them — so the tree keeps both and the
 * rules carry them through to the writer.
 */
export type SampleRole = "source" | "target";

export interface ParsedSample {
  /** Null when the text could not be read. */
  root: DocumentNode | null;
  error: string | null;
}

/**
 * Reads a sample document into a tree.
 *
 * A format that has no reader here has no tree to show, which is honest rather than
 * showing another format's one.
 */
export function parseSample(
  text: string,
  format: string,
  role: SampleRole = "source",
  csv: CsvOptions = defaultCsvOptions(),
): ParsedSample {
  if (!text.trim()) return { root: null, error: null };

  if (format === "xml") return parseXmlSample(text, role);

  if (format === "csv") return parseCsvSample(text, csv);

  if (format !== "json") {
    return { root: null, error: `No preview of the document shape for ${format} yet.` };
  }

  try {
    return { root: toNode("", "", JSON.parse(text)), error: null };
  } catch (e) {
    return { root: null, error: `The sample is not valid JSON: ${(e as Error).message}` };
  }
}

function toNode(key: string, path: string, value: unknown): DocumentNode {
  if (Array.isArray(value)) {
    // Every entry, not just the first: one entry's shape is only the whole list's
    // shape when nothing in it is optional. Children are named relative to the item —
    // `sku`, not `line[0].sku` — because that is the path a rule inside the list uses.
    return {
      key,
      path,
      kind: "list",
      count: value.length,
      children: mergeShape(value.flatMap(itemShape)),
    };
  }

  if (value !== null && typeof value === "object") {
    return {
      key,
      path,
      kind: "object",
      children: Object.entries(value as Record<string, unknown>).map(([k, v]) =>
        toNode(k, path ? `${path}.${k}` : k, v),
      ),
    };
  }

  return { key, path, kind: "value", sample: value, children: [] };
}

/** The fields of one list entry, named relative to it. */
function itemShape(item: unknown): DocumentNode[] {
  if (item === null || typeof item !== "object" || Array.isArray(item)) return [];
  return Object.entries(item as Record<string, unknown>).map(([k, v]) => toNode(k, k, v));
}

/**
 * One shape covering every entry of a list.
 *
 * An optional field is the whole reason. If the first entry has no `discount` and the
 * second does, taking the first entry's shape means the editor never offers `discount`
 * at all — and the field is right there in the document, so nothing looks wrong: the
 * mapping reads complete and quietly drops it. Merging over the list means a field that
 * appears in any entry is offered for all of them, which is what the mapper does anyway.
 *
 * Entry order is kept, so a list whose entries all have the same shape — nearly all of
 * them — comes out exactly as it did when only the first was read.
 */
function mergeShape(nodes: DocumentNode[]): DocumentNode[] {
  const merged = new Map<string, DocumentNode>();
  for (const node of nodes) {
    const seen = merged.get(node.key);
    merged.set(node.key, seen === undefined ? node : mergeNode(seen, node));
  }
  return [...merged.values()];
}

/** Two entries' readings of the same field, reconciled towards whichever offers more. */
function mergeNode(a: DocumentNode, b: DocumentNode): DocumentNode {
  // A name that repeats in one entry and appears once in another is still a list: that
  // is the shape a rule has to be written against, and the mapper walks the single
  // occurrence as a list of one rather than as no entries.
  if (a.kind === "list" || b.kind === "list") {
    const list = a.kind === "list" ? a : b;
    return {
      ...list,
      count: Math.max(a.count ?? 1, b.count ?? 1),
      children: mergeShape([...a.children, ...b.children]),
    };
  }

  // An object beats a value: keeping the value would drop every path underneath it,
  // and those paths do resolve for the entries that have them.
  if (a.kind === "object" || b.kind === "object") {
    return {
      ...(a.kind === "object" ? a : b),
      children: mergeShape([...a.children, ...b.children]),
    };
  }

  // Two values. A null or an empty first entry says nothing about what the field holds,
  // so the sample shown beside it comes from an entry that has something in it.
  return a.sample === null || a.sample === undefined || a.sample === "" ? b : a;
}

// ─── Delimited text ──────────────────────────────────────────────────────────
//
// These conventions have to match `CsvFormat` on the server exactly, for the same
// reason the XML ones do: this tree is what the editor offers, and that reader is
// what the mapping actually gets. `CsvSampleTreeTests` and `CsvFormatReadTests`
// walk the same files on both sides to keep the two in step.

/**
 * A delimited file as a tree: a list, whose children are its columns.
 *
 * The same shape a JSON document that is a bare array produces, which is the whole
 * reason this format needed nothing from the mapper. A rule walks the document with
 * an empty path and reads a column off each row.
 */
function parseCsvSample(text: string, options: CsvOptions): ParsedSample {
  // d3-dsv takes one character. The wire format allows more, since the server's parser
  // does — so a mapping hand-edited to a longer delimiter still runs, it just cannot be
  // shown here, and saying so is better than drawing a tree that is wrong.
  const delimiter = options.delimiter;
  if (delimiter.length !== 1) {
    return {
      root: null,
      error: `The shape of a file separated by '${options.delimiter}' cannot be shown here.`,
    };
  }

  const rows = dsvFormat(delimiter)
    .parseRows(stripByteOrderMark(text))
    // A blank line, and a line holding only delimiters, are gaps between blocks of
    // records rather than rows. Both turn up in the middle of real files.
    .filter((row) => row.some((field) => (field ?? "").length > 0));

  if (rows.length === 0) return { root: null, error: null };

  const names = options.hasHeader ? columnNames(rows[0]) : [];
  const body = options.hasHeader ? rows.slice(1) : rows;

  return {
    root: {
      key: "",
      path: "",
      kind: "list",
      count: body.length,
      // Merged over every row, so a column that is empty in the first one still shows a
      // sample from a row that has something in it — and a row with extra fields still
      // offers them.
      children: mergeShape(
        body.flatMap((row) =>
          row.map((field, at) => {
            const name = nameAt(names, at);
            return {
              key: name,
              path: name,
              kind: "value" as const,
              sample: field ?? "",
              children: [],
            };
          }),
        ),
      ),
    },
    error: null,
  };
}

/**
 * The column names a header record gives, with every column left addressable.
 *
 * A blank name and a repeated one both fall back to the column's position — the name
 * that column would have had with no header at all. Two columns sharing a name would
 * mean the second silently replacing the first, which on the server is a column of the
 * partner's file simply missing.
 */
function columnNames(header: string[]): string[] {
  const used = new Set<string>();
  return header.map((name, at) => {
    const taken = name.length === 0 || used.has(name);
    const chosen = taken ? String(at + 1) : name;
    used.add(name);
    used.add(chosen);
    return chosen;
  });
}

/** What the field at `at` is called: its header name, or its position counting from one. */
const nameAt = (names: string[], at: number) => names[at] ?? String(at + 1);

/**
 * Drops the byte-order mark Excel writes.
 *
 * Left in place it becomes part of the first column's name, so every rule reading that
 * column resolves to nothing while the editor shows a name that looks exactly right.
 */
const stripByteOrderMark = (text: string) =>
  text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;

// ─── XML ─────────────────────────────────────────────────────────────────────
//
// These conventions have to match `XmlFormat` on the server exactly, because this
// tree is what the editor offers and that reader is what the mapping actually gets.
// A path shown here that resolves to nothing there is the worst kind of bug: the
// mapping looks right and quietly writes nothing. `XmlSampleTreeTests` and
// `XmlFormatReadTests` walk the same document on both sides to keep them in step.

const XMLNS = "http://www.w3.org/2000/xmlns/";

/**
 * Where a browser puts its complaint about a document it could not parse.
 *
 * Firefox makes `parsererror` the document element in a namespace of its own; Chromium
 * and WebKit graft an XHTML one into whatever they recovered. Matching the namespace as
 * well as the name matters in both directions: a partner's document is free to contain
 * an element called `parsererror`, and reporting that as a broken sample would refuse a
 * document that parsed perfectly well.
 */
const PARSER_ERROR_NAMESPACES = [
  "http://www.mozilla.org/newlayout/xml/parsererror.xml",
  "http://www.w3.org/1999/xhtml",
];

/** The key holding an element's own text when it also has attributes or children. */
const TEXT_KEY = "#text";

function parseXmlSample(text: string, role: SampleRole): ParsedSample {
  const parsed = new DOMParser().parseFromString(text, "application/xml");

  // How a DOMParser reports a broken document: not by throwing, but by handing back a
  // document that contains the complaint. Missing it would show a tree of the error.
  const failure = PARSER_ERROR_NAMESPACES.map(
    (ns) => parsed.getElementsByTagNameNS(ns, "parsererror")[0],
  ).find(Boolean);
  if (failure) {
    const detail = (failure.textContent ?? "").trim().split("\n")[0];
    return { root: null, error: `The sample is not valid XML: ${detail || "it could not be read"}` };
  }

  const root = parsed.documentElement;
  if (!root) return { root: null, error: "The sample has no root element." };

  const qualified = role === "target";
  const name = qualified ? root.tagName : root.localName;

  // The root element is a named key rather than the tree itself, so its name is part
  // of every path — the same as the server's reader.
  return {
    root: {
      key: "",
      path: "",
      kind: "object",
      children: [xmlNode(name, name, root, qualified)],
    },
    error: null,
  };
}

function xmlNode(
  key: string,
  path: string,
  element: Element,
  qualified: boolean,
): DocumentNode {
  // Declarations are kept only for a document being written, where they are what makes
  // the output valid; on the way in they are machinery, and four of them at the top of
  // a SOAP document would bury the fields anyone is looking for.
  const attributes = Array.from(element.attributes).filter(
    (a) => qualified || a.namespaceURI !== XMLNS,
  );
  const elements = Array.from(element.children);
  const nameOf = (e: Element) => (qualified ? e.tagName : e.localName);

  // Nothing but text: the element is its value. An empty element is "" — present and
  // empty, which is what it says, and different from one that is not there at all.
  if (attributes.length === 0 && elements.length === 0) {
    return { key, path, kind: "value", sample: element.textContent ?? "", children: [] };
  }

  const children: DocumentNode[] = [];

  for (const attribute of attributes) {
    const name = `@${qualified ? attribute.name : attribute.localName}`;
    children.push({ key: name, path: under(path, name), kind: "value", sample: attribute.value, children: [] });
  }

  // Grouped by name — by the local one when reading, because the same namespace turns
  // up under a different prefix in the very next document.
  const groups = new Map<string, Element[]>();
  for (const child of elements) {
    const group = groups.get(nameOf(child));
    if (group) group.push(child);
    else groups.set(nameOf(child), [child]);
  }

  for (const [name, occurrences] of groups) {
    if (occurrences.length === 1) {
      children.push(xmlNode(name, under(path, name), occurrences[0], qualified));
      continue;
    }

    // A repeated name is how XML writes a list. Its contents are named relative to one
    // entry, because that is what a rule inside the list is written against — hence the
    // empty path — and they are merged over every occurrence, because an element the
    // first `<line>` happens to leave out is still a field the later ones have.
    children.push({
      key: name,
      path: under(path, name),
      kind: "list",
      count: occurrences.length,
      children: mergeShape(
        occurrences.flatMap((o) => xmlNode(name, "", o, qualified).children),
      ),
    });
  }

  const text = ownText(element);
  if (text.trim()) {
    children.push({ key: TEXT_KEY, path: under(path, TEXT_KEY), kind: "value", sample: text, children: [] });
  }

  return { key, path, kind: "object", children };
}

/** An element's own text, not its descendants' — which is all mixed content is. */
function ownText(element: Element): string {
  let text = "";
  for (const node of Array.from(element.childNodes)) {
    if (node.nodeType === 3 || node.nodeType === 4) text += node.nodeValue ?? "";
  }
  return text;
}

const under = (path: string, key: string) => (path ? `${path}.${key}` : key);

/**
 * Every path a field rule can read, which is every `value` node not inside a list.
 *
 * A path inside a list is reachable only from a list that walks it, so offering it
 * at the top level would offer a mapping that resolves to null.
 */
export function readablePaths(root: DocumentNode | null): string[] {
  if (!root) return [];
  const out: string[] = [];
  const walk = (node: DocumentNode) => {
    if (node.kind === "value") {
      if (node.path) out.push(node.path);
      return;
    }
    if (node.kind === "list") return; // its contents belong to a list
    node.children.forEach(walk);
  };
  walk(root);
  return out;
}

/** Every path that holds a list, which is what a list can walk. */
export function listPaths(root: DocumentNode | null): string[] {
  if (!root) return [];
  const out: string[] = [];
  const walk = (node: DocumentNode) => {
    if (node.kind === "list") {
      // The root itself is a list for a root-array document; a list says so with an
      // empty `over`, so it is offered as "" rather than skipped.
      out.push(node.path);
      return;
    }
    if (node.kind === "object") node.children.forEach(walk);
  };
  walk(root);
  return out;
}

/** The item shape of the list at a path, for a list's field rules. */
export function itemShapeAt(root: DocumentNode | null, path: string): DocumentNode[] {
  if (!root) return [];
  const found = findNode(root, path);
  return found?.kind === "list" ? found.children : [];
}

/**
 * What the rules inside a list read their paths against: one entry of the list at
 * `over`, wrapped so it can be walked like a document of its own.
 *
 * This is the scope the mapper itself uses — `MapInto` hands a list's nested lists the
 * current item, not the document — so a list nested in another names its list
 * relative to the entry around it (`tags`), never from the root (`order.line.tags`).
 */
export function itemScopeOf(scope: DocumentNode | null, over: string): DocumentNode {
  return { key: "", path: "", kind: "object", children: itemShapeAt(scope, over) };
}

/**
 * The node at a path, within one namespace.
 *
 * A list's children are named relative to one entry — `sku`, not `order.line.sku` — so
 * they live in a different namespace to everything above them. Searching through a list
 * would let `findNode(root, "tag")` answer with a `tag` nested inside some list when the
 * caller meant a `tag` at the top, which is how `itemShapeAt` could scope a list's rules
 * against the wrong entry shape.
 */
export function findNode(root: DocumentNode, path: string): DocumentNode | undefined {
  if (root.path === path) return root;
  if (root.kind === "list") return undefined;
  for (const child of root.children) {
    const found = findNode(child, path);
    if (found) return found;
  }
  return undefined;
}

/** A value shown next to a source field, short enough to sit on one line. */
export function describeSample(value: unknown): string {
  if (value === null) return "null";
  if (typeof value === "string") return value.length > 24 ? `"${value.slice(0, 24)}…"` : `"${value}"`;
  if (typeof value === "boolean" || typeof value === "number") return String(value);
  return "";
}

export interface Coverage {
  mapped: number;
  total: number;
}

/**
 * How much of what is under each object and list some rule already reads.
 *
 * Built once for the whole tree rather than asked per row: every row subscribes to the
 * hovered path, so each hover re-rendered all of them, and a per-row walk of its own
 * subtree made that quadratic on a document with any depth to it.
 *
 * Counted over the paths as they are named here, which for a list's contents is
 * relative to one entry — the same name a rule inside that list uses, so the two
 * halves agree.
 */
export function coverageByPath(
  root: DocumentNode | null,
  assignedPaths: Set<string>,
): Map<string, Coverage> {
  const out = new Map<string, Coverage>();
  if (!root) return out;

  const walk = (node: DocumentNode): Coverage => {
    if (node.kind === "value")
      return { mapped: assignedPaths.has(node.path) ? 1 : 0, total: 1 };

    let mapped = 0;
    let total = 0;
    for (const child of node.children) {
      const under = walk(child);
      mapped += under.mapped;
      total += under.total;
    }
    out.set(node.path, { mapped, total });
    return { mapped, total };
  };

  walk(root);
  return out;
}
