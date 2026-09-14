// ─── The rules a NativeMapper mapping is made of ─────────────────────────────
//
// These mirror SW.Bitween.NativeAdapters.Mapper.MappingRules. The wire format is
// pinned from the other side by RulesSerializationTests.cs: camelCase property
// names and lowercase enum values, so this file can read the way TypeScript
// normally does rather than mirroring C# spelling.
//
// Unlike the old mapper, this is what gets saved. Nothing is generated from it and
// nothing is recovered out of anything, so the editor reads back exactly what it
// wrote.

export type DocumentFormatId = "json" | "xml" | "csv";

/**
 * Which of the day and the month comes first in the incoming document's dates.
 *
 * Asked once for the mapping rather than on every rule: a partner writes dates one
 * way throughout a document, so per-rule would be the same answer many times over,
 * and one row set wrongly would be invisible.
 */
export type DateOrderName = "yearFirst" | "dayFirst" | "monthFirst";

/** Each option is the same day — 4 September 2026 — written the three ways a partner might. */
export const DATE_ORDERS: { value: DateOrderName; label: string }[] = [
  { value: "yearFirst", label: "Year first — 2026-09-04" },
  { value: "dayFirst", label: "Day first — 04.09.2026" },
  { value: "monthFirst", label: "Month first — 09.04.2026" },
];

/**
 * How one side reads or writes delimited text.
 *
 * Per side rather than per mapping: a partner's semicolon file routinely becomes somebody
 * else's comma file, and one client alone sends all three of comma, semicolon and pipe.
 * Nothing is sniffed from the sample — a guessed delimiter is right until the first field
 * that legitimately contains a comma, and by then the mapping is in production.
 */
export interface CsvOptions {
  /** The characters between one field and the next. */
  delimiter: string;
  /** Whether the first line names the columns rather than carrying data. */
  hasHeader: boolean;
  /**
   * Whether to start a written file with the three bytes that tell Excel it is UTF-8.
   *
   * Without them a name like `BEAUTRAIT Raphaël` opens as `RaphaÃ«l`, and nobody downstream
   * can put that right afterwards. Off by default, because a partner's own parser can just as
   * easily choke on three bytes it did not expect.
   */
  byteOrderMark?: boolean;
}

export const defaultCsvOptions = (): CsvOptions => ({
  delimiter: ",",
  hasHeader: true,
  byteOrderMark: false,
});

/** The separators worth offering, all of which turn up in real partner files. */
export const CSV_DELIMITERS: { value: string; label: string }[] = [
  { value: ",", label: "Comma ," },
  { value: ";", label: "Semicolon ;" },
  { value: "|", label: "Pipe |" },
  { value: "\t", label: "Tab" },
];

export type ValueSourceKind =
  | "path"
  | "rootPath"
  | "fixed"
  | "partner"
  | "global"
  | "count";

export type ValueTypeName = "string" | "number" | "boolean";

export type FilterOperatorName =
  | "equal"
  | "notEqual"
  | "greaterThan"
  | "greaterThanOrEqual"
  | "lessThan"
  | "lessThanOrEqual";

/** Where a field's value comes from. Exactly one shape, chosen by `kind`. */
export interface ValueSource {
  kind: ValueSourceKind;
  /**
   * `path` — a dot-separated path, read from wherever the rule sits: the whole
   * document at the top level, the current entry inside a list.
   *
   * `rootPath` — the same, but always read from the top of the document. Only
   * different inside a list, where it is how one value from the order gets written
   * onto every line of it.
   */
  path?: string;
  /** `fixed` — a literal. */
  value?: unknown;
  /** `partner` — an adapter-property key. `global` — a key within `setId`. */
  key?: string;
  /** `global` — which values set. */
  setId?: string;
}

/** A named function and its arguments, e.g. `{ fn: "multiply", by: 1.16 }`. */
export interface TransformRule {
  fn: string;
  [arg: string]: unknown;
}

