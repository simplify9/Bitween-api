import { describe, expect, it } from "vitest";
import { fromWire, loadMapping, saveMapping, toWire } from "../serialize";
import {
  everyFieldRule,
  initialRulesEditorState,
  isAssigned,
  isItemAssigned,
  rulesEditorReducer,
  type RulesEditorAction,
  type RulesEditorState,
} from "../rulesReducer";
import {
  MAPPING_RULES_KEY,
  RULES_VERSION,
  SOURCE_SAMPLE_KEY,
  TRANSFORMS,
  emptyFieldRule,
  emptyListEntry,
  emptyListRule,
  emptyRules,
  type EditorRules,
  type MappingRules,
} from "../types";

const run = (actions: RulesEditorAction[], from = initialRulesEditorState): RulesEditorState =>
  actions.reduce(rulesEditorReducer, from);

const loaded = (rules: EditorRules): RulesEditorState =>
  run([{ type: "LOAD", rules, sourceSample: "", targetSample: "" }]);

// ─── The round trip ───────────────────────────────────────────────────────────

describe("saving and loading", () => {
  /**
   * The thing the old design got wrong. It saved a generated template and had to
   * reverse-engineer the rules back out of it; here what comes back is what went in.
   */
  it("reloads exactly what it saved", () => {
    const wire: MappingRules = {
      version: RULES_VERSION,
      sourceFormat: "json",
      targetFormat: "json",
      fields: [
        {
          target: ["customer", "name"],
          from: { kind: "path", path: "order.customer" },
          transform: { fn: "upper" },
          type: "string",
        },
        { target: ["region"], from: { kind: "partner", key: "region-code" } },
        {
          target: ["state"],
          from: { kind: "path", path: "order.state" },
          lookup: { table: { NEW: 1 }, fallback: 0 },
          type: "number",
        },
      ],
      lists: [
        {
          over: "order.line",
          as: "line",
          target: ["lines"],
          where: { field: "qty", operator: "greaterThan", value: 0 },
          fields: [{ target: ["sku"], from: { kind: "path", path: "sku" } }],
          lists: [
            { over: "tags", target: ["labels"], item: { target: [], from: { kind: "path", path: "t" } }, fields: [], lists: [] },
          ],
        },
      ],
    };

    expect(toWire(fromWire(wire))).toEqual(wire);
  });

  it("survives a trip through the stored properties", () => {
    const original = fromWire({
      version: RULES_VERSION,
      sourceFormat: "json",
      targetFormat: "json",
      fields: [{ target: ["a"], from: { kind: "fixed", value: "WEB" } }],
      lists: [],
    });

    const stored = saveMapping(original, '{"x":1}', "{}");
    const back = loadMapping(stored);

    expect(back.error).toBeUndefined();
    expect(toWire(back.rules)).toEqual(toWire(original));
    expect(back.sourceSample).toBe('{"x":1}');
  });

  it("keeps a dotted key as one segment", () => {
    const stored = saveMapping(
      fromWire({
        version: RULES_VERSION,
        sourceFormat: "json",
        targetFormat: "json",
        fields: [{ target: ["file.txt"], from: { kind: "fixed", value: 1 } }],
        lists: [],
      }),
      "",
      "",
    );

    expect(loadMapping(stored).rules.fields[0].target).toEqual(["file.txt"]);
  });

  it("never writes editor ids to the wire", () => {
    const wire = toWire(
      fromWire({
        version: RULES_VERSION,
        sourceFormat: "json",
        targetFormat: "json",
        fields: [{ target: ["a"], from: { kind: "fixed", value: 1 } }],
        lists: [
          { over: "x", target: ["y"], fields: [{ target: ["b"], from: { kind: "fixed", value: 2 } }], lists: [] },
        ],
      }),
    );

    expect(JSON.stringify(wire)).not.toContain('"id"');
  });

  it("gives every rule its own id", () => {
    const rules = fromWire({
      version: RULES_VERSION,
      sourceFormat: "json",
      targetFormat: "json",
      fields: [
        { target: ["a"], from: { kind: "fixed", value: 1 } },
        { target: ["b"], from: { kind: "fixed", value: 2 } },
      ],
      lists: [],
    });

    expect(rules.fields[0].id).not.toBe(rules.fields[1].id);
  });

  it("starts blank when there is nothing stored", () => {
    const back = loadMapping({});

    expect(back.error).toBeUndefined();
    expect(back.rules.fields).toEqual([]);
    expect(back.rules.version).toBe(RULES_VERSION);
  });

  /**
   * Reported rather than swallowed. Opening blank over rules that were merely
   * unparseable would let the next save destroy them.
   */
  it("reports unreadable rules instead of starting blank", () => {
    const back = loadMapping({ [MAPPING_RULES_KEY]: "{ not json" });

    expect(back.error).toMatch(/could not be read/);
  });

  it("refuses rules from a newer version", () => {
    const back = loadMapping({
      [MAPPING_RULES_KEY]: JSON.stringify({ version: 99, fields: [] }),
    });

    expect(back.error).toMatch(/version 99/);
  });

  it("keeps the samples even when the rules are unreadable", () => {
    const back = loadMapping({
      [MAPPING_RULES_KEY]: "{ not json",
      [SOURCE_SAMPLE_KEY]: '{"kept":true}',
    });

    expect(back.sourceSample).toBe('{"kept":true}');
  });
});

