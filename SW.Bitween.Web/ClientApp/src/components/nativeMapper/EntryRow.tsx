import { useState } from "react";
import { ChevronDown, ChevronRight, CornerDownRight, Trash2 } from "lucide-react";
import type { OutputEntryNode } from "../../lib/nativeMapper/outputTree";
import { useRules, useRulesDispatch } from "../../lib/nativeMapper/RulesEditorContext";
import {
  TYPE_BADGES,
  freshSource,
  sourceKindsFor,
  type EditorFieldRule,
} from "../../lib/nativeMapper/types";
import { SegmentedControl } from "../ui/SegmentedControl";
import { RuleDetail } from "./RuleDetail";
import { ValueCell, type SourcePaths } from "./ValueCell";

/**
 * One entry written into a list, rather than one produced by walking a source list.
 *
 * A header line a partner expects, or one slot of a list whose entries each come
 * from somewhere different. Its rules read whatever the list reads, because there
 * is no entry of its own to read — which is what lets a constant entry still pull a
 * value out of the document, the partner or a values set.
 *
 * An entry holding one value is a whole rule on this line, so it gets what any other
 * rule gets: a transform, a substitution table and a type, behind the chevron. It is
 * a slot in the output like any other, and "it is written in by hand" is no reason
 * for it to be the one value in the mapping that cannot be rounded or reformatted.
 */
export function EntryRow({
  node,
  /** The paths this entry may read — the list's own scope, not an entry of it. */
  paths,
}: {
  node: OutputEntryNode;
  paths: SourcePaths;
}) {
  const { entry, position } = node;
  // Both ends number from one, so the words have to say which end this is.
  const named = node.placement === "after" ? `closing entry ${position}` : `entry ${position}`;
  const dispatch = useRulesDispatch();
  const { ruleErrors } = useRules();
  const [open, setOpen] = useState(false);

  const error = ruleErrors[node.errorKey];
  const item = entry.item;
  const extras = (item?.transform ? 1 : 0) + (item?.lookup ? 1 : 0);

  const updateItem = (changes: Partial<Omit<EditorFieldRule, "id">>) =>
    dispatch({
      type: "UPDATE_FIXED_ENTRY",
      id: entry.id,
      changes: { item: { ...item!, ...changes } },
    });

  return (
    <div
      // Named, so this entry's own controls can be told apart from the list's and
      // from the other entries' — the position is the only thing distinguishing them.
      role="group"
      aria-label={named}
      className={`rounded border ${
        error ? "border-danger-300 bg-danger-50" : "border-warn-200/70 bg-warn-100/20"
      }`}
    >
      <div className="flex items-center gap-1.5 px-1.5 py-0.5">
        <CornerDownRight size={11} className="flex-shrink-0 text-warn-700" aria-hidden />
        <span className="flex-shrink-0 text-[11px] font-medium text-warn-700">{named}</span>

        {item && (
          <>
            <span aria-hidden className="flex-shrink-0 text-ink-300">
              ←
            </span>
            <SegmentedControl
              size="sm"
              label={`Where ${named} comes from`}
              options={sourceKindsFor(true)}
              value={item.from.kind === "rootPath" ? "path" : item.from.kind}
              onChange={(kind) => updateItem({ from: freshSource(kind) })}
            />
            <ValueCell
              source={item.from}
              paths={paths}
              valueType={item.type}
              onChange={(from) => updateItem({ from })}
            />
            <button
              type="button"
              onClick={() => setOpen((o) => !o)}
              aria-expanded={open}
              aria-label={`Details for ${named}`}
              title={
                extras > 0
                  ? "Has a transform or a table"
                  : "Change it, substitute it, or set its type"
              }
              className={`flex flex-shrink-0 items-center gap-0.5 rounded px-1 py-0.5 hover:bg-ink-100 ${
                extras > 0 ? "text-crimson-600" : "text-ink-400 hover:text-ink-700"
              }`}
            >
              {open ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
              {item.type && (
                <span className="font-mono text-[9px]">{TYPE_BADGES[item.type]}</span>
              )}
              {extras > 0 && <span aria-hidden className="size-1 rounded-full bg-crimson-500" />}
            </button>
          </>
        )}

        <button
          type="button"
          onClick={() => dispatch({ type: "REMOVE_FIXED_ENTRY", id: entry.id })}
          aria-label={`Remove ${named}`}
          title="Remove this entry"
          className="ml-auto flex-shrink-0 rounded p-0.5 text-ink-300 hover:bg-danger-50 hover:text-danger-700"
        >
          <Trash2 size={12} />
        </button>
      </div>

      {open && item && (
        <div className="px-1.5 pb-1.5">
          <RuleDetail rule={item} onChange={updateItem} />
        </div>
      )}

      {/* A colour is not a message. The reason is rendered here as a list's is, so it
          reaches a reader who cannot tell the two borders apart. */}
      {error && (
        <p role="alert" className="px-1.5 pb-1 font-mono text-[10px] text-danger-700">
          {error}
        </p>
      )}
    </div>
  );
}
