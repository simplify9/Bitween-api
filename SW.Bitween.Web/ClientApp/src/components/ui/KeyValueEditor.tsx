import { Fragment, useState, type ReactNode } from "react";
import { Check, ChevronDown, ChevronRight, Copy, Plus, Trash2 } from "lucide-react";
import { Button } from "./basics";

export interface KvRow {
  key: string;
  value: string;
}

const cellInput =
  "h-8.5 w-full rounded-md border border-ink-200 bg-white px-2.5 text-sm text-ink-900 placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none disabled:border-transparent disabled:bg-transparent disabled:px-0";

/** Shared metrics so the textarea and its invisible sizing ghost stay identical. */
const growBox = "border px-2.5 py-1.5 text-sm leading-5 break-words whitespace-pre-wrap";

/**
 * A value box that starts one line tall and grows with its content.
 * An invisible ghost with the same text sits in the same grid cell,
 * so the textarea auto-sizes without any JS measuring.
 */
function GrowingValueInput({
  value,
  disabled,
  placeholder,
  ariaLabel,
  onChange,
}: {
  value: string;
  disabled: boolean;
  placeholder?: string;
  ariaLabel: string;
  onChange: (value: string) => void;
}) {
  return (
    <div className="grid grid-cols-[minmax(0,1fr)]">
      <textarea
        rows={1}
        value={value}
        disabled={disabled}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        aria-label={ariaLabel}
        className={`${growBox} [grid-area:1/1] w-full resize-none overflow-hidden rounded-md border-ink-200 bg-white text-ink-900 placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none disabled:border-transparent disabled:bg-transparent disabled:px-0`}
      />
      <span aria-hidden className={`${growBox} [grid-area:1/1] invisible border-transparent ${disabled ? "px-0" : ""}`}>
        {value || placeholder || " "}{" "}
      </span>
    </div>
  );
}

/**
 * The runtime reference token, click-to-copy. Wraps instead of truncating —
 * a cut-off token is useless since it can't be pasted correctly.
 */
function ReferenceToken({ token }: { token: string }) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    await navigator.clipboard.writeText(token);
    setCopied(true);
    setTimeout(() => setCopied(false), 1400);
  };

  return (
    <button
      type="button"
      onClick={copy}
      title="Copy reference"
      className="group flex w-full items-start gap-1.5 rounded-md py-1 pr-1 text-left hover:bg-ink-100"
    >
      <code className="min-w-0 flex-1 font-mono text-xs break-all text-ink-500 group-hover:text-ink-700">
        {token}
      </code>
      {copied ? (
        <Check className="mt-0.5 size-3 shrink-0 text-ok-600" aria-hidden />
      ) : (
        <Copy className="mt-0.5 size-3 shrink-0 text-ink-300 group-hover:text-ink-500" aria-hidden />
      )}
    </button>
  );
}

/**
 * Inline key-value table used for partner properties, value sets and
 * promoted properties. Fully controlled: the parent owns the draft rows
 * and decides when to save. `token` renders the runtime reference for a
 * row (e.g. {{partner.KEY}}); `rowDetails` adds a collapsible drawer per
 * row (e.g. "where is this used?").
 */
