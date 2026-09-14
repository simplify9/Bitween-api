import { useState } from "react";
import { ChevronDown, ChevronRight, Trash2 } from "lucide-react";
import { useRules, useRulesDispatch } from "../../lib/nativeMapper/RulesEditorContext";
import { isAssigned, isItemAssigned } from "../../lib/nativeMapper/rulesReducer";
import type { OutputRowNode } from "../../lib/nativeMapper/outputTree";
import { TYPE_BADGES, freshSource, sourceKindsFor } from "../../lib/nativeMapper/types";
import { SegmentedControl } from "../ui/SegmentedControl";
import { RuleDetail } from "./RuleDetail";
import { RowInput } from "./rowControls";
import { ValueCell, type SourcePaths } from "./ValueCell";

/**
 * One rule, on one line.
 *
 * Two kinds of rule are drawn this way. A field has a name, shown as its last
 * segment only — the indentation carries the rest, the way the source side already
 * did — and typing dots into it still nests, because the rule stores segments. The
 * single value each entry of a list of plain values produces has no name to show,
 * because `["A1","B7"]` has nowhere to put one; everything else about the row is
 * the same, which is the point of it being a row at all.
 *
 * Everything that is not the value lives behind the chevron. A row that carries a
 * transform or a table says so with a dot rather than by taking three lines to
 * announce that it has neither.
 */
