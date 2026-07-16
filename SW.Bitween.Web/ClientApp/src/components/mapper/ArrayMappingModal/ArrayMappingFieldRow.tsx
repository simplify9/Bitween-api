import type React from "react";
import { Trash2 } from "lucide-react";
import type { MappingMode } from "../../../lib/mapping/types";
import { ModeToggleButtons } from "../ModeToggleButtons";
import { GlobalSetSelector } from "../GlobalSetSelector";
import { LookupDictionaryPanel } from "../LookupDictionaryPanel";
import type { GlobalValuesSetRow } from "../../../api";
import type { PendingMapping } from "./useArrayMappingModal";
import { FixedStringInput } from "../FixedStringInput";

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ArrayMappingFieldRowProps {
  m: PendingMapping;
  alias: string;
  panel: 'transform' | 'lookup' | 'fixed' | null;
  targetFieldType: 'string' | 'number' | 'boolean' | undefined;
  targetItemProps: string[];
  usedTargets: Set<string>;
  sourceItemProps: string[];
  inputScalarProps: string[];
  partnerAdapterProperties: Record<string, string>;
  allGlobalSets: GlobalValuesSetRow[];
  onPatch: (patch: Partial<PendingMapping>) => void;
  onRemove: () => void;
  onOpenPanel: (panel: 'transform' | 'lookup' | 'fixed' | null) => void;
}

// ─── ArrayMappingFieldRow ─────────────────────────────────────────────────────
// A single field mapping row inside ArrayMappingModal. Handles mode toggle,
// value input, lookup/transform secondary panels.