export interface LookupRule {
  table: Record<string, unknown>;
  /** Used when the value is not a key in `table`. Absent means output null. */
  fallback?: unknown;
}

export interface FilterRule {
  field: string;
  operator: FilterOperatorName;
  value?: unknown;
}

export interface FieldRule {
  /** Path segments, so a key may contain a dot. */
  target: string[];
  from: ValueSource;
  transform?: TransformRule;
  lookup?: LookupRule;
  /** Absent leaves the value as the source produced it. */
  type?: ValueTypeName;
}

/** One entry of a list that no source list produced. */
export interface ListEntry {
  /** When set, the entry is a single value rather than an object. */
  item?: FieldRule;
  fields: FieldRule[];
  lists: ListRule[];
}

export interface ListRule {
  /**
   * Path to the source list to walk.
   *
   * Three states, and the first two differ: absent means nothing is walked and the
   * list is whatever `fixed` holds; empty means the source document is itself the
   * list; anything else is a path to one.
   */
  over?: string;
  /** What the item is called, for the reader's benefit. */
  as?: string;
  target: string[];
  where?: FilterRule;
  /** Set for a list of plain values instead of a list of records. */
  item?: FieldRule;
  fields: FieldRule[];
  lists: ListRule[];
  /**
   * Entries put into the list before the walked ones.
   *
   * For a list that is partly or wholly constant — a header line a partner expects,
   * or a list whose entries each come from a different field. Each is built from its
   * own rules, so a constant entry may still read the document, the partner or a
   * values set, and may hold lists of its own.
   *
   * Optional on the wire, and left out when there are none: both sides default it,
   * and writing it into every stored list would be noise.
   */
  fixed?: ListEntry[];
  /**
   * Entries put into the list after the walked ones.
   *
   * The other end of `fixed`, and why it exists: a carrier's file ends with a trailer record
   * carrying the number of records above it. Built the same way and reading the same scope —
   * the only differences are where they land and that `count` can see the whole list by then.
   */
  after?: ListEntry[];
}

export interface MappingRules {
  version: number;
  sourceFormat: DocumentFormatId;
  targetFormat: DocumentFormatId;
  /** Absent means year-first, which is the only unambiguous shape. */
  sourceDateOrder?: DateOrderName;
  /** How the incoming document is delimited, when it is delimited text. */
  sourceCsv?: CsvOptions;
  /** How the produced document is delimited, when it is delimited text. */
  targetCsv?: CsvOptions;
  fields: FieldRule[];
  lists: ListRule[];
  /** When set, the whole output is this list rather than an object. */
  root?: ListRule;
}

export const RULES_VERSION = 1;

export const NATIVE_MAPPER_ID = "NativeMapper";

/** The key the rules are stored under in a subscription's mapper properties. */
export const MAPPING_RULES_KEY = "MappingRules";
export const SOURCE_SAMPLE_KEY = "SourceSample";
export const TARGET_SAMPLE_KEY = "TargetSample";

// ─── Editor-side model ────────────────────────────────────────────────────────
//
// The same rules with a client-side id on each one, so React can key rows and the
// reducer can address a rule without walking indices through nested lists. Ids are
// added on load and stripped on save — they never reach the wire.

export type RuleId = string;

export interface EditorFieldRule extends FieldRule {
  id: RuleId;
}

export interface EditorListEntry extends Omit<ListEntry, "fields" | "lists" | "item"> {
  id: RuleId;
  item?: EditorFieldRule;
  fields: EditorFieldRule[];
  lists: EditorListRule[];
}

export interface EditorListRule
  extends Omit<ListRule, "fields" | "lists" | "item" | "fixed"> {
  id: RuleId;
  item?: EditorFieldRule;
  fields: EditorFieldRule[];
  lists: EditorListRule[];
  fixed: EditorListEntry[];
  after: EditorListEntry[];
}

