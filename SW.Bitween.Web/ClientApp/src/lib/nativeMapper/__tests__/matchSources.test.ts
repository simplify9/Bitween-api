import { describe, expect, it } from "vitest";
import { parseSample } from "../documentTree";
import {
  initialRulesEditorState,
  rulesEditorReducer,
  type RulesEditorAction,
} from "../rulesReducer";
import { matchSources } from "../scaffold";
import { fromWire, toWire } from "../serialize";
import {
  emptyFieldRule,
  emptyListEntry,
  emptyListRule,
  emptyRules,
  type EditorRules,
} from "../types";

const tree = (json: string) => parseSample(json, "json").root;

/** A field rule with a target and, optionally, a source already chosen. */
function field(target: string, path?: string) {
  const rule = emptyFieldRule(target.split("."));
  if (path !== undefined) rule.from = { kind: "path", path };
  return rule;
}

/** Every field rule as "target ← source", so a test reads in one line. */
function wiring(rules: EditorRules): string[] {
  const out: string[] = [];
  const walk = (c: { fields: EditorRules["fields"]; lists: EditorRules["lists"] }) => {
    for (const f of c.fields) out.push(`${f.target.join(".")} ← ${f.from.path ?? ""}`);
    for (const l of c.lists) {
      for (const e of l.fixed) walk(e);
      walk(l);
    }
  };
  if (rules.root) walk(rules.root);
  else walk(rules);
  return out;
}

const SOURCE = JSON.stringify({
  order: {
    customer: "Ali",
    net: 100,
    line: [{ sku: "A1", qty: 2 }],
  },
});

describe("matchSources", () => {
  it("points an empty rule at the field of the same name", () => {
    const rules = emptyRules();
    rules.fields.push(field("customer"), field("net"));

    const tally = matchSources(rules, tree(SOURCE));

    expect(wiring(rules)).toEqual(["customer ← order.customer", "net ← order.net"]);
    expect(tally).toEqual({ matched: 2, unmatched: 0 });
  });

  it("leaves a rule that already reads something alone", () => {
    const rules = emptyRules();
    rules.fields.push(field("customer", "order.net"));

    const tally = matchSources(rules, tree(SOURCE));

    // Deliberately crossed over. Overwriting it would undo a decision someone made,
    // which is worse than leaving a rule this button did not help with.
    expect(wiring(rules)).toEqual(["customer ← order.net"]);
    expect(tally).toEqual({ matched: 0, unmatched: 0 });
  });

  it("leaves a literal, a partner key and a values-set key alone", () => {
    const rules = emptyRules();
    const fixed = emptyFieldRule(["channel"]);
    fixed.from = { kind: "fixed", value: "WEB" };
    const partner = emptyFieldRule(["customer"]);
    partner.from = { kind: "partner", key: "customer" };
    const global = emptyFieldRule(["net"]);
    global.from = { kind: "global", setId: "s", key: "net" };
    rules.fields.push(fixed, partner, global);

    const tally = matchSources(rules, tree(SOURCE));

    // Each is an answer already given, even where a source field has the same name.
    expect(tally).toEqual({ matched: 0, unmatched: 0 });
    expect(rules.fields.map((f) => f.from.kind)).toEqual(["fixed", "partner", "global"]);
  });

  it("leaves a read of the whole document alone, inside a list", () => {
    const rules = emptyRules();
    const list = emptyListRule(["lines"]);
    list.over = "order.line";
    const fromDocument = emptyFieldRule(["sku"]);
    fromDocument.from = { kind: "rootPath", path: "" };
    list.fields.push(fromDocument);
    rules.lists.push(list);

    const tally = matchSources(rules, tree(SOURCE));

    // An empty document-scoped read was asked for on purpose; filling it in with the
    // entry's own `sku` would change which document the value comes from.
    expect(rules.lists[0].fields[0].from).toEqual({ kind: "rootPath", path: "" });
    expect(tally).toEqual({ matched: 0, unmatched: 0 });
  });

  it("matches a rule inside a list against one entry, not the document", () => {
    const rules = emptyRules();
    const list = emptyListRule(["lines"]);
    list.over = "order.line";
    list.fields.push(field("sku"), field("qty"));
    rules.lists.push(list);

    const tally = matchSources(rules, tree(SOURCE));

    // `sku`, not `order.line.sku`: a rule in a list reads its own entry.
    expect(wiring(rules)).toEqual(["sku ← sku", "qty ← qty"]);
    expect(tally.matched).toBe(2);
  });

  it("matches a written entry's rules against what the list reads", () => {
    const rules = emptyRules();
    const list = emptyListRule(["lines"]);
    list.over = "order.line";
    const entry = emptyListEntry();
    entry.fields.push(field("customer"));
    list.fixed.push(entry);
    rules.lists.push(list);

    matchSources(rules, tree(SOURCE));

    // A written entry is not produced by walking anything, so its rules read from
    // where the list sits - the document - exactly as the mapper runs them.
    expect(rules.lists[0].fixed[0].fields[0].from).toEqual({
      kind: "path",
      path: "order.customer",
    });
  });

  it("matches the written entries of a root list", () => {
    const rules = emptyRules();
    const root = emptyListRule([]);
    root.over = "order.line";
    const entry = emptyListEntry();
    entry.fields.push(field("customer"));
    root.fixed.push(entry);
    rules.root = root;

    matchSources(rules, tree(SOURCE));

    // The root list is not inside any container, so the walk that reaches a nested
    // list's written entries never reached these - they were skipped in silence.
    expect(rules.root!.fixed[0].fields[0].from).toEqual({
      kind: "path",
      path: "order.customer",
    });
  });

  it("counts a rule it could not place rather than guessing", () => {
    const rules = emptyRules();
    rules.fields.push(field("somethingElse"));

    const tally = matchSources(rules, tree(SOURCE));

    expect(wiring(rules)).toEqual(["somethingElse ← "]);
    expect(tally).toEqual({ matched: 0, unmatched: 1 });
  });

  it("refuses a name that two source fields fit equally well", () => {
    const rules = emptyRules();
    rules.fields.push(field("id"));

    const tally = matchSources(
      rules,
      tree(JSON.stringify({ order: { id: 1 }, customer: { id: 2 } })),
    );

    // Guessing between them would be wrong half the time, and a filled-in wrong
    // answer is harder to notice than an empty one.
    expect(tally).toEqual({ matched: 0, unmatched: 1 });
  });

  it("matches a differently spelled name", () => {
    const rules = emptyRules();
    rules.fields.push(field("customerName"));

    matchSources(rules, tree(JSON.stringify({ customer_name: "Ali" })));

    expect(wiring(rules)).toEqual(["customerName ← customer_name"]);
  });

  it("does nothing at all without a sample", () => {
    const rules = emptyRules();
    rules.fields.push(field("customer"));

    const tally = matchSources(rules, null);

    expect(wiring(rules)).toEqual(["customer ← "]);
    expect(tally).toEqual({ matched: 0, unmatched: 1 });
  });
});

