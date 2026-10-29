import { useRef } from "react";

export interface SegmentedOption<T extends string> {
  value: T;
  label: string;
  /** Tooltip for this choice, for a label too short to explain itself. */
  title?: string;
}

/**
 * A joined row of buttons with one of them chosen.
 *
 * For a handful of options that has to stay readable inside a dense row. A
 * `Select` hides what is on offer behind a click, which is the wrong trade when
 * the point is seeing at a glance where a value comes from - and it costs two
 * clicks instead of one to change.
 *
 * A radiogroup rather than a tablist: choosing does not reveal a panel.
 */
export function SegmentedControl<T extends string>({
  options,
  value,
  onChange,
  label,
  size = "md",
  disabled = false,
  className = "",
}: {
  options: SegmentedOption<T>[];
  value: T;
  onChange: (value: T) => void;
  /** Names the group for a screen reader, e.g. "Where the value comes from". */
  label: string;
  size?: "sm" | "md";
  disabled?: boolean;
  className?: string;
}) {
  const group = useRef<HTMLDivElement>(null);

  // Arrow keys walk the group, which is what a radiogroup is expected to do - and
  // the reason each button is only in the tab order when it is the chosen one.
  const onKeyDown = (e: React.KeyboardEvent) => {
    const step = e.key === "ArrowRight" || e.key === "ArrowDown" ? 1 : e.key === "ArrowLeft" || e.key === "ArrowUp" ? -1 : 0;
    if (step === 0 || disabled) return;
    e.preventDefault();
    const at = options.findIndex((o) => o.value === value);
    const next = options[(at + step + options.length) % options.length];
    onChange(next.value);
    // Keep the focus ring on the group as the choice moves.
    group.current?.querySelector<HTMLButtonElement>(`[data-value="${next.value}"]`)?.focus();
  };

  const pad = size === "sm" ? "px-1.5 py-0.5 text-[10px]" : "px-2.5 py-1 text-xs";

  return (
    <div
      ref={group}
      role="radiogroup"
      aria-label={label}
      onKeyDown={onKeyDown}
      className={`inline-flex flex-shrink-0 overflow-hidden rounded border border-ink-200 font-medium ${
        disabled ? "opacity-50" : ""
      } ${className}`}
    >
      {options.map((option, i) => {
        const chosen = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={chosen}
            data-value={option.value}
            disabled={disabled}
            tabIndex={chosen ? 0 : -1}
            title={option.title}
            onClick={(e) => {
              // Rows select themselves on click; changing the source is not that.
              e.stopPropagation();
              onChange(option.value);
            }}
            className={`${pad} ${i > 0 ? "border-l border-ink-200" : ""} ${
              chosen
                ? "bg-ink-900 text-white"
                : "bg-white text-ink-500 hover:bg-ink-50 hover:text-ink-700"
            } disabled:cursor-not-allowed`}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