export interface EditorRules extends Omit<MappingRules, "fields" | "lists" | "root"> {
  fields: EditorFieldRule[];
  lists: EditorListRule[];
  root?: EditorListRule;
}

let nextId = 0;

export const newRuleId = (): RuleId => `r${++nextId}`;

/** A field rule with nothing assigned yet, for a row the user has just added. */
export const emptyFieldRule = (target: string[] = []): EditorFieldRule => ({
  id: newRuleId(),
  target,
  from: { kind: "path", path: "" },
});

export const emptyListRule = (target: string[] = []): EditorListRule => ({
  id: newRuleId(),
  over: "",
  target,
  fields: [],
  lists: [],
  fixed: [],
  after: [],
});

export const emptyListEntry = (): EditorListEntry => ({
  id: newRuleId(),
  fields: [],
  lists: [],
});

export const emptyRules = (): EditorRules => ({
  version: RULES_VERSION,
  sourceFormat: "json",
  targetFormat: "json",
  sourceDateOrder: "yearFirst",
  fields: [],
  lists: [],
});

/** The formats the mapper can read and write, for the dropdowns. */
export const DOCUMENT_FORMATS: { id: DocumentFormatId; label: string }[] = [
  { id: "json", label: "JSON" },
  { id: "xml", label: "XML" },
  { id: "csv", label: "CSV" },
];

/**
 * Where a value can come from, with labels short enough to sit inside a row.
 */
/**
 * The segments shown on a row. `rootPath` is deliberately absent: it is not a
 * different kind of thing to a reader, it is the same "from the document" choice
 * pointed at the top rather than at the entry — so the path dropdown offers it as
 * its own group instead of adding a fifth segment that means nothing at the top level.
 */
export const SOURCE_KINDS: { value: ValueSourceKind; label: string; title: string }[] = [
  { value: "path", label: "Source", title: "Read a field out of the incoming document" },
  { value: "fixed", label: "Fixed", title: "The same value every time" },
  { value: "partner", label: "Partner", title: "A property of the exchange's partner" },
  { value: "global", label: "Global", title: "A key from one of the global values sets" },
];

/**
 * Short enough for a row; "boo" is not a word anyone wants to read.
 *
 * Keyed by the type union rather than by `string`, so adding a value type is a
 * compile error here instead of a badge that silently renders as nothing.
 */
export const TYPE_BADGES: Record<ValueTypeName, string> = {
  string: "txt",
  number: "num",
  boolean: "y/n",
};

/**
 * Counting the entries of a list, offered only on a rule that sits inside one.
 *
 * Outside a list there is nothing to count, and a segment that produced an error whenever it
 * was chosen would be a control that exists to be wrong.
 */
const COUNT_KIND = {
  value: "count" as ValueSourceKind,
  label: "Count",
  title:
    "How many rows the source list produced. Entries you write by hand are not counted, so a " +
    "trailer says how many records there are and adding a header line cannot change it.",
};

/** The segments a rule shows, which depends only on whether it sits inside a list. */
export const sourceKindsFor = (insideList: boolean) =>
  insideList ? [...SOURCE_KINDS, COUNT_KIND] : SOURCE_KINDS;

/** What a value can be written as. Blank leaves it as the source produced it. */
export const VALUE_TYPES: { value: string; label: string }[] = [
  { value: "", label: "As it comes" },
  { value: "string", label: "Text" },
  { value: "number", label: "Number" },
  { value: "boolean", label: "Yes / no" },
];

/**
 * A fresh source of the given kind.
 *
 * Switching replaces the whole source rather than adding a field to it, so two
 * sources can never both be half-set — the thing the old model's six optional
 * fields kept getting wrong, since every switch had to remember to clear five.
 */
