import { useMemo, useState } from "react";
import {
  ChevronDown,
  ChevronRight,
  FileCode2,
  FilePlus,
  Folder,
  FolderOpen,
  ListCollapse,
  Pencil,
  Trash2,
} from "lucide-react";
import { baseName, buildTree, pathProblem, type FolderNode } from "./fileTree";
import { fileTint, PALETTE, type WorkspaceTheme } from "./palette";

/** The file that names the adapter can't be removed or renamed: the build looks for it. */
const FIXED = new Set(["adapter.json"]);

export function Explorer({
  theme,
  paths,
  active,
  dirty,
  onOpen,
  onCreate,
  onRename,
  onRemove,
}: {
  theme: WorkspaceTheme;
  paths: string[];
  active: string | null;
  dirty: Set<string>;
  onOpen: (path: string) => void;
  onCreate: (path: string) => void;
  onRename: (from: string, to: string) => void;
  onRemove: (path: string) => void;
}) {
  const c = PALETTE[theme];
  const tree = useMemo(() => buildTree(paths), [paths]);
  const [closed, setClosed] = useState<Set<string>>(new Set());
  // A file being named: a new one (under a folder), or an existing one being renamed.
  const [naming, setNaming] = useState<{ mode: "new"; folder: string } | { mode: "rename"; path: string } | null>(null);
  const [name, setName] = useState("");
  const [error, setError] = useState("");

  const startNew = (folder: string) => {
    setNaming({ mode: "new", folder });
    setName(folder ? `${folder}/` : "");
    setError("");
  };
  const startRename = (path: string) => {
    setNaming({ mode: "rename", path });
    setName(path);
    setError("");
  };
  const finish = () => {
    if (!naming) return;
    const path = name.trim();
    if (naming.mode === "rename" && path === naming.path) return setNaming(null);
    const problem = pathProblem(path, paths);
    if (problem) return setError(problem);
    if (naming.mode === "new") onCreate(path);
    else onRename(naming.path, path);
    setNaming(null);
  };

  const nameInput = (
    <form
      className="px-2 py-1"
      onSubmit={(e) => {
        e.preventDefault();
        finish();
      }}
    >
      <input
        aria-label={naming?.mode === "rename" ? "New name" : "New file name"}
        autoFocus
        value={name}
        placeholder="helpers.py, or lib/helpers.py"
        onChange={(e) => {
          setName(e.target.value);
          setError("");
        }}
        onKeyDown={(e) => e.key === "Escape" && setNaming(null)}
        onBlur={() => !name.trim() && setNaming(null)}
        className={`h-7 w-full rounded border px-2 font-mono text-[12px] focus:border-crimson-500 focus:outline-none ${c.input}`}
      />
      {error && <p className="mt-1 text-[11.5px] text-crimson-400">{error}</p>}
    </form>
  );

  const renderFolder = (node: FolderNode, depth: number) => (
    <>
      {node.folders.map((folder) => {
        const open = !closed.has(folder.path);
        return (
          <li key={folder.path}>
            <button
              type="button"
              aria-expanded={open}
              onClick={() =>
                setClosed((prev) => {
                  const next = new Set(prev);
                  if (open) next.add(folder.path);
                  else next.delete(folder.path);
                  return next;
                })
              }
              style={{ paddingLeft: 8 + depth * 12 }}
              className={`flex w-full items-center gap-1 py-[3px] pr-2 text-left text-[13px] ${c.row}`}
            >
              {open ? <ChevronDown className="size-3.5 shrink-0" /> : <ChevronRight className="size-3.5 shrink-0" />}
              {open ? (
                <FolderOpen className="size-4 shrink-0 text-[#dcb67a]" />
              ) : (
                <Folder className="size-4 shrink-0 text-[#dcb67a]" />
              )}
              <span className="truncate">{folder.name}</span>
            </button>
            {open && <ul>{renderFolder(folder, depth + 1)}</ul>}
          </li>
        );
      })}
      {node.files.map((path) =>
        naming?.mode === "rename" && naming.path === path ? (
          <li key={path}>{nameInput}</li>
        ) : (
          <li key={path} className={`group flex items-center ${path === active ? c.rowActive : c.row}`}>
            <button
              type="button"
              aria-current={path === active ? "true" : undefined}
              onClick={() => onOpen(path)}
              onDoubleClick={() => !FIXED.has(path) && startRename(path)}
              style={{ paddingLeft: 8 + depth * 12 + 18 }}
              title={path}
              className="flex min-w-0 flex-1 items-center gap-1.5 py-[3px] text-left text-[13px]"
            >
              <FileCode2 className={`size-4 shrink-0 ${fileTint(path)}`} aria-hidden />
              <span className="truncate">{baseName(path)}</span>
            </button>
            {dirty.has(path) && (
              <span className="mr-2 size-2 shrink-0 rounded-full bg-current opacity-70" title="Unsaved" aria-hidden />
            )}
            {!FIXED.has(path) && (
              <span className="mr-1 hidden shrink-0 gap-0.5 group-focus-within:flex group-hover:flex">
                <button
                  type="button"
                  aria-label={`Rename ${path}`}
                  onClick={() => startRename(path)}
                  className={`rounded p-0.5 ${c.icon}`}
                >
                  <Pencil className="size-3.5" />
                </button>
                <button
                  type="button"
                  aria-label={`Remove ${path}`}
                  onClick={() => onRemove(path)}
                  className={`rounded p-0.5 ${c.icon}`}
                >
                  <Trash2 className="size-3.5" />
                </button>
              </span>
            )}
          </li>
        ),
      )}
    </>
  );

  return (
    <section aria-label="Files" className="flex h-full min-h-0 flex-col">
      <div
        className={`flex h-9 shrink-0 items-center justify-between pr-1.5 pl-4 text-[11px] font-semibold tracking-wider uppercase ${c.heading}`}
      >
        Explorer
        <span className="flex gap-0.5">
          <button
            type="button"
            aria-label="New file"
            title="New file"
            onClick={() => startNew("")}
            className={`rounded p-1 ${c.icon}`}
          >
            <FilePlus className="size-4" />
          </button>
          <button
            type="button"
            aria-label="Collapse folders"
            title="Collapse folders"
            onClick={() =>
              setClosed(new Set(paths.filter((p) => p.includes("/")).map((p) => p.slice(0, p.indexOf("/")))))
            }
            className={`rounded p-1 ${c.icon}`}
          >
            <ListCollapse className="size-4" />
          </button>
        </span>
      </div>
      <ul className="min-h-0 flex-1 overflow-y-auto pb-4">
        {renderFolder(tree, 0)}
        {naming?.mode === "new" && <li>{nameInput}</li>}
      </ul>
    </section>
  );
}
