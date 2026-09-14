import { produce } from "immer";
import { parseSample } from "./documentTree";
import { matchSources, scaffoldFromTarget, type MatchTally, type ScaffoldTally } from "./scaffold";
import {
  emptyFieldRule,
  emptyListEntry,
  emptyListRule,
  emptyRules,
  type CsvOptions,
  type DateOrderName,
  type DocumentFormatId,
  type EditorFieldRule,
  type EditorListEntry,
  type EditorListRule,
  type EditorRules,
  type RuleId,
} from "./types";

// ─── Editor state ─────────────────────────────────────────────────────────────

export interface RulesEditorState {
  rules: EditorRules;
  sourceSample: string;
  targetSample: string;
  /** Which rule row is selected, for the connection canvas and the detail panel. */
  selectedId: RuleId | null;
  hoveredPath: string | null;
  searchSource: string;
  searchTarget: string;
  /** Rules that failed in the last preview, by target path. */
  ruleErrors: Record<string, string>;
  /** Set when the mapping could not be previewed at all. */
  previewError: string | null;
  previewOutput: string | null;
  /** Set when stored rules could not be read, which blocks editing. */
  loadError: string | null;
  /** What the last "build from the sample" did, for the line under the button. */
  scaffold: ScaffoldTally | null;
  /** What the last "match the source fields" did, for the line under that button. */
  match: MatchTally | null;
  /**
   * Whose partner values the preview resolves against.
   *
   * A question about this preview rather than about the mapping, so it is not in
   * `rules`, never saved, and never on the undo stack. It lives here anyway because
   * a rule row deep in the tree needs it to offer that partner's real keys.
   */
  testPartnerId: number | null;
  dirty: boolean;
  past: EditorRules[];
  future: EditorRules[];
}

export const initialRulesEditorState: RulesEditorState = {
  rules: emptyRules(),
  sourceSample: "",
  targetSample: "",
  selectedId: null,
  hoveredPath: null,
  searchSource: "",
  searchTarget: "",
  ruleErrors: {},
  previewError: null,
  previewOutput: null,
  loadError: null,
  scaffold: null,
  match: null,
  testPartnerId: null,
  dirty: false,
  past: [],
  future: [],
};

export type RulesEditorAction =
  | { type: "LOAD"; rules: EditorRules; sourceSample: string; targetSample: string; error?: string }
  | { type: "SET_SOURCE_FORMAT"; format: DocumentFormatId }
  | { type: "SET_TARGET_FORMAT"; format: DocumentFormatId }
  | { type: "SET_DATE_ORDER"; order: DateOrderName }
  | { type: "SET_CSV_OPTIONS"; side: "source" | "target"; options: CsvOptions }
  | { type: "SET_SOURCE_SAMPLE"; text: string }
  | { type: "SET_TARGET_SAMPLE"; text: string }
  | { type: "SCAFFOLD_FROM_TARGET" }
  | { type: "MATCH_SOURCES" }
  | { type: "CLEAR_RULES" }
  | { type: "SET_TEST_PARTNER"; partnerId: number | null }
  | { type: "ADD_FIELD"; listId: RuleId | null; target?: string[] }
  | { type: "UPDATE_FIELD"; id: RuleId; changes: Partial<Omit<EditorFieldRule, "id">> }
  | { type: "REMOVE_FIELD"; id: RuleId }
  | { type: "ADD_LIST"; parentListId: RuleId | null; target?: string[] }
  | { type: "UPDATE_LIST"; id: RuleId; changes: Partial<Omit<EditorListRule, "id" | "fields" | "lists">> }
  | { type: "REMOVE_LIST"; id: RuleId }
  | { type: "MAKE_LIST_OF_VALUES"; listId: RuleId }
  | { type: "ADD_FIXED_ENTRY"; listId: RuleId }
  | { type: "ADD_CLOSING_ENTRY"; listId: RuleId }
  | { type: "REMOVE_FIXED_ENTRY"; id: RuleId }
  | {
      type: "UPDATE_FIXED_ENTRY";
      id: RuleId;
      changes: Partial<Omit<EditorListEntry, "id" | "fields" | "lists">>;
    }
  | { type: "SET_ROOT_LIST"; enabled: boolean }
  | { type: "SELECT"; id: RuleId | null }
  | { type: "HOVER_PATH"; path: string | null }
  | { type: "SET_SEARCH_SOURCE"; text: string }
  | { type: "SET_SEARCH_TARGET"; text: string }
  | { type: "PREVIEW_RESULT"; output: string | null; ruleErrors: Record<string, string>; error: string | null }
  | { type: "SAVED" }
  | { type: "UNDO" }
  | { type: "REDO" };

