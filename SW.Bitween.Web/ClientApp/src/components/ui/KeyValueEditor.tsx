import { Fragment, useEffect, useRef, useState, type ReactNode } from "react";
import { Check, Copy, Lock, LockOpen, Plus, Trash2 } from "lucide-react";
import { SECRET_SENTINEL } from "../../api";
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
 * A stored secret's value: dots, with a Replace button, exactly as adapter config
 * shows one. Used whenever the value is the sentinel, which covers both a row that
 * is still marked secret and one just unmarked — unmarking cannot reveal the value
 * on its own, because the browser was never sent it.
 */
function SecretValueCell({
  value,
  disabled,
  placeholder,
  ariaLabel,
  stillSecret,
  onChange,
}: {
  value: string;
  disabled: boolean;
  placeholder?: string;
  ariaLabel: string;
  stillSecret: boolean;
  onChange: (value: string) => void;
}) {
  // Keyed off "is the operator part-way through typing", not off whether the box
  // holds text — the latter re-masks on the first keystroke and hides what they typed.
  const [entering, setEntering] = useState(false);
  const lastEmitted = useRef<string | null>(null);

  // A save or a discard re-supplies the value from the server. Anything this field
  // did not emit means the draft was reset from outside, so mask it again.
  useEffect(() => {
    if (value !== lastEmitted.current) setEntering(false);
  }, [value]);

  const emit = (next: string) => {
    lastEmitted.current = next;
    setEntering(true);
    onChange(next);
  };

  if (!entering && value === SECRET_SENTINEL) {
    return (
      <div
        className="flex h-8.5 items-center justify-between rounded-md border border-ink-200 bg-ink-50 px-2.5"
        title={
          stillSecret
            ? "Stored and hidden. Replace it to set a new value."
            : "Still hidden — save to reveal this value."
        }
      >
        <span className="font-mono text-sm tracking-widest text-ink-400" aria-label={ariaLabel}>
          ••••••••
        </span>
        {!disabled && (
          <Button size="sm" variant="ghost" onClick={() => emit("")}>
            Replace
          </Button>
        )}
      </div>
    );
  }

  return (
    <GrowingValueInput
      value={value}
      disabled={disabled}
      placeholder={placeholder}
      ariaLabel={ariaLabel}
      onChange={emit}
    />
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
 * row (e.g. {{partner.KEY}}); `rowDetails` renders a per-row note under it
 * (e.g. "where is this used?"), always visible. `secrets` adds a lock column:
 * a locked value never leaves the server, so it reads back as the sentinel and
 * shows as dots until someone replaces it.
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
  secrets,
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
  /** Omit entirely for editors whose values are never secrets. */
  secrets?: { names: string[]; onChange: (names: string[]) => void };
}) {
  const [focusLast, setFocusLast] = useState(false);

  // Names are compared case-insensitively, matching how the resolver looks a key up.
  const isSecret = (key: string) =>
    !!secrets && secrets.names.some((n) => n.toLowerCase() === key.trim().toLowerCase());

  const toggleSecret = (key: string) => {
    const name = key.trim();
    if (!secrets || !name) return;
    secrets.onChange(
      isSecret(name)
        ? secrets.names.filter((n) => n.toLowerCase() !== name.toLowerCase())
        : [...secrets.names, name],
    );
  };

  const update = (index: number, patch: Partial<KvRow>) =>
    onChange(rows.map((r, i) => (i === index ? { ...r, ...patch } : r)));

  const add = () => {
    onChange([...rows, { key: "", value: "" }]);
    setFocusLast(true);
  };

  const remove = (index: number) => onChange(rows.filter((_, x) => x !== index));

  if (rows.length === 0 && !editable) {
    return <p className="text-sm text-ink-500">{emptyText}</p>;
  }

  const columns = 2 + (token ? 1 : 0) + (secrets ? 1 : 0) + (editable ? 1 : 0);

  return (
    <div>
      {rows.length > 0 && (
        <table className="table-fixed text-sm">
          <thead>
            <tr className="text-left text-xs text-ink-500">
              <th className={`${keyWidthClass} pb-1.5 pr-3 font-medium`}>{keyLabel}</th>
              <th className={`${valueWidthClass} pb-1.5 pr-3 font-medium`}>{valueLabel}</th>
              {token && <th className="hidden w-56 pb-1.5 pr-3 font-medium xl:table-cell">Reference</th>}
              {secrets && (
                <th className="w-10 pb-1.5 pr-3 font-medium" title="Hide this value from the API and from this page">
                  Secret
                </th>
              )}
              {editable && <th className="w-8 pb-1.5" />}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => (
              <Fragment key={i}>
                <tr className="align-top">
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
                    {secrets && (isSecret(row.key) || row.value === SECRET_SENTINEL) ? (
                      <SecretValueCell
                        value={row.value}
                        disabled={!editable}
                        placeholder={valuePlaceholder}
                        ariaLabel={`${valueLabel} ${i + 1}`}
                        stillSecret={isSecret(row.key)}
                        onChange={(value) => update(i, { value })}
                      />
                    ) : (
                      <GrowingValueInput
                        value={row.value}
                        disabled={!editable}
                        placeholder={valuePlaceholder}
                        onChange={(value) => update(i, { value })}
                        ariaLabel={`${valueLabel} ${i + 1}`}
                      />
                    )}
                  </td>
                  {token && (
                    <td className="hidden pt-1.5 pb-1 pr-3 xl:table-cell">
                      {row.key.trim() && <ReferenceToken token={token(row)} />}
                    </td>
                  )}
                  {secrets && (
                    <td className="pt-1.5 pb-1 pr-3">
                      {row.key.trim() && (
                        <button
                          type="button"
                          disabled={!editable}
                          onClick={() => toggleSecret(row.key)}
                          aria-pressed={isSecret(row.key)}
                          aria-label={`Mark ${row.key.trim()} secret`}
                          title={
                            isSecret(row.key)
                              ? "Secret: the value is never sent back to this page. Click to unlock."
                              : "Ordinary value, readable by anyone who can open this page. Click to make it secret."
                          }
                          className={`rounded-md p-1.5 disabled:cursor-default ${
                            isSecret(row.key)
                              ? "text-crimson-600 hover:bg-crimson-50"
                              : "text-ink-300 hover:bg-ink-100 hover:text-ink-500"
                          }`}
                        >
                          {isSecret(row.key) ? (
                            <Lock className="size-3.5" />
                          ) : (
                            <LockOpen className="size-3.5" />
                          )}
                        </button>
                      )}
                    </td>
                  )}
                  {editable && (
                    <td className="pt-1.5 pb-1">
                      <button
                        onClick={() => remove(i)}
                        aria-label={`Remove ${row.key || "row"}`}
                        className="rounded-md p-1.5 text-ink-400 hover:bg-danger-50 hover:text-danger-700"
                      >
                        <Trash2 className="size-3.5" />
                      </button>
                    </td>
                  )}
                </tr>
                {rowDetails?.(row) !== null && rowDetails !== undefined && (
                  <tr>
                    <td colSpan={columns} className="pt-0.5 pb-2">
                      <div className="rounded-lg bg-ink-50 px-3 py-2">{rowDetails(row)}</div>
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
