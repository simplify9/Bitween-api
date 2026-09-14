import { describe, expect, it } from "vitest";
import { describeTarget, filterTree, outputTreeOf, type OutputNode } from "../outputTree";
import {
  emptyFieldRule,
  emptyListEntry,
  emptyListRule,
  emptyRules,
  type EditorRules,
} from "../types";

/** The tree as indented lines, so a test reads the way the panel looks. */
function drawn(rules: EditorRules): string[] {
  const out: string[] = [];
  const walk = (nodes: OutputNode[], depth: number) => {
    for (const node of nodes) {
      const pad = "  ".repeat(depth);
      if (node.kind === "field") out.push(`${pad}${node.name || "(unnamed)"}`);
      if (node.kind === "branch") {
        out.push(`${pad}${node.name} {}`);
        walk(node.children, depth + 1);
      }
      if (node.kind === "list") {
        out.push(`${pad}${node.name || "(root)"} []`);
        walk(node.children, depth + 1);
      }
    }
  };
  walk(outputTreeOf(rules), 0);
  return out;
}

const withFields = (...targets: string[][]): EditorRules => {
  const rules = emptyRules();
  for (const t of targets) rules.fields.push(emptyFieldRule(t));
  return rules;
};

// ─── Grouping ─────────────────────────────────────────────────────────────────

describe("folding paths into branches", () => {
  it("leaves single-segment targets flat", () => {
    expect(drawn(withFields(["a"], ["b"]))).toEqual(["a", "b"]);
  });

  it("nests a multi-segment target under a branch", () => {
    expect(drawn(withFields(["billing", "city"], ["billing", "postCode"]))).toEqual([
      "billing {}",
      "  city",
      "  postCode",
    ]);
  });

  it("nests as deep as the target goes", () => {
    expect(drawn(withFields(["a", "b", "c"]))).toEqual(["a {}", "  b {}", "    c"]);
  });

  it("shows a rule with no name yet, so a new row is not invisible", () => {
    expect(drawn(withFields([]))).toEqual(["(unnamed)"]);
  });

  it("makes no branch that has nothing under it", () => {
    const drawnTree = drawn(withFields(["a"], ["b", "c"]));

    expect(drawnTree.filter((line) => line.includes("{}"))).toEqual(["b {}"]);
  });
});

// ─── Order ────────────────────────────────────────────────────────────────────

describe("order", () => {
  /**
   * OutputOrder_FollowsTheRules pins the document's key order to the rule order,
   * and XML sequences will depend on it. So the tree shows that order, never a
   * tidier one of its own.
   */
  it("keeps rule order rather than sorting names", () => {
    expect(drawn(withFields(["zebra"], ["apple"], ["mango"]))).toEqual([
      "zebra",
      "apple",
      "mango",
    ]);
  });

  it("puts a branch where its first member is, not at the end", () => {
    expect(drawn(withFields(["billing", "city"], ["ref"], ["billing", "country"]))).toEqual([
      "billing {}",
      "  city",
      "  country",
      "ref",
    ]);
  });

  /** MapInto writes every field, then every list — so that is what is shown. */
  it("shows fields before lists, because that is the order they are written in", () => {
    const rules = emptyRules();
    rules.lists.push(emptyListRule(["lines"]));
    rules.fields.push(emptyFieldRule(["ref"]));

    expect(drawn(rules)).toEqual(["ref", "lines []"]);
  });

  it("lets a field and a list share one branch", () => {
    const rules = emptyRules();
    rules.fields.push(emptyFieldRule(["order", "ref"]));
    rules.lists.push(emptyListRule(["order", "lines"]));

    expect(drawn(rules)).toEqual(["order {}", "  ref", "  lines []"]);
  });
});

// ─── Lists ────────────────────────────────────────────────────────────────────