// ─── Editing ──────────────────────────────────────────────────────────────────

describe("editing rules", () => {
  it("adds a top-level field and selects it", () => {
    const state = run([{ type: "ADD_FIELD", listId: null, target: ["a"] }]);

    expect(state.rules.fields).toHaveLength(1);
    expect(state.selectedId).toBe(state.rules.fields[0].id);
    expect(state.dirty).toBe(true);
  });

  it("updates a field by id", () => {
    let state = run([{ type: "ADD_FIELD", listId: null, target: ["a"] }]);
    const id = state.rules.fields[0].id;

    state = rulesEditorReducer(state, {
      type: "UPDATE_FIELD",
      id,
      changes: { from: { kind: "fixed", value: "WEB" }, type: "string" },
    });

    expect(state.rules.fields[0].from).toEqual({ kind: "fixed", value: "WEB" });
    expect(state.rules.fields[0].type).toBe("string");
  });

  /**
   * Switching where a value comes from replaces the whole source object, so two
   * sources cannot both be set. The old model had six optional fields and picked
   * whichever happened to be filled, which is what its mode-switching bugs were.
   */
  it("switching source kind leaves nothing behind", () => {
    let state = run([{ type: "ADD_FIELD", listId: null, target: ["a"] }]);
    const id = state.rules.fields[0].id;

    state = rulesEditorReducer(state, {
      type: "UPDATE_FIELD",
      id,
      changes: { from: { kind: "path", path: "order.customer" } },
    });
    state = rulesEditorReducer(state, {
      type: "UPDATE_FIELD",
      id,
      changes: { from: { kind: "partner", key: "region" } },
    });

    expect(state.rules.fields[0].from).toEqual({ kind: "partner", key: "region" });
    expect(state.rules.fields[0].from.path).toBeUndefined();
  });

  it("removes a field and clears the selection", () => {
    let state = run([{ type: "ADD_FIELD", listId: null, target: ["a"] }]);
    const id = state.rules.fields[0].id;

    state = rulesEditorReducer(state, { type: "REMOVE_FIELD", id });

    expect(state.rules.fields).toHaveLength(0);
    expect(state.selectedId).toBeNull();
  });

  it("adds a field inside a list", () => {
    let state = run([{ type: "ADD_LIST", parentListId: null, target: ["lines"] }]);
    const listId = state.rules.lists[0].id;

    state = rulesEditorReducer(state, { type: "ADD_FIELD", listId, target: ["sku"] });

    expect(state.rules.lists[0].fields).toHaveLength(1);
    expect(state.rules.fields).toHaveLength(0);
  });

  it("nests a list inside a list and updates the inner one", () => {
    let state = run([{ type: "ADD_LIST", parentListId: null, target: ["orders"] }]);
    const outer = state.rules.lists[0].id;

    state = rulesEditorReducer(state, { type: "ADD_LIST", parentListId: outer, target: ["items"] });
    const inner = state.rules.lists[0].lists[0].id;

    state = rulesEditorReducer(state, { type: "UPDATE_LIST", id: inner, changes: { over: "lines" } });

    expect(state.rules.lists[0].lists[0].over).toBe("lines");
  });

  it("removes a nested list without touching its parent", () => {
    let state = run([{ type: "ADD_LIST", parentListId: null, target: ["orders"] }]);
    const outer = state.rules.lists[0].id;
    state = rulesEditorReducer(state, { type: "ADD_LIST", parentListId: outer, target: ["items"] });
    const inner = state.rules.lists[0].lists[0].id;

    state = rulesEditorReducer(state, { type: "REMOVE_LIST", id: inner });

    expect(state.rules.lists).toHaveLength(1);
    expect(state.rules.lists[0].lists).toHaveLength(0);
  });

  it("turns the root into a list and back", () => {
    let state = run([{ type: "SET_ROOT_LIST", enabled: true }]);
    expect(state.rules.root).toBeDefined();

    state = rulesEditorReducer(state, { type: "SET_ROOT_LIST", enabled: false });
    expect(state.rules.root).toBeUndefined();
  });

  it("finds a field inside the root list", () => {
    let state = run([{ type: "SET_ROOT_LIST", enabled: true }]);
    const rootId = state.rules.root!.id;

    state = rulesEditorReducer(state, { type: "ADD_FIELD", listId: rootId, target: ["code"] });
    const fieldId = state.rules.root!.fields[0].id;

    state = rulesEditorReducer(state, {
      type: "UPDATE_FIELD",
      id: fieldId,
      changes: { from: { kind: "path", path: "sku" } },
    });

    expect(state.rules.root!.fields[0].from.path).toBe("sku");
  });
});

