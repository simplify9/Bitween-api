import React, { useState } from "react";
import { SquarePen, Trash2 } from "lucide-react";
import type { TreeNode } from "../../../lib/mapping/mappingPreview";
import {
  useMappingEditorDispatch,
  useMappingEditorState,
  addArrayMapping,
  openArrayModal,
  toggleNodeCollapsed,
  updateArrayMapping,
} from "../../../lib/mapping/MappingEditorContext";
import { getFullTargetPrefix } from "../../../lib/mapping/mappingTreeUtils";
import { OutputLeaf } from "./OutputLeaf";
import {
  initDraftFieldsFromNode,
  draftFieldsToRecord,
  recordToDraftFields,
} from "../../../lib/mapping/fixedItemHelpers";
import type { DraftField } from "../../../lib/mapping/fixedItemHelpers";
import { FixedItemFieldRows } from "./FixedItemPanel";

// ─── Types ────────────────────────────────────────────────────────────────────

export interface OutputBranchProps {
  node: TreeNode;
  depth?: number;
  sourcePaths: string[];
  typeMap: Record<string, 'string' | 'number' | 'boolean'>;
  onLeafRef?: (path: string, el: HTMLElement | null) => void;
  search?: string;
}

// ─── OutputBranch ─────────────────────────────────────────────────────────────

