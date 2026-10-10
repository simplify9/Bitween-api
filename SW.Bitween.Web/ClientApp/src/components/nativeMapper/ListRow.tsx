import { useState } from "react";
import { ChevronDown, ChevronRight, Repeat, Trash2 } from "lucide-react";
import { listPaths, type DocumentNode } from "../../lib/nativeMapper/documentTree";
import type { OutputListNode } from "../../lib/nativeMapper/outputTree";
import { useRules, useRulesDispatch } from "../../lib/nativeMapper/RulesEditorContext";
import {
  FILTER_OPERATORS,
  type FilterOperatorName,
} from "../../lib/nativeMapper/types";
import { Checkbox } from "../ui/forms";
import { RowInput, RowSelect } from "./rowControls";

/**
 * The three states of a list's source, encoded for a select.
 *
 * A select hands back one string, and "" already means "the source document is
 * itself the list" — so "walks nothing" needs a value of its own rather than
 * being confused with it.
 */
const NO_SOURCE_LIST = "none";

const encodeOver = (over: string | undefined) =>
  over === undefined ? NO_SOURCE_LIST : `p:${over}`;

const decodeOver = (value: string) =>
  value === NO_SOURCE_LIST ? undefined : value.slice(2);

/**
 * A list in the output: one entry per entry of a list on the way in.
 *
 * The header is one line like a field's, and what the list walks sits on it because
 * that is the thing you check when reading a mapping. The filter is behind the
 * chevron. What each entry holds is not a setting at all — it is the rows in the
 * list, the same as everywhere else in the tree.
 */
