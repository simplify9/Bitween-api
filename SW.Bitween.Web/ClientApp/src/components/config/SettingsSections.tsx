import { useEffect, useId, useState, type ReactNode } from "react";
import { ChevronRight } from "lucide-react";
import { isLongHelp } from "./propertyLabels";

/**
 * A setting's hint: its key, quietly, then its help.
 *
 * The label above is the humanised name, but the key is what the adapter reads, what is stored and
 * what docs and other operators say, so it stays on screen. Help longer than a couple of lines is
 * clamped with a "more": an HTTP handler's LoginBody explained itself over four lines and pushed
 * every field after it down.
 */
export function SettingHint({ rawKey, label, help }: { rawKey: string; label: string; help?: string | null }) {
  const [expanded, setExpanded] = useState(false);
  const showKey = rawKey !== label;
  if (!showKey && !help) return null;
  const long = !!help && isLongHelp(help);
  return (
    <>
      <span className={long && !expanded ? "line-clamp-2" : "whitespace-pre-line"}>
        {showKey && (
          <span className="font-mono text-[11.5px] text-ink-500" title="The setting's key, as the adapter reads it">
            {rawKey}
          </span>
        )}
        {showKey && help && <span className="text-ink-300"> · </span>}
        {help}
      </span>
      {long && (
        <button type="button" className="link text-[12px]" aria-expanded={expanded} onClick={() => setExpanded((e) => !e)}>
          {expanded ? "less" : "more"}
        </button>
      )}
    </>
  );
}

/**
 * One collapsible group of optional settings. It says in its header what is set, so a closed
 * section still answers "did anyone change this?", and it opens by itself when something in it
 * has a value - including one that arrives after the first render, as a saved subscription's do.
 * Once open it stays open while its last value is cleared, rather than folding up under the cursor.
 */
export function SettingsSection({
  title,
  summary,
  count,
  hasValue,
  children,
}: {
  title: string;
  summary: string;
  count: number;
  hasValue: boolean;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(hasValue);
  const bodyId = useId();
  useEffect(() => {
    if (hasValue) setOpen(true);
  }, [hasValue]);

  return (
    <div className="rounded-lg border border-ink-100">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={open ? bodyId : undefined}
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left hover:bg-ink-50"
      >
        <ChevronRight
          className={`size-4 shrink-0 text-ink-500 transition-transform ${open ? "rotate-90" : ""}`}
          aria-hidden
        />
        <span className="shrink-0 text-[13px] font-medium text-ink-800">{title}</span>
        <span className="min-w-0 flex-1 truncate text-[12px] text-ink-500">
          {open ? `${count} setting${count === 1 ? "" : "s"}` : summary}
        </span>
      </button>
      {open && (
        <div id={bodyId} className="border-t border-ink-100 px-3 pt-3 pb-3.5">
          {children}
        </div>
      )}
    </div>
  );
}
