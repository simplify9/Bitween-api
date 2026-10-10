import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import CodeMirror from "@uiw/react-codemirror";
import { EditorState, type Extension } from "@codemirror/state";
import { EditorView } from "@codemirror/view";
import { unifiedMergeView } from "@codemirror/merge";
import { FileCode, X } from "lucide-react";
import { api, type AdapterSourceFile } from "../../api";
import { compareListings, languageFor, type FileChange } from "../../lib/adapterSource";
import { keys } from "../../api/queryKeys";
import { Badge, LoadingBlock } from "../../components/ui/basics";

const editorTheme = EditorView.theme({
  "&": { maxHeight: "32rem", backgroundColor: "#ffffff", fontSize: "12.5px" },
  ".cm-scroller": { fontFamily: "var(--font-mono)", overflow: "auto" },
  "&.cm-focused": { outline: "none" },
});

const CHANGE_TONE = { added: "ok", removed: "danger", changed: "warn" } as const;

/**
 * The source a published version carries — any language — and, compared with another version,
 * what changed between them. Read-only: a published version never changes. Every file read is
 * recorded in the audit trail by the server.
 */
export default function AdapterSourceViewer({
  adapterId,
  label,
  versions,
  initialVersion,
  onClose,
}: {
  adapterId: string;
  label: string;
  /** Versions that carry source, newest first. */
  versions: string[];
  initialVersion: string;
  onClose: () => void;
}) {
  const [version, setVersion] = useState(initialVersion);
  const [baseVersion, setBaseVersion] = useState<string>("");
  const [selected, setSelected] = useState<string | null>(null);
  const [changedOnly, setChangedOnly] = useState(true);

  const listing = useQuery({
    queryKey: keys.adapterSource(adapterId, version),
    queryFn: () => api.getAdapterSource(adapterId, version),
  });
  const baseListing = useQuery({
    queryKey: keys.adapterSource(adapterId, baseVersion),
    queryFn: () => api.getAdapterSource(adapterId, baseVersion),
    enabled: baseVersion !== "",
  });

  const comparing = baseVersion !== "" && !!baseListing.data;
  const rows = useMemo(
    () => (listing.data ? compareListings(listing.data, comparing ? baseListing.data! : null) : []),
    [listing.data, baseListing.data, comparing],
  );
  const shownRows = comparing && changedOnly ? rows.filter((r) => r.change !== "unchanged") : rows;
  const current = selected && rows.some((r) => r.path === selected) ? selected : (shownRows[0]?.path ?? null);
  const change = rows.find((r) => r.path === current)?.change ?? null;

  return (
    <section
      aria-label={`Source of ${label}`}
      className="rounded-xl border border-ink-200 bg-white"
    >
      <header className="flex flex-wrap items-center gap-3 border-b border-ink-100 px-4 py-2.5">
        <h3 className="flex items-center gap-2 text-[13.5px] font-semibold text-ink-900">
          <FileCode className="size-4 text-ink-500" aria-hidden />
          Source
        </h3>
        <label className="flex items-center gap-1.5 text-[12.5px] text-ink-600">
          Version
          <select
            value={version}
            onChange={(e) => {
              setVersion(e.target.value);
              if (e.target.value === baseVersion) setBaseVersion("");
            }}
            className="rounded-md border border-ink-200 bg-white px-2 py-1 font-mono text-[12.5px]"
          >
            {versions.map((v) => (
              <option key={v} value={v}>
                v{v}
              </option>
            ))}
          </select>
        </label>
        <label className="flex items-center gap-1.5 text-[12.5px] text-ink-600">
          Compare with
          <select
            value={baseVersion}
            onChange={(e) => setBaseVersion(e.target.value)}
            className="rounded-md border border-ink-200 bg-white px-2 py-1 font-mono text-[12.5px]"
          >
            <option value="">Nothing</option>
            {versions
              .filter((v) => v !== version)
              .map((v) => (
                <option key={v} value={v}>
                  v{v}
                </option>
              ))}
          </select>
        </label>
        {comparing && (
          <label className="flex items-center gap-1.5 text-[12.5px] text-ink-600">
            <input
              type="checkbox"
              checked={changedOnly}
              onChange={(e) => setChangedOnly(e.target.checked)}
              className="accent-crimson-600"
            />
            Changed files only
          </label>
        )}
        <button
          type="button"
          onClick={onClose}
          aria-label="Close source"
          className="ml-auto rounded-md p-1 text-ink-500 hover:bg-ink-100 hover:text-ink-700"
        >
          <X className="size-4" />
        </button>
      </header>

      {listing.isPending ? (
        <LoadingBlock label="Reading the source…" />
      ) : listing.isError ? (
        <p className="px-4 py-3 text-[13px] text-danger-700">{listing.error.message}</p>
      ) : listing.data.files.length === 0 ? (
        <p className="px-4 py-3 text-[13px] text-ink-500">This version was published without its source.</p>
      ) : (
        <>
          {(listing.data.language || listing.data.buildCommand) && (
            <p className="border-b border-ink-100 px-4 py-1.5 text-[12px] text-ink-500">
              {listing.data.language}
              {listing.data.buildCommand && (
                <>
                  {listing.data.language ? " · built with " : "Built with "}
                  <code className="font-mono">{listing.data.buildCommand}</code>
                </>
              )}
            </p>
          )}
          <div className="flex min-h-64 flex-col md:flex-row">
            <ul
              aria-label="Source files"
              className="max-h-[32rem] shrink-0 overflow-auto border-b border-ink-100 py-1 md:w-64 md:border-r md:border-b-0"
            >
              {shownRows.length === 0 && (
                <li className="px-4 py-2 text-[12.5px] text-ink-500">No file changed between these versions.</li>
              )}
              {shownRows.map((r) => (
                <li key={r.path}>
                  <button
                    type="button"
                    aria-current={r.path === current ? "true" : undefined}
                    onClick={() => setSelected(r.path)}
                    className={`flex w-full items-center gap-2 px-4 py-1 text-left font-mono text-[12px] ${
                      r.path === current ? "bg-crimson-50 text-ink-900" : "text-ink-700 hover:bg-ink-50"
                    }`}
                  >
                    <span className="min-w-0 flex-1 truncate" title={r.path}>
                      {r.path}
                    </span>
                    {r.change && r.change !== "unchanged" && (
                      <Badge tone={CHANGE_TONE[r.change]}>{r.change}</Badge>
                    )}
                  </button>
                </li>
              ))}
            </ul>
            <div className="min-w-0 flex-1">
              {current && (
                <FilePane
                  key={`${version}|${comparing ? baseVersion : ""}|${current}`}
                  adapterId={adapterId}
                  path={current}
                  version={version}
                  baseVersion={comparing ? baseVersion : null}
                  change={change}
                />
              )}
            </div>
          </div>
        </>
      )}
    </section>
  );
}

