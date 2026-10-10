import { useState } from "react";
import { ChevronDown, ChevronRight, Search } from "lucide-react";
import {
  describeSample,
  type Coverage,
  type DocumentNode,
} from "../../lib/nativeMapper/documentTree";
import { useRules, useRulesDispatch } from "../../lib/nativeMapper/rulesEditorHooks";
import { FormatButton } from "../ui/FormatButton";
import { TextInput } from "../ui/forms";
import { RowSelect } from "./rowControls";
import { DATE_ORDERS, type DateOrderName } from "../../lib/nativeMapper/types";

/**
 * The shape of the source document, as a tree of paths a rule can read.
 *
 * A list's own contents are shown, named relative to one entry — `sku`, not
 * `order.line.sku` — because that is what a rule inside a list reading that list
 * writes. Dragging one onto a rule outside such a list produces a path that names
 * nothing, and the rule says so rather than the tree pretending the field is not
 * there at all.
 */
export function SourcePanel({
  root,
  parseError,
  assignedPaths,
  coverage,
  onPick,
}: {
  root: DocumentNode | null;
  parseError: string | null;
  assignedPaths: Set<string>;
  /** Coverage per object and list path, built once for the whole tree. */
  coverage: Map<string, Coverage>;
  /** Called when a path is clicked, to fill whichever rule is selected. */
  onPick: (path: string) => void;
}) {
  const { searchSource, sourceSample, rules } = useRules();
  const dispatch = useRulesDispatch();

  return (
    <div className="flex w-[30%] min-w-[260px] max-w-[400px] flex-col overflow-hidden border-r border-ink-200">
      <div className="flex-shrink-0 border-b border-ink-100 bg-ink-50 px-3 py-2">
        <div className="mb-1.5 flex items-center gap-2">
          <span className="text-xs font-semibold tracking-wide text-ink-500 uppercase">Source</span>
          <span
            className="rounded bg-ink-100 px-1.5 py-0.5 font-mono text-[10px] text-ink-600"
            title="The format of the document arriving from the partner"
          >
            {rules.sourceFormat}
          </span>
        </div>
        <div className="relative">
          <Search className="absolute top-1.5 left-2 text-ink-500" size={11} />
          <TextInput
            className="h-7 pl-6 font-mono text-xs"
            placeholder="Search fields…"
            value={searchSource}
            onChange={(e) => dispatch({ type: "SET_SEARCH_SOURCE", text: e.target.value })}
          />
        </div>
      </div>

      <div className="flex-shrink-0 border-b border-ink-100 px-3 py-2">
        <textarea
          className="min-h-[72px] w-full resize-y rounded-lg border border-ink-200 bg-ink-50 px-2 py-1.5 font-mono text-[11px] focus:border-focus-400 focus:outline-none"
          placeholder='Paste a sample document, e.g. { "order": { "customer": "Ali" } }'
          value={sourceSample}
          onChange={(e) => dispatch({ type: "SET_SOURCE_SAMPLE", text: e.target.value })}
          aria-label="Sample source document"
        />
        <div className="flex items-center gap-2">
          {parseError && <p className="mt-1 text-[11px] text-danger-700">{parseError}</p>}
          <FormatButton
            className="mt-1 ml-auto"
            value={sourceSample}
            onChange={(text) => dispatch({ type: "SET_SOURCE_SAMPLE", text })}
          />
        </div>

        {/* Part of the mapping, unlike the sample above it: how this partner writes
            dates is a fact about their documents, and it is the same for every field —
            so it is asked once here rather than on each rule that formats a date. */}
        <label className="mt-1.5 flex items-center gap-1.5 text-[11px] text-ink-500">
          Dates
          <RowSelect
            className="min-w-0 flex-1"
            aria-label="Dates in the incoming document"
            title="Which of the day and the month comes first. Year-first needs no setting."
            value={rules.sourceDateOrder ?? "yearFirst"}
            onChange={(e) =>
              dispatch({ type: "SET_DATE_ORDER", order: e.target.value as DateOrderName })
            }
            options={DATE_ORDERS}
          />
        </label>

        <p className="mt-1 text-[11px] text-ink-500">
          The sample is used only here, to show the fields and the preview. The mapping never
          reads it.
        </p>
      </div>

      <div data-mapper-scroll className="flex-1 overflow-y-auto py-1">
        {!root ? (
          <p className="px-3 py-4 text-xs text-ink-500">
            Paste a sample document to see its fields.
          </p>
        ) : (
          <NodeRows
            nodes={root.kind === "object" ? root.children : [root]}
            depth={0}
            search={searchSource.trim().toLowerCase()}
            assignedPaths={assignedPaths}
            coverage={coverage}
            onPick={onPick}
          />
        )}
      </div>
    </div>
  );
}