// ─── Undo ─────────────────────────────────────────────────────────────────────

describe("undo and redo", () => {
  it("undoes a change to the mapping", () => {
    let state = run([{ type: "ADD_FIELD", listId: null, target: ["a"] }]);
    state = rulesEditorReducer(state, { type: "UNDO" });

    expect(state.rules.fields).toHaveLength(0);
  });

  it("redoes it", () => {
    let state = run([{ type: "ADD_FIELD", listId: null, target: ["a"] }]);
    state = rulesEditorReducer(state, { type: "UNDO" });
    state = rulesEditorReducer(state, { type: "REDO" });

    expect(state.rules.fields).toHaveLength(1);
  });

  /** Selecting a row is not an edit, so it must not consume an undo step. */
  it("selection does not go on the undo stack", () => {
    let state = run([{ type: "ADD_FIELD", listId: null, target: ["a"] }]);
    const before = state.past.length;

    state = rulesEditorReducer(state, { type: "SELECT", id: null });

    expect(state.past.length).toBe(before);
  });

  it("undo on a fresh editor does nothing", () => {
    expect(rulesEditorReducer(initialRulesEditorState, { type: "UNDO" }).rules.fields).toEqual([]);
  });

  it("loading clears the history", () => {
    let state = run([{ type: "ADD_FIELD", listId: null, target: ["a"] }]);
    state = rulesEditorReducer(state, {
      type: "LOAD",
      rules: emptyRules(),
      sourceSample: "",
      targetSample: "",
    });

    expect(state.past).toEqual([]);
    expect(state.dirty).toBe(false);
  });
});

// ─── Reading the state ────────────────────────────────────────────────────────

