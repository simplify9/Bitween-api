import { useId, useState, type InputHTMLAttributes, type SelectHTMLAttributes } from "react";

/**
 * The inputs a dense row is made of.
 *
 * The shared `TextInput` and `Select` are form controls: their base class is
 * `w-full`, which is right inside a `Field` and fatal in a row, where a fixed
 * width set alongside it loses depending only on which class Tailwind emitted
 * last. These set no width at all, so the caller's width is the width.
 */
const base =
  "h-7 rounded border border-ink-200 bg-white px-1.5 text-[11px] text-ink-900 placeholder:text-ink-400 focus:border-focus-400 focus:ring-1 focus:ring-focus-100 focus:outline-none disabled:bg-ink-50 disabled:text-ink-400";

export function RowInput({ className = "", ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={`${base} ${className}`} />;
}

/**
 * A box that can be typed into, and that offers what is known as suggestions.
 *
 * Both halves matter. A plain box means remembering every path and getting one
 * wrong silently; a plain dropdown means a path the sample does not happen to
 * contain cannot be named at all — and a sample whose list is empty then has no
 * fields to offer, so that list could not be mapped. The sample is an aid, not the
 * set of legal answers.
 */
export function RowSuggestInput({
  suggestions,
  className = "",
  onFocus,
  onBlur,
  ...props
}: Omit<InputHTMLAttributes<HTMLInputElement>, "list"> & { suggestions: string[] }) {
  const listId = useId();
  // Only the focused box's list is populated. A mapping of a hundred rules against a
  // document of two hundred paths would otherwise put twenty thousand <option>
  // elements in the page, all but one set of them for a box nobody is typing in.
  //
  // The `list` attribute itself stays put. It is what makes this input a combobox
  // rather than a plain text box, and a control that changes what it is depending on
  // whether it has focus is announced differently every time focus moves.
  const [active, setActive] = useState(false);

  return (
    <>
      <input
        {...props}
        list={listId}
        onFocus={(e) => {
          setActive(true);
          onFocus?.(e);
        }}
        onBlur={(e) => {
          setActive(false);
          onBlur?.(e);
        }}
        className={`${base} ${className}`}
      />
      <datalist id={listId}>
        {active &&
          suggestions.map((s) => <option key={s} value={s} />)}
      </datalist>
    </>
  );
}

export interface RowOption {
  value: string;
  label: string;
}

/** A named set of options, for a list that comes from more than one place. */
export interface RowOptionGroup {
  label: string;
  options: RowOption[];
}

export function RowSelect({
  options,
  groups,
  className = "",
  ...props
}: SelectHTMLAttributes<HTMLSelectElement> & {
  options?: RowOption[];
  /** Rendered as optgroups after `options`, and skipped when empty. */
  groups?: RowOptionGroup[];
}) {
  return (
    <select {...props} className={`${base} cursor-pointer ${className}`}>
      {options?.map((o) => (
        <option key={o.value} value={o.value}>
          {o.label}
        </option>
      ))}
      {groups
        ?.filter((g) => g.options.length > 0)
        .map((g) => (
          <optgroup key={g.label} label={g.label}>
            {g.options.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </optgroup>
        ))}
    </select>
  );
}
