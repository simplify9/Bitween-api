import { useState, type ReactNode } from "react";
import { ChevronDown, ChevronRight } from "lucide-react";

/**
 * Progressive disclosure: a subdued one-line summary that expands to its
 * full controls. Keeps rarely-touched or auto-derived fields visible but
 * out of the way (the "don't confuse the user" pattern). Uncontrolled by
 * default; pass `open`/`onOpenChange` to drive it (e.g. to force it open on
 * a failed submit).
 */
export function SummaryDisclosure({
  summary,
  changeLabel = "Change",
  defaultOpen = false,
  open: openProp,
  onOpenChange,
  children,
}: {
  /** The collapsed one-liner, shown after the chevron. */
  summary: ReactNode;
  /** Right-aligned affordance shown while collapsed. */
  changeLabel?: string;
  defaultOpen?: boolean;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  /** The controls revealed when expanded. */
  children: ReactNode;
}) {
  const [internal, setInternal] = useState(defaultOpen);
  const open = openProp ?? internal;
  const toggle = () => {
    const next = !open;
    if (onOpenChange) onOpenChange(next);
    else setInternal(next);
  };

  return (
    <div className="rounded-lg bg-ink-50 px-3 py-2.5">
      <button
        type="button"
        onClick={toggle}
        aria-expanded={open}
        className="flex w-full items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-700"
      >
        {open ? (
          <ChevronDown className="size-3.5 shrink-0" aria-hidden />
        ) : (
          <ChevronRight className="size-3.5 shrink-0" aria-hidden />
        )}
        <span className="min-w-0 truncate text-left">{summary}</span>
        {!open && <span className="ml-auto shrink-0 font-medium text-crimson-700">{changeLabel}</span>}
      </button>
      {open && <div className="mt-3 space-y-4">{children}</div>}
    </div>
  );
}