describe("reading the state", () => {
  it("lists every field rule including ones inside nested lists", () => {
    const rules = fromWire({
      version: RULES_VERSION,
      sourceFormat: "json",
      targetFormat: "json",
      fields: [{ target: ["a"], from: { kind: "fixed", value: 1 } }],
      lists: [
        {
          over: "x",
          target: ["y"],
          fields: [{ target: ["b"], from: { kind: "fixed", value: 2 } }],
          lists: [
            {
              over: "z",
              target: ["w"],
              item: { target: [], from: { kind: "path", path: "t" } },
              fields: [],
              lists: [],
            },
          ],
        },
      ],
    });

    const targets = everyFieldRule(rules).map((f) => f.rule.target.join("."));

    expect(targets).toHaveLength(3);
    expect(targets).toContain("a");
    expect(targets).toContain("b");
  });

  it("knows whether a rule has a value assigned", () => {
    const state = loaded(
      fromWire({
        version: RULES_VERSION,
        sourceFormat: "json",
        targetFormat: "json",
        fields: [
          { target: ["a"], from: { kind: "path", path: "" } },
          { target: ["b"], from: { kind: "path", path: "x" } },
          { target: ["c"], from: { kind: "fixed", value: "WEB" } },
          { target: ["d"], from: { kind: "partner", key: "" } },
          { target: ["e"], from: { kind: "global", setId: "s", key: "k" } },
          { target: ["f"], from: { kind: "global", setId: "s" } },
        ],
        lists: [],
      }),
    );

    expect(state.rules.fields.map(isAssigned)).toEqual([false, true, true, false, true, false]);
  });

  it("records preview errors per rule", () => {
    const state = run([
      {
        type: "PREVIEW_RESULT",
        output: null,
        ruleErrors: { total: "cannot convert 'abc' to number" },
        error: null,
      },
    ]);

    expect(state.ruleErrors.total).toMatch(/cannot convert/);
  });
});

// ─── The transform list ───────────────────────────────────────────────────────

describe("transforms offered to the user", () => {
  /**
   * Mirrors Transforms.Names in C#, which has its own test asserting every listed
   * name is implemented. A name here that C# does not know would be a dropdown
   * entry that fails at runtime.
   */
  it("matches the functions the mapper implements", () => {
    expect(TRANSFORMS.map((t) => t.fn).sort()).toEqual(
      [
        "add",
        "concat",
        "defaultIfEmpty",
        "formatDate",
        "lower",
        "multiply",
        "replace",
        "round",
        "substring",
        "trim",
        "upper",
      ].sort(),
    );
  });

  it("gives every function a label and named arguments", () => {
    for (const transform of TRANSFORMS) {
      expect(transform.label).toBeTruthy();
      for (const arg of transform.args) {
        expect(arg.name).toBeTruthy();
        expect(arg.label).toBeTruthy();
      }
    }
  });
});

// ─── The pre-release rename ───────────────────────────────────────────────────

describe("rules saved before lists were renamed", () => {
  /**
   * `loops` became `lists`. Such a mapping parses fine and simply has no lists,
   * so reading it would drop every one of them and saving would then destroy it.
   */
  it("refuses to open rather than dropping the lists", () => {
    const stored = JSON.stringify({
      version: 1,
      sourceFormat: "json",
      targetFormat: "json",
      fields: [{ target: ["ref"], from: { kind: "path", path: "order.ref" } }],
      loops: [{ over: "order.line", target: ["lines"], fields: [], loops: [] }],
    });

    const loaded = loadMapping({ [MAPPING_RULES_KEY]: stored });

    expect(loaded.error).toMatch(/before lists were renamed/);
    expect(loaded.rules.fields).toHaveLength(0);
  });

  it("opens rules that use the current name", () => {
    const stored = JSON.stringify({
      version: 1,
      sourceFormat: "json",
      targetFormat: "json",
      fields: [],
      lists: [{ over: "order.line", target: ["lines"], fields: [], lists: [] }],
    });

    const loaded = loadMapping({ [MAPPING_RULES_KEY]: stored });

    expect(loaded.error).toBeUndefined();
    expect(loaded.rules.lists).toHaveLength(1);
  });
});

// ─── Entries written into a list ──────────────────────────────────────────────

