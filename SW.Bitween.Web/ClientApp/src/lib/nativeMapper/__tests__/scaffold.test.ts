import { describe, expect, it } from "vitest";
import { parseSample } from "../documentTree";
import {
  initialRulesEditorState,
  rulesEditorReducer,
  type RulesEditorAction,
} from "../rulesReducer";
import { scaffoldFromTarget } from "../scaffold";
import { emptyFieldRule, emptyListRule, emptyRules, type EditorRules } from "../types";

const tree = (json: string) => parseSample(json, "json").root;

/** Runs the scaffolder over two sample documents and hands back both halves. */
function build(targetJson: string, sourceJson = "", rules: EditorRules = emptyRules()) {
  const tally = scaffoldFromTarget(rules, tree(targetJson), tree(sourceJson));
  return { rules, tally };
}

/** Every field rule in the output, as "target ← source" so a test reads in one line. */
const wiring = (rules: EditorRules): string[] => {
  const out: string[] = [];
  const walk = (c: { fields: EditorRules["fields"]; lists: EditorRules["lists"] }) => {
    for (const f of c.fields) out.push(`${f.target.join(".")} ← ${f.from.path ?? ""}`);
    for (const l of c.lists) {
      out.push(`${l.target.join(".")}[] ← ${l.over}`);
      walk(l);
    }
  };
  if (rules.root) {
    out.push(`[] ← ${rules.root.over}`);
    walk(rules.root);
  } else {
    walk(rules);
  }
  return out;
};

// ─── Structure ────────────────────────────────────────────────────────────────

describe("the structure it builds", () => {
  it("makes one field rule per leaf, nesting with path segments", () => {
    const { rules } = build('{ "ref": "", "customer": { "name": "", "city": "" } }');

    expect(rules.fields.map((f) => f.target)).toEqual([
      ["ref"],
      ["customer", "name"],
      ["customer", "city"],
    ]);
  });

  /**
   * The old generator produced a flat string `lines[*].sku`, which this model cannot
   * express - a list is a list, and its fields are written against one entry.
   */
  it("makes a list for a list, with the entry's fields inside it", () => {
    const { rules } = build('{ "lines": [{ "code": "", "qty": 0 }] }');

    expect(rules.fields).toHaveLength(0);
    expect(rules.lists).toHaveLength(1);
    expect(rules.lists[0].target).toEqual(["lines"]);
    expect(rules.lists[0].fields.map((f) => f.target)).toEqual([["code"], ["qty"]]);
  });

  it("nests a list inside a list", () => {
    const { rules } = build('{ "orders": [{ "id": "", "lines": [{ "sku": "" }] }] }');

    const orders = rules.lists[0];
    expect(orders.target).toEqual(["orders"]);
    expect(orders.fields.map((f) => f.target)).toEqual([["id"]]);
    expect(orders.lists[0].target).toEqual(["lines"]);
    expect(orders.lists[0].fields.map((f) => f.target)).toEqual([["sku"]]);
  });

  it("reaches a list that sits inside an object", () => {
    const { rules } = build('{ "order": { "lines": [{ "sku": "" }] } }');

    expect(rules.lists[0].target).toEqual(["order", "lines"]);
  });

  /** A list of plain values says so with `item`; it has no fields of its own. */
  it("marks a list of plain values as producing values", () => {
    const { rules } = build('{ "codes": ["A1", "B7"] }');

    expect(rules.lists[0].item).toBeDefined();
    expect(rules.lists[0].fields).toHaveLength(0);
  });
});

// ─── Types read off the sample ────────────────────────────────────────────────

describe("the types it pins", () => {
  it("takes each field's type from the sample's value", () => {
    const { rules } = build('{ "name": "", "total": 0, "paid": false }');

    expect(rules.fields.map((f) => f.type)).toEqual(["string", "number", "boolean"]);
  });

  /**
   * A null says nothing about the type. It is also the reason pinning types here is
   * safe: the mapper skips a cast when the value is missing, so an optional field
   * that never arrives cannot fail on its type.
   */
  it("pins nothing for a null", () => {
    const { rules } = build('{ "note": null }');

    expect(rules.fields[0].type).toBeUndefined();
  });
});

