import { useMemo, useState, type ReactNode } from "react";
import { Combobox, ComboboxButton, ComboboxInput, ComboboxOption, ComboboxOptions } from "@headlessui/react";
import { Check, ChevronsUpDown } from "lucide-react";

export interface SearchSelectOption {
  value: string;
  label: string;
  /** Extra text search also matches (an information-type code, an id…). */
  code?: string;
  /** Muted right-aligned text on the option row. */
  hint?: string;
  /** Fully custom option row; label/code/hint rendering is skipped. */
  render?: ReactNode;
}

/**
 * A searchable dropdown (Headless UI combobox) for entity pickers —
 * anything whose option list grows with data. Typing filters in memory
 * across label + code; rows can carry hints or fully custom rendering.
 * Small fixed enums should keep using the native `Select`.
 */
export function SearchSelect({
  value,
  onChange,
  options,
  placeholder = "Pick…",
  clearLabel,
  disabled = false,
  size = "md",
  id,
  "aria-label": ariaLabel,
  freeText,
}: {
  /** "" = nothing selected. */
  value: string;
  onChange: (value: string) => void;
  options: SearchSelectOption[];
  placeholder?: string;
  /** When set, an empty choice with this label is offered (e.g. "Any partner"). */
  clearLabel?: string;
  disabled?: boolean;
  size?: "sm" | "md";
  id?: string;
  "aria-label"?: string;
  /**
   * Accept a typed value the options don't carry, offered as the last row.
   * For fields where the known list is a convenience rather than the rule —
   * without it, typing a name nothing matches dead-ends on "Nothing matches",
   * and the way out is a link elsewhere on the page that reads as unrelated.
   *
   * Return a string instead of an option to refuse this particular value and
   * say why, rather than offering a row that only fails on save.
   */
  freeText?: (query: string) => SearchSelectOption | string;
}) {
  const [query, setQuery] = useState("");

  const all = useMemo(
    () => (clearLabel ? [{ value: "", label: clearLabel }, ...options] : options),
    [clearLabel, options],
  );

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return all;
    return all.filter(
      (o) => o.label.toLowerCase().includes(needle) || o.code?.toLowerCase().includes(needle),
    );
  }, [all, query]);

  const selected = all.find((o) => o.value === value);

  // The typed value as its own choice: an option to accept it, a string saying why it
  // can't be, or null when there is nothing to offer.
  const typed = query.trim();
  const offered =
    freeText && typed !== "" && !all.some((o) => o.label === typed) ? freeText(typed) : null;

  return (
    <Combobox
      immediate
      value={value}
      disabled={disabled}
      onChange={(v: string | null) => onChange(v ?? "")}
      onClose={() => setQuery("")}
    >
      <div className="relative">
        <ComboboxInput
          id={id}
          aria-label={ariaLabel}
          placeholder={selected && selected.value !== "" ? undefined : (clearLabel ?? placeholder)}
          displayValue={(v: string) => (v === "" ? "" : (all.find((o) => o.value === v)?.label ?? ""))}
          onChange={(e) => setQuery(e.target.value)}
          className={`${size === "sm" ? "h-8 text-[13px]" : "h-9.5 text-sm"} w-full rounded-lg border border-ink-200 bg-white pr-8 pl-3 text-ink-900 placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none disabled:bg-ink-50 disabled:text-ink-500`}
        />
        <ComboboxButton className="absolute inset-y-0 right-0 flex w-8 cursor-pointer items-center justify-center text-ink-400 hover:text-ink-600">
          <ChevronsUpDown className="size-3.5" aria-hidden />
        </ComboboxButton>
      </div>
      <ComboboxOptions
        anchor={{ to: "bottom start", gap: 4 }}
        className="z-50 max-h-64 w-(--input-width) overflow-auto rounded-lg border border-ink-200 bg-white py-1 shadow-lg empty:hidden"
      >
        {filtered.map((o) => (
          <ComboboxOption
            key={o.value || "∅"}
            value={o.value}
            className="group flex cursor-pointer items-center gap-2 px-3 py-1.5 data-focus:bg-ink-50"
          >
            {o.render ?? (
              <>
                <span
                  className={`min-w-0 flex-1 truncate text-sm ${o.value === "" ? "text-ink-500 italic" : "text-ink-800"}`}
                >
                  {o.label}
                </span>
                {o.code && (
                  <code className="shrink-0 rounded bg-ink-100 px-1.5 py-0.5 font-mono text-[11px] text-ink-600">
                    {o.code}
                  </code>
                )}
                {o.hint && <span className="shrink-0 text-xs text-ink-400">{o.hint}</span>}
                <Check className="size-3.5 shrink-0 text-crimson-600 opacity-0 group-data-selected:opacity-100" aria-hidden />
              </>
            )}
          </ComboboxOption>
        ))}
        {offered !== null && typeof offered !== "string" && (
          <ComboboxOption
            value={query.trim()}
            className="group flex cursor-pointer items-center gap-2 border-t border-ink-100 px-3 py-1.5 first:border-t-0 data-focus:bg-ink-50"
          >
            {offered.render ?? (
              <span className="min-w-0 flex-1 truncate text-sm text-ink-800">{offered.label}</span>
            )}
          </ComboboxOption>
        )}
        {typeof offered === "string" && (
          <div className="border-t border-ink-100 px-3 py-2 text-sm text-danger-700 first:border-t-0">
            {offered}
          </div>
        )}
        {filtered.length === 0 && offered === null && (
          <div className="px-3 py-2 text-sm text-ink-400">Nothing matches “{query}”.</div>
        )}
      </ComboboxOptions>
    </Combobox>
  );
}