// ─── Finding a rule ───────────────────────────────────────────────────────────
//
// Rules are addressed by id rather than by a path of indices. Lists nest, so an
// index path would have to be rebuilt every time a row moved — which is the kind
// of bookkeeping the old model needed three recursive helpers for.

/**
 * Anywhere field and list rules live: the mapping itself, a list, or one of a
 * list's fixed entries.
 *
 * One notion rather than three, so adding a field to a fixed entry is the same
 * operation as adding one anywhere else.
 */
export interface RuleContainer {
  fields: EditorFieldRule[];
  lists: EditorListRule[];
}

/** Every list in the tree, outermost first, including the root and those in fixed entries. */
function allLists(rules: EditorRules): EditorListRule[] {
  const found: EditorListRule[] = [];
  const walk = (lists: EditorListRule[]) => {
    for (const list of lists) {
      found.push(list);
      walk(list.lists);
      for (const entry of list.fixed) walk(entry.lists);
    }
  };
  if (rules.root) walk([rules.root]);
  walk(rules.lists);
  return found;
}

/** Every fixed entry in the tree, with the list it belongs to. */
/** Every written entry of every list, at both ends of it. */
function allEntries(
  rules: EditorRules,
): { entry: EditorListEntry; list: EditorListRule; where: "fixed" | "after" }[] {
  return allLists(rules).flatMap((list) => [
    ...list.fixed.map((entry) => ({ entry, list, where: "fixed" as const })),
    ...list.after.map((entry) => ({ entry, list, where: "after" as const })),
  ]);
}

/**
 * The container with this id, or the mapping itself for null.
 *
 * Undefined means the id names nothing — a container removed while something still
 * referred to it, which is a miss rather than a reason to write into the wrong place.
 */
function findContainer(rules: EditorRules, id: RuleId | null): RuleContainer | undefined {
  if (id === null) return rules;
  const list = allLists(rules).find((l) => l.id === id);
  if (list) return list;
  return allEntries(rules).find((e) => e.entry.id === id)?.entry;
}

/** The list a rule lives in, or null when it is a top-level field. */
function findFieldOwner(
  rules: EditorRules,
  id: RuleId,
): { fields: EditorFieldRule[] } | null {
  if (rules.fields.some((f) => f.id === id)) return rules;
  for (const list of allLists(rules)) {
    if (list.fields.some((f) => f.id === id)) return list;
    if (list.item?.id === id) return null; // item rules are updated through their list
  }
  for (const { entry } of allEntries(rules)) {
    if (entry.fields.some((f) => f.id === id)) return entry;
    if (entry.item?.id === id) return null;
  }
  return null;
}

function findField(rules: EditorRules, id: RuleId): EditorFieldRule | undefined {
  if (rules.root?.item?.id === id) return rules.root.item;
  const top = rules.fields.find((f) => f.id === id);
  if (top) return top;
  for (const list of allLists(rules)) {
    const inList = list.fields.find((f) => f.id === id);
    if (inList) return inList;
    if (list.item?.id === id) return list.item;
  }
  for (const { entry } of allEntries(rules)) {
    const inEntry = entry.fields.find((f) => f.id === id);
    if (inEntry) return inEntry;
    if (entry.item?.id === id) return entry.item;
  }
  return undefined;
}

function findList(rules: EditorRules, id: RuleId): EditorListRule | undefined {
  return allLists(rules).find((l) => l.id === id);
}

/** The list a list lives in, so it can be removed from it. */
function findListSiblings(rules: EditorRules, id: RuleId): EditorListRule[] | null {
  if (rules.lists.some((l) => l.id === id)) return rules.lists;
  for (const list of allLists(rules)) {
    if (list.lists.some((l) => l.id === id)) return list.lists;
  }
  for (const { entry } of allEntries(rules)) {
    if (entry.lists.some((l) => l.id === id)) return entry.lists;
  }
  return null;
}

