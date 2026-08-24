import type React from "react";
import type { TreeNode } from "../../../lib/mapping/mappingPreview";
import type { MappingMode, PrimitiveArrayItem } from "../../../lib/mapping/types";
import { ModeToggleButtons } from "../ModeToggleButtons";
import { LeafValueInput } from "../LeafValueInput";
import type { GlobalValuesSetRow } from "../../../api";
import { MODE_INITIAL_FIELDS } from "../../../lib/mapping/mappingModeDefaults";
import { usePrimitiveArrayLeaf } from "./usePrimitiveArrayLeaf";

// ─── Types ────────────────────────────────────────────────────────────────────

export interface PrimitiveArrayLeafProps {
  node: TreeNode;
  sourcePaths: string[];
  primitiveArrayValues: unknown[];
  primAmId: string | undefined;
  currentItems: PrimitiveArrayItem[];
  partnerAdapterProperties: Record<string, string>;
  allGlobalSets: GlobalValuesSetRow[];
  onLeafRef?: (path: string, el: HTMLElement | null) => void;
  isSearchMatch?: boolean;
}

// ─── PrimitiveArrayLeaf ───────────────────────────────────────────────────────
// Handles a target field that is a primitive (string/number) array.
// Each index is mapped individually.

export const PrimitiveArrayLeaf: React.FC<PrimitiveArrayLeafProps> = ({
  node,
  sourcePaths,
  primitiveArrayValues,
  primAmId,
  currentItems,
  partnerAdapterProperties,
  allGlobalSets,
  onLeafRef,
  isSearchMatch,
}) => {
  const { panelOpen, setPanelOpen, ref, mappedCount, saveItem, onMapEmptyArray, onClearEmptyArray } =
    usePrimitiveArrayLeaf(node, primAmId, currentItems, onLeafRef);

  const firstVal = primitiveArrayValues[0];
  const itemType: 'string' | 'number' | 'boolean' | undefined =
    typeof firstVal === 'number' ? 'number' :
    typeof firstVal === 'boolean' ? 'boolean' :
    typeof firstVal === 'string' ? 'string' : undefined;

  return (
    <div
      ref={ref}
      className={[
        'rounded border text-xs select-none transition-all',
        isSearchMatch ? 'bg-warn-100 ring-1 ring-warn-100' : 'border-transparent',
      ].join(' ')}
    >
      {/* Header row */}
      <div
        className="flex items-center gap-1.5 px-2 py-[3px] cursor-pointer hover:bg-ink-50 rounded"
        onClick={() => setPanelOpen((v) => !v)}
      >
        <span className={`w-2 h-2 rounded-full flex-shrink-0 ${mappedCount > 0 ? 'bg-ok-100' : 'bg-danger-300'}`} />
        <span className="font-mono text-ink-700">{node.key}</span>
        <span className="text-crimson-500 font-mono ml-0.5">[]</span>
        <span className="ml-auto text-[10px] font-medium text-warn-700 border border-warn-100 bg-warn-100 rounded px-1.5 py-px">
          {mappedCount}/{currentItems.length} mapped
        </span>
        <span className="text-ink-400 text-[10px]">{panelOpen ? '▾' : '▸'}</span>
      </div>

      {/* Inline mapping panel */}
      {panelOpen && (
        <div className="mx-2 mb-1.5 rounded-lg border border-warn-100 bg-warn-100 px-2 py-2 space-y-1">
          {currentItems.length === 0 && !primAmId && (
            <div className="flex items-center gap-2 py-0.5">
              <span className="text-[10px] text-ink-400 italic">Empty array — no elements to map.</span>
              <button
                className="ml-auto text-[10px] font-medium text-ok-600 border border-ok-100 bg-white rounded px-2 py-0.5 hover:bg-ok-100 transition"
                onClick={onMapEmptyArray}
              >
                Map as empty array
              </button>
            </div>
          )}

          {currentItems.length === 0 && primAmId && (
            <div className="flex items-center gap-2 py-0.5">
              <span className="text-[10px] text-ok-600 font-medium">✓ Mapped as empty array</span>
              <button
                className="ml-auto text-[10px] text-crimson-400 hover:text-crimson-600 transition"
                onClick={onClearEmptyArray}
                title="Clear mapping"
              >
                Clear
              </button>
            </div>
          )}

          {currentItems.map((item, idx) => {
            const mode: MappingMode =
              item.partnerPropKey !== undefined ? 'partner' :
              item.globalSetId !== undefined ? 'global' :
              item.fixedValue !== undefined ? 'fixed' : 'source';

            return (
              <div key={idx} className="flex items-center gap-1">
                <span className="font-mono text-[10px] text-ink-400 w-5 text-right flex-shrink-0">[{idx}]</span>
                <span className="text-ink-300 flex-shrink-0 text-xs">←</span>
                <ModeToggleButtons
                  current={mode}
                  onChange={(next) => saveItem(idx, { ...MODE_INITIAL_FIELDS[next], source: MODE_INITIAL_FIELDS[next].source ?? item.source ?? '' })}
                />
                <LeafValueInput
                  mode={mode}
                  sourceValue={item.source ?? ''}
                  sourcePaths={sourcePaths}
                  onSourceChange={(v) => saveItem(idx, { ...item, source: v })}
                  fixedValue={item.fixedValue ?? ''}
                  targetFieldType={itemType}
                  onFixedChange={(v) => saveItem(idx, { ...item, fixedValue: v })}
                  partnerPropKey={item.partnerPropKey ?? ''}
                  partnerAdapterProperties={partnerAdapterProperties}
                  datalistId={`prim-partner-${node.path}-${idx}`}
                  onPartnerChange={(v) => saveItem(idx, { ...item, partnerPropKey: v })}
                  globalSetId={item.globalSetId ?? ''}
                  globalKey={item.globalKey ?? ''}
                  allGlobalSets={allGlobalSets}
                  onGlobalSetChange={(setId) => saveItem(idx, { ...item, globalSetId: setId, globalKey: '' })}
                  onGlobalKeyChange={(key) => saveItem(idx, { ...item, globalKey: key })}
                />
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