describe("fixed entries", () => {
  const runFrom = (state: RulesEditorState, actions: RulesEditorAction[]) =>
    actions.reduce(rulesEditorReducer, state);

  const withOneList = (): RulesEditorState => {
    const rules = emptyRules();
    rules.lists.push(emptyListRule(["lines"]));
    return loaded(rules);
  };

  const theList = (state: RulesEditorState) => state.rules.lists[0];

  it("adds an entry to a list", () => {
    const start = withOneList();

    const after = runFrom(start, [{ type: "ADD_FIXED_ENTRY", listId: start.rules.lists[0].id }]);

    expect(theList(after).fixed).toHaveLength(1);
  });

  it("adds nothing for a list id that names no list", () => {
    const after = runFrom(withOneList(), [{ type: "ADD_FIXED_ENTRY", listId: "not-a-list" }]);

    expect(theList(after).fixed).toHaveLength(0);
  });

  /** A fixed entry is a container like any other, so a field goes into it the same way. */
  it("puts a field inside an entry rather than in the list's per-entry rules", () => {
    const start = withOneList();
    const withEntry = runFrom(start, [
      { type: "ADD_FIXED_ENTRY", listId: start.rules.lists[0].id },
    ]);
    const entryId = theList(withEntry).fixed[0].id;

    const after = runFrom(withEntry, [{ type: "ADD_FIELD", listId: entryId, target: ["sku"] }]);

    expect(theList(after).fixed[0].fields.map((f) => f.target)).toEqual([["sku"]]);
    expect(theList(after).fields).toHaveLength(0);
  });

  it("removes one without touching the others", () => {
    const start = withOneList();
    const listId = start.rules.lists[0].id;
    const two = runFrom(start, [
      { type: "ADD_FIXED_ENTRY", listId },
      { type: "ADD_FIXED_ENTRY", listId },
    ]);
    const first = theList(two).fixed[0].id;

    const after = runFrom(two, [{ type: "REMOVE_FIXED_ENTRY", id: first }]);

    expect(theList(after).fixed).toHaveLength(1);
    expect(theList(after).fixed[0].id).not.toBe(first);
  });

  /** Adding one is a change to the mapping, so it goes on the undo stack. */
  it("is undone in one step", () => {
    const start = withOneList();
    const after = runFrom(start, [
      { type: "ADD_FIXED_ENTRY", listId: start.rules.lists[0].id },
      { type: "UNDO" },
    ]);

    expect(theList(after).fixed).toHaveLength(0);
  });

  it("mirrors a list of plain values, so its entry is a value too", () => {
    const rules = emptyRules();
    const list = emptyListRule(["codes"]);
    list.item = emptyFieldRule();
    rules.lists.push(list);
    const start = loaded(rules);

    const after = runFrom(start, [
      { type: "ADD_FIXED_ENTRY", listId: start.rules.lists[0].id },
    ]);

    expect(theList(after).fixed[0].item).toBeDefined();
  });

  it("counts a rule inside an entry in the assigned total", () => {
    const start = withOneList();
    const listId = start.rules.lists[0].id;
    const withEntry = runFrom(start, [{ type: "ADD_FIXED_ENTRY", listId }]);
    const entryId = theList(withEntry).fixed[0].id;
    const after = runFrom(withEntry, [{ type: "ADD_FIELD", listId: entryId, target: ["sku"] }]);

    expect(everyFieldRule(after.rules)).toHaveLength(1);
  });

  it("survives the round trip, and stays out of the wire when there are none", () => {
    const rules = emptyRules();
    const list = emptyListRule(["lines"]);
    const entry = emptyListEntry();
    entry.fields.push(emptyFieldRule(["sku"]));
    list.fixed.push(entry);
    rules.lists.push(list, emptyListRule(["other"]));

    const wire = toWire(rules);

    expect(wire.lists[0].fixed).toHaveLength(1);
    expect(wire.lists[0].fixed![0].fields[0].target).toEqual(["sku"]);
    // A list with none carries no key at all, so what was saved equals what loads.
    expect("fixed" in wire.lists[1]).toBe(false);
    expect(toWire(fromWire(wire))).toEqual(wire);
  });
});

// ─── What a list is made of ───────────────────────────────────────────────────
//
// Records or plain values, decided by what is put into the list rather than by a
// setting sitting off to one side. These pin both halves: the click that makes it
// values, and what takes it back.

