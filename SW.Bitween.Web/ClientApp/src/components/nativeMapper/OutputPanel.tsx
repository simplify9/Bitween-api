import { useMemo } from "react";
import { Search } from "lucide-react";
import type { DocumentNode } from "../../lib/nativeMapper/documentTree";
import { filterTree, outputTreeOf } from "../../lib/nativeMapper/outputTree";
import { useRules, useRulesDispatch } from "../../lib/nativeMapper/rulesEditorHooks";
import { everyFieldRule, isAssigned, isItemAssigned } from "../../lib/nativeMapper/rulesReducer";
import { TextInput } from "../ui/forms";
import { AddRuleButtons, OutputTreeView } from "./OutputTreeView";

/**
 * The output document as a tree of rules.
 *
 * A tree because that is the shape of the thing being built, and because at one
 * line a row a real document fits on a screen. The tree itself is derived from the
 * rules on every render — there is no second copy of the structure to keep in step.
 */
export function OutputPanel({ sourceRoot }: { sourceRoot: DocumentNode | null }) {
  const { rules, searchTarget } = useRules();
  const dispatch = useRulesDispatch();

  const tree = useMemo(() => outputTreeOf(rules), [rules]);
  const shown = useMemo(() => filterTree(tree, searchTarget), [tree, searchTarget]);

  const fields = everyFieldRule(rules);
  const assigned = fields.filter((f) =>
    f.isItem ? isItemAssigned(f.rule) : isAssigned(f.rule),
  ).length;
  const empty = rules.fields.length === 0 && rules.lists.length === 0 && !rules.root;

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <div className="flex-shrink-0 border-b border-ink-100 bg-ink-50 px-3 py-2">
        <div className="mb-1.5 flex flex-wrap items-center gap-2">
          <span className="text-xs font-semibold tracking-wide text-ink-500 uppercase">Output</span>
          <span
            className="rounded bg-ink-100 px-1.5 py-0.5 font-mono text-[10px] text-ink-600"
            title="The format of the document sent onward"
          >
            {rules.targetFormat}
          </span>
          <span className="text-xs text-ink-500" title="How many rules have a value assigned">
            {fields.length} {fields.length === 1 ? "rule" : "rules"} · {assigned} assigned
          </span>

          <label
            className="ml-auto flex cursor-pointer items-center gap-1.5 text-[11px] text-ink-600"
            title="For a partner that expects a bare array rather than an object with a list inside it"
          >
            <input
              type="checkbox"
              className="size-3.5 cursor-pointer rounded accent-crimson-600"
              checked={rules.root !== undefined}
              onChange={(e) => dispatch({ type: "SET_ROOT_LIST", enabled: e.target.checked })}
            />
            The whole output is a list
          </label>
        </div>

        <div className="relative">
          <Search className="absolute top-1.5 left-2 text-ink-500" size={11} aria-hidden />
          <TextInput
            className="h-7 pl-6 font-mono text-xs"
            placeholder="Search output fields…"
            aria-label="Search output fields"
            value={searchTarget}
            onChange={(e) => dispatch({ type: "SET_SEARCH_TARGET", text: e.target.value })}
          />
        </div>
      </div>

      <div data-mapper-scroll className="flex-1 overflow-y-auto px-2 py-1.5">
        {empty ? (
          <p className="py-6 text-center text-xs text-ink-500">
            No rules yet. Add a field, or build them from a sample of the output.
          </p>
        ) : (
          <OutputTreeView
            nodes={shown}
            scope={sourceRoot}
            root={sourceRoot}
            prefix={[]}
            listId={null}
            listDepth={0}
            indent={0}
          />
        )}

        {/* In root-list mode every rule belongs to that list, which brings its own. */}
        {rules.root === undefined && <AddRuleButtons listId={null} canAddList />}
      </div>
    </div>
  );
}