// ─── Through the reducer ──────────────────────────────────────────────────────

function run(actions: RulesEditorAction[], from = initialRulesEditorState) {
  return actions.reduce(rulesEditorReducer, from);
}

describe("the toolbar actions", () => {
  it("matches the source fields of the rules already there", () => {
    const state = run([
      { type: "SET_SOURCE_SAMPLE", text: SOURCE },
      { type: "ADD_FIELD", listId: null, target: ["customer"] },
      { type: "MATCH_SOURCES" },
    ]);

    expect(state.rules.fields[0].from).toEqual({ kind: "path", path: "order.customer" });
    expect(state.match).toEqual({ matched: 1, unmatched: 0 });
  });

  it("undoes a match in one step", () => {
    const matched = run([
      { type: "SET_SOURCE_SAMPLE", text: SOURCE },
      { type: "ADD_FIELD", listId: null, target: ["customer"] },
      { type: "ADD_FIELD", listId: null, target: ["net"] },
      { type: "MATCH_SOURCES" },
    ]);
    expect(matched.rules.fields.every((f) => f.from.path)).toBe(true);

    // One press, one undo - not one per rule it filled in.
    const undone = rulesEditorReducer(matched, { type: "UNDO" });
    expect(undone.rules.fields.every((f) => f.from.path === "")).toBe(true);
  });

  it("clears every rule, and undo puts them all back", () => {
    const built = run([
      { type: "SET_SOURCE_SAMPLE", text: SOURCE },
      { type: "ADD_FIELD", listId: null, target: ["customer"] },
      { type: "ADD_LIST", parentListId: null, target: ["lines"] },
    ]);

    const cleared = rulesEditorReducer(built, { type: "CLEAR_RULES" });
    expect(cleared.rules.fields).toEqual([]);
    expect(cleared.rules.lists).toEqual([]);
    expect(cleared.selectedId).toBeNull();
    // The sample is not a rule, so clearing the rules does not throw it away.
    expect(cleared.sourceSample).toBe(SOURCE);

    const undone = rulesEditorReducer(cleared, { type: "UNDO" });
    expect(undone.rules.fields).toHaveLength(1);
    expect(undone.rules.lists).toHaveLength(1);
  });

  it("clearing keeps the formats, which describe the exchange and not the rules", () => {
    const cleared = run([{ type: "CLEAR_RULES" }]);
    expect(cleared.rules.sourceFormat).toBe("json");
    expect(cleared.rules.targetFormat).toBe("json");
  });

  it("clears a list-shaped output too", () => {
    const built = run([
      { type: "SET_ROOT_LIST", enabled: true },
      { type: "ADD_FIELD", listId: null, target: ["code"] },
    ]);
    expect(built.rules.root).toBeDefined();

    expect(rulesEditorReducer(built, { type: "CLEAR_RULES" }).rules.root).toBeUndefined();
  });

  it("remembers which partner the preview runs as, without touching the mapping", () => {
    const before = run([
      { type: "ADD_FIELD", listId: null, target: ["customer"] },
      { type: "SAVED" },
    ]);
    const state = rulesEditorReducer(before, { type: "SET_TEST_PARTNER", partnerId: 7 });

    expect(state.testPartnerId).toBe(7);
    // Not a change to the mapping: it is a question about this preview, so it leaves
    // the editor clean and puts nothing on the undo stack.
    expect(state.dirty).toBe(false);
    expect(state.past).toHaveLength(before.past.length);
    expect(state.rules).toBe(before.rules);
  });

  it("forgets the previewed partner when a different mapping is loaded", () => {
    const state = run([
      { type: "SET_TEST_PARTNER", partnerId: 7 },
      {
        type: "LOAD",
        rules: emptyRules(),
        sourceSample: "",
        targetSample: "",
      },
    ]);

    expect(state.testPartnerId).toBeNull();
  });
});