describe("a list of plain values", () => {
  const runFrom = (state: RulesEditorState, actions: RulesEditorAction[]) =>
    actions.reduce(rulesEditorReducer, state);

  const theList = (state: RulesEditorState) => state.rules.lists[0];

  const withAList = (over: string | undefined): RulesEditorState => {
    const rules = emptyRules();
    const list = emptyListRule(["codes"]);
    list.over = over;
    rules.lists.push(list);
    return loaded(rules);
  };

  it("gives a list that walks a source one value rule, and nothing written into it", () => {
    const start = withAList("order.line");
    const after = runFrom(start, [
      { type: "MAKE_LIST_OF_VALUES", listId: theList(start).id },
    ]);

    // The rule describing each walked entry, which is the row the editor draws.
    expect(theList(after).item).toBeDefined();
    expect(theList(after).fixed).toHaveLength(0);
  });

  it("gives a list that walks nothing its first slot as well", () => {
    // There is no walked entry for that rule to describe, so on its own the click
    // would set a mark nobody can see and add nothing anyone can fill in.
    const start = withAList(undefined);
    const after = runFrom(start, [
      { type: "MAKE_LIST_OF_VALUES", listId: theList(start).id },
    ]);

    expect(theList(after).fixed).toHaveLength(1);
    expect(theList(after).fixed[0].item).toBeDefined();
  });

  it("forgets that a list walking nothing held values once its last slot goes", () => {
    // Otherwise it could never be built out of records again: every entry added
    // afterwards would keep mirroring a decision with nothing left to justify it.
    const start = withAList(undefined);
    const made = runFrom(start, [
      { type: "MAKE_LIST_OF_VALUES", listId: theList(start).id },
    ]);
    const after = runFrom(made, [
      { type: "REMOVE_FIXED_ENTRY", id: theList(made).fixed[0].id },
    ]);

    expect(theList(after).item).toBeUndefined();
  });

  it("is one step to undo", () => {
    const start = withAList(undefined);
    const after = runFrom(start, [
      { type: "MAKE_LIST_OF_VALUES", listId: theList(start).id },
      { type: "UNDO" },
    ]);

    expect(theList(after).item).toBeUndefined();
    expect(theList(after).fixed).toHaveLength(0);
  });

  it("counts a value reading the whole entry as assigned", () => {
    // An empty path means the entry itself, which is the ordinary case and what the
    // scaffolder writes. Counted as blank, a correctly built list of values would
    // sit in the "not assigned yet" tally for ever with nothing to fill in.
    const rule = emptyFieldRule();
    expect(isAssigned(rule)).toBe(false);
    expect(isItemAssigned(rule)).toBe(true);

    // A fixed value is still blank until something is typed into it.
    expect(isItemAssigned({ ...rule, from: { kind: "fixed", value: "" } })).toBe(false);
    expect(isItemAssigned({ ...rule, from: { kind: "fixed", value: "WEB" } })).toBe(true);
  });

  it("does not call a value read from the document assigned", () => {
    // Switching the box to "document" and leaving the path empty is the whole
    // incoming document written into every slot of the list — a mistake, and the
    // one empty path here that nobody means.
    const rule = emptyFieldRule();
    expect(isItemAssigned({ ...rule, from: { kind: "rootPath", path: "" } })).toBe(false);
    expect(isItemAssigned({ ...rule, from: { kind: "rootPath", path: "ref" } })).toBe(true);
  });

  it("does not treat an entry written by hand as reading an entry", () => {
    // It has no entry of its own: the mapper runs its rules against whatever the
    // list reads, so an empty path there is the enclosing scope and still a blank.
    const rules = emptyRules();
    const list = emptyListRule(["codes"]);
    list.over = "order.line";
    list.item = emptyFieldRule();
    const entry = emptyListEntry();
    entry.item = emptyFieldRule();
    list.fixed.push(entry);
    rules.lists.push(list);

    const marked = everyFieldRule(rules).filter((f) => f.isItem);
    expect(marked).toHaveLength(1);
    expect(marked[0].rule).toBe(list.item);
  });

  it("does not treat the mark on a list that walks nothing as a rule with a value", () => {
    // Nothing is walked, so that rule never runs — counting it assigned would pad
    // the tally with a rule the mapper does not read.
    const rules = emptyRules();
    const list = emptyListRule(["codes"]);
    list.over = undefined;
    list.item = emptyFieldRule();
    rules.lists.push(list);

    expect(everyFieldRule(rules).filter((f) => f.isItem)).toHaveLength(0);
  });

  it("reports the value among the mapping's rules, marked as one", () => {
    const start = withAList("order.line");
    const after = runFrom(start, [
      { type: "MAKE_LIST_OF_VALUES", listId: theList(start).id },
    ]);

    const items = everyFieldRule(after.rules).filter((f) => f.isItem);
    expect(items).toHaveLength(1);
    expect(items[0].rule).toBe(theList(after).item);
  });
});