// ─── Reducer ──────────────────────────────────────────────────────────────────

const HISTORY_LIMIT = 50;

/** Whether an action changes the mapping itself, as opposed to what is selected. */
function changesTheMapping(action: RulesEditorAction): boolean {
  switch (action.type) {
    case "SET_SOURCE_FORMAT":
    case "SET_TARGET_FORMAT":
    case "SET_CSV_OPTIONS":
    case "SET_DATE_ORDER":
    case "ADD_FIELD":
    case "UPDATE_FIELD":
    case "REMOVE_FIELD":
    case "ADD_LIST":
    case "UPDATE_LIST":
    case "REMOVE_LIST":
    case "MAKE_LIST_OF_VALUES":
    case "ADD_FIXED_ENTRY":
    case "ADD_CLOSING_ENTRY":
    case "REMOVE_FIXED_ENTRY":
    case "UPDATE_FIXED_ENTRY":
    case "SET_ROOT_LIST":
    case "SCAFFOLD_FROM_TARGET":
    case "MATCH_SOURCES":
    case "CLEAR_RULES":
      return true;
    default:
      return false;
  }
}

export function rulesEditorReducer(
  state: RulesEditorState,
  action: RulesEditorAction,
): RulesEditorState {
  return produce(state, (draft) => {
    if (changesTheMapping(action)) {
      draft.past = [...draft.past.slice(-(HISTORY_LIMIT - 1)), state.rules];
      draft.future = [];
      draft.dirty = true;
    }

    switch (action.type) {
      case "LOAD":
        draft.rules = action.rules;
        draft.sourceSample = action.sourceSample;
        draft.targetSample = action.targetSample;
        draft.loadError = action.error ?? null;
        draft.scaffold = null;
        draft.match = null;
        // A different subscription is a different partner's mapping, so the one
        // chosen for the last preview says nothing about this one.
        draft.testPartnerId = null;
        draft.selectedId = null;
        draft.ruleErrors = {};
        draft.previewError = null;
        draft.previewOutput = null;
        draft.dirty = false;
        draft.past = [];
        draft.future = [];
        break;

      case "SET_SOURCE_FORMAT":
        draft.rules.sourceFormat = action.format;
        break;

      case "SET_TARGET_FORMAT":
        draft.rules.targetFormat = action.format;
        break;

      case "SET_DATE_ORDER":
        draft.rules.sourceDateOrder = action.order;
        break;

      case "SET_CSV_OPTIONS":
        if (action.side === "source") draft.rules.sourceCsv = action.options;
        else draft.rules.targetCsv = action.options;
        break;

      // Samples are an editor convenience — runtime never reads them — so changing
      // one is not a change to the mapping and does not go on the undo stack.
      case "SET_SOURCE_SAMPLE":
        draft.sourceSample = action.text;
        draft.dirty = true;
        break;

      case "SET_TARGET_SAMPLE":
        draft.targetSample = action.text;
        // The old count described the old sample, so it stops being true here.
        draft.scaffold = null;
        draft.dirty = true;
        break;

      // Both samples are parsed here rather than passed in, so the action carries
      // nothing and the button cannot hand the reducer a tree from a stale render.
      case "SCAFFOLD_FROM_TARGET": {
        const target = parseSample(draft.targetSample, draft.rules.targetFormat, "target", draft.rules.targetCsv);
        const source = parseSample(draft.sourceSample, draft.rules.sourceFormat, "source", draft.rules.sourceCsv);
        draft.scaffold = target.error
          ? { created: 0, matched: 0, problem: target.error }
          : scaffoldFromTarget(draft.rules, target.root, source.root);
        break;
      }

      case "MATCH_SOURCES": {
        const source = parseSample(draft.sourceSample, draft.rules.sourceFormat, "source", draft.rules.sourceCsv);
        draft.match = matchSources(draft.rules, source.root);
        break;
      }

      // The formats are kept: they describe the exchange, not the rules, and
      // clearing the rules is not a decision to stop speaking JSON.
      case "CLEAR_RULES":
        draft.rules = {
          ...emptyRules(),
          sourceFormat: draft.rules.sourceFormat,
          targetFormat: draft.rules.targetFormat,
          sourceCsv: draft.rules.sourceCsv,
          targetCsv: draft.rules.targetCsv,
        };
        draft.selectedId = null;
        draft.scaffold = null;
        draft.match = null;
        break;

      case "SET_TEST_PARTNER":
        draft.testPartnerId = action.partnerId;
        break;

      case "ADD_FIELD": {
        const rule = emptyFieldRule(action.target ?? []);
        findContainer(draft.rules, action.listId)?.fields.push(rule);
        draft.selectedId = rule.id;
        break;
      }

      case "UPDATE_FIELD": {
        const rule = findField(draft.rules, action.id);
        if (rule) Object.assign(rule, action.changes);
        break;
      }

      case "REMOVE_FIELD": {
        const owner = findFieldOwner(draft.rules, action.id);
        if (owner) owner.fields = owner.fields.filter((f) => f.id !== action.id);
        if (draft.selectedId === action.id) draft.selectedId = null;
        break;
      }

      case "ADD_LIST": {
        const list = emptyListRule(action.target ?? []);
        findContainer(draft.rules, action.parentListId)?.lists.push(list);
        draft.selectedId = list.id;
        break;
      }

      case "MAKE_LIST_OF_VALUES": {
        const list = findList(draft.rules, action.listId);
        if (!list) break;
        list.item = emptyFieldRule();
        // A list that walks nothing has no walked entry for that rule to describe, so
        // what it needs is the first slot written into it. Without this the click
        // would set a mark nobody can see and add nothing anyone can fill in.
        if (list.over === undefined) {
          const entry = emptyListEntry();
          entry.item = emptyFieldRule();
          list.fixed.push(entry);
        }
        break;
      }

      case "ADD_FIXED_ENTRY": {
        const list = findList(draft.rules, action.listId);
        if (!list) break;
        // Mirrors the list it joins: a list of plain values gets a value, a list of
        // records gets a record. Each entry keeps its own shape, so switching the
        // list afterwards leaves the ones already written alone.
        const entry = emptyListEntry();
        if (list.item) entry.item = emptyFieldRule();
        list.fixed.push(entry);
        break;
      }

      case "ADD_CLOSING_ENTRY": {
        const list = findList(draft.rules, action.listId);
        if (!list) break;
        // Mirrors the list it joins exactly as a leading entry does, so switching the list
        // afterwards leaves the ones already written alone.
        const entry = emptyListEntry();
        if (list.item) entry.item = emptyFieldRule();
        list.after.push(entry);
        break;
      }

      case "REMOVE_FIXED_ENTRY": {
        const found = allEntries(draft.rules).find((e) => e.entry.id === action.id);
        if (!found) break;
        const owner = found.list;
        if (found.where === "after") owner.after = owner.after.filter((e) => e.id !== action.id);
        else owner.fixed = owner.fixed.filter((e) => e.id !== action.id);
        // A list that walks nothing is exactly the entries written into it, so with the
        // last one gone there is nothing left to say it holds values — and leaving the
        // mark on would mean the list could never be built out of records instead.
        if (owner.over === undefined && owner.fixed.length === 0 && owner.after.length === 0)
          owner.item = undefined;
        break;
      }

      case "UPDATE_FIXED_ENTRY": {
        const entry = allEntries(draft.rules).find((e) => e.entry.id === action.id)?.entry;
        if (entry) Object.assign(entry, action.changes);
        break;
      }

      case "UPDATE_LIST": {
        const list = findList(draft.rules, action.id);
        if (list) Object.assign(list, action.changes);
        break;
      }

      case "REMOVE_LIST": {
        if (draft.rules.root?.id === action.id) {
          draft.rules.root = undefined;
        } else {
          const siblings = findListSiblings(draft.rules, action.id);
          if (siblings) {
            const at = siblings.findIndex((l) => l.id === action.id);
            if (at !== -1) siblings.splice(at, 1);
          }
        }
        if (draft.selectedId === action.id) draft.selectedId = null;
        break;
      }

      // A document is either an object with fields in it or a bare array, so turning
      // the root into a list puts the field and list rules aside rather than trying to
      // keep both — and turning it off brings them back.
      case "SET_ROOT_LIST":
        if (action.enabled && !draft.rules.root) draft.rules.root = emptyListRule();
        else if (!action.enabled) draft.rules.root = undefined;
        break;

      case "SELECT":
        draft.selectedId = action.id;
        break;

      case "HOVER_PATH":
        draft.hoveredPath = action.path;
        break;

      case "SET_SEARCH_SOURCE":
        draft.searchSource = action.text;
        break;

      case "SET_SEARCH_TARGET":
        draft.searchTarget = action.text;
        break;

      case "PREVIEW_RESULT":
        draft.previewOutput = action.output;
        draft.ruleErrors = action.ruleErrors;
        draft.previewError = action.error;
        break;

      case "SAVED":
        draft.dirty = false;
        break;

      case "UNDO": {
        if (state.past.length === 0) break;
        draft.future = [state.rules, ...state.future.slice(0, HISTORY_LIMIT - 1)];
        draft.rules = state.past[state.past.length - 1];
        draft.past = state.past.slice(0, -1);
        draft.dirty = true;
        break;
      }

      case "REDO": {
        if (state.future.length === 0) break;
        draft.past = [...state.past.slice(-(HISTORY_LIMIT - 1)), state.rules];
        draft.rules = state.future[0];
        draft.future = state.future.slice(1);
        draft.dirty = true;
        break;
      }
    }
  });
}

