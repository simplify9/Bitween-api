import { Search } from "lucide-react";

/** The search field over a list. `label` names it for a screen reader and is its placeholder too. */
export function SearchBox({
  value,
  onChange,
  label,
  placeholder = label,
  className = "w-full max-w-xs",
}: {
  value: string;
  onChange: (text: string) => void;
  label: string;
  placeholder?: string;
  /** Width and placement in the toolbar. */
  className?: string;
}) {
  return (
    <div className={`relative ${className}`}>
      <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-500" />
      <input
        type="search"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label={label}
        className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-focus-400 focus:ring-2 focus:ring-focus-100 focus:outline-none"
      />
    </div>
  );
}