export const OutputBranch: React.FC<OutputBranchProps> = ({ node, depth = 0, sourcePaths, typeMap, onLeafRef, search }) => {
  const dispatch = useMappingEditorDispatch();
  const { collapsedNodes, arrayMappings, inputJson } = useMappingEditorState();
  const collapsed = collapsedNodes.includes(`out:${node.path}`);
  const isOpen = !collapsed;

  // ── Fixed-item inline panel state ────────────────────────────────────────
  const [fixedPanelOpen, setFixedPanelOpen] = useState(false);
  const [draftFields, setDraftFields] = useState<DraftField[]>([]);
  const [editingFixedIdx, setEditingFixedIdx] = useState<number | null>(null);
  const [editDraftFields, setEditDraftFields] = useState<DraftField[]>([]);

  // Is this node inside another array? (path like "lineOrders[*].lineItems")
  const isNestedArrayNode = node.type === 'array' && node.path.includes('[*].');

  // Is this the synthetic root-array node (output schema is [...])?
  const isRootArrayNode = node.key === 'root' && node.path === '[*]';

  // Find the parent AM for this node (when nested)
  const parentContainerPath = isNestedArrayNode
    ? node.path.substring(0, node.path.lastIndexOf('[*].') + 3) // e.g. "lineOrders[*]"
    : null;
  const parentAm = parentContainerPath
    ? arrayMappings.find((am) => {
        const fullPrefix = getFullTargetPrefix(am.id, arrayMappings);
        return `${fullPrefix}[*]` === parentContainerPath;
      })
    : null;

  // Relative target name for this node (last segment, no [*])
  const relativeTarget = isNestedArrayNode
    ? node.path.split('[*].').pop()?.replace('[*]', '') ?? node.key
    : isRootArrayNode ? '' : node.key;

  // Check if this node corresponds to an array mapping (top-level or nested)
  const arrayMapping = arrayMappings.find((am) => {
    const fullPrefix = getFullTargetPrefix(am.id, arrayMappings);
    // node.path already includes [*] suffix (e.g. "orders[*]" or "orders[*].items[*]")
    return `${fullPrefix}[*]` === node.path;
  });

  // Count nesting depth by number of [*] in the path — max 3 levels allowed
  const nestingDepth = (node.path.match(/\[\*\]/g) ?? []).length;
  const canConfigureLoop = nestingDepth <= 3;

  // Scalar (non-array) leaf paths from the root input JSON — for source field suggestions
  const inputScalarProps = React.useMemo(() => {
    try {
      const parsed = JSON.parse(inputJson);
      // For root arrays, expose scalar props from the first element
      const root: Record<string, unknown> = Array.isArray(parsed)
        ? (parsed.length > 0 && typeof parsed[0] === 'object' && parsed[0] !== null ? parsed[0] as Record<string, unknown> : {})
        : parsed as Record<string, unknown>;
      const collect = (obj: Record<string, unknown>, prefix: string): string[] => {
        if (obj === null || typeof obj !== 'object' || Array.isArray(obj)) {
          return prefix ? [prefix] : [];
        }
        return Object.entries(obj).flatMap(([k, v]) => {
          const path = prefix ? `${prefix}.${k}` : k;
          if (Array.isArray(v)) return [];
          if (v && typeof v === 'object') return collect(v as Record<string, unknown>, path);
          return [path];
        });
      };
      return collect(root, '');
    } catch {
      return [];
    }
  }, [inputJson]);

  const handleLoopClick = () => {
    dispatch(
      openArrayModal({
        id: arrayMapping?.id ?? '__new__',
        presetTarget: relativeTarget,
        parentArrayId: parentAm?.id ?? null,
        isRootOutput: isRootArrayNode,
      })
    );
  };

  const openFixedPanel = () => {
    setDraftFields(initDraftFieldsFromNode(node));
    setFixedPanelOpen(true);
  };

  const handleAddFixedItem = () => {
    const newItem = draftFieldsToRecord(draftFields);
    if (Object.keys(newItem).length === 0) return;

    const existing = arrayMapping?.fixedItems ?? [];
    const updated = [...existing, newItem];

    if (arrayMapping) {
      dispatch(updateArrayMapping({ id: arrayMapping.id, fixedItems: updated }));
    } else {
      dispatch(
        addArrayMapping({
          source: '',
          target: relativeTarget,
          alias: 'item',
          mappings: [],
          fixedItems: updated,
          parentArrayId: parentAm?.id ?? undefined,
          isRootOutput: isRootArrayNode || undefined,
        })
      );
    }
    setDraftFields(initDraftFieldsFromNode(node));
  };

  const handleUpdateFixedItem = (idx: number) => {
    const newItem = draftFieldsToRecord(editDraftFields);
    if (Object.keys(newItem).length === 0) return;
    const updated = [...arrayMapping!.fixedItems!];
    updated[idx] = newItem;
    dispatch(updateArrayMapping({ id: arrayMapping!.id, fixedItems: updated }));
    setEditingFixedIdx(null);
  };

  return (
    <div>
      <div className="flex items-center gap-1 px-2 py-[3px] rounded hover:bg-ink-50 group">
        <button
          onClick={() => dispatch(toggleNodeCollapsed(`out:${node.path}`))}
          className="flex items-center gap-1 flex-1 text-left"
        >
          <span className="text-ink-400 text-xs w-3 flex-shrink-0">{isOpen ? '▾' : '▸'}</span>
          <span className="text-xs font-medium text-ink-700 font-mono truncate">
            {isRootArrayNode ? <span className="text-crimson-500 italic">root array</span> : node.key}
          </span>
          {node.type === 'array' && (
            <span className="text-xs text-crimson-500 font-mono ml-0.5">[]</span>
          )}
          {node.type === 'array' && (arrayMapping?.fixedItems?.length ?? 0) > 0 && (
            <span
              className="ml-1 inline-flex items-center gap-0.5 text-[9px] font-semibold px-1 py-px rounded-full bg-ok-100 text-ok-600 border border-ok-100 leading-none"
              title={`${arrayMapping!.fixedItems!.length} fixed item${arrayMapping!.fixedItems!.length !== 1 ? 's' : ''}`}
            >
              <svg width="8" height="8" viewBox="0 0 16 16" fill="currentColor" className="flex-shrink-0">
                <path d="M9.5 0a.5.5 0 0 1 .5.5V2h1a2 2 0 0 1 2 2v1.5h.5a.5.5 0 0 1 0 1H13V13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6.5h-.5a.5.5 0 0 1 0-1H3V4a2 2 0 0 1 2-2h1V.5a.5.5 0 0 1 .5-.5h1z"/>
              </svg>
              {arrayMapping!.fixedItems!.length}
            </span>
          )}
          {node.type === 'object' && (
            <span className="text-xs text-ink-400 font-mono ml-0.5">{'{}'}</span>
          )}
        </button>

        {node.type === 'array' && canConfigureLoop && (
          <button
            className={`opacity-0 group-hover:opacity-100 text-xs border rounded px-1 transition ${
              isNestedArrayNode && (!parentAm || !parentAm.source)
                ? 'text-ink-300 border-ink-200 cursor-not-allowed'
                : 'text-crimson-500 hover:text-crimson-700 border-crimson-200'
            }`}
            onClick={isNestedArrayNode && (!parentAm || !parentAm.source) ? undefined : handleLoopClick}
            disabled={isNestedArrayNode && (!parentAm || !parentAm.source)}
            title={isNestedArrayNode && (!parentAm || !parentAm.source) ? 'Map the parent array first' : 'Configure array mapping + filter'}
          >
            ⚙ map
          </button>
        )}
        {node.type === 'array' && canConfigureLoop && (
          <button
            className={`opacity-0 group-hover:opacity-100 text-xs border rounded px-1 transition ${
              isNestedArrayNode && (!parentAm || !parentAm.source)
                ? 'text-ink-300 border-ink-200 cursor-not-allowed'
                : fixedPanelOpen
                  ? 'text-ok-600 border-ok-100 bg-ok-100'
                  : 'text-ok-600 hover:text-ok-600 border-ok-100'
            }`}
            onClick={isNestedArrayNode && (!parentAm || !parentAm.source) ? undefined : () => fixedPanelOpen ? setFixedPanelOpen(false) : openFixedPanel()}
            disabled={isNestedArrayNode && (!parentAm || !parentAm.source)}
            title={isNestedArrayNode && (!parentAm || !parentAm.source) ? 'Map the parent array first' : 'Add fixed item to this array'}
          >
            ＋ fixed
          </button>
        )}
      </div>

      {/* ── Inline fixed-item panel ────────────────────────────────────────── */}
      {node.type === 'array' && fixedPanelOpen && (
        <div className="mx-2 mb-1 rounded-lg border border-ok-100 bg-ok-100 px-3 py-2 space-y-2 text-xs">
          {/* Existing fixed items */}
          {(arrayMapping?.fixedItems?.length ?? 0) > 0 && (
            <div className="space-y-1">
              <span className="text-[10px] font-semibold text-ok-600 uppercase tracking-wide">Existing fixed items</span>
              {arrayMapping!.fixedItems!.map((item, idx) => (
                <div key={idx} className="rounded border border-ok-100 bg-white">
                  {editingFixedIdx === idx ? (
                    /* ── Edit form ── */
                    <div className="px-2 py-1.5 space-y-1.5">
                      <FixedItemFieldRows
                        fields={editDraftFields}
                        onFieldsChange={setEditDraftFields}
                        parentNode={node}
                        depth={0}
                        inputScalarProps={inputScalarProps}
                        idPrefix={`edit-${node.path}-${idx}`}
                        typeMap={typeMap}
                      />
                      <div className="flex gap-1 pt-0.5">
                        <button
                          className="flex-1 text-[11px] bg-ok-600 hover:bg-ok-600 text-white rounded py-0.5 transition"
                          onClick={() => handleUpdateFixedItem(idx)}
                        >Update</button>
                        <button
                          className="flex-1 text-[11px] border border-ink-200 hover:bg-ink-50 text-ink-500 rounded py-0.5 transition"
                          onClick={() => setEditingFixedIdx(null)}
                        >Cancel</button>
                      </div>
                    </div>
                  ) : (
                    /* ── Compact summary row ── */
                    <div className="flex items-center justify-between px-2 py-1 font-mono text-[10px] text-ink-600">
                      <span className="truncate">{JSON.stringify(item)}</span>
                      <div className="flex items-center gap-1 ml-2 flex-shrink-0">
                        <button
                          className="text-ink-300 hover:text-crimson-500 transition"
                          onClick={() => { setEditDraftFields(recordToDraftFields(item as Record<string, unknown>, node)); setEditingFixedIdx(idx); }}
                        >
                          <SquarePen size={12} />
                        </button>
                        <button
                          className="text-ink-300 hover:text-crimson-500 transition"
                          onClick={() =>
                            dispatch(updateArrayMapping({
                              id: arrayMapping!.id,
                              fixedItems: arrayMapping!.fixedItems!.filter((_, i) => i !== idx),
                            }))
                          }
                        >
                          <Trash2 size={12} />
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}

          {/* New item form */}
          <div className="space-y-1.5">
            <span className="text-[10px] font-semibold text-ok-600 uppercase tracking-wide">New fixed item</span>
            <FixedItemFieldRows
              fields={draftFields}
              onFieldsChange={setDraftFields}
              parentNode={node}
              depth={0}
              inputScalarProps={inputScalarProps}
              idPrefix={`new-${node.path}`}
              typeMap={typeMap}
            />

            <button
              className="mt-1 w-full text-[11px] bg-ok-600 hover:bg-ok-600 text-white rounded py-1 transition"
              onClick={handleAddFixedItem}
            >
              Add fixed item
            </button>
          </div>
        </div>
      )}

      {isOpen && (
        <div className="pl-3 border-l border-ink-100 ml-3">
          {node.children.map((child) =>
            child.type === 'leaf' ? (
              <OutputLeaf
                key={child.path}
                node={child}
                sourcePaths={sourcePaths}
                typeMap={typeMap}
                onLeafRef={onLeafRef}
                isSearchMatch={Boolean(search && child.key.toLowerCase().includes(search.toLowerCase()))}
              />
            ) : (
              <OutputBranch
                key={child.path}
                node={child}
                depth={depth + 1}
                sourcePaths={sourcePaths}
                typeMap={typeMap}
                onLeafRef={onLeafRef}
                search={search}
              />
            )
          )}
        </div>
      )}
    </div>
  );
};
