import { useState } from "react";
import { ChevronDown, ChevronRight, Plus } from "lucide-react";
import {
  itemScopeOf,
  readablePaths,
  type DocumentNode,
} from "../../lib/nativeMapper/documentTree";
import type { OutputNode } from "../../lib/nativeMapper/outputTree";
import { useRulesDispatch } from "../../lib/nativeMapper/RulesEditorContext";
import type { EditorListRule, RuleId } from "../../lib/nativeMapper/types";
import { EntryRow } from "./EntryRow";
import { ListRow } from "./ListRow";
import { OutputRow } from "./OutputRow";
import type { SourcePaths } from "./ValueCell";

/** How deep lists may nest before the indentation stops being readable. */
const MAX_LIST_DEPTH = 3;

/**
 * Draws the output tree.
 *
 * Two things travel down with it. `prefix` is the branch segments a rule at this
 * point sits under, so a row can show its own name and still rebuild the whole
 * target. `scope` is the source node paths here are read against, which narrows at
 * every list — because the mapper hands a list's rules the current entry, not the
 * document.
 */
export function OutputTreeView({
  nodes,
  scope,
  root,
  prefix,
  listId,
  listDepth,
  indent,
}: {
  nodes: OutputNode[];
  scope: DocumentNode | null;
  /** The whole document, which a rule inside a list can also read from. */
  root: DocumentNode | null;
  prefix: string[];
  /** The list these rules belong to, or null at the top level. */
  listId: RuleId | null;
  listDepth: number;
  indent: number;
}) {
  return (
    <>
      {nodes.map((node) => (
        <TreeNode
          key={node.key}
          node={node}
          scope={scope}
          root={root}
          prefix={prefix}
          listId={listId}
          listDepth={listDepth}
          indent={indent}
        />
      ))}
    </>
  );
}

