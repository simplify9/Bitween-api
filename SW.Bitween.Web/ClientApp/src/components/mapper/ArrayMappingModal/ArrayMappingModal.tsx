import type React from "react";
import { PlusCircle, X } from "lucide-react";
import { useMappingEditorState } from "../../../lib/mapping/MappingEditorContext";
import type { FilterOperator } from "../../../lib/mapping/types";
import { generateExample } from "../../../lib/mapping/arrayMappingHelpers";
import { useArrayMappingModal } from "./useArrayMappingModal";
import { ArrayMappingFieldRow } from "./ArrayMappingFieldRow";

// ─── Operators ────────────────────────────────────────────────────────────────

const OPERATORS: { label: string; value: FilterOperator }[] = [
  { label: '==', value: '==' },
  { label: '!=', value: '!=' },
  { label: '>', value: '>' },
  { label: '>=', value: '>=' },
  { label: '<', value: '<' },
  { label: '<=', value: '<=' },
];

// ─── ArrayMappingModal ────────────────────────────────────────────────────────

const ArrayMappingModal: React.FC = () => {
  const { editingArrayId } = useMappingEditorState();

  const {
    isCreating,
    isNested,
    isRootOutput,
    parentAmTarget,
    source,
    target,
    alias,
    hasFilter,
    filterField,
    filterOp,
    filterValue,
    pendingMappings,
    sourceArrayPaths,
    sourceItemProps,
    targetItemProps,
    inputScalarProps,
    outputTypeMap,
    usedTargets,
    childArrayMappings,
    allGlobalSets,
    partnerAdapterProperties,
    setSource,
    setAlias,
    setHasFilter,
    setFilterField,
    setFilterOp,
    setFilterValue,
    setOpenPanels,
    handleSave,
    handleClose,
    handleDelete,
    addFieldRow,
    removeFieldRow,
    patchFieldRow,
    getPanel,
  } = useArrayMappingModal();

  if (editingArrayId === null) return null;

  const fullTargetBase = target;

  return (
    <div className="fixed inset-0 z-[60] bg-black/30 flex items-center justify-center p-4">
      <div className="bg-white rounded-xl shadow-2xl border border-ink-200 w-full max-w-2xl flex flex-col max-h-[90vh] overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-3 border-b border-ink-200 bg-ink-50 flex-shrink-0">
          <div>
            <h2 className="font-bold text-ink-800 text-sm">
              {isCreating ? 'New Array Mapping' : 'Edit Array Mapping'}
              {isNested && (
                <span className="ml-2 text-[10px] font-normal bg-warn-100 text-warn-700 border border-warn-100 rounded px-1.5 py-0.5">
                  nested inside {parentAmTarget}
                </span>
              )}
            </h2>
            <p className="text-xs text-ink-500 mt-0.5">Configure a loop over a source array with optional filters</p>
          </div>
          <button onClick={handleClose} className="text-ink-400 hover:text-ink-600 transition">
            <X size={18} />
          </button>
        </div>

        <div className="overflow-y-auto flex-1 px-5 py-4 space-y-5">
          {/* Source / Target / Alias */}
          <div className="grid grid-cols-3 gap-3">
            <div>
              <label className="block text-xs font-semibold text-ink-600 mb-1">Target Array Path</label>
              {isRootOutput ? (
                <div className="w-full border border-crimson-200 rounded px-2 py-1.5 text-xs font-mono bg-crimson-50 text-crimson-600 flex items-center gap-1.5">
                  <span className="text-[10px] font-bold bg-crimson-100 border border-crimson-300 text-crimson-700 rounded px-1">root</span>
                  <span className="text-crimson-500 italic">output is a root array</span>
                </div>
              ) : (
                <input
                  className="w-full border border-ink-200 rounded px-2 py-1.5 text-xs font-mono bg-ink-50 text-ink-500 cursor-not-allowed"
                  value={target}
                  readOnly
                  disabled
                  title="Target is determined by the array you clicked in the output tree"
                />
              )}
            </div>
            <div>
              <label className="block text-xs font-semibold text-ink-600 mb-1">Source Array Path</label>
              {sourceArrayPaths.length > 0 ? (
                <select
                  className="w-full border border-ink-200 rounded px-2 py-1.5 text-xs font-mono focus:outline-none focus:border-crimson-400 bg-white"
                  value={source}
                  onChange={(e) => { setSource(e.target.value); }}
                >
                  <option value="">— select array —</option>
                  {sourceArrayPaths.map((p) => <option key={p} value={p}>{p}</option>)}
                </select>
              ) : (
                <input
                  className="w-full border border-ink-200 rounded px-2 py-1.5 text-xs font-mono focus:outline-none focus:border-crimson-400"
                  placeholder="e.g. order.items"
                  value={source}
                  onChange={(e) => { setSource(e.target.value); }}
                />
              )}
            </div>
            <div>
              <label className="block text-xs font-semibold text-ink-600 mb-1">Loop Alias</label>
              <input
                className="w-full border border-ink-200 rounded px-2 py-1.5 text-xs font-mono focus:outline-none focus:border-crimson-400"
                placeholder="item"
                value={alias}
                onChange={(e) => setAlias(e.target.value)}
              />
            </div>
          </div>

          {/* Filter */}
          <div className="bg-ink-50 rounded-lg border border-ink-200 p-3">
            <div className="flex items-center gap-2 mb-3">
              <label className="flex items-center gap-2 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={hasFilter}
                  onChange={(e) => setHasFilter(e.target.checked)}
                  className="rounded border-ink-300 text-crimson-600 focus:ring-crimson-500"
                />
                <span className="text-xs font-semibold text-ink-700">Apply filter on array items</span>
              </label>
              {hasFilter && (
                <span className="text-xs text-ink-400">
                  e.g. only include items where{' '}
                  <code className="font-mono bg-white px-1 rounded border border-ink-200">
                    {alias}.{filterField || 'field'} {filterOp} {filterValue || 'value'}
                  </code>
                </span>
              )}
            </div>
            {hasFilter && (
              <div className="grid grid-cols-3 gap-2">
                <div>
                  <label className="block text-xs text-ink-500 mb-1">Field</label>
                  <input
                    className="w-full border border-ink-200 rounded px-2 py-1 text-xs font-mono focus:outline-none focus:border-crimson-400 bg-white"
                    placeholder="status"
                    value={filterField}
                    onChange={(e) => setFilterField(e.target.value)}
                  />
                </div>
                <div>
                  <label className="block text-xs text-ink-500 mb-1">Operator</label>
                  <select
                    className="w-full border border-ink-200 rounded px-2 py-1 text-xs font-mono focus:outline-none focus:border-crimson-400 bg-white"
                    value={filterOp}
                    onChange={(e) => setFilterOp(e.target.value as FilterOperator)}
                  >
                    {OPERATORS.map((op) => <option key={op.value} value={op.value}>{op.label}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-xs text-ink-500 mb-1">Value</label>
                  <input
                    className="w-full border border-ink-200 rounded px-2 py-1 text-xs font-mono focus:outline-none focus:border-crimson-400 bg-white"
                    placeholder="10"
                    value={filterValue}
                    onChange={(e) => setFilterValue(e.target.value)}
                  />
                </div>
              </div>
            )}
          </div>

          {/* Field Mappings */}
          <div>
            <div className="flex items-center gap-2 mb-2">
              <span className="text-xs font-semibold text-ink-700">Field Mappings inside loop</span>
              <span className="text-xs text-ink-400">
                Use <code className="font-mono">{alias || 'item'}.fieldName</code> for source fields
              </span>
            </div>
            {isCreating && (
              <p className="text-xs text-crimson-500 mb-2">
                Field mappings will be saved along with the array mapping.
              </p>
            )}
            <div className="space-y-1.5 max-h-56 overflow-y-auto pr-0.5">
              {pendingMappings.map((m) => {
                const targetFieldType = m.target && fullTargetBase
                  ? outputTypeMap[`${fullTargetBase}[*].${m.target}`]
                  : undefined;

                return (
                  <ArrayMappingFieldRow
                    key={m.id}
                    m={m}
                    alias={alias}
                    panel={getPanel(m)}
                    targetFieldType={targetFieldType}
                    targetItemProps={targetItemProps}
                    usedTargets={usedTargets}
                    sourceItemProps={sourceItemProps}
                    inputScalarProps={inputScalarProps}
                    partnerAdapterProperties={partnerAdapterProperties}
                    allGlobalSets={allGlobalSets}
                    onPatch={(patch) => patchFieldRow(m.id, patch)}
                    onRemove={() => removeFieldRow(m.id)}
                    onOpenPanel={(panel) => setOpenPanels((prev) => ({ ...prev, [m.id]: panel }))}
                  />
                );
              })}
            </div>
            <button
              disabled={!source}
              className="mt-2 flex items-center gap-1 text-xs text-crimson-500 hover:text-crimson-700 transition disabled:opacity-40 disabled:cursor-not-allowed disabled:pointer-events-none"
              onClick={addFieldRow}
            >
              <PlusCircle size={13} /> Add field mapping
            </button>
          </div>

          {/* Child Array Mappings — read-only */}
          {!isCreating && childArrayMappings.length > 0 && (
            <div>
              <div className="flex items-center gap-2 mb-2">
                <span className="text-xs font-semibold text-ink-700">Nested Array Mappings</span>
                <span className="text-xs text-ink-400">configured via their own loop buttons</span>
              </div>
              <div className="space-y-1.5">
                {childArrayMappings.map((child) => (
                  <div key={child.id} className="rounded border border-warn-100 bg-warn-100 px-3 py-2">
                    <div className="flex items-center gap-2 mb-1">
                      <span className="text-[10px] font-bold bg-warn-100 text-warn-700 border border-warn-100 rounded px-1.5">loop</span>
                      <span className="text-xs font-mono text-ink-700">
                        {child.source} <span className="text-ink-400">{'→'}</span> {child.target}
                        <span className="text-ink-400 ml-1">as {child.alias}</span>
                      </span>
                      {child.filter && (
                        <span className="text-[10px] text-ink-500 font-mono border border-ink-200 rounded px-1 bg-white">
                          if {child.alias}.{child.filter.field} {child.filter.operator} {child.filter.value}
                        </span>
                      )}
                    </div>
                    {child.mappings.length > 0 && (
                      <div className="space-y-0.5 pl-2 border-l border-warn-100">
                        {child.mappings.map((m) => (
                          <div key={m.id} className="text-[10px] font-mono text-ink-500">
                            {m.source || (m.fixedValue !== undefined ? `"${m.fixedValue}"` : '—')}
                            {' '}<span className="text-ink-300">{'→'}</span>{' '}
                            {m.target}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Example preview */}
          {source && target && (
            <div className="bg-ink-900 rounded-lg p-3 text-xs font-mono text-ink-300 overflow-x-auto">
              <pre className="whitespace-pre leading-5">
                {generateExample(source, target, alias, hasFilter, filterField, filterOp, filterValue)}
              </pre>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between px-5 py-3 border-t border-ink-200 bg-ink-50 flex-shrink-0">
          {!isCreating && (
            <button className="text-xs text-danger-600 hover:text-danger-800 transition" onClick={handleDelete}>
              Delete array mapping
            </button>
          )}
          <div className="flex items-center gap-2 ml-auto">
            <button onClick={handleClose} className="text-xs border border-ink-300 rounded px-3 py-1.5 hover:bg-ink-100 transition">
              Cancel
            </button>
            <button
              onClick={handleSave}
              disabled={
                (!target && !isRootOutput) ||
                (hasFilter && !source) ||
                pendingMappings.some(
                  (m) => m.target && !m.source && m.fixedValue === undefined && !m.lookupDictionary && !m.partnerPropKey && !(m.globalSetId && m.globalKey)
                )
              }
              className="text-xs bg-crimson-600 text-white rounded px-4 py-1.5 hover:bg-crimson-700 transition disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {isCreating ? 'Create' : 'Save'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ArrayMappingModal;