// ─── Matching to the source ───────────────────────────────────────────────────

describe("matching source fields", () => {
  it("matches an identical path", () => {
    const { rules, tally } = build('{ "order": { "ref": "" } }', '{ "order": { "ref": "A1" } }');

    expect(wiring(rules)).toEqual(["order.ref ← order.ref"]);
    expect(tally.matched).toBe(1);
  });

  it("matches across a difference in case and separators", () => {
    const { rules } = build('{ "customer_name": "" }', '{ "customerName": "Ali" }');

    expect(wiring(rules)).toEqual(["customer_name ← customerName"]);
  });

  it("matches on the field's own name when the path around it differs", () => {
    const { rules } = build('{ "ref": "" }', '{ "order": { "deep": { "ref": "A1" } } }');

    expect(wiring(rules)).toEqual(["ref ← order.deep.ref"]);
  });

  /**
   * The important negative. Two source fields both reduce to `id`, so `id` names
   * neither of them - and a coin flip between the two would be wrong half the time.
   * An empty field the editor counts is worth more than a mapping that looks done.
   */
  it("leaves a field empty when two source fields answer to the same name", () => {
    const { rules, tally } = build(
      '{ "id": "" }',
      '{ "order": { "id": "1" }, "customer": { "id": "2" } }',
    );

    expect(wiring(rules)).toEqual(["id ← "]);
    expect(tally.created).toBe(1);
    expect(tally.matched).toBe(0);
  });

  it("prefers the exact path over a name that also matches elsewhere", () => {
    const { rules } = build(
      '{ "ref": "" }',
      '{ "ref": "right", "nested": { "ref": "wrong" } }',
    );

    expect(wiring(rules)).toEqual(["ref ← ref"]);
  });

  it("points a list at the source list of the same name", () => {
    const { rules } = build(
      '{ "lines": [{ "code": "" }] }',
      '{ "order": { "lines": [{ "code": "A1" }] } }',
    );

    expect(rules.lists[0].over).toBe("order.lines");
  });

  /**
   * Where "the same name" stops. `lines` and `line` are not the same name, and
   * nothing here stems words: a rule that made them equal would also have to rule
   * on `status`/`statuses` and `company`/`companies`, where stripping an "s" is
   * simply wrong. The list is still created - only its source is left to pick.
   */
  it("leaves the source list empty for a name that is merely close", () => {
    const { rules } = build(
      '{ "lines": [{ "code": "" }] }',
      '{ "line": [{ "code": "A1" }] }',
    );

    expect(rules.lists[0].target).toEqual(["lines"]);
    expect(rules.lists[0].over).toBe("");
  });

  /**
   * A rule inside a list is written against one entry, so `code` there must find the
   * entry's `code` - not a `code` sitting at the top of the document.
   */
  it("matches a list's fields against one entry, not the whole document", () => {
    const { rules } = build(
      '{ "lines": [{ "sku": "" }] }',
      '{ "sku": "top level", "lines": [{ "sku": "per entry" }] }',
    );

    expect(wiring(rules)).toEqual(["lines[] ← lines", "sku ← sku"]);
    // Named relative to the entry: an absolute `lines.sku` is not a path this model has.
    expect(rules.lists[0].fields[0].from.path).toBe("sku");
  });

  /**
   * The scope narrows on the way down. `MapInto` hands a list's nested lists the current
   * item, so a nested list names its list relative to the entry around it - `tags`,
   * never `order.line.tags`, which resolves to nothing.
   */
  it("names a nested list's list relative to the entry around it", () => {
    const { rules } = build(
      '{ "line": [{ "sku": "", "tags": [{ "code": "" }] }] }',
      '{ "order": { "line": [{ "sku": "A1", "tags": [{ "code": "fragile" }] }] } }',
    );

    const line = rules.lists[0];
    expect(line.over).toBe("order.line");
    expect(line.lists[0].target).toEqual(["tags"]);
    expect(line.lists[0].over).toBe("tags");
    expect(line.lists[0].fields[0].from.path).toBe("code");
  });

  /**
   * The other half of the same rule. A `tags` at the top of the document is not the
   * entry's `tags`, and matching it would walk the wrong list.
   */
  it("does not reach out to a list of the same name at the top of the document", () => {
    const { rules } = build(
      '{ "line": [{ "tags": [{ "code": "" }] }] }',
      '{ "tags": [{ "code": "wrong" }], "order": { "line": [{ "sku": "A1" }] } }',
    );

    expect(rules.lists[0].over).toBe("order.line");
    expect(rules.lists[0].lists[0].over).toBe("");
  });

  it("keeps narrowing at three levels deep", () => {
    const { rules } = build(
      '{ "orders": [{ "lines": [{ "parts": [{ "code": "" }] }] }] }',
      '{ "orders": [{ "lines": [{ "parts": [{ "code": "X" }] }] }] }',
    );

    const parts = rules.lists[0].lists[0].lists[0];
    expect(parts.over).toBe("parts");
    expect(parts.fields[0].from.path).toBe("code");
  });

  it("builds the structure and matches nothing when there is no source sample", () => {
    const { rules, tally } = build('{ "a": "", "b": 0 }');

    expect(wiring(rules)).toEqual(["a ← ", "b ← "]);
    expect(tally.created).toBe(2);
    expect(tally.matched).toBe(0);
  });
});