export function ListRow({
  node,
  /** The source node this list's own list is named against. */
  scope,
  prefix,
  collapsed,
  onToggleCollapsed,
}: {
  node: OutputListNode;
  scope: DocumentNode | null;
  prefix: string[];
  collapsed: boolean;
  onToggleCollapsed: () => void;
}) {
  const { list, errorKey } = node;
  const dispatch = useRulesDispatch();
  const { ruleErrors } = useRules();

  const error = ruleErrors[errorKey];
  const listOptions = listPaths(scope);

  // Two separate things, which one chevron used to do at once: the left one folds
  // the list's own rules away, like an object's does; this one shows the list's
  // settings. Kept shut, a list that uses neither costs one line like anything else.
  const [detail, setDetail] = useState(false);
  const settings = list.where ? 1 : 0;

  const update = (changes: Partial<Omit<typeof list, "id" | "fields" | "lists">>) =>
    dispatch({ type: "UPDATE_LIST", id: list.id, changes });


  return (
    <div
      // As with a field row: clicking the row itself shows the settings behind the
      // chevron, and clicking a control in it does that control's job.
      onClick={(e) => {
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
        setDetail((d) => !d);
      }}
      className={`rounded border ${
        error ? "border-danger-300 bg-danger-50" : "border-warn-200 bg-warn-100/30"
      }`}
    >
      <div className="flex items-center gap-1.5 px-1.5 py-0.5">
        <button
          type="button"
          onClick={onToggleCollapsed}
          aria-expanded={!collapsed}
          aria-label={`${collapsed ? "Expand" : "Collapse"} the list ${node.name || "at the root"}`}
          className="flex-shrink-0 rounded p-0.5 text-ink-500 hover:bg-ink-100"
        >
          {collapsed ? <ChevronRight size={13} /> : <ChevronDown size={13} />}
        </button>

        <Repeat size={11} className="flex-shrink-0 text-warn-700" aria-hidden />

        {node.isRoot ? (
          <span className="flex-shrink-0 font-mono text-[11px] font-semibold text-warn-700">
            the whole output
          </span>
        ) : (
          <RowInput
            className="w-28 flex-shrink-0 border-transparent bg-transparent font-mono font-semibold hover:border-ink-200 focus:bg-white"
            aria-label="Output list name"
            placeholder="lines"
            title="The name of the list in the output. Use dots to nest."
            value={node.name}
            onChange={(e) =>
              update({
                target: [...prefix, ...(e.target.value === "" ? [] : e.target.value.split("."))],
              })
            }
          />
        )}

        <span className="flex-shrink-0 text-[10px] text-ink-500">
          {list.over === undefined ? "made of" : "for each of"}
        </span>

        <RowSelect
          className="min-w-0 flex-1 font-mono"
          aria-label="Source list"
          title="Which list to walk. A list inside another is named relative to its entry."
          value={encodeOver(list.over)}
          onChange={(e) => update({ over: decodeOver(e.target.value) })}
          options={[
            // Not the same as choosing nothing: this list is exactly the entries
            // written into it, rather than one per entry of a source list.
            { value: NO_SOURCE_LIST, label: "— just the entries below —" },
            ...(list.over !== undefined && !listOptions.includes(list.over)
              ? [{ value: encodeOver(list.over), label: list.over || "choose a list…" }]
              : []),
            ...listOptions.map((p) => ({
              value: encodeOver(p),
              label: p === "" ? "(the document)" : p,
            })),
          ]}
        />

        {list.where && (
          <span
            className="flex-shrink-0 rounded bg-warn-100 px-1 font-mono text-[10px] text-warn-700"
            title="Only entries matching this condition are included"
          >
            {list.where.field || "?"}{" "}
            {FILTER_OPERATORS.find((o) => o.value === list.where!.operator)?.label}{" "}
            {String(list.where.value ?? "")}
          </span>
        )}
        {list.item && (
          <span
            className="flex-shrink-0 rounded bg-warn-100 px-1 text-[10px] text-warn-700"
            title="Each entry becomes a single value rather than an object"
          >
            values
          </span>
        )}

        <button
          type="button"
          onClick={() => setDetail((d) => !d)}
          aria-expanded={detail}
          aria-label={`Settings for the list ${node.name || "at the root"}`}
          title="Skip some of the entries"
          className={`flex flex-shrink-0 items-center gap-0.5 rounded px-1 py-0.5 hover:bg-ink-100 ${
            settings > 0 ? "text-crimson-600" : "text-ink-500 hover:text-ink-700"
          }`}
        >
          {detail ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
          {settings > 0 && <span aria-hidden className="size-1 rounded-full bg-crimson-500" />}
        </button>

        <button
          type="button"
          onClick={() => dispatch({ type: "REMOVE_LIST", id: list.id })}
          aria-label={node.isRoot ? "Remove the root list" : `Remove the list ${node.name}`}
          title="Remove this list"
          className="flex-shrink-0 rounded p-0.5 text-ink-300 hover:bg-danger-50 hover:text-danger-700"
        >
          <Trash2 size={12} />
        </button>
      </div>

      {detail && (
        <div className="space-y-1.5 px-1.5 pb-1.5">
          <div className="ml-4 space-y-1.5 border-l-2 border-warn-200 pl-2.5">
            <Checkbox
              label="Only some entries"
              checked={list.where !== undefined}
              onChange={(e) =>
                update({
                  where: e.target.checked
                    ? { field: "", operator: "equal", value: "" }
                    : undefined,
                })
              }
            />
            {list.where && (
              <div className="flex flex-wrap items-center gap-1.5">
                <RowInput
                  className="w-28 font-mono"
                  aria-label="Filter field"
                  placeholder="qty"
                  title="A field of the entry, not of the whole document"
                  value={list.where.field}
                  onChange={(e) => update({ where: { ...list.where!, field: e.target.value } })}
                />
                <RowSelect
                  className="w-16"
                  aria-label="Filter comparison"
                  value={list.where.operator}
                  onChange={(e) =>
                    update({
                      where: { ...list.where!, operator: e.target.value as FilterOperatorName },
                    })
                  }
                  options={FILTER_OPERATORS.map((o) => ({ value: o.value, label: o.label }))}
                />
                <RowInput
                  className="w-24"
                  aria-label="Filter value"
                  placeholder="0"
                  value={String(list.where.value ?? "")}
                  onChange={(e) => update({ where: { ...list.where!, value: e.target.value } })}
                />
              </div>
            )}
          </div>
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