function TreeNode({
  node,
  scope,
  root,
  prefix,
  listId,
  listDepth,
  indent,
}: {
  node: OutputNode;
  scope: DocumentNode | null;
  root: DocumentNode | null;
  prefix: string[];
  listId: RuleId | null;
  listDepth: number;
  indent: number;
}) {
  const dispatch = useRulesDispatch();
  const [collapsed, setCollapsed] = useState(false);
  const pad = { paddingLeft: `${indent * 14}px` };

  // Inside a list, a rule may read the entry or the document, and they are
  // different lists of paths. At the top level they are the same thing, so the
  // second is left off rather than offered twice.
  const paths: SourcePaths = {
    entry: readablePaths(scope),
    document: scope === root ? null : readablePaths(root),
  };

  if (node.kind === "field" || node.kind === "item") {
    return (
      <div style={pad}>
        <OutputRow node={node} prefix={prefix} paths={paths} inList={listId !== null} />
      </div>
    );
  }

  if (node.kind === "branch") {
    return (
      <>
        <div style={pad}>
          <BranchHeader
            name={node.name}
            count={node.children.length}
            collapsed={collapsed}
            onToggle={() => setCollapsed((c) => !c)}
            // An object is not a rule of its own, so adding "under" it just means a
            // rule whose target starts with its name — and the empty last segment
            // is the name the user is about to type.
            onAddField={() =>
              dispatch({ type: "ADD_FIELD", listId, target: [...prefix, node.name, ""] })
            }
          />
        </div>
        {!collapsed && (
          // A named group, so it is possible to tell which object a row belongs
          // to — the indentation says it to the eye and nothing to a reader.
          <div role="group" aria-label={`Fields inside ${node.name}`}>
            <OutputTreeView
              nodes={node.children}
              scope={scope}
              root={root}
              prefix={[...prefix, node.name]}
              listId={listId}
              listDepth={listDepth}
              indent={indent + 1}
            />
          </div>
        )}
      </>
    );
  }

  if (node.kind === "entry") {
    const named =
      node.placement === "after" ? `closing entry ${node.position}` : `entry ${node.position}`;
    return (
      <>
        <div style={pad}>
          <EntryRow node={node} paths={paths} />
        </div>
        {node.entry.item === undefined && (
          <div role="group" aria-label={`Rules for ${named}`}>
            <OutputTreeView
              nodes={node.children}
              scope={scope}
              root={root}
              prefix={[]}
              listId={node.entry.id}
              listDepth={listDepth}
              indent={indent + 1}
            />
            <div style={{ paddingLeft: `${(indent + 1) * 14}px` }}>
              <AddRuleButtons
                listId={node.entry.id}
                canAddList={listDepth < MAX_LIST_DEPTH}
                inside={named}
              />
            </div>
          </div>
        )}
      </>
    );
  }

  return (
    <>
      <div style={pad}>
        <ListRow
          node={node}
          scope={scope}
          prefix={prefix}
          collapsed={collapsed}
          onToggleCollapsed={() => setCollapsed((c) => !c)}
        />
      </div>
      {!collapsed && (
        <div
          role="group"
          aria-label={`Rules for the list ${node.name || "at the root"}`}
        >
          {/* A fixed entry has no entry of its own, so its rules read what the list
              reads — which is why these are not given the item scope below. */}
          <OutputTreeView
            nodes={node.fixed}
            scope={scope}
            root={root}
            prefix={[]}
            listId={node.list.id}
            listDepth={listDepth}
            indent={indent + 1}
          />

          {/* Whatever a walked entry produces: the field rows of a record, or the one
              row of a plain value. A list that walks nothing has no walked entry, so
              it shows neither and is exactly the entries written into it. */}
          {node.list.over !== undefined && (
            <OutputTreeView
              nodes={node.children}
              scope={itemScopeOf(scope, node.list.over ?? "")}
              root={root}
              prefix={[]}
              listId={node.list.id}
              listDepth={listDepth + 1}
              indent={indent + 1}
            />
          )}
          {/* Written after everything above them, which is where a trailer goes — and the
              only point from which counting can see the whole list. */}
          <OutputTreeView
            nodes={node.after}
            scope={scope}
            root={root}
            prefix={[]}
            listId={node.list.id}
            listDepth={listDepth}
            indent={indent + 1}
          />

          <div style={{ paddingLeft: `${(indent + 1) * 14}px` }}>
            <AddRuleButtons
              listId={node.list.id}
              canAddList={listDepth + 1 < MAX_LIST_DEPTH}
              inside={node.name || "the root list"}
              onAddFixedEntry={() =>
                dispatch({ type: "ADD_FIXED_ENTRY", listId: node.list.id })
              }
              onAddClosingEntry={() =>
                dispatch({ type: "ADD_CLOSING_ENTRY", listId: node.list.id })
              }
              showPerEntryRules={node.list.over !== undefined && node.list.item === undefined}
              onAddValue={
                stillUndecided(node.list)
                  ? () => dispatch({ type: "MAKE_LIST_OF_VALUES", listId: node.list.id })
                  : undefined
              }
            />
          </div>
        </div>
      )}
    </>
  );
}

/**
 * Whether a list is still open to being made a list of plain values.
 *
 * Only while it holds nothing at all. A list's shape follows what is put in it, so
 * one that already has fields, nested lists or written entries has answered the
 * question — and offering to change it then would mean deciding what happens to
 * everything already there.
 */
function stillUndecided(list: EditorListRule): boolean {
  return (
    list.item === undefined &&
    list.fields.length === 0 &&
    list.lists.length === 0 &&
    list.fixed.length === 0
  );
}

function BranchHeader({
  name,
  count,
  collapsed,
  onToggle,
  onAddField,
}: {
  name: string;
  count: number;
  collapsed: boolean;
  onToggle: () => void;
  onAddField: () => void;
}) {
  return (
    <div className="group/branch flex items-center gap-1 py-0.5 pl-0.5">
      <button
        type="button"
        onClick={onToggle}
        aria-expanded={!collapsed}
        aria-label={`${collapsed ? "Expand" : "Collapse"} ${name}`}
        className="rounded p-0.5 text-ink-400 hover:bg-ink-100"
      >
        {collapsed ? <ChevronRight size={13} /> : <ChevronDown size={13} />}
      </button>
      <span className="font-mono text-[11px] font-semibold text-ink-600">{name}</span>
      <span className="text-ink-300" title="An object, made by the rules nested under it">
        {"{}"}
      </span>
      {collapsed && (
        <span className="text-[10px] text-ink-400">
          {count} {count === 1 ? "rule" : "rules"}
        </span>
      )}
      <button
        type="button"
        onClick={onAddField}
        aria-label={`Add a field inside ${name}`}
        title={`Add a field inside ${name}`}
        className="rounded p-0.5 text-ink-300 opacity-0 group-hover/branch:opacity-100 hover:bg-ink-100 hover:text-ink-600 focus:opacity-100"
      >
        <Plus size={11} />
      </button>
    </div>
  );
}