function NodeRows({
  nodes,
  depth,
  search,
  assignedPaths,
  coverage,
  onPick,
}: {
  nodes: DocumentNode[];
  depth: number;
  search: string;
  assignedPaths: Set<string>;
  coverage: Map<string, Coverage>;
  onPick: (path: string) => void;
}) {
  return (
    <>
      {nodes.map((node) => (
        <NodeRow
          key={node.path || node.key || "root"}
          node={node}
          depth={depth}
          search={search}
          assignedPaths={assignedPaths}
          coverage={coverage}
          onPick={onPick}
        />
      ))}
    </>
  );
}

function NodeRow({
  node,
  depth,
  search,
  assignedPaths,
  coverage,
  onPick,
}: {
  node: DocumentNode;
  depth: number;
  search: string;
  assignedPaths: Set<string>;
  coverage: Map<string, Coverage>;
  onPick: (path: string) => void;
}) {
  const dispatch = useRulesDispatch();
  const { hoveredPath } = useRules();
  const [folded, setFolded] = useState(false);

  const matches = search.length > 0 && node.path.toLowerCase().includes(search);
  const dim = search.length > 0 && !matches && !hasDescendantMatch(node, search);
  const pad = { paddingLeft: `${depth * 12 + 12}px` };

  if (node.kind === "value") {
    const assigned = assignedPaths.has(node.path);
    return (
      <button
        type="button"
        style={pad}
        // Dragging is the quick way to wire a field up; clicking still fills
        // whichever rule is selected, for anyone who would rather not drag.
        draggable
        onDragStart={(e) => {
          e.dataTransfer.setData("text/plain", node.path);
          e.dataTransfer.effectAllowed = "link";
        }}
        // How the connection canvas finds this end of the line.
        data-source-path={node.path}
        onClick={() => onPick(node.path)}
        onMouseEnter={() => dispatch({ type: "HOVER_PATH", path: node.path })}
        onMouseLeave={() => dispatch({ type: "HOVER_PATH", path: null })}
        // The row shows only the last segment, because the indentation carries the
        // rest. That leaves the accessible name as a bare "customer", which is not
        // enough to tell one field from another — so the name is the full path.
        aria-label={node.path}
        title={`${node.path} — drag it onto a rule, or click to fill the selected one`}
        className={`flex w-full items-center gap-2 py-0.5 pr-2 text-left font-mono text-[11px] hover:bg-ink-50 ${
          dim ? "opacity-40" : ""
        } ${hoveredPath === node.path ? "bg-crimson-50" : ""}`}
      >
        <span
          aria-hidden
          className={`size-1.5 flex-shrink-0 rounded-full ${assigned ? "bg-ok-600" : "bg-ink-300"}`}
        />
        <span className={`truncate ${matches ? "font-semibold text-crimson-700" : "text-ink-800"}`}>
          {node.key}
        </span>
        <span className="ml-auto flex-shrink-0 truncate text-ink-500">
          {describeSample(node.sample)}
        </span>
      </button>
    );
  }

  const isList = node.kind === "list";
  const covered = coverage.get(node.path) ?? { mapped: 0, total: 0 };

  return (
    <div className={dim ? "opacity-40" : ""}>
      <button
        type="button"
        style={pad}
        onClick={() => setFolded((f) => !f)}
        aria-expanded={!folded}
        aria-label={`${folded ? "Expand" : "Collapse"} ${node.path || "the document"}`}
        className="flex w-full items-center gap-1.5 py-0.5 pr-2 text-left font-mono text-[11px] text-ink-600 hover:bg-ink-50"
      >
        {folded ? (
          <ChevronRight size={11} className="flex-shrink-0 text-ink-500" aria-hidden />
        ) : (
          <ChevronDown size={11} className="flex-shrink-0 text-ink-500" aria-hidden />
        )}
        <span className="truncate font-semibold">{node.key || "(root)"}</span>
        {isList ? (
          <span
            className="flex-shrink-0 rounded bg-warn-100 px-1 text-[9px] font-semibold tracking-wide text-warn-700 uppercase"
            title="A list. Add a list on the output side to walk it — the fields below are read one entry at a time."
          >
            list · {node.count}
          </span>
        ) : (
          <span className="flex-shrink-0 text-ink-500" title="An object">
            {"{}"}
          </span>
        )}
        {covered.total > 0 && (
          <span
            className="ml-auto flex-shrink-0 text-[10px] text-ink-500"
            title={`${covered.mapped} of ${covered.total} fields in here are read by a rule`}
          >
            {covered.mapped}/{covered.total}
          </span>
        )}
      </button>

      {/* A list's fields are shown too, and named the way a rule inside that list
          names them — `sku`, not `order.line.sku`. Hiding them meant the only way to
          see what was in a list was to paste the sample somewhere else. */}
      {!folded && (
        <NodeRows
          nodes={node.children}
          depth={depth + 1}
          search={search}
          assignedPaths={assignedPaths}
          coverage={coverage}
          onPick={onPick}
        />
      )}
    </div>
  );
}

function hasDescendantMatch(node: DocumentNode, search: string): boolean {
  return node.children.some(
    (child) => child.path.toLowerCase().includes(search) || hasDescendantMatch(child, search),
  );
}
