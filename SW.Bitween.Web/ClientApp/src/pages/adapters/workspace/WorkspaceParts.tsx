import { useMemo, useRef, useState } from "react";
import { Combobox, ComboboxInput, ComboboxOption, ComboboxOptions } from "@headlessui/react";
import { AlertTriangle, ChevronRight, CircleX, FileCode2, Info, Moon, Sun, X } from "lucide-react";
import type { CheckFinding, EditorProblem } from "../../../components/code/problems";
import { useModalFocus } from "../../../components/ui/useModalFocus";
import { baseName, folderOf } from "./fileTree";
import { fileTint, PALETTE, type WorkspaceTheme } from "./palette";

/** The open files, as tabs over the editor, with the open one's path as a breadcrumb under them. */
export function EditorTabs({
  theme,
  tabs,
  active,
  dirty,
  onSelect,
  onClose,
}: {
  theme: WorkspaceTheme;
  tabs: string[];
  active: string | null;
  dirty: Set<string>;
  onSelect: (path: string) => void;
  onClose: (path: string) => void;
}) {
  const c = PALETTE[theme];
  return (
    <div className="shrink-0">
      <div role="tablist" aria-label="Open files" className={`flex h-9 overflow-x-auto border-b ${c.bar}`}>
        {tabs.map((path) => {
          const selected = path === active;
          return (
            <div
              key={path}
              role="tab"
              aria-selected={selected}
              tabIndex={0}
              title={path}
              onClick={() => onSelect(path)}
              onKeyDown={(e) => (e.key === "Enter" || e.key === " ") && onSelect(path)}
              // A middle click closes, as in VS Code.
              onAuxClick={(e) => e.button === 1 && onClose(path)}
              className={`group relative flex shrink-0 cursor-pointer items-center gap-1.5 border-r pr-1.5 pl-3 text-[13px] ${c.divider} ${
                selected ? c.tabActive : c.tab
              }`}
            >
              {selected && <span className="absolute inset-x-0 top-0 h-px bg-crimson-500" aria-hidden />}
              <FileCode2 className={`size-3.5 ${fileTint(path)}`} aria-hidden />
              <span className="whitespace-nowrap">{baseName(path)}</span>
              <button
                type="button"
                aria-label={`Close ${path}`}
                onClick={(e) => {
                  e.stopPropagation();
                  onClose(path);
                }}
                className={`grid size-5 place-items-center rounded ${c.icon}`}
              >
                {dirty.has(path) ? (
                  <>
                    <span className="size-2 rounded-full bg-current group-hover:hidden" aria-hidden />
                    <X className="hidden size-3.5 group-hover:block" />
                  </>
                ) : (
                  <X className={`size-3.5 ${selected ? "" : "opacity-0 group-hover:opacity-100"}`} />
                )}
              </button>
            </div>
          );
        })}
      </div>
      {active && (
        <div
          className={`flex h-6 items-center gap-1 px-3 text-[12px] ${c.muted} ${theme === "dark" ? "bg-[#1e1e1e]" : "bg-white"}`}
        >
          {[...folderOf(active).split("/").filter(Boolean), baseName(active)].map((part, i, all) => (
            <span key={i} className="flex items-center gap-1">
              {i > 0 && <ChevronRight className="size-3" aria-hidden />}
              <span className={i === all.length - 1 ? (theme === "dark" ? "text-[#cccccc]" : "text-ink-800") : ""}>
                {part}
              </span>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

const SEVERITY_ICON = {
  error: <CircleX className="size-3.5 shrink-0 text-[#f14c4c]" aria-label="Error" />,
  warning: <AlertTriangle className="size-3.5 shrink-0 text-[#cca700]" aria-label="Warning" />,
  info: <Info className="size-3.5 shrink-0 text-[#3794ff]" aria-label="Note" />,
};

/**
 * Everything wrong with the draft as far as anyone can tell: what the last Check said, and what the
 * editor's language services see as you type. A finding that names a line opens it.
 */
export function ProblemsPanel({
  theme,
  findings,
  live,
  onReveal,
  onClose,
}: {
  theme: WorkspaceTheme;
  findings: CheckFinding[];
  live: EditorProblem[];
  onReveal: (at: { path: string; line: number; column: number }) => void;
  onClose: () => void;
}) {
  const c = PALETTE[theme];
  const rows = [
    ...findings.map((f) => ({ ...f, source: "Check" })),
    ...live.map((p) => ({
      message: p.message,
      severity: p.severity,
      source: p.source,
      at: { path: p.path, line: p.line, column: p.column },
    })),
  ];
  return (
    <section
      aria-label="Problems"
      className={`flex h-full min-h-0 flex-col border-t ${c.divider} ${theme === "dark" ? "bg-[#181818]" : "bg-white"}`}
    >
      <div
        className={`flex h-8 shrink-0 items-center justify-between px-3 text-[11px] font-semibold tracking-wider uppercase ${c.heading}`}
      >
        <span>
          Problems <span className="ml-1 rounded-full bg-current/15 px-1.5 py-px font-medium">{rows.length}</span>
        </span>
        <button type="button" aria-label="Close problems" onClick={onClose} className={`rounded p-0.5 ${c.icon}`}>
          <X className="size-4" />
        </button>
      </div>
      <ul className="min-h-0 flex-1 overflow-y-auto pb-2 font-mono text-[12px]">
        {rows.length === 0 && (
          <li className={`px-4 py-2 font-sans text-[13px] ${c.muted}`}>
            No problems found in the open files. Check the draft to build it.
          </li>
        )}
        {rows.map((r, i) => (
          <li key={i}>
            <button
              type="button"
              disabled={!r.at}
              onClick={() => r.at && onReveal(r.at)}
              className={`flex w-full items-start gap-2 px-4 py-1 text-left ${r.at ? c.row : ""}`}
            >
              {SEVERITY_ICON[r.severity]}
              <span className="min-w-0 flex-1 whitespace-pre-wrap">{r.message}</span>
              <span className={`shrink-0 ${c.muted}`}>
                {r.source}
                {r.at && ` · ${baseName(r.at.path)}:${r.at.line}`}
              </span>
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** ⌘P: open a file of the draft by typing part of its name. */
export function QuickOpen({
  paths,
  onOpen,
  onClose,
}: {
  paths: string[];
  onOpen: (path: string) => void;
  onClose: () => void;
}) {
  const [query, setQuery] = useState("");
  const panel = useRef<HTMLDivElement>(null);
  useModalFocus(panel);
  const shown = useMemo(() => {
    const q = query.toLowerCase();
    return paths.filter((p) => p.toLowerCase().includes(q));
  }, [paths, query]);
  return (
    <div
      className="fixed inset-0 z-50 flex justify-center bg-ink-950/30 pt-[10vh]"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        ref={panel}
        tabIndex={-1}
        role="dialog"
        aria-label="Go to file"
        className="h-fit w-full max-w-lg overflow-hidden rounded-xl bg-white shadow-2xl outline-none"
      >
        <Combobox
          onChange={(path: string | null) => {
            if (!path) return;
            onOpen(path);
            onClose();
          }}
        >
          <ComboboxInput
            autoFocus
            aria-label="File name"
            placeholder="Go to file…"
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => e.key === "Escape" && onClose()}
            className="h-11 w-full border-b border-ink-100 px-4 font-mono text-[13px] focus:outline-none"
          />
          <ComboboxOptions static className="max-h-80 overflow-y-auto p-1.5">
            {shown.map((path) => (
              <ComboboxOption
                key={path}
                value={path}
                className="flex cursor-pointer items-center gap-2 rounded-md px-2.5 py-1.5 text-[13px] data-focus:bg-ink-50"
              >
                <FileCode2 className={`size-4 ${fileTint(path)}`} aria-hidden />
                <span className="font-medium text-ink-900">{baseName(path)}</span>
                <span className="truncate text-[12px] text-ink-500">{folderOf(path)}</span>
              </ComboboxOption>
            ))}
            {shown.length === 0 && <p className="px-3 py-3 text-[13px] text-ink-500">No file matches.</p>}
          </ComboboxOptions>
        </Combobox>
      </div>
    </div>
  );
}

/** The strip along the bottom: what's wrong, whether it's saved, where the cursor is, in what. */
export function StatusBar({
  theme,
  errors,
  warnings,
  dirty,
  saving,
  cursor,
  language,
  onProblems,
  onTheme,
}: {
  theme: WorkspaceTheme;
  errors: number;
  warnings: number;
  dirty: boolean;
  saving: boolean;
  cursor: { line: number; column: number } | null;
  language: string;
  onProblems: () => void;
  onTheme: () => void;
}) {
  return (
    <div className="flex h-6 shrink-0 items-center gap-1 bg-crimson-700 px-2 text-[12px] text-white">
      <button
        type="button"
        onClick={onProblems}
        className="flex items-center gap-2 rounded px-1.5 hover:bg-white/15"
        aria-label={`${errors} errors, ${warnings} warnings`}
      >
        <span className="flex items-center gap-1">
          <CircleX className="size-3.5" aria-hidden /> {errors}
        </span>
        <span className="flex items-center gap-1">
          <AlertTriangle className="size-3.5" aria-hidden /> {warnings}
        </span>
      </button>
      <span className="px-1.5">{saving ? "Saving…" : dirty ? "Unsaved changes" : "All changes saved"}</span>
      <span className="ml-auto px-1.5">{cursor ? `Ln ${cursor.line}, Col ${cursor.column}` : ""}</span>
      <span className="px-1.5">UTF-8</span>
      <span className="px-1.5">{language}</span>
      <button
        type="button"
        onClick={onTheme}
        aria-label={theme === "dark" ? "Light editor" : "Dark editor"}
        title={theme === "dark" ? "Light editor" : "Dark editor"}
        className="rounded px-1.5 py-0.5 hover:bg-white/15"
      >
        {theme === "dark" ? <Sun className="size-3.5" /> : <Moon className="size-3.5" />}
      </button>
    </div>
  );
}