export const ArrayMappingFieldRow: React.FC<ArrayMappingFieldRowProps> = ({
  m,
  alias,
  panel,
  targetFieldType,
  targetItemProps,
  usedTargets,
  sourceItemProps,
  inputScalarProps,
  partnerAdapterProperties,
  allGlobalSets,
  onPatch,
  onRemove,
  onOpenPanel,
}) => {
  const hasLookup = (m.lookupDictionary?.entries?.length ?? 0) > 0;
  const hasTransform = Boolean(m.transform);
  const isMapped = Boolean(m.source) || m.fixedValue !== undefined || Boolean(m.partnerPropKey) || Boolean(m.globalSetId && m.globalKey);

  const rowMode: MappingMode =
    m.partnerPropKey !== undefined ? 'partner' :
    m.globalSetId !== undefined ? 'global' :
    m.fixedValue !== undefined ? 'fixed' : 'source';

  const handleModeChange = (next: MappingMode) => {
    if (next === rowMode) return;
    onOpenPanel(null);
    if (next === 'source') onPatch({ fixedValue: undefined, partnerPropKey: undefined, globalSetId: undefined, globalKey: undefined, lookupDictionary: undefined });
    else if (next === 'fixed') onPatch({ source: '', fixedValue: '', partnerPropKey: undefined, globalSetId: undefined, globalKey: undefined, lookupDictionary: undefined });
    else if (next === 'partner') onPatch({ source: '', fixedValue: undefined, partnerPropKey: '', globalSetId: undefined, globalKey: undefined, lookupDictionary: undefined, transform: undefined });
    else if (next === 'global') onPatch({ source: '', fixedValue: undefined, partnerPropKey: undefined, globalSetId: '', globalKey: '', lookupDictionary: undefined, transform: undefined });
  };

  const handleLookupToggle = () => {
    const isOpen = panel === 'lookup';
    if (!isOpen && hasTransform) onPatch({ transform: undefined });
    else if (isOpen) {
      const hasValid = (m.lookupDictionary?.entries ?? []).some((e) => e.from.trim() !== '' && e.to.trim() !== '');
      if (!hasValid) onPatch({ lookupDictionary: undefined });
    }
    onOpenPanel(isOpen ? null : 'lookup');
  };

  const handleTransformToggle = () => {
    if (hasLookup) onPatch({ lookupDictionary: undefined });
    onOpenPanel(panel === 'transform' ? null : 'transform');
  };

  return (
    <div className="rounded border border-ink-100 bg-white px-2 py-1.5 space-y-1">
      {/* Main row */}
      <div className="flex items-center gap-1">
        <span className={`w-2 h-2 rounded-full flex-shrink-0 ${isMapped ? 'bg-ok-100' : 'bg-crimson-300'}`} />

        {/* Target field */}
        {targetItemProps.length > 0 ? (
          <select
            className="font-mono text-xs text-ink-700 bg-transparent border-0 focus:outline-none min-w-0 max-w-[7rem] truncate"
            value={m.target}
            onChange={(e) => onPatch({ target: e.target.value })}
          >
            <option value="">— target —</option>
            {[...new Set([...targetItemProps, m.target].filter(Boolean))]
              .filter((prop) => !usedTargets.has(prop) || prop === m.target)
              .map((prop) => <option key={prop} value={prop}>{prop}</option>)}
          </select>
        ) : (
          <input
            className="font-mono text-xs text-ink-700 bg-transparent border-0 focus:outline-none min-w-0 w-24"
            placeholder="target"
            value={m.target}
            onChange={(e) => onPatch({ target: e.target.value })}
          />
        )}

        <span className="text-ink-300 flex-shrink-0 text-xs select-none">←</span>

        <div className={!m.target ? 'opacity-30 pointer-events-none' : ''}>
          <ModeToggleButtons current={rowMode} onChange={handleModeChange} />
        </div>

        {/* Lookup button — source mode only */}
        {rowMode === 'source' && (
          <button
            disabled={!m.source}
            title={!m.source ? 'Select a source field first' : hasLookup ? `Lookup: ${m.lookupDictionary!.entries.length} entries` : 'Map source values to different output values'}
            onClick={handleLookupToggle}
            className={[
              'flex-shrink-0 text-[10px] font-medium px-1.5 py-0.5 rounded border transition leading-none',
              !m.source ? 'opacity-30 cursor-not-allowed bg-white border-ink-200 text-ink-400'
                : hasLookup ? 'bg-crimson-100 border-crimson-300 text-crimson-700'
                : 'bg-white border-ink-200 text-ink-400 hover:border-crimson-300 hover:text-crimson-500',
            ].join(' ')}
          >
            Lookup
          </button>
        )}

        {/* Value input area */}
        {rowMode === 'fixed' ? (
          targetFieldType === 'boolean' ? (
            <select
              className="flex-1 min-w-0 border-0 bg-transparent font-mono text-xs text-warn-700 focus:outline-none"
              value={m.fixedValue ?? ''}
              onChange={(e) => onPatch({ fixedValue: e.target.value, source: '' })}
            >
              <option value="">— pick —</option>
              <option value="true">true</option>
              <option value="false">false</option>
            </select>
          ) : targetFieldType === 'number' ? (
            <input
              className="flex-1 min-w-0 border-0 bg-transparent font-mono text-xs text-warn-700 focus:outline-none placeholder-warn-100"
              placeholder="0"
              type="number"
              value={m.fixedValue ?? ''}
              onChange={(e) => onPatch({ fixedValue: e.target.value, source: '' })}
            />
          ) : (
            <FixedStringInput
              value={m.fixedValue ?? ''}
              sourcePaths={inputScalarProps}
              onChange={(v) => onPatch({ fixedValue: v, source: '' })}
            />
          )
        ) : rowMode === 'partner' ? (
          <div className="flex-1 relative min-w-0">
            <input
              list={`partner-props-am-${m.id}`}
              className="w-full border-0 bg-transparent font-mono text-xs focus:outline-none text-ok-600 placeholder-ok-100"
              placeholder="property key…"
              value={m.partnerPropKey ?? ''}
              onChange={(e) => onPatch({ partnerPropKey: e.target.value })}
            />
            <datalist id={`partner-props-am-${m.id}`}>
              {Object.keys(partnerAdapterProperties).map((k) => <option key={k} value={k} />)}
            </datalist>
          </div>
        ) : rowMode === 'global' ? (
          <GlobalSetSelector
            setId={m.globalSetId ?? ''}
            keyValue={m.globalKey ?? ''}
            allGlobalSets={allGlobalSets}
            onSetChange={(setId) => onPatch({ globalSetId: setId, globalKey: '' })}
            onKeyChange={(key) => onPatch({ globalKey: key })}
          />
        ) : (
          <select
            disabled={!m.target}
            className={`flex-1 min-w-0 border-0 bg-transparent font-mono text-xs focus:outline-none ${!m.target ? 'opacity-30 cursor-not-allowed text-ink-400' : 'text-ink-500'}`}
            value={m.source}
            onChange={(e) => {
              const val = e.target.value;
              const isRoot = inputScalarProps.includes(val) && !sourceItemProps.includes(val);
              onPatch({ source: val, fixedValue: undefined, lookupDictionary: undefined, isRootSource: isRoot || undefined });
            }}
          >
            <option value="">— unassigned —</option>
            {sourceItemProps.length > 0 && (
              <optgroup label={`${alias} fields`}>
                {sourceItemProps.map((prop) => <option key={prop} value={prop}>{prop}</option>)}
              </optgroup>
            )}
            {inputScalarProps.length > 0 && (
              <optgroup label="root fields">
                {inputScalarProps.map((prop) => <option key={prop} value={prop}>{prop}</option>)}
              </optgroup>
            )}
          </select>
        )}

        {/* Transform toggle — source mode, has source, no lookup */}
        {rowMode === 'source' && Boolean(m.source) && !hasLookup && (
          <button
            title={hasTransform ? `Transform: ${m.transform}` : 'Add a transform expression to modify the value'}
            onClick={handleTransformToggle}
            className={[
              'flex-shrink-0 text-[10px] font-medium px-1.5 py-0.5 rounded border transition leading-none',
              panel === 'transform' ? 'bg-crimson-100 border-crimson-300 text-crimson-700'
                : hasTransform ? 'bg-crimson-50 border-crimson-200 text-crimson-500'
                : 'bg-white border-ink-200 text-ink-400 hover:border-crimson-300 hover:text-crimson-500',
            ].join(' ')}
          >
            Transform
          </button>
        )}

        <button
          className="flex-shrink-0 text-ink-300 hover:text-crimson-500 transition"
          onClick={onRemove}
        >
          <Trash2 size={13} />
        </button>
      </div>

      {/* Transform panel */}
      {panel === 'transform' && (
        <div className="space-y-0.5">
          <span className="text-[10px] font-medium text-crimson-600 select-none">
            Transform{' '}
            <span className="font-normal text-ink-400">
              (optional — modify the value before output. Use <code className="font-mono">value</code> to refer to the source field.)
            </span>
          </span>
          <input
            autoFocus
            className="w-full border border-crimson-200 bg-white rounded px-2 py-1 text-xs font-mono focus:outline-none focus:border-crimson-400 placeholder-ink-300 text-crimson-700"
            placeholder="e.g.  value * 1.1    or    value + ' USD'"
            value={m.transform ?? ''}
            onChange={(e) => onPatch({ transform: e.target.value || undefined })}
          />
        </div>
      )}

      {/* Lookup panel */}
      {panel === 'lookup' && (
        <div className="mt-0.5">
          <LookupDictionaryPanel
            dictionary={m.lookupDictionary}
            targetFieldType={targetFieldType}
            onChange={(next) => onPatch({ lookupDictionary: next })}
            onClose={() => onOpenPanel(null)}
          />
        </div>
      )}
    </div>
  );
};
