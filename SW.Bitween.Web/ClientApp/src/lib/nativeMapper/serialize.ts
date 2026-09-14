import {
  MAPPING_RULES_KEY,
  RULES_VERSION,
  SOURCE_SAMPLE_KEY,
  TARGET_SAMPLE_KEY,
  defaultCsvOptions,
  emptyRules,
  newRuleId,
  type DocumentFormatId,
  type EditorFieldRule,
  type EditorListEntry,
  type EditorListRule,
  type EditorRules,
  type FieldRule,
  type ListEntry,
  type ListRule,
  type MappingRules,
} from "./types";

// ─── Between the editor and the wire ──────────────────────────────────────────
//
// The only translation in the whole mapper, and it is a trivial one: add a client
// id on the way in, drop it on the way out. Everything else is the same object.
//
// That is the difference from the old mapper, which converted its state into a
// Scriban template on save and had to reverse-engineer it with 338 lines of
// regular expressions on load.

const withFieldIds = (fields: FieldRule[] | undefined): EditorFieldRule[] =>
  (fields ?? []).map((f) => ({ ...f, id: newRuleId() }));

const withListIds = (lists: ListRule[] | undefined): EditorListRule[] =>
  (lists ?? []).map(listWithIds);

function listWithIds(list: ListRule): EditorListRule {
  return {
    ...list,
    id: newRuleId(),
    item: list.item ? { ...list.item, id: newRuleId() } : undefined,
    fields: withFieldIds(list.fields),
    lists: withListIds(list.lists),
    fixed: (list.fixed ?? []).map(entryWithIds),
    after: (list.after ?? []).map(entryWithIds),
  };
}

function entryWithIds(entry: ListEntry): EditorListEntry {
  return {
    ...entry,
    id: newRuleId(),
    item: entry.item ? { ...entry.item, id: newRuleId() } : undefined,
    fields: withFieldIds(entry.fields),
    lists: withListIds(entry.lists),
  };
}

const stripFieldId = ({ id: _id, ...rest }: EditorFieldRule): FieldRule => rest;

// `fixed` comes out of the spread rather than being overwritten after it: spreading
// the editor's rule would put an empty array in every stored list.
function stripListId({ id: _id, fixed, after, ...list }: EditorListRule): ListRule {
  const wire: ListRule = {
    ...list,
    item: list.item ? stripFieldId(list.item) : undefined,
    fields: list.fields.map(stripFieldId),
    lists: list.lists.map(stripListId),
  };

  // Only when there are some: a list that has none reads back identically without
  // the key, which keeps what was saved equal to what was loaded.
  if (fixed.length > 0) wire.fixed = fixed.map(stripEntryId);
  if (after.length > 0) wire.after = after.map(stripEntryId);
  return wire;
}

function stripEntryId({ id: _id, ...entry }: EditorListEntry): ListEntry {
  return {
    ...entry,
    item: entry.item ? stripFieldId(entry.item) : undefined,
    fields: entry.fields.map(stripFieldId),
    lists: entry.lists.map(stripListId),
  };
}

/** Drops the editor's ids, leaving exactly what the mapper reads. */
export function toWire(rules: EditorRules): MappingRules {
  const wire: MappingRules = {
    version: rules.version,
    sourceFormat: rules.sourceFormat,
    targetFormat: rules.targetFormat,
    fields: rules.fields.map(stripFieldId),
    lists: rules.lists.map(stripListId),
    root: rules.root ? stripListId(rules.root) : undefined,
  };

  // Left out when it is the default, the same way an empty set of written entries is:
  // both sides read absent as year-first, so writing it would make a mapping that never
  // touched dates differ from the one that was loaded.
  if (rules.sourceDateOrder && rules.sourceDateOrder !== "yearFirst")
    wire.sourceDateOrder = rules.sourceDateOrder;

  // Written whenever that side is delimited text, default or not. Leaving a comma out
  // because it happens to match the default would make the mapping depend on two sides
  // agreeing about a default for ever, and the delimiter is the one thing about such a
  // file nobody should have to infer. Left out entirely for JSON and XML, where it would
  // be configuration that never applies.
  if (rules.sourceFormat === "csv") wire.sourceCsv = rules.sourceCsv ?? defaultCsvOptions();
  if (rules.targetFormat === "csv") wire.targetCsv = rules.targetCsv ?? defaultCsvOptions();

  return wire;
}

