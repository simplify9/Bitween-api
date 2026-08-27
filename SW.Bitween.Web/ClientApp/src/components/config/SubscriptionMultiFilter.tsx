import { useEffect, useRef, useState } from "react";
import { ChevronDown, Search } from "lucide-react";
import type { SubscriptionInfo } from "../../api";
import { SUBSCRIPTION_TYPE_LABELS } from "./shared";

/**
 * Pick any number of subscriptions, to filter a table down to the rows connected to at
 * least one of them — "which partners does this subscription use", read the other way.
 *
 * Modelled on `ReferenceMenu`'s local popover rather than the portalled `Popover`: a
 * filter row sits above the table, not inside its `overflow-x-auto` wrapper, so nothing
 * clips it and the lighter, in-flow panel is enough.
 */
export function SubscriptionMultiFilter({
  subscriptions,
  selected,
  onChange,
  label = "Filter by subscription",
}: {
  subscriptions: SubscriptionInfo[];
  selected: number[];
  onChange: (ids: number[]) => void;
  label?: string;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const needle = query.trim().toLowerCase();
  const matches = subscriptions.filter((i) => !needle || i.name.toLowerCase().includes(needle));
  const selectedSet = new Set(selected);

  const toggle = (id: number) =>
    onChange(selectedSet.has(id) ? selected.filter((x) => x !== id) : [...selected, id]);

  const selectedNames = subscriptions.filter((i) => selectedSet.has(i.id)).map((i) => i.name);
  const buttonLabel =
    selectedNames.length === 0
      ? "Any subscription"
      : selectedNames.length === 1
        ? selectedNames[0]
        : `${selectedNames.length} subscriptions`;

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-label={label}
        aria-expanded={open}
        title={selectedNames.length > 1 ? selectedNames.join(", ") : undefined}
        className={`flex h-9 w-full items-center gap-1.5 rounded-lg border px-3 text-sm ${
          selectedNames.length > 0
            ? "border-crimson-300 bg-crimson-50 text-crimson-800"
            : "border-ink-200 bg-white text-ink-600 hover:border-ink-300"
        }`}
      >
        <span className="min-w-0 flex-1 truncate text-left">{buttonLabel}</span>
        <ChevronDown className="size-3.5 shrink-0 text-ink-400" />
      </button>
      {open && (
        <div className="absolute top-full left-0 z-40 mt-1.5 w-72 rounded-xl border border-ink-100 bg-white p-2 shadow-lg">
          <div className="relative mb-1.5">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-ink-400" />
            <input
              autoFocus
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search subscriptions"
              aria-label="Search subscriptions"
              className="h-8 w-full rounded-md border border-ink-200 bg-white pr-2 pl-8 text-[13px] placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
            />
          </div>
          {selectedNames.length > 0 && (
            <button
              type="button"
              onClick={() => onChange([])}
              className="mb-1 block w-full rounded-md px-2 py-1 text-left text-[12.5px] font-medium text-crimson-700 hover:bg-crimson-50"
            >
              Clear {selectedNames.length} selected
            </button>
          )}
          <div className="max-h-60 overflow-y-auto">
            {matches.length === 0 ? (
              <p className="px-2 py-2 text-[13px] text-ink-400">No subscriptions match.</p>
            ) : (
              matches.map((i) => (
                <label
                  key={i.id}
                  className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-[13px] hover:bg-ink-50"
                >
                  <input
                    type="checkbox"
                    checked={selectedSet.has(i.id)}
                    onChange={() => toggle(i.id)}
                    className="size-3.5 shrink-0 cursor-pointer rounded accent-crimson-600"
                  />
                  <span className="min-w-0 flex-1 truncate text-ink-800">{i.name}</span>
                  <span className="shrink-0 text-[11px] text-ink-400">{SUBSCRIPTION_TYPE_LABELS[i.type]}</span>
                </label>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  );
}
