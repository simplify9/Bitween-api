import type React from "react";
import type { GlobalValuesSetRow } from "../../api";

// ─── Types ────────────────────────────────────────────────────────────────────

export interface GlobalSetSelectorProps {
  setId: string;
  keyValue: string;
  allGlobalSets: GlobalValuesSetRow[];
  onSetChange: (setId: string) => void;
  onKeyChange: (key: string) => void;
}

// ─── GlobalSetSelector ────────────────────────────────────────────────────────

export const GlobalSetSelector: React.FC<GlobalSetSelectorProps> = ({
  setId,
  keyValue,
  allGlobalSets,
  onSetChange,
  onKeyChange,
}) => {
  const selectedSet = allGlobalSets.find((s) => s.id === setId);
  const availableKeys = selectedSet ? Object.keys(selectedSet.values) : [];

  return (
    <div className="flex gap-1 flex-1 min-w-0" onClick={(e) => e.stopPropagation()}>
      <select
        className="border-0 bg-transparent font-mono text-xs focus:outline-none text-ok-600 flex-1 min-w-0"
        value={setId}
        onChange={(e) => onSetChange(e.target.value)}
      >
        <option value="">— pick set —</option>
        {allGlobalSets.map((s) => (
          <option key={s.id} value={s.id}>{s.name}</option>
        ))}
      </select>

      {setId && (
        <select
          className="border-0 bg-transparent font-mono text-xs focus:outline-none text-ok-600 flex-1 min-w-0"
          value={keyValue}
          onChange={(e) => onKeyChange(e.target.value)}
        >
          <option value="">— pick key —</option>
          {availableKeys.map((k) => (
            <option key={k} value={k}>{k}</option>
          ))}
        </select>
      )}
    </div>
  );
};