/** Adds the editor's ids to rules that came off the wire. */
export function fromWire(rules: MappingRules): EditorRules {
  return {
    version: rules.version ?? RULES_VERSION,
    sourceFormat: (rules.sourceFormat ?? "json") as DocumentFormatId,
    targetFormat: (rules.targetFormat ?? "json") as DocumentFormatId,
    // Kept as they were stored. A mapping whose format is delimited text but whose options
    // are missing — hand-written, or saved before these existed — falls back to the same
    // defaults the server would apply, rather than to nothing.
    sourceCsv: rules.sourceCsv ?? (rules.sourceFormat === "csv" ? defaultCsvOptions() : undefined),
    targetCsv: rules.targetCsv ?? (rules.targetFormat === "csv" ? defaultCsvOptions() : undefined),
    // Absent means year-first: a mapping saved before this existed could only ever have
    // read the unambiguous shapes correctly anyway.
    sourceDateOrder: rules.sourceDateOrder ?? "yearFirst",
    fields: withFieldIds(rules.fields),
    lists: withListIds(rules.lists),
    root: rules.root ? listWithIds(rules.root) : undefined,
  };
}

export interface LoadedMapping {
  rules: EditorRules;
  sourceSample: string;
  targetSample: string;
  /** Set when stored rules could not be read, so the editor can say so rather than silently starting blank. */
  error?: string;
}

/**
 * Reads a mapping out of a subscription's mapper properties.
 *
 * Unreadable rules are reported rather than swallowed. Starting the editor blank
 * and letting the user save over a mapping that was merely unparseable would
 * destroy it, which is worse than refusing to open.
 */
export function loadMapping(properties: Record<string, string> | undefined): LoadedMapping {
  const raw = properties?.[MAPPING_RULES_KEY];
  const sourceSample = properties?.[SOURCE_SAMPLE_KEY] ?? "";
  const targetSample = properties?.[TARGET_SAMPLE_KEY] ?? "";

  if (!raw?.trim()) return { rules: emptyRules(), sourceSample, targetSample };

  let parsed: MappingRules;
  try {
    parsed = JSON.parse(raw) as MappingRules;
  } catch (e) {
    return {
      rules: emptyRules(),
      sourceSample,
      targetSample,
      error: `The saved mapping rules could not be read: ${(e as Error).message}`,
    };
  }

  if (typeof parsed?.version === "number" && parsed.version > RULES_VERSION) {
    return {
      rules: emptyRules(),
      sourceSample,
      targetSample,
      error:
        `These mapping rules are version ${parsed.version}, but this version of Bitween ` +
        `understands up to version ${RULES_VERSION}. Opening them here would lose whatever ` +
        `the newer version added.`,
    };
  }

  // `loops` was renamed to `lists` before release, when a list stopped being only a
  // loop. Reading such a mapping would find no lists and quietly drop every one of
  // them, and saving from there would destroy it — so refuse instead.
  if (Object.prototype.hasOwnProperty.call(parsed ?? {}, "loops")) {
    return {
      rules: emptyRules(),
      sourceSample,
      targetSample,
      error:
        "These mapping rules were saved before lists were renamed, and its lists cannot be " +
        "read here. Rebuild the mapping rather than saving over it.",
    };
  }

  // Valid JSON of the wrong shape — `"fields": {}`, a list where a field belongs —
  // reaches this line, and `fromWire` maps over what it is given. A thrown TypeError
  // here would escape into the route as a blank screen rather than the refusal above.
  try {
    return { rules: fromWire(parsed), sourceSample, targetSample };
  } catch (e) {
    return {
      rules: emptyRules(),
      sourceSample,
      targetSample,
      error:
        "The saved mapping rules are not shaped the way this editor understands: " +
        `${(e as Error).message}. Rebuild the mapping rather than saving over it.`,
    };
  }
}

/** The mapper properties to save for a mapping. */
export function saveMapping(
  rules: EditorRules,
  sourceSample: string,
  targetSample: string,
): Record<string, string> {
  return {
    [MAPPING_RULES_KEY]: JSON.stringify(toWire(rules)),
    [SOURCE_SAMPLE_KEY]: sourceSample,
    [TARGET_SAMPLE_KEY]: targetSample,
  };
}
