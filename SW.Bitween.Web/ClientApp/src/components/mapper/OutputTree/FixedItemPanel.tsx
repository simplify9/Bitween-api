import type React from "react";
import { useState } from "react";
import { SquarePen, Trash2 } from "lucide-react";
import type { TreeNode } from "../../../lib/mapping/mappingPreview";
import { useMappingEditorState } from "../../../lib/mapping/MappingEditorContext";
import {
  FIXED_ITEM_MAX_DEPTH,
  initDraftFieldsFromNode,
  draftFieldsToRecord,
} from "../../../lib/mapping/fixedItemHelpers";
import type { DraftField, DraftFieldMode } from "../../../lib/mapping/fixedItemHelpers";
import { ModeToggleButtons } from "../ModeToggleButtons";
import { LeafValueInput } from "../LeafValueInput";
import { LookupDictionaryPanel } from "../LookupDictionaryPanel";
import { useGlobalSets } from "../data";

// ─── FixedItemFieldRows — recursive form for one fixed item's fields ──────────

export interface FixedItemFieldRowsProps {
  fields: DraftField[];
  onFieldsChange: (fields: DraftField[]) => void;
  parentNode: TreeNode;
  depth: number;
  inputScalarProps: string[];
  idPrefix: string;
  typeMap?: Record<string, 'string' | 'number' | 'boolean'>;
}