describe("lists", () => {
  it("hangs a list's own rules under it", () => {
    const rules = emptyRules();
    const list = emptyListRule(["lines"]);
    list.fields.push(emptyFieldRule(["sku"]), emptyFieldRule(["price", "net"]));
    rules.lists.push(list);

    expect(drawn(rules)).toEqual(["lines []", "  sku", "  price {}", "    net"]);
  });

  it("nests a list inside a list", () => {
    const rules = emptyRules();
    const outer = emptyListRule(["lines"]);
    const inner = emptyListRule(["tags"]);
    inner.fields.push(emptyFieldRule(["code"]));
    outer.lists.push(inner);
    rules.lists.push(outer);

    expect(drawn(rules)).toEqual(["lines []", "  tags []", "    code"]);
  });

  it("shows a list of plain values as its one value row, not its fields", () => {
    const rules = emptyRules();
    const list = emptyListRule(["codes"]);
    list.item = emptyFieldRule();
    // Fields left over from before it became a list of values. The mapper ignores
    // them, so drawing them would offer rules that do nothing.
    list.fields.push(emptyFieldRule(["ignored"]));
    rules.lists.push(list);

    const [node] = outputTreeOf(rules);
    expect(node.kind).toBe("list");
    const children = node.kind === "list" ? node.children : [];
    expect(children.map((c) => c.kind)).toEqual(["item"]);
    expect(drawn(rules)).toEqual(["codes []"]);
  });

  it("keeps a list of plain values whole when its name is searched for", () => {
    const rules = emptyRules();
    const list = emptyListRule(["codes"]);
    list.item = emptyFieldRule();
    rules.lists.push(list);

    // The value row has no name of its own, so it can only be reached through its
    // list — and a search that finds the list has to keep what is in it.
    const [found] = filterTree(outputTreeOf(rules), "codes");
    expect(found.kind === "list" && found.children).toHaveLength(1);

    expect(filterTree(outputTreeOf(rules), "nothing")).toHaveLength(0);
  });

  it("puts the whole output under one root list", () => {
    const rules = emptyRules();
    rules.root = emptyListRule();
    rules.root.fields.push(emptyFieldRule(["code"]));

    expect(drawn(rules)).toEqual(["(root) []", "  code"]);
  });
});

// ─── Error keys ───────────────────────────────────────────────────────────────

describe("the keys rule errors arrive under", () => {
  const keysOf = (rules: EditorRules): string[] => {
    const out: string[] = [];
    const walk = (nodes: OutputNode[]) => {
      for (const n of nodes) {
        // A field and a list's value are both leaves; only the value shares its key
        // with something else, because the mapper reports it under the list.
        if (n.kind === "field" || n.kind === "item") out.push(n.errorKey);
        else if (n.kind === "list") {
          out.push(n.errorKey);
          walk(n.children);
        } else walk(n.children);
      }
    };
    walk(outputTreeOf(rules));
    return out;
  };

  it("names a list of plain values twice, because both report under the list", () => {
    // The list's own troubles — no target, a filter that will not read — and the
    // value's arrive under one key, so the editor lights up both rows. Worth pinning:
    // it is why a failing value shows a reason at all, having no key of its own.
    const rules = emptyRules();
    const list = emptyListRule(["codes"]);
    list.item = emptyFieldRule();
    rules.lists.push(list);

    expect(keysOf(rules)).toEqual(["codes", "codes"]);
  });

  it("names a top-level rule by its dotted target", () => {
    expect(keysOf(withFields(["ref"], ["billing", "city"]))).toEqual(["ref", "billing.city"]);
  });

  it("names a rule inside a list after the list", () => {
    const rules = emptyRules();
    const list = emptyListRule(["lines"]);
    list.fields.push(emptyFieldRule(["sku"]));
    rules.lists.push(list);

    expect(keysOf(rules)).toEqual(["lines", "lines[].sku"]);
  });

  it("chains through a nested list", () => {
    const rules = emptyRules();
    const outer = emptyListRule(["lines"]);
    const inner = emptyListRule(["tags"]);
    inner.fields.push(emptyFieldRule(["code"]));
    outer.lists.push(inner);
    rules.lists.push(outer);

    expect(keysOf(rules)).toEqual(["lines", "lines[].tags", "lines[].tags[].code"]);
  });

  /**
   * The case the old per-component version got wrong. A root list has no target,
   * so the mapper describes it as "(no target)" and its fields come back as
   * "(no target)[].qty" — measured against the preview endpoint. Looking those up
   * as a bare "qty" meant a broken rule inside a root list never lit up.
   */
  it("names a rule inside a root list the way the mapper reports it", () => {
    const rules = emptyRules();
    rules.root = emptyListRule();
    rules.root.fields.push(emptyFieldRule(["qty"]));

    expect(keysOf(rules)).toEqual(["(no target)", "(no target)[].qty"]);
  });

  it("mirrors Describe for a rule with no target at all", () => {
    expect(describeTarget("", [])).toBe("(no target)");
    expect(describeTarget("lines", [])).toBe("lines[].(no target)");
    expect(describeTarget("", ["a", "b"])).toBe("a.b");
  });
});

