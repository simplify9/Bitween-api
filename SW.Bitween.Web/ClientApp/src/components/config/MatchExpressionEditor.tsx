import { Plus, Trash2 } from "lucide-react";
import type { MatchCondition, MatchGroup, MatchNode } from "../../api";
import { matchSummary } from "../../lib/match";
import { Button } from "../ui/basics";
import { Select } from "../ui/forms";

const emptyCondition = (path: string): MatchCondition => ({ op: "oneOf", path, values: [] });

function ConditionRow({
  condition,
  properties,
  disabled,
  onChange,
  onRemove,
}: {
  condition: MatchCondition;
  properties: string[];
  disabled: boolean;
  onChange: (c: MatchCondition) => void;
  onRemove: () => void;
}) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <Select
        aria-label="Property"
        value={condition.path}
        disabled={disabled}
        onChange={(e) => onChange({ ...condition, path: e.target.value })}
        options={properties.map((p) => ({ value: p, label: p }))}
        className="!w-40"
      />
      <Select
        aria-label="Operator"
        value={condition.op}
        disabled={disabled}
        onChange={(e) => onChange({ ...condition, op: e.target.value as MatchCondition["op"] })}
        options={[
          { value: "oneOf", label: "is one of" },
          { value: "notOneOf", label: "is none of" },
        ]}
        className="!w-32"
      />
      <input
        aria-label="Values"
        value={condition.values.join(", ")}
        disabled={disabled}
        placeholder="values, comma separated"
        onChange={(e) =>
          onChange({ ...condition, values: e.target.value.split(",").map((v) => v.trim()).filter(Boolean) })
        }
        className="h-9.5 min-w-40 flex-1 rounded-lg border border-ink-200 bg-white px-3 font-mono text-xs text-ink-900 placeholder:font-sans placeholder:text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none disabled:bg-ink-50"
      />
      {!disabled && (
        <button
          onClick={onRemove}
          aria-label="Remove condition"
          className="rounded-md p-1.5 text-ink-400 hover:bg-crimson-50 hover:text-crimson-700"
        >
          <Trash2 className="size-3.5" />
        </button>
      )}
    </div>
  );
}

function GroupCard({
  group,
  properties,
  disabled,
  depth,
  onChange,
  onRemove,
}: {
  group: MatchGroup;
  properties: string[];
  disabled: boolean;
  depth: number;
  onChange: (g: MatchGroup) => void;
  onRemove?: () => void;
}) {
  const setChild = (index: number, node: MatchNode) =>
    onChange({ ...group, children: group.children.map((c, i) => (i === index ? node : c)) });
  const removeChild = (index: number) =>
    onChange({ ...group, children: group.children.filter((_, i) => i !== index) });

  return (
    <div className={`space-y-2.5 rounded-xl border border-ink-200 p-3 ${depth > 0 ? "bg-ink-50/50" : ""}`}>
      <div className="flex items-center gap-2">
        <span className="text-[13px] text-ink-500">Matches</span>
        <Select
          aria-label="Group operator"
          value={group.op}
          disabled={disabled}
          onChange={(e) => onChange({ ...group, op: e.target.value as MatchGroup["op"] })}
          options={[
            { value: "and", label: "all of" },
            { value: "or", label: "any of" },
          ]}
          className="!h-8 !w-24 text-[13px]"
        />
        {onRemove && !disabled && (
          <button
            onClick={onRemove}
            aria-label="Remove group"
            className="ml-auto rounded-md p-1.5 text-ink-400 hover:bg-crimson-50 hover:text-crimson-700"
          >
            <Trash2 className="size-3.5" />
          </button>
        )}
      </div>

      {group.children.length === 0 && (
        <p className="text-[13px] text-ink-400">Empty group — matches every message.</p>
      )}
      {group.children.map((child, i) =>
        "path" in child ? (
          <ConditionRow
            key={i}
            condition={child}
            properties={properties}
            disabled={disabled}
            onChange={(c) => setChild(i, c)}
            onRemove={() => removeChild(i)}
          />
        ) : (
          <GroupCard
            key={i}
            group={child}
            properties={properties}
            disabled={disabled}
            depth={depth + 1}
            onChange={(g) => setChild(i, g)}
            onRemove={() => removeChild(i)}
          />
        ),
      )}

      {!disabled && (
        <div className="flex gap-2">
          <Button
            size="sm"
            disabled={properties.length === 0}
            onClick={() => onChange({ ...group, children: [...group.children, emptyCondition(properties[0])] })}
          >
            <Plus className="size-3.5" /> Condition
          </Button>
          {depth < 2 && (
            <Button
              size="sm"
              variant="ghost"
              onClick={() => onChange({ ...group, children: [...group.children, { op: "or", children: [] }] })}
            >
              <Plus className="size-3.5" /> Nested group
            </Button>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * Message filter builder over an information type's promoted properties.
 * null = no filter (every message matches).
 */
export function MatchExpressionEditor({
  value,
  onChange,
  properties,
  disabled,
}: {
  value: MatchGroup | null;
  onChange: (value: MatchGroup | null) => void;
  /** Promoted property names of the information type being filtered. */
  properties: string[];
  disabled: boolean;
}) {
  if (value === null) {
    return (
      <div className="space-y-2.5">
        <p className="text-sm text-ink-600">
          No filter — <strong className="font-medium">every message</strong> is picked up.
        </p>
        {!disabled && (
          <Button
            size="sm"
            disabled={properties.length === 0}
            onClick={() => onChange({ op: "and", children: properties.length ? [emptyCondition(properties[0])] : [] })}
          >
            <Plus className="size-3.5" /> Add a filter
          </Button>
        )}
        {properties.length === 0 && (
          <p className="text-[13px] text-ink-400">
            Filters match on promoted properties — this information type has none yet.
          </p>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-2.5">
      <p className="rounded-lg bg-ink-50 px-3 py-2 font-mono text-xs text-ink-600">{matchSummary(value)}</p>
      <GroupCard group={value} properties={properties} disabled={disabled} depth={0} onChange={onChange} />
      {!disabled && (
        <Button size="sm" variant="ghost" onClick={() => onChange(null)}>
          Clear filter — match every message
        </Button>
      )}
    </div>
  );
}
