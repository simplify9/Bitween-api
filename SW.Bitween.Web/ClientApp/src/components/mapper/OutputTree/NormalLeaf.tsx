import type React from "react";
import { Trash2 } from "lucide-react";
import type { TreeNode } from "../../../lib/mapping/mappingPreview";
import { ModeToggleButtons } from "../ModeToggleButtons";
import { LeafValueInput } from "../LeafValueInput";
import { useNormalLeaf } from "./useNormalLeaf";
import { LookupDictionaryPanel } from "../LookupDictionaryPanel";

// ─── Types ────────────────────────────────────────────────────────────────────

export interface NormalLeafProps {
  node: TreeNode;
  sourcePaths: string[];
  targetFieldType: 'string' | 'number' | 'boolean' | undefined;
  onLeafRef?: (path: string, el: HTMLElement | null) => void;
  isSearchMatch?: boolean;
}

// ─── NormalLeaf ───────────────────────────────────────────────────────────────
// Standard (non-array) output field — supports source, fixed, partner, global
// modes plus optional lookup dictionary and transform expression.

export const NormalLeaf: React.FC<NormalLeafProps> = ({
  node,
  sourcePaths,
  targetFieldType,
  onLeafRef,
  isSearchMatch,
}) => {
  const {
    mapping,
    isMapped,
    isSelected,
    currentMode,
    hasLookup,
    lookupOpen,
    allGlobalSets,
    partnerAdapterProperties,
    ref,
    handleDrop,
    handleSelect,
    switchMode,
    handleLookupToggle,
    onSourceChange,
    onFixedChange,
    onPartnerChange,
    onGlobalSetChange,
    onGlobalKeyChange,
    removeMapping,
    updateTransform,
    patchLookup,
    removeLookup,
  } = useNormalLeaf(node, onLeafRef);

  // ── Derived: source path exists in the known input paths ──────────────────
  const isInvalidSource =
    currentMode === 'source' && !!(mapping?.source) && !sourcePaths.includes(mapping.source);

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div
      ref={ref}
      onDragOver={(e) => e.preventDefault()}
      onDrop={handleDrop}
      onClick={handleSelect}
      className={[
        'rounded border text-xs cursor-pointer transition-all select-none',
        isSelected
          ? 'border-crimson-400 bg-crimson-50 ring-1 ring-crimson-200'
          : isSearchMatch
          ? 'bg-warn-100 ring-1 ring-warn-100'
          : 'border-transparent hover:border-ink-200 hover:bg-ink-50',
      ].filter(Boolean).join(' ')}
    >
      {/* ── Main row ── */}
      <div className="flex items-center gap-1.5 px-2 py-[3px]">
        <span
          className={[
            'w-2 h-2 rounded-full flex-shrink-0',
            isInvalidSource ? 'bg-warn-100' : isMapped ? 'bg-ok-100' : 'bg-crimson-300',
          ].join(' ')}
          title={isInvalidSource ? `Source path not found in input: "${mapping!.source}"` : undefined}
        />
        <span className="font-mono text-ink-700 truncate">{node.key}</span>
        <span className="text-ink-300 mx-0.5 flex-shrink-0">←</span>

        <ModeToggleButtons current={currentMode} onChange={switchMode} />

        {currentMode === 'source' && (
          <button
            onClick={handleLookupToggle}
            title={hasLookup ? `Lookup: ${mapping!.lookupDictionary!.entries.length} entries` : 'Map source values to different output values'}
            className={[
              'flex-shrink-0 text-[10px] font-medium px-1.5 py-0.5 rounded border transition',
              hasLookup || lookupOpen
                ? 'border-crimson-400 bg-crimson-50 text-crimson-600'
                : 'border-ink-200 text-ink-400 hover:border-crimson-300 hover:text-crimson-500',
            ].join(' ')}
          >Lookup</button>
        )}

        <LeafValueInput
          mode={currentMode}
          sourceValue={mapping?.source ?? ''}
          sourcePaths={sourcePaths}
          onSourceChange={onSourceChange}
          fixedValue={mapping?.fixedValue ?? ''}
          targetFieldType={targetFieldType}
          onFixedChange={onFixedChange}
          partnerPropKey={mapping?.partnerPropKey ?? ''}
          partnerAdapterProperties={partnerAdapterProperties}
          datalistId={`partner-props-${mapping?.id}`}
          onPartnerChange={onPartnerChange}
          globalSetId={mapping?.globalSetId ?? ''}
          globalKey={mapping?.globalKey ?? ''}
          allGlobalSets={allGlobalSets}
          onGlobalSetChange={onGlobalSetChange}
          onGlobalKeyChange={onGlobalKeyChange}
        />

        {/* Transform badge — shown when NOT selected but transform exists */}
        {!isSelected && mapping?.transform && currentMode === 'source' && (
          <span
            title={`Transform: ${mapping.transform}`}
            className="flex-shrink-0 text-[10px] font-medium text-crimson-600 border border-crimson-200 bg-crimson-50 rounded px-1 cursor-default"
          >
            transform
          </span>
        )}

        {mapping && (
          <button
            className="flex-shrink-0 text-ink-300 hover:text-crimson-500 transition"
            onClick={(e) => { e.stopPropagation(); removeMapping(); }}
          >
            <Trash2 size={12} />
          </button>
        )}
      </div>

      {/* ── Lookup dictionary panel ── */}
      {lookupOpen && currentMode === 'source' && (
        <div className="px-2 pb-2 pt-0.5" onClick={(e) => e.stopPropagation()}>
          <LookupDictionaryPanel
            dictionary={mapping?.lookupDictionary}
            targetFieldType={targetFieldType}
            onChange={patchLookup}
            onClose={removeLookup}
          />
        </div>
      )}

      {/* ── Transform row — only when selected + source mode + source assigned + no lookup ── */}
      {isSelected && currentMode === 'source' && mapping?.source && !hasLookup && (
        <div className="px-2 pb-1.5 space-y-0.5" onClick={(e) => e.stopPropagation()}>
          <span className="text-[10px] font-medium text-crimson-600 select-none">
            Transform{' '}
            <span className="font-normal text-ink-400">(optional — modify the source value before output)</span>
          </span>
          <input
            className="w-full border border-crimson-200 bg-white rounded px-2 py-1 text-xs font-mono focus:outline-none focus:border-crimson-400 placeholder-ink-300 text-crimson-700"
            placeholder="e.g.  value * 1.2    or    value + ' USD'    — use 'value' to refer to the source field"
            value={mapping.transform ?? ''}
            onChange={(e) => updateTransform(e.target.value)}
          />
        </div>
      )}
    </div>
  );
};