// ─── Search ───────────────────────────────────────────────────────────────────

describe("filtering", () => {
  const shown = (rules: EditorRules, search: string): string[] => {
    const out: string[] = [];
    const walk = (nodes: OutputNode[], depth: number) => {
      for (const node of nodes) {
        const pad = "  ".repeat(depth);

        // Two of the five kinds have no name to print. A list's value has nothing
        // under it either; an entry is numbered and holds rules of its own.
        if (node.kind === "item") {
          out.push(`${pad}(value)`);
          continue;
        }
        if (node.kind === "entry") {
          out.push(`${pad}(entry ${node.position})`);
          walk(node.children, depth + 1);
          continue;
        }

        out.push(pad + (node.name || "(root)"));
        if (node.kind !== "field") walk(node.children, depth + 1);
      }
    };
    walk(filterTree(outputTreeOf(rules), search), 0);
    return out;
  };

  const nested = () => {
    const rules = withFields(["ref"], ["billing", "city"], ["billing", "country"]);
    const list = emptyListRule(["lines"]);
    list.fields.push(emptyFieldRule(["sku"]));
    rules.lists.push(list);
    return rules;
  };

  it("returns everything for an empty search", () => {
    expect(shown(nested(), "  ")).toEqual(shown(nested(), ""));
  });

  it("keeps the branch above a match so the hit is reachable", () => {
    expect(shown(nested(), "city")).toEqual(["billing", "  city"]);
  });

  it("keeps everything inside a branch whose own name matches", () => {
    expect(shown(nested(), "billing")).toEqual(["billing", "  city", "  country"]);
  });

  it("reaches inside a list", () => {
    expect(shown(nested(), "sku")).toEqual(["lines", "  sku"]);
  });

  it("drops a branch with nothing matching under it", () => {
    expect(shown(nested(), "ref")).toEqual(["ref"]);
  });

  it("matches on the whole path, not just the last segment", () => {
    expect(shown(nested(), "billing.co")).toEqual(["billing", "  country"]);
  });
});

// ─── The root list ────────────────────────────────────────────────────────────

describe("telling the root list from an unnamed one", () => {
  const listNodes = (rules: EditorRules) =>
    outputTreeOf(rules).filter((n): n is Extract<OutputNode, { kind: "list" }> => n.kind === "list");

  it("marks the mapping's root list as the root", () => {
    const rules = emptyRules();
    rules.root = emptyListRule();

    expect(listNodes(rules)[0].isRoot).toBe(true);
  });

  /**
   * A list the user has only just added has no name yet either. Inferring "root"
   * from an empty target called it the root and left it with no name box, so a new
   * list could not be named at all.
   */
  it("does not mark a newly added, still unnamed list as the root", () => {
    const rules = emptyRules();
    rules.lists.push(emptyListRule());

    expect(listNodes(rules)[0].isRoot).toBe(false);
  });

  it("does not mark a named list as the root", () => {
    const rules = emptyRules();
    rules.lists.push(emptyListRule(["lines"]));

    expect(listNodes(rules)[0].isRoot).toBe(false);
  });
});