function useSourceFile(adapterId: string, version: string | null, path: string, enabled: boolean) {
  return useQuery({
    queryKey: keys.adapterSourceFile(adapterId, version ?? "", path),
    queryFn: () => api.getAdapterSourceFile(adapterId, version!, path),
    enabled: enabled && !!version,
  });
}

function FilePane({
  adapterId,
  path,
  version,
  baseVersion,
  change,
}: {
  adapterId: string;
  path: string;
  version: string;
  baseVersion: string | null;
  change: FileChange | null;
}) {
  // Only what the comparison needs is read: an unchanged file is shown once, an added one has
  // nothing before it, a removed one nothing after.
  const after = useSourceFile(adapterId, version, path, change !== "removed");
  const before = useSourceFile(adapterId, baseVersion, path, change === "changed" || change === "removed");

  const pending = (change !== "removed" && after.isPending) || ((change === "changed" || change === "removed") && before.isPending);
  const error = after.error ?? before.error;
  if (error) return <p className="px-4 py-3 text-[13px] text-danger-700">{error.message}</p>;
  if (pending) return <LoadingBlock label={`Reading ${path}…`} />;

  const shown: AdapterSourceFile | undefined = change === "removed" ? before.data : after.data;
  if (shown?.binary || (change === "changed" && before.data?.binary)) {
    return (
      <p className="px-4 py-3 text-[13px] text-ink-500">
        {path} is not text, or too large to show ({shown?.size.toLocaleString()} bytes).
      </p>
    );
  }

  const content = change === "removed" ? "" : (after.data?.content ?? "");
  const original =
    change === "changed" || change === "removed" ? (before.data?.content ?? "") : change === "added" ? "" : null;

  return <SourceEditor path={path} content={content} original={original} />;
}

function SourceEditor({ path, content, original }: { path: string; content: string; original: string | null }) {
  const extensions = useMemo(() => {
    const list: Extension[] = [editorTheme, EditorState.readOnly.of(true), EditorView.editable.of(false)];
    const language = languageFor(path);
    if (language) list.push(language);
    if (original !== null)
      list.push(unifiedMergeView({ original, mergeControls: false, highlightChanges: true, gutter: true }));
    return list;
  }, [path, original]);

  return (
    <div data-testid="source-editor" data-path={path} data-diff={original !== null ? "true" : "false"}>
      <CodeMirror value={content} extensions={extensions} basicSetup={{ foldGutter: false }} editable={false} />
    </div>
  );
}