export const freshSource = (kind: ValueSourceKind): ValueSource =>
  kind === "path" || kind === "rootPath"
    ? { kind, path: "" }
    : kind === "fixed"
      ? { kind: "fixed", value: "" }
      : kind === "partner"
        ? { kind: "partner", key: "" }
        : kind === "count"
          ? // Nothing to configure: the question is the whole rule.
            { kind: "count" }
          : { kind: "global", setId: "", key: "" };

/** Filter operators, with the symbol a user recognises. */
export const FILTER_OPERATORS: { value: FilterOperatorName; label: string }[] = [
  { value: "equal", label: "=" },
  { value: "notEqual", label: "≠" },
  { value: "greaterThan", label: ">" },
  { value: "greaterThanOrEqual", label: "≥" },
  { value: "lessThan", label: "<" },
  { value: "lessThanOrEqual", label: "≤" },
];

/**
 * The transform functions and the arguments each takes.
 *
 * Mirrors Transforms.Names in C#, which has a test asserting every listed name is
 * implemented. A name here that C# does not know would be a dropdown entry that
 * fails at runtime.
 */
export interface TransformArg {
  name: string;
  label: string;
  /**
   * `choice` is a closed list; `suggest` is a box that offers `options` and still
   * takes anything typed, for an argument with common answers but no fixed set.
   */
  kind: "text" | "number" | "choice" | "suggest";
  options?: { value: string; label: string }[];
}

export const TRANSFORMS: {
  fn: string;
  label: string;
  args: TransformArg[];
}[] = [
  { fn: "upper", label: "Uppercase", args: [] },
  { fn: "lower", label: "Lowercase", args: [] },
  { fn: "trim", label: "Trim spaces", args: [] },
  {
    fn: "substring",
    label: "Take part of the text",
    args: [
      { name: "start", label: "Start at", kind: "number" },
      { name: "length", label: "Length", kind: "number" },
    ],
  },
  {
    fn: "replace",
    label: "Replace text",
    args: [
      { name: "find", label: "Find", kind: "text" },
      { name: "with", label: "Replace with", kind: "text" },
    ],
  },
  { fn: "concat", label: "Append text", args: [{ name: "with", label: "Append", kind: "text" }] },
  { fn: "round", label: "Round", args: [{ name: "decimals", label: "Decimals", kind: "number" }] },
  { fn: "multiply", label: "Multiply", args: [{ name: "by", label: "By", kind: "number" }] },
  { fn: "add", label: "Add", args: [{ name: "amount", label: "Amount", kind: "number" }] },
  {
    fn: "formatDate",
    label: "Format a date",
    args: [
      {
        name: "format",
        label: "Format",
        kind: "choice",
        // A closed list on purpose. There was a second box here for which way round the
        // incoming date is written, and two dropdowns on one row asking different
        // questions read as one question asked twice — so that moved to the source
        // document, where it is answered once for the whole mapping.
        options: [
          { value: "", label: "Format…" },
          { value: "yyyy-MM-dd", label: "2026-09-04" },
          { value: "dd/MM/yyyy", label: "04/09/2026" },
          { value: "MM/dd/yyyy", label: "09/04/2026" },
          { value: "dd.MM.yyyy", label: "04.09.2026" },
          { value: "dd-MM-yyyy", label: "04-09-2026" },
          { value: "yyyyMMdd", label: "20260904" },
          { value: "dd MMM yyyy", label: "04 Sep 2026" },
          { value: "d MMMM yyyy", label: "4 September 2026" },
          { value: "yyyy-MM-ddTHH:mm:ss", label: "2026-09-04T13:45:00" },
          { value: "yyyy-MM-dd HH:mm", label: "2026-09-04 13:45" },
          { value: "yyyyMMddHHmmss", label: "20260904134500" },
          { value: "HH:mm", label: "13:45" },
          { value: "HH:mm:ss", label: "13:45:00" },
        ],
      },
    ],
  },
  {
    fn: "defaultIfEmpty",
    label: "Use a default when empty",
    args: [{ name: "value", label: "Default", kind: "text" }],
  },
];