export function KeyValueEditor({
  rows,
  onChange,
  keyLabel,
  valueLabel,
  keyPlaceholder,
  valuePlaceholder,
  editable,
  token,
  emptyText,
  keyWidthClass = "w-40 sm:w-48 lg:w-56",
  valueWidthClass = "w-56 sm:w-72 lg:w-96",
  rowDetails,
}: {
  rows: KvRow[];
  onChange: (rows: KvRow[]) => void;
  keyLabel: string;
  valueLabel: string;
  keyPlaceholder?: string;
  valuePlaceholder?: string;
  editable: boolean;
  token?: (row: KvRow) => string;
  emptyText: string;
  /** Fixed (not percentage) column widths — comfortable at rest, shrinks at narrower breakpoints. */
  keyWidthClass?: string;
  valueWidthClass?: string;
  rowDetails?: (row: KvRow) => ReactNode | null;
}) {
  const [focusLast, setFocusLast] = useState(false);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  const update = (index: number, patch: Partial<KvRow>) =>
    onChange(rows.map((r, i) => (i === index ? { ...r, ...patch } : r)));

  const add = () => {
    onChange([...rows, { key: "", value: "" }]);
    setFocusLast(true);
  };

  const toggleExpanded = (index: number) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });

  const remove = (index: number) => {
    onChange(rows.filter((_, x) => x !== index));
    setExpanded(new Set());
  };

  if (rows.length === 0 && !editable) {
    return <p className="text-sm text-ink-500">{emptyText}</p>;
  }

  const columns = 2 + (rowDetails ? 1 : 0) + (token ? 1 : 0) + (editable ? 1 : 0);

  return (
    <div>
      {rows.length > 0 && (
        <table className="table-fixed text-sm">
          <thead>
            <tr className="text-left text-xs text-ink-500">
              {rowDetails && <th className="w-7 pb-1.5" />}
              <th className={`${keyWidthClass} pb-1.5 pr-3 font-medium`}>{keyLabel}</th>
              <th className={`${valueWidthClass} pb-1.5 pr-3 font-medium`}>{valueLabel}</th>
              {token && <th className="hidden w-56 pb-1.5 pr-3 font-medium xl:table-cell">Reference</th>}
              {editable && <th className="w-8 pb-1.5" />}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => (
              <Fragment key={i}>
                <tr className="align-top">
                  {rowDetails && (
                    <td className="pt-2 pb-1 pr-1">
                      {rowDetails(row) !== null && (
                        <button
                          onClick={() => toggleExpanded(i)}
                          aria-expanded={expanded.has(i)}
                          aria-label={`Details for ${row.key || "row"}`}
                          className="rounded-md p-1 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                        >
                          {expanded.has(i) ? (
                            <ChevronDown className="size-3.5" />
                          ) : (
                            <ChevronRight className="size-3.5" />
                          )}
                        </button>
                      )}
                    </td>
                  )}
                  <td className="py-1 pr-3">
                    <input
                      value={row.key}
                      disabled={!editable}
                      placeholder={keyPlaceholder}
                      autoFocus={focusLast && i === rows.length - 1}
                      onChange={(e) => update(i, { key: e.target.value })}
                      aria-label={`${keyLabel} ${i + 1}`}
                      className={`${cellInput} font-medium`}
                    />
                  </td>
                  <td className="py-1 pr-3">
                    <GrowingValueInput
                      value={row.value}
                      disabled={!editable}
                      placeholder={valuePlaceholder}
                      onChange={(value) => update(i, { value })}
                      ariaLabel={`${valueLabel} ${i + 1}`}
                    />
                  </td>
                  {token && (
                    <td className="hidden pt-1.5 pb-1 pr-3 xl:table-cell">
                      {row.key.trim() && <ReferenceToken token={token(row)} />}
                    </td>
                  )}
                  {editable && (
                    <td className="pt-1.5 pb-1">
                      <button
                        onClick={() => remove(i)}
                        aria-label={`Remove ${row.key || "row"}`}
                        className="rounded-md p-1.5 text-ink-400 hover:bg-crimson-50 hover:text-crimson-700"
                      >
                        <Trash2 className="size-3.5" />
                      </button>
                    </td>
                  )}
                </tr>
                {rowDetails && expanded.has(i) && (
                  <tr>
                    <td />
                    <td colSpan={columns - 1} className="pt-0.5 pb-2">
                      <div className="rounded-lg bg-ink-50 px-3 py-2.5">{rowDetails(row)}</div>
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
      )}
      {rows.length === 0 && <p className="pb-2 text-sm text-ink-500">{emptyText}</p>}
      {editable && (
        <Button size="sm" onClick={add} className="mt-1.5">
          <Plus className="size-3.5" /> Add {keyLabel.toLowerCase()}
        </Button>
      )}
    </div>
  );
}

/** Record<string,string> ⇄ ordered rows helpers. */
export const toRows = (record: Record<string, string>): KvRow[] =>
  Object.entries(record).map(([key, value]) => ({ key, value }));

export const toRecord = (rows: KvRow[]): Record<string, string> =>
  Object.fromEntries(rows.filter((r) => r.key.trim()).map((r) => [r.key.trim(), r.value]));