// ─── Reading the state ────────────────────────────────────────────────────────

/** The target path of a rule, as the preview endpoint reports it in an error. */
export function targetPathOf(target: string[]): string {
  return target.length === 0 ? "(no target)" : target.join(".");
}

/** Every field rule in the mapping, with the list it belongs to. */
export function everyFieldRule(
  rules: EditorRules,
): { rule: EditorFieldRule; list: EditorListRule | null; isItem: boolean }[] {
  const out: { rule: EditorFieldRule; list: EditorListRule | null; isItem: boolean }[] = [];
  const add = (rule: EditorFieldRule, list: EditorListRule | null, isItem = false) =>
    out.push({ rule, list, isItem });

  for (const rule of rules.fields) add(rule, null);
  for (const list of allLists(rules)) {
    // Only a list that walks something has an entry for an empty path to mean, and
    // only then does the rule run at all: for a list that walks nothing this is the
    // mark saying it holds values, and the mapper never reads it.
    if (list.item) add(list.item, list, list.over !== undefined);
    for (const rule of list.fields) add(rule, list);
  }
  for (const { entry, list } of allEntries(rules)) {
    // Not marked: an entry written into a list by hand has no entry of its own, so
    // the mapper runs its rules against whatever the list reads. An empty path there
    // is the enclosing scope, not one item of the list, and nothing anyone means.
    if (entry.item) add(entry.item, list);
    for (const rule of entry.fields) add(rule, list);
  }
  return out;
}