export function AddRuleButtons({
  listId,
  canAddList,
  inside,
  onAddFixedEntry,
  onAddClosingEntry,
  onAddValue,
  showPerEntryRules = true,
}: {
  listId: RuleId | null;
  canAddList: boolean;
  /** Named so the buttons say which list they add to. */
  inside?: string;
  /** Offered on a list, where an entry can be written into it. */
  onAddFixedEntry?: () => void;
  /**
   * Offered on a list, for an entry written after the walked ones.
   *
   * Separate from the leading one because a trailer is a different thing from a header, and
   * because it is the only place a rule can count what the list ended up holding.
   */
  onAddClosingEntry?: () => void;
  /**
   * Offered on a list that has not yet been made of anything.
   *
   * What a list holds is decided by what is put in it, so this sits beside Field and
   * List rather than being a setting behind a chevron — and once one of the three has
   * been used the other kind is no longer offered.
   */
  onAddValue?: () => void;
  /** False for a list that walks nothing: there are no per-entry rules to add. */
  showPerEntryRules?: boolean;
}) {
  const dispatch = useRulesDispatch();
  const where = inside ? ` to ${inside}` : "";

  return (
    <div className="flex flex-wrap gap-1.5 py-1">
      {onAddValue && (
        <button
          type="button"
          onClick={onAddValue}
          aria-label={`Add a value${where}`}
          title={'Every entry is one value rather than a record, e.g. ["A1", "B7"]'}
          className="flex items-center gap-1 rounded border border-ink-200 bg-white px-1.5 py-0.5 text-[11px] text-ink-600 hover:border-ink-300 hover:bg-ink-50"
        >
          <Plus size={11} /> Value
        </button>
      )}
      {onAddFixedEntry && (
        <button
          type="button"
          onClick={onAddFixedEntry}
          aria-label={`Add an entry${where}`}
          title="An entry written into the list, rather than one per entry of a source list"
          className="flex items-center gap-1 rounded border border-warn-300 bg-white px-1.5 py-0.5 text-[11px] text-warn-700 hover:bg-warn-100/50"
        >
          <Plus size={11} /> Entry
        </button>
      )}
      {onAddClosingEntry && (
        <button
          type="button"
          onClick={onAddClosingEntry}
          aria-label={`Add a closing entry${where}`}
          title="An entry written after the walked ones — a trailer, which is the only place a rule can count what the list holds"
          className="flex items-center gap-1 rounded border border-warn-300 bg-white px-1.5 py-0.5 text-[11px] text-warn-700 hover:bg-warn-100/50"
        >
          <Plus size={11} /> Closing entry
        </button>
      )}
      {showPerEntryRules && (
        <button
          type="button"
          onClick={() => dispatch({ type: "ADD_FIELD", listId })}
          aria-label={`Add a field${where}`}
          className="flex items-center gap-1 rounded border border-ink-200 bg-white px-1.5 py-0.5 text-[11px] text-ink-600 hover:border-ink-300 hover:bg-ink-50"
        >
          <Plus size={11} /> Field
        </button>
      )}
      {showPerEntryRules && canAddList && (
        <button
          type="button"
          onClick={() => dispatch({ type: "ADD_LIST", parentListId: listId })}
          aria-label={`Add a list${where}`}
          className="flex items-center gap-1 rounded border border-ink-200 bg-white px-1.5 py-0.5 text-[11px] text-ink-600 hover:border-ink-300 hover:bg-ink-50"
        >
          <Plus size={11} /> List
        </button>
      )}
    </div>
  );
}