export function OutputRow({
  node,
  /**
   * The branch segments above this row inside its container, so the name box can
   * show just this field's own name and still rebuild the whole target.
   */
  prefix,
  paths,
  /** Whether this rule sits inside a list, which is the only place counting means anything. */
  inList,
}: {
  node: OutputRowNode;
  prefix: string[];
  paths: SourcePaths;
  inList: boolean;
}) {
  const { rule, errorKey } = node;
  const dispatch = useRulesDispatch();
  const { selectedId, ruleErrors, hoveredPath } = useRules();
  const [open, setOpen] = useState(false);

  const isItem = node.kind === "item";
  const error = ruleErrors[errorKey];
  const selected = selectedId === rule.id;
  // Only a path read in this scope has a source row to draw a line to; one read
  // from the top of the document inside a list has no single row to point at.
  const sourcePath = rule.from.kind === "path" ? (rule.from.path ?? "") : "";
  const extras = (rule.transform ? 1 : 0) + (rule.lookup ? 1 : 0);
  // A field's name, or the words standing in for the name a list's value has not
  // got. Used in every label on the row, so both kinds read the same way.
  const describe = node.kind === "item" ? "each entry" : node.name || "this field";
  const assigned = isItem ? isItemAssigned(rule) : isAssigned(rule);

  const update = (changes: Partial<Omit<typeof rule, "id">>) =>
    node.kind === "item"
      ? dispatch({
          type: "UPDATE_LIST",
          id: node.listId,
          changes: { item: { ...rule, ...changes } },
        })
      : dispatch({ type: "UPDATE_FIELD", id: rule.id, changes });

  // Removing a list's value does not remove a row from the list — it puts the list
  // back to having decided nothing, so it can be built out of fields instead.
  const remove = () =>
    node.kind === "item"
      ? dispatch({ type: "UPDATE_LIST", id: node.listId, changes: { item: undefined } })
      : dispatch({ type: "REMOVE_FIELD", id: rule.id });

  return (
    <div
      // Dropping a source field on the row wires it up, whatever it was reading
      // before. This is the whole of the drag-and-drop story on this side.
      onDragOver={(e) => {
        if (e.dataTransfer.types.includes("text/plain")) e.preventDefault();
      }}
      onDrop={(e) => {
        e.preventDefault();
        const path = e.dataTransfer.getData("text/plain");
        if (path) update({ from: { kind: "path", path } });
      }}
      onFocus={() => dispatch({ type: "SELECT", id: rule.id })}
      // Clicking the row selects it and shows what is behind the chevron, which is
      // where the transform, the table and the type live. Clicking a control in the
      // row is doing that control's job, not asking to see the rest.
      onClick={(e) => {
        dispatch({ type: "SELECT", id: rule.id });
        // `label` matters as much as `input`: a checkbox's text is a label that
        // forwards the click to its box, so treating it as the row's own click
        // toggled the box and folded the panel away in the same tick — which read
        // as the box refusing to tick at all.
        if (
          (e.target as HTMLElement).closest(
            "input, select, textarea, button, label, [role='radio']",
          )
        )
          return;
        setOpen((o) => !o);
      }}
      onMouseEnter={() => sourcePath && dispatch({ type: "HOVER_PATH", path: sourcePath })}
      onMouseLeave={() => sourcePath && dispatch({ type: "HOVER_PATH", path: null })}
      // The canvas finds its endpoints by these attributes rather than by a ref
      // map the panels have to keep in step.
      data-rule-id={rule.id}
      className={`rounded border ${
        error
          ? "border-danger-300 bg-danger-50"
          : selected
            ? "border-crimson-300 bg-crimson-50"
            : hoveredPath && hoveredPath === sourcePath
              ? "border-ink-200 bg-crimson-50/40"
              : "border-transparent hover:border-ink-200 hover:bg-ink-50"
      }`}
    >
      <div className="flex items-center gap-1.5 px-1.5 py-0.5">
        <span
          aria-hidden
          title={assigned ? "Has a value" : "Nothing assigned yet"}
          className={`size-1.5 flex-shrink-0 rounded-full ${
            assigned ? "bg-ok-600" : "bg-ink-300"
          }`}
        />

        {node.kind === "item" ? (
          <span
            className="w-28 flex-shrink-0 px-1 font-mono text-[11px] text-warn-700"
            title="Every entry of this list is one value, so it has no name of its own"
          >
            each entry
          </span>
        ) : (
          <RowInput
            className="w-28 flex-shrink-0 border-transparent bg-transparent font-mono hover:border-ink-200 focus:bg-white"
            aria-label="Output field name"
            placeholder="name"
            title="Use dots to nest, e.g. billing.city"
            value={node.name}
            onChange={(e) =>
              update({
                target: [...prefix, ...(e.target.value === "" ? [] : e.target.value.split("."))],
              })
            }
          />
        )}

        <span aria-hidden className="flex-shrink-0 text-ink-300">
          ←
        </span>

        <SegmentedControl
          size="sm"
          label="Where the value comes from"
          options={sourceKindsFor(inList)}
          value={rule.from.kind === "rootPath" ? "path" : rule.from.kind}
          // A row showing "Source" may be reading the entry or the document; both are
          // that one choice, and the dropdown beside it says which.
          onChange={(kind) => update({ from: freshSource(kind) })}
        />

        <ValueCell
          source={rule.from}
          paths={paths}
          valueType={rule.type}
          emptyPathLabel={isItem ? "the entry itself" : undefined}
          onChange={(from) => update({ from })}
        />

        <button
          type="button"
          onClick={() => setOpen((o) => !o)}
          aria-expanded={open}
          aria-label={`Details for ${describe}`}
          title={
            extras > 0
              ? "Has a transform or a table"
              : "Change it, substitute it, or set its type"
          }
          className={`flex flex-shrink-0 items-center gap-0.5 rounded px-1 py-0.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700 ${
            extras > 0 ? "text-crimson-600" : ""
          }`}
        >
          {open ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
          {rule.type && (
            <span className="font-mono text-[9px]">{TYPE_BADGES[rule.type]}</span>
          )}
          {extras > 0 && <span aria-hidden className="size-1 rounded-full bg-crimson-500" />}
        </button>

        <button
          type="button"
          onClick={remove}
          aria-label={
            isItem ? "Remove the value rule" : `Remove the rule for ${node.name || "the unnamed field"}`
          }
          title={isItem ? "Stop this list being a list of plain values" : "Remove this rule"}
          className="flex-shrink-0 rounded p-0.5 text-ink-300 hover:bg-danger-50 hover:text-danger-700"
        >
          <Trash2 size={12} />
        </button>
      </div>

      {open && (
        <div className="px-1.5 pb-1.5">
          <RuleDetail rule={rule} onChange={update} />
        </div>
      )}

      {error && (
        <p role="alert" className="px-1.5 pb-1 font-mono text-[10px] text-danger-700">
          {error}
        </p>
      )}
    </div>
  );
}