export const FixedItemFieldRows: React.FC<FixedItemFieldRowsProps> = ({
  fields, onFieldsChange, parentNode, depth, inputScalarProps, idPrefix, typeMap,
}) => {
  const { partnerAdapterProperties } = useMappingEditorState();
  const allGlobalSets = useGlobalSets();
  const [lookupOpenIdx, setLookupOpenIdx] = useState<number | null>(null);

  const hasFreeKey = parentNode.children.filter((c) => c.type === 'leaf').length === 0;
  const childArrayNodes = parentNode.children.filter((c) => c.type === 'array');
  const usedArrayKeys = new Set(fields.filter((f) => f.mode === 'array').map((f) => f.key));
  const availableArrayNodes = depth < FIXED_ITEM_MAX_DEPTH
    ? childArrayNodes.filter((c) => !usedArrayKeys.has(c.key))
    : [];
  const updateField = (i: number, patch: Partial<DraftField>) =>
    onFieldsChange(fields.map((f, j) => (j === i ? { ...f, ...patch } : f)));

  return (
    <div className="space-y-1">
      {fields.map((df, i) => {
        if (df.mode === 'array') {
          return (
            <div key={`${df.key}-arr-${i}`} className="rounded border border-crimson-200 bg-crimson-50/40 p-1.5 space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-[10px] font-semibold text-crimson-700 font-mono">{df.key}[]</span>
                <button className="text-ink-300 hover:text-crimson-500 transition"
                  onClick={() => onFieldsChange(fields.filter((_, j) => j !== i))}>
                  <Trash2 size={11} />
                </button>
              </div>
              {/* committed sub-items */}
              {(df.nestedItems ?? []).map((itemFields, itemIdx) => (
                <div key={itemIdx} className="rounded border border-crimson-200 bg-white">
                  {df.editingSubIdx === itemIdx ? (
                    /* ─ edit form for this sub-item ─ */
                    <div className="px-2 py-1.5 space-y-1.5">
                      <FixedItemFieldRows
                        fields={df.nestedItems![itemIdx]}
                        onFieldsChange={(updated) => {
                          const items = [...df.nestedItems!];
                          items[itemIdx] = updated;
                          updateField(i, { nestedItems: items });
                        }}
                        parentNode={df.nestedChildNode!}
                        depth={depth + 1}
                        inputScalarProps={inputScalarProps}
                        idPrefix={`${idPrefix}-${df.key}-edit-${itemIdx}`}
                        typeMap={typeMap}
                      />
                      <div className="flex gap-1 pt-0.5">
                        <button className="flex-1 text-[10px] bg-crimson-500 hover:bg-crimson-600 text-white rounded py-0.5 transition"
                          onClick={() => updateField(i, { editingSubIdx: null })}>Done</button>
                        <button className="flex-1 text-[10px] border border-ink-200 hover:bg-ink-50 text-ink-500 rounded py-0.5 transition"
                          onClick={() => updateField(i, { editingSubIdx: null })}>Cancel</button>
                      </div>
                    </div>
                  ) : (
                    /* ─ compact summary card ─ */
                    <div className="rounded border border-crimson-100 overflow-hidden">
                      {/* header row: #N, ✏, 🗑 */}
                      <div className="flex items-center justify-between px-1.5 py-0.5 bg-crimson-50">
                        <span className="text-[9px] font-semibold text-crimson-600">item #{itemIdx + 1}</span>
                        <div className="flex items-center gap-1">
                          <button className="text-ink-300 hover:text-crimson-500 transition"
                            onClick={() => updateField(i, { editingSubIdx: itemIdx })}>
                            <SquarePen size={11} />
                          </button>
                          <button className="text-ink-300 hover:text-crimson-500 transition"
                            onClick={() => updateField(i, { nestedItems: (df.nestedItems ?? []).filter((_, ii) => ii !== itemIdx) })}>
                            <Trash2 size={11} />
                          </button>
                        </div>
                      </div>
                      {/* field-by-field content */}
                      <div className="px-2 py-1 space-y-0.5">
                        {itemFields.map((f, fi) => f.mode !== 'array' ? (
                          <div key={fi} className="flex items-center gap-1 text-[9px] font-mono">
                            <span className="text-crimson-700 flex-shrink-0">{f.key}:</span>
                            <span className="text-ink-500 truncate">
                              {f.mode === 'source' ? (f.transform ? `fx(${f.value})` : f.value)
                                : f.mode === 'partner' ? `__partner__.${f.partnerPropKey ?? ''}`
                                : f.mode === 'global' ? `__globals__.${f.globalSetId ?? ''}["${f.globalKey ?? ''}"]`
                                : (f.value || '—')}
                            </span>
                          </div>
                        ) : (
                          <div key={fi} className="text-[9px] font-mono text-crimson-500">
                            {f.key}[]: {(f.nestedItems ?? []).length} item{(f.nestedItems ?? []).length !== 1 ? 's' : ''}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              ))}
              {/* add sub-item */}
              {df.addingItem != null ? (
                <div className="bg-white rounded border border-crimson-100 p-1.5 space-y-1">
                  <FixedItemFieldRows
                    fields={df.addingItem!}
                    onFieldsChange={(updated) => updateField(i, { addingItem: updated })}
                    parentNode={df.nestedChildNode!}
                    depth={depth + 1}
                    inputScalarProps={inputScalarProps}
                    idPrefix={`${idPrefix}-${df.key}`}
                    typeMap={typeMap}
                  />
                  <div className="flex gap-1 pt-0.5">
                    <button className="flex-1 text-[10px] bg-crimson-500 hover:bg-crimson-600 text-white rounded py-0.5 transition"
                      onClick={() => {
                        const rec = draftFieldsToRecord(df.addingItem!);
                        if (Object.keys(rec).length > 0) {
                          updateField(i, { nestedItems: [...(df.nestedItems ?? []), df.addingItem!], addingItem: null });
                        }
                      }}>Add</button>
                    <button className="flex-1 text-[10px] border border-ink-200 hover:bg-ink-50 text-ink-500 rounded py-0.5 transition"
                      onClick={() => updateField(i, { addingItem: null })}>Cancel</button>
                  </div>
                </div>
              ) : (
                <button className="text-[10px] text-crimson-600 hover:text-crimson-800 transition"
                  onClick={() => updateField(i, { addingItem: initDraftFieldsFromNode(df.nestedChildNode!) })}>
                  + item
                </button>
              )}
            </div>
          );
        }
        // leaf field (fixed / source / partner / global)
        const hasLookup = (df.lookupDictionary?.entries?.length ?? 0) > 0;
        const isLookupOpen = lookupOpenIdx === i;
        const fieldTargetType = typeMap?.[`${parentNode.path}.${df.key}`];
        return (
          <div key={`${df.key}-leaf-${i}`} className="space-y-0.5">
            <div className="flex items-center gap-1.5">
              {hasFreeKey ? (
                <input
                  className="w-20 flex-shrink-0 border border-ink-200 bg-transparent rounded px-1.5 py-0.5 text-xs font-mono focus:outline-none focus:border-crimson-400"
                  placeholder="key" value={df.key}
                  onChange={(e) => updateField(i, { key: e.target.value })} />
              ) : (
                <span className="max-w-[10rem] flex-shrink-0 font-mono text-ink-700 text-xs truncate" title={df.key}>{df.key}</span>
              )}
              <span className="text-ink-300 flex-shrink-0">←</span>

              <ModeToggleButtons
                current={(df.mode as DraftFieldMode) === 'array' ? 'source' : df.mode}
                onChange={(next) => {
                  setLookupOpenIdx(null);
                  if (next === 'source') updateField(i, { mode: 'source', partnerPropKey: undefined, globalSetId: undefined, globalKey: undefined });
                  else if (next === 'fixed') updateField(i, { mode: 'fixed', value: '', partnerPropKey: undefined, globalSetId: undefined, globalKey: undefined, lookupDictionary: undefined });
                  else if (next === 'partner') updateField(i, { mode: 'partner', value: '', transform: '', globalSetId: undefined, globalKey: undefined, lookupDictionary: undefined });
                  else if (next === 'global') updateField(i, { mode: 'global', value: '', transform: '', partnerPropKey: undefined, lookupDictionary: undefined });
                }}
              />

              {/* Lookup button — only in source mode, matches OutputLeaf styling */}
              {df.mode === 'source' && (
                <button
                  onClick={() => {
                    if (isLookupOpen && !hasLookup) {
                      updateField(i, { lookupDictionary: undefined });
                    }
                    setLookupOpenIdx(isLookupOpen ? null : i);
                  }}
                  title={hasLookup ? `Lookup: ${df.lookupDictionary!.entries.length} entries` : 'Map source values to different output values'}
                  className={[
                    'flex-shrink-0 text-[10px] font-medium px-1.5 py-0.5 rounded border transition',
                    hasLookup || isLookupOpen
                      ? 'border-crimson-400 bg-crimson-50 text-crimson-600'
                      : 'border-ink-200 text-ink-400 hover:border-crimson-300 hover:text-crimson-500',
                  ].join(' ')}
                >Lookup</button>
              )}

              {/* Value area */}
              <LeafValueInput
                mode={df.mode as 'source' | 'fixed' | 'partner' | 'global'}
                sourceValue={df.value}
                sourcePaths={inputScalarProps}
                onSourceChange={(v) => updateField(i, { value: v })}
                fixedValue={df.value}
                targetFieldType={fieldTargetType}
                onFixedChange={(v) => updateField(i, { value: v })}
                partnerPropKey={df.partnerPropKey ?? ''}
                partnerAdapterProperties={partnerAdapterProperties}
                datalistId={`${idPrefix}-partner-${i}`}
                onPartnerChange={(v) => updateField(i, { partnerPropKey: v })}
                globalSetId={df.globalSetId ?? ''}
                globalKey={df.globalKey ?? ''}
                allGlobalSets={allGlobalSets}
                onGlobalSetChange={(setId) => updateField(i, { globalSetId: setId, globalKey: undefined })}
                onGlobalKeyChange={(key) => updateField(i, { globalKey: key })}
              />

              {/* Delete row — only for free-key fields */}
              {hasFreeKey && (
                <button
                  className="flex-shrink-0 text-ink-300 hover:text-crimson-500 transition"
                  onClick={() => { onFieldsChange(fields.filter((_, j) => j !== i)); if (lookupOpenIdx === i) setLookupOpenIdx(null); }}>
                  <Trash2 size={12} />
                </button>
              )}
            </div>

            {/* Lookup dictionary panel */}
            {df.mode === 'source' && isLookupOpen && (
              <div className="px-2 pb-2 pt-0.5">
                <LookupDictionaryPanel
                  dictionary={df.lookupDictionary}
                  targetFieldType={fieldTargetType}
                  onChange={(next) => { updateField(i, { lookupDictionary: next }); if (!next) setLookupOpenIdx(null); }}
                  onClose={() => setLookupOpenIdx(null)}
                />
              </div>
            )}
          </div>
        );
      })}
      {/* Nested array dropdown — schema-only, depth-gated */}
      {depth < FIXED_ITEM_MAX_DEPTH && availableArrayNodes.length > 0 && (
        <select
          className="w-full border border-crimson-200 bg-white rounded px-1.5 py-0.5 text-[10px] font-mono text-crimson-700 focus:outline-none focus:border-crimson-400"
          value=""
          onChange={(e) => {
            const childNode = childArrayNodes.find((c) => c.key === e.target.value);
            if (!childNode) return;
            onFieldsChange([...fields, {
              key: childNode.key, mode: 'array', value: '', transform: '', showTransform: false,
              nestedChildNode: childNode, nestedItems: [], addingItem: null,
            }]);
          }}>
          <option value="">＋ add nested array…</option>
          {availableArrayNodes.map((c) => <option key={c.key} value={c.key}>{c.key}[]</option>)}
        </select>
      )}
      {/* Free-key: add more rows */}
      {hasFreeKey && (
        <button className="text-[11px] text-ok-600 hover:text-ok-600 transition"
          onClick={() => onFieldsChange([...fields, { key: '', mode: 'fixed', value: '', transform: '', showTransform: false }])}>
          + field
        </button>
      )}
    </div>
  );
};