describe("a transform argument that is only punctuation and spaces", () => {
  it("keeps a leading space through the editor and onto the wire", () => {
    const rules = emptyRules();
    const rule = emptyFieldRule(["label"]);
    rule.from = { kind: "path", path: "order.ref" };
    // The whole point of `concat` is usually the separator, and a leading space is
    // invisible in the box - so if anything trimmed it, it would look like the
    // transform had simply ignored the argument.
    rule.transform = { fn: "concat", with: " (EDI)" };
    rules.fields.push(rule);

    const wire = JSON.parse(JSON.stringify(toWire(rules)));
    expect(wire.fields[0].transform).toEqual({ fn: "concat", with: " (EDI)" });
    expect(fromWire(wire).fields[0].transform?.with).toBe(" (EDI)");
  });

  it("keeps one through the reducer", () => {
    const state = run([
      { type: "ADD_FIELD", listId: null, target: ["label"] },
    ]);
    const id = state.rules.fields[0].id;
    const next = rulesEditorReducer(state, {
      type: "UPDATE_FIELD",
      id,
      changes: { transform: { fn: "concat", with: " (EDI)" } },
    });

    expect(next.rules.fields[0].transform?.with).toBe(" (EDI)");
  });
});

describe("how the incoming document writes its dates", () => {
  it("is part of the mapping, and survives the wire", () => {
    const state = run([{ type: "SET_DATE_ORDER", order: "dayFirst" }]);

    expect(state.rules.sourceDateOrder).toBe("dayFirst");
    // A fact about the document, so it changes the mapping and can be undone.
    expect(state.dirty).toBe(true);

    const wire = JSON.parse(JSON.stringify(toWire(state.rules)));
    expect(wire.sourceDateOrder).toBe("dayFirst");
    expect(fromWire(wire).sourceDateOrder).toBe("dayFirst");
  });

  it("reads back as year-first when a stored mapping never said", () => {
    // Every mapping saved before this existed could only have read year-first dates
    // correctly anyway, so that is what absent has to mean.
    expect(fromWire({ version: 1, sourceFormat: "json", targetFormat: "json", fields: [], lists: [] })
      .sourceDateOrder).toBe("yearFirst");
  });

  it("undoes in one step", () => {
    const set = run([{ type: "SET_DATE_ORDER", order: "monthFirst" }]);
    expect(rulesEditorReducer(set, { type: "UNDO" }).rules.sourceDateOrder).toBe("yearFirst");
  });
});
