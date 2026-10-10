import { useEffect, useState } from "react";
import type { LookupRule } from "../../lib/nativeMapper/types";
import { KeyValueEditor, type KvRow } from "../ui/KeyValueEditor";
import { toRecord, toRows } from "../ui/keyValueRows";
import { Checkbox } from "../ui/forms";
import { RowInput } from "./rowControls";

/**
 * A substitution table: this incoming value becomes that outgoing one.
 *
 * The mapper compares keys as text — a table is written by hand, and the document
 * it matches may be XML or CSV where 1 and "1" are the same thing. So the editor
 * stores text on both sides and the field's own type cast turns "116" into a
 * number if that is what the output wants.
 *
 * A value that is not in the table gets the fallback, which is nothing unless the
 * rule sets one. That is also what a missing value gets, without consulting the
 * table at all.
 */
export function LookupFields({
  lookup,
  onChange,
}: {
  lookup: LookupRule | undefined;
  onChange: (lookup: LookupRule | undefined) => void;
}) {
  if (!lookup) {
    return (
      <Checkbox
        label="Substitute values from a table"
        checked={false}
        onChange={() => onChange({ table: {} })}
      />
    );
  }

  return <LookupTable lookup={lookup} onChange={onChange} />;
}

/**
 * The rows are held here rather than derived from the rule on every render.
 *
 * `toRecord` drops a row whose key is still empty — which is right for a table
 * keyed by name, and means a row the user has only just added cannot survive a
 * trip through the rule. Every other user of KeyValueEditor keeps draft rows and
 * converts them when saving; this editor has no save step, so it keeps them here.
 *
 * The rule is still the truth: if it stops agreeing with these rows — an undo, or
 * the mapping being reloaded — the draft is stale and gets replaced.
 */
function LookupTable({
  lookup,
  onChange,
}: {
  lookup: LookupRule;
  onChange: (lookup: LookupRule | undefined) => void;
}) {
  const stored = JSON.stringify(
    Object.fromEntries(Object.entries(lookup.table).map(([k, v]) => [k, String(v ?? "")])),
  );
  const [rows, setRows] = useState<KvRow[]>(() => toRows(JSON.parse(stored)));

  useEffect(() => {
    // An empty-keyed row contributes nothing to the record, so a blank row being
    // typed into does not read as a disagreement.
    setRows((current) =>
      JSON.stringify(toRecord(current)) === stored ? current : toRows(JSON.parse(stored)),
    );
  }, [stored]);

  const update = (next: KvRow[]) => {
    setRows(next);
    onChange({ ...lookup, table: toRecord(next) });
  };

  return (
    <div className="space-y-1.5">
      <Checkbox
        label="Substitute values from a table"
        checked
        onChange={() => onChange(undefined)}
      />

      <KeyValueEditor
        rows={rows}
        onChange={update}
        keyLabel="Incoming value"
        valueLabel="Becomes"
        keyPlaceholder="JO"
        valuePlaceholder="Jordan"
        editable
        emptyText="No substitutions yet. Anything not listed gets the fallback."
        keyWidthClass="w-32 sm:w-40"
        valueWidthClass="w-40 sm:w-56"
      />

      <div className="flex flex-wrap items-center gap-2">
        <Checkbox
          label="Use a fallback for anything not listed"
          checked={lookup.fallback !== undefined}
          onChange={(e) =>
            onChange({ ...lookup, fallback: e.target.checked ? "" : undefined })
          }
        />
        {lookup.fallback !== undefined && (
          <RowInput
            className="w-32"
            aria-label="Lookup fallback"
            placeholder="Unknown"
            value={String(lookup.fallback ?? "")}
            onChange={(e) => onChange({ ...lookup, fallback: e.target.value })}
          />
        )}
        {lookup.fallback === undefined && (
          <span className="text-[11px] text-ink-500">Otherwise the field is left empty.</span>
        )}
      </div>
    </div>
  );
}