// ─── Entries written after the walked rows ────────────────────────────────────

describe("a list's closing entries", () => {
  const runFrom = (state: RulesEditorState, actions: RulesEditorAction[]) =>
    actions.reduce(rulesEditorReducer, state);

  const theList = (state: RulesEditorState) => state.rules.lists[0];

  const withAList = (): RulesEditorState => {
    const rules = emptyRules();
    rules.lists.push(emptyListRule(["rows"]));
    return run([{ type: "LOAD", rules, sourceSample: "", targetSample: "" }]);
  };

  it("go at the other end from the leading ones", () => {
    const start = withAList();
    const after = runFrom(start, [
      { type: "ADD_FIXED_ENTRY", listId: theList(start).id },
      { type: "ADD_CLOSING_ENTRY", listId: theList(start).id },
    ]);

    expect(theList(after).fixed).toHaveLength(1);
    expect(theList(after).after).toHaveLength(1);
  });

  it("are removed by the same action, wherever they sit", () => {
    const start = withAList();
    const added = runFrom(start, [{ type: "ADD_CLOSING_ENTRY", listId: theList(start).id }]);
    const after = runFrom(added, [
      { type: "REMOVE_FIXED_ENTRY", id: theList(added).after[0].id },
    ]);

    expect(theList(after).after).toHaveLength(0);
  });

  it("mirror a list of plain values, as a leading entry does", () => {
    const rules = emptyRules();
    const list = emptyListRule(["codes"]);
    list.item = emptyFieldRule();
    rules.lists.push(list);
    const start = loaded(rules);

    const after = runFrom(start, [
      { type: "ADD_CLOSING_ENTRY", listId: start.rules.lists[0].id },
    ]);

    expect(theList(after).after[0].item).toBeDefined();
  });

  it("survive being saved and loaded", () => {
    const rules = emptyRules();
    const list = emptyListRule(["rows"]);
    const entry = emptyListEntry();
    entry.fields.push({ ...emptyFieldRule(["total"]), from: { kind: "count" } });
    list.after.push(entry);
    rules.lists.push(list);

    const reloaded = fromWire(toWire(rules));

    expect(reloaded.lists[0].after).toHaveLength(1);
    expect(reloaded.lists[0].after[0].fields[0].from).toEqual({ kind: "count" });
  });

  it("are left out of a stored list that has none", () => {
    // The same reason `fixed` is: both sides default it, and writing an empty array into
    // every list stored would be noise in a file people read.
    const rules = emptyRules();
    rules.lists.push(emptyListRule(["rows"]));

    expect(toWire(rules).lists[0].after).toBeUndefined();
  });

  it("counts as assigned without anything being filled in", () => {
    // The question is the whole rule, so a count is never a row waiting for a value.
    expect(isAssigned({ ...emptyFieldRule(), from: { kind: "count" } })).toBe(true);
  });
});

describe("delimited-text options off the wire", () => {
  // Rules reach the editor from storage, so they can be hand-written or saved by an older
  // build. Neither is worth refusing a whole mapping over, and neither can be passed on as it
  // came: the editor reads the delimiter while it is drawing.

  it("fills in what an incomplete stored options object leaves out", () => {
    const loaded = fromWire({ ...emptyRules(), sourceFormat: "csv", sourceCsv: {} as never });

    expect(loaded.sourceCsv).toEqual({ delimiter: ",", hasHeader: true, byteOrderMark: false });
  });

  it("replaces a delimiter that is not usable text", () => {
    const loaded = fromWire({
      ...emptyRules(),
      sourceFormat: "csv",
      sourceCsv: { delimiter: "", hasHeader: false } as never,
    });

    expect(loaded.sourceCsv?.delimiter).toBe(",");
    expect(loaded.sourceCsv?.hasHeader).toBe(false);
  });

  it("keeps options off a format that has none", () => {
    const loaded = fromWire({ ...emptyRules(), sourceFormat: "json", sourceCsv: {} as never });

    expect(loaded.sourceCsv).toBeUndefined();
  });
});