/**
 * Whether a rule producing a whole entry of a list of plain values has a value.
 *
 * Separate from `isAssigned` because an empty path means something here:
 * the entry itself, which is what a list of plain values almost always wants and what
 * the scaffolder writes. Counted as blank, a correctly built list of values would sit
 * in the "not assigned yet" tally for ever with nothing for anyone to fill in.
 */
export function isItemAssigned(rule: EditorFieldRule): boolean {
  // `rootPath` deliberately not included: an empty path read from the document is
  // the whole document, which would write the same thing into every slot of the
  // list. That is a blank someone still has to fill in, not an answer.
  return rule.from.kind === "path" ? true : isAssigned(rule);
}

/**
 * Whether a rule has a value assigned, for the "n of m assigned" count.
 *
 * Takes only the rule on purpose: it is passed straight to `Array.map` in places, so
 * a second parameter would quietly receive the index.
 */
export function isAssigned(rule: EditorFieldRule): boolean {
  switch (rule.from.kind) {
    case "path":
    case "rootPath":
      return Boolean(rule.from.path?.trim());
    case "fixed":
      return rule.from.value !== undefined && rule.from.value !== "";
    case "partner":
      return Boolean(rule.from.key?.trim());
    case "global":
      return Boolean(rule.from.setId?.trim() && rule.from.key?.trim());
    // Nothing to fill in: asking how many entries there are is the whole rule.
    case "count":
      return true;
    default:
      return false;
  }
}

export { allLists };
