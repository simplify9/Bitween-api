import type React from "react";
import { PlusCircle, Trash2 } from "lucide-react";
import type { LookupDictionary } from "../../lib/mapping/types";

// ─── Types ────────────────────────────────────────────────────────────────────

export interface LookupDictionaryPanelProps {
  dictionary: LookupDictionary | undefined;
  targetFieldType?: 'string' | 'number' | 'boolean';
  onChange: (next: LookupDictionary | undefined) => void;
  onClose: () => void;
}

// ─── LookupDictionaryPanel ────────────────────────────────────────────────────
// Renders the crimson lookup panel: entry rows (from → to), fallback, remove.
// Fully controlled — callers own the LookupDictionary value.

export const LookupDictionaryPanel: React.FC<LookupDictionaryPanelProps> = ({
  dictionary,
  targetFieldType,
  onChange,
  onClose,
}) => {
  const entries = dictionary?.entries ?? [];

  const patchEntry = (idx: number, patch: { from?: string; to?: string }) => {
    const next = entries.map((e, i) => i === idx ? { ...e, ...patch } : e);
    onChange({ fallback: 'null', ...dictionary, entries: next });
  };

  const removeEntry = (idx: number) => {
    const next = entries.filter((_, i) => i !== idx);
    onChange(next.length === 0 ? undefined : { fallback: 'null', ...dictionary, entries: next });
  };

  const addEntry = () => {
    onChange({ fallback: 'null', ...dictionary, entries: [...entries, { from: '', to: '' }] });
  };

  const patchFallback = (fallback: LookupDictionary['fallback']) => {
    onChange({ ...({ fallback: 'null', ...dictionary, entries } as LookupDictionary), fallback });
  };

  const patchFallbackValue = (fallbackValue: string) => {
    onChange({ fallback: 'null', ...dictionary, entries, fallbackValue });
  };

  const removeLookup = () => {
    onChange(undefined);
    onClose();
  };

  return (
    <div className="rounded-lg border border-crimson-200 bg-crimson-50 p-2 space-y-1.5">
      {/* Entry rows */}
      <div className="space-y-1 max-h-40 overflow-y-auto">
        {entries.length === 0 && (
          <p className="text-[10px] text-crimson-400 italic px-1">No entries yet.</p>
        )}
        {entries.map((entry, idx) => (
          <div key={idx} className="flex items-center gap-1">
            <input
              autoFocus={idx === 0}
              className="flex-1 min-w-0 border border-crimson-200 bg-white rounded px-2 py-0.5 text-xs font-mono focus:outline-none focus:border-crimson-400 placeholder-ink-300"
              placeholder="source value"
              value={entry.from}
              onChange={(e) => patchEntry(idx, { from: e.target.value })}
            />
            <span className="text-ink-300 text-[10px] flex-shrink-0 select-none">→</span>
            {targetFieldType === 'boolean' ? (
              <select
                className="flex-1 min-w-0 border border-crimson-200 bg-white rounded px-2 py-0.5 text-xs font-mono focus:outline-none focus:border-crimson-400 text-crimson-700"
                value={entry.to}
                onChange={(e) => patchEntry(idx, { to: e.target.value })}
              >
                <option value="">— pick —</option>
                <option value="true">true</option>
                <option value="false">false</option>
              </select>
            ) : (
              <input
                type={targetFieldType === 'number' ? 'number' : 'text'}
                className="flex-1 min-w-0 border border-crimson-200 bg-white rounded px-2 py-0.5 text-xs font-mono focus:outline-none focus:border-crimson-400 placeholder-ink-300"
                placeholder={targetFieldType === 'number' ? '0' : 'output value'}
                value={entry.to}
                onChange={(e) => patchEntry(idx, { to: e.target.value })}
              />
            )}
            <button
              className="flex-shrink-0 text-ink-300 hover:text-crimson-400 transition"
              onClick={() => removeEntry(idx)}
            >
              <Trash2 size={11} />
            </button>
          </div>
        ))}
      </div>

      {/* Add entry */}
      <button
        className="flex items-center gap-1 text-[10px] text-crimson-500 hover:text-crimson-700 transition font-medium"
        onClick={addEntry}
      >
        <PlusCircle size={11} /> Add entry
      </button>

      {/* Fallback — only when at least one entry exists */}
      {entries.length > 0 && (
        <div className="flex items-center gap-2 pt-1 border-t border-crimson-200">
          <span className="text-[10px] text-ink-500 flex-shrink-0 select-none">If not found:</span>
          <select
            className="text-xs border border-crimson-200 bg-white rounded px-1.5 py-0.5 font-mono focus:outline-none focus:border-crimson-400 text-crimson-700"
            value={dictionary?.fallback ?? 'null'}
            onChange={(e) => patchFallback(e.target.value as LookupDictionary['fallback'])}
          >
            <option value="null">output null</option>
            <option value="custom">use custom fallback</option>
          </select>
          {dictionary?.fallback === 'custom' && (
            targetFieldType === 'boolean' ? (
              <select
                className="flex-1 min-w-0 border border-crimson-200 bg-white rounded px-1.5 py-0.5 text-xs font-mono focus:outline-none focus:border-crimson-400 text-crimson-700"
                value={dictionary.fallbackValue ?? ''}
                onChange={(e) => patchFallbackValue(e.target.value)}
              >
                <option value="">— pick —</option>
                <option value="true">true</option>
                <option value="false">false</option>
              </select>
            ) : (
              <input
                type={targetFieldType === 'number' ? 'number' : 'text'}
                className="flex-1 min-w-0 border border-crimson-200 bg-white rounded px-2 py-0.5 text-xs font-mono focus:outline-none focus:border-crimson-400 placeholder-ink-300 text-crimson-700"
                placeholder={targetFieldType === 'number' ? '0' : 'fallback value'}
                value={dictionary.fallbackValue ?? ''}
                onChange={(e) => patchFallbackValue(e.target.value)}
              />
            )
          )}
        </div>
      )}

      {/* Remove lookup */}
      <button
        className="text-[10px] text-crimson-400 hover:text-crimson-600 transition"
        onClick={removeLookup}
      >
        Remove lookup
      </button>
    </div>
  );
};