// ─── Only ever adding ─────────────────────────────────────────────────────────

describe("what it does to rules that are already there", () => {
  it("leaves an existing rule alone and adds only the missing one", () => {
    const rules = emptyRules();
    const mine = emptyFieldRule(["ref"]);
    mine.from = { kind: "fixed", value: "PINNED" };
    rules.fields.push(mine);

    const { tally } = build('{ "ref": "", "city": "" }', '{ "ref": "A1", "city": "Amman" }', rules);

    expect(rules.fields).toHaveLength(2);
    expect(rules.fields[0].from).toEqual({ kind: "fixed", value: "PINNED" });
    expect(wiring(rules)[1]).toBe("city ← city");
    expect(tally.created).toBe(1);
  });

  it("adds into a list that already exists rather than making a second one", () => {
    const rules = emptyRules();
    const list = emptyListRule(["lines"]);
    list.over = "line";
    list.fields.push(emptyFieldRule(["code"]));
    rules.lists.push(list);

    const { tally } = build(
      '{ "lines": [{ "code": "", "qty": 0 }] }',
      '{ "line": [{ "code": "A1", "qty": 2 }] }',
      rules,
    );

    expect(rules.lists).toHaveLength(1);
    expect(rules.lists[0].fields.map((f) => f.target)).toEqual([["code"], ["qty"]]);
    expect(tally.created).toBe(1);
  });

  it("reports that there was nothing to add when the sample is already covered", () => {
    const rules = emptyRules();
    rules.fields.push(emptyFieldRule(["ref"]));

    const { tally } = build('{ "ref": "" }', "", rules);

    expect(tally.created).toBe(0);
    expect(tally.problem).toBeNull();
  });

  it("does not add fields to a list that produces plain values", () => {
    const rules = emptyRules();
    const list = emptyListRule(["codes"]);
    list.item = emptyFieldRule();
    rules.lists.push(list);

    build('{ "codes": [{ "sku": "" }] }', "", rules);

    expect(rules.lists[0].fields).toHaveLength(0);
  });
});

// ─── The root ─────────────────────────────────────────────────────────────────