// ─── Entries written into a list ──────────────────────────────────────────────

describe("fixed entries", () => {
  const listWithEntries = (): EditorRules => {
    const rules = emptyRules();
    const list = emptyListRule(["lines"]);
    list.fields.push(emptyFieldRule(["sku"]));
    const header = emptyListEntry();
    header.fields.push(emptyFieldRule(["sku"]));
    list.fixed.push(header);
    rules.lists.push(list);
    return rules;
  };

  const listNode = (rules: EditorRules) =>
    outputTreeOf(rules).find((n): n is Extract<OutputNode, { kind: "list" }> => n.kind === "list")!;

  it("hangs them off the list, apart from what each source entry produces", () => {
    const node = listNode(listWithEntries());

    expect(node.fixed).toHaveLength(1);
    expect(node.fixed[0].position).toBe(1);
    expect(node.fixed[0].children.map((c) => c.kind === "field" && c.name)).toEqual(["sku"]);
    // The per-entry rules are unaffected by there being a fixed entry.
    expect(node.children.map((c) => c.kind === "field" && c.name)).toEqual(["sku"]);
  });

  /**
   * The mapper hands a fixed entry's rules the same target as a walked entry's, so
   * a failure in either lights up the same row key.
   */
  it("reports its rules under the list, as a walked entry's are", () => {
    const node = listNode(listWithEntries());

    expect(node.fixed[0].errorKey).toBe("lines");
    const field = node.fixed[0].children[0];
    expect(field.kind === "field" && field.errorKey).toBe("lines[].sku");
  });

  it("gives an entry that is a single value no children", () => {
    const rules = emptyRules();
    const list = emptyListRule(["codes"]);
    const entry = emptyListEntry();
    entry.item = emptyFieldRule();
    entry.fields.push(emptyFieldRule(["ignored"]));
    list.fixed.push(entry);
    rules.lists.push(list);

    expect(listNode(rules).fixed[0].children).toHaveLength(0);
  });

  it("counts them from one, because the number is shown to a reader", () => {
    const rules = emptyRules();
    const list = emptyListRule(["lines"]);
    list.fixed.push(emptyListEntry(), emptyListEntry());
    rules.lists.push(list);

    expect(listNode(rules).fixed.map((e) => e.position)).toEqual([1, 2]);
  });

  it("searches inside them as well as inside the per-entry rules", () => {
    const rules = emptyRules();
    const list = emptyListRule(["lines"]);
    const entry = emptyListEntry();
    entry.fields.push(emptyFieldRule(["headerCode"]));
    list.fixed.push(entry);
    rules.lists.push(list);

    const found = filterTree(outputTreeOf(rules), "headerCode");
    const node = found[0] as Extract<OutputNode, { kind: "list" }>;

    expect(node.fixed).toHaveLength(1);
    expect(filterTree(outputTreeOf(rules), "nothingLikeThis")).toHaveLength(0);
  });
});

describe("the two ends of a list", () => {
  it("draws the closing entries as their own rows", () => {
    const rules = emptyRules();
    const list = emptyListRule(["rows"]);
    list.fixed.push(emptyListEntry());
    list.after.push(emptyListEntry());
    rules.lists.push(list);

    const [node] = outputTreeOf(rules);
    expect(node.kind).toBe("list");
    if (node.kind !== "list") return;

    expect(node.fixed).toHaveLength(1);
    expect(node.after).toHaveLength(1);
    // Numbered from one at each end: they are two sets of entries, not one run.
    expect(node.after[0].position).toBe(1);
  });

  it("keeps a list whose only match is a closing entry's rule", () => {
    const rules = emptyRules();
    const list = emptyListRule(["rows"]);
    const entry = emptyListEntry();
    entry.fields.push(emptyFieldRule(["recordCount"]));
    list.after.push(entry);
    rules.lists.push(list);

    const found = filterTree(outputTreeOf(rules), "recordCount");

    expect(found).toHaveLength(1);
    expect(found[0].kind === "list" && found[0].after).toHaveLength(1);
  });
});