describe("a document that is a list", () => {
  it("turns an empty mapping into a root list", () => {
    const { rules } = build('[{ "code": "" }]', '{ "lines": [{ "code": "A1" }] }');

    expect(rules.root).toBeDefined();
    expect(rules.root!.over).toBe("lines");
    expect(rules.root!.fields.map((f) => f.target)).toEqual([["code"]]);
  });

  it("walks the source document itself when the source is a list too", () => {
    const { rules } = build('[{ "code": "" }]', '[{ "code": "A1" }]');

    expect(rules.root!.over).toBe("");
    expect(wiring(rules)).toEqual(["[] ← ", "code ← code"]);
  });

  it("adds into a root list that is already there", () => {
    const rules = emptyRules();
    rules.root = emptyListRule();
    rules.root.over = "";

    const { tally } = build('[{ "code": "", "qty": 0 }]', '[{ "code": "A1", "qty": 2 }]', rules);

    expect(rules.root!.fields.map((f) => f.target)).toEqual([["code"], ["qty"]]);
    expect(tally.created).toBe(2);
    expect(tally.matched).toBe(2);
  });

  /**
   * Switching a mapping between an object and a list moves every rule in it, so the
   * sample is not allowed to do it quietly on top of work already done.
   */
  it("refuses to restructure a mapping that already has rules, and changes nothing", () => {
    const rules = emptyRules();
    rules.fields.push(emptyFieldRule(["ref"]));

    const { tally } = build('[{ "code": "" }]', "", rules);

    expect(tally.problem).toMatch(/whole output is a list/);
    expect(tally.created).toBe(0);
    expect(rules.root).toBeUndefined();
    expect(rules.fields).toHaveLength(1);
  });

  it("refuses an object sample when the mapping builds a list", () => {
    const rules = emptyRules();
    rules.root = emptyListRule();

    const { tally } = build('{ "ref": "" }', "", rules);

    expect(tally.problem).toMatch(/builds a list/);
    expect(rules.root.fields).toHaveLength(0);
  });
});

// ─── Samples that ask for nothing ─────────────────────────────────────────────

describe("samples it cannot build from", () => {
  it("asks for a sample when there is none", () => {
    const rules = emptyRules();
    const tally = scaffoldFromTarget(rules, null, null);

    expect(tally.problem).toMatch(/Paste a sample/);
    expect(tally.created).toBe(0);
  });

  it("says so when the sample is a single value", () => {
    const { tally } = build('"just a string"');

    expect(tally.problem).toMatch(/single value/);
  });

  it("builds nothing from an empty object", () => {
    const { tally } = build("{}");

    expect(tally.created).toBe(0);
    expect(tally.problem).toBeNull();
  });
});

// ─── Through the reducer ──────────────────────────────────────────────────────

describe("the editor action", () => {
  const run = (actions: RulesEditorAction[]) =>
    actions.reduce(rulesEditorReducer, initialRulesEditorState);

  const pasted = (target: string, source = ""): RulesEditorAction[] => [
    { type: "SET_SOURCE_SAMPLE", text: source },
    { type: "SET_TARGET_SAMPLE", text: target },
    { type: "SCAFFOLD_FROM_TARGET" },
  ];

  it("builds the rules from the sample already in the editor", () => {
    const state = run(pasted('{ "ref": "" }', '{ "ref": "A1" }'));

    expect(state.rules.fields).toHaveLength(1);
    expect(state.scaffold).toEqual({ created: 1, matched: 1, problem: null });
    expect(state.dirty).toBe(true);
  });

  /** A wrong press has to be one keystroke away from undone, or it is not safe. */
  it("is undone in one step", () => {
    const state = run([...pasted('{ "a": "", "b": "", "c": "" }'), { type: "UNDO" }]);

    expect(state.rules.fields).toHaveLength(0);
  });

  it("forgets the count once the sample changes, because it described the old one", () => {
    const state = run([...pasted('{ "ref": "" }'), { type: "SET_TARGET_SAMPLE", text: "{}" }]);

    expect(state.scaffold).toBeNull();
  });

  it("reports a sample it cannot read and builds nothing", () => {
    const state = run(pasted("{ not json"));

    expect(state.scaffold?.problem).toMatch(/not valid JSON/);
    expect(state.rules.fields).toHaveLength(0);
  });
});
