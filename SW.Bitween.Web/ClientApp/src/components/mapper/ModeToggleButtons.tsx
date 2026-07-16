import type React from "react";
import type { MappingMode } from "../../lib/mapping/types";

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ModeToggleButtonsProps {
  current: MappingMode;
  onChange: (mode: MappingMode) => void;
}

// ─── Config ───────────────────────────────────────────────────────────────────

const MODE_CONFIG: { mode: MappingMode; label: string; activeCls: string; inactiveCls: string }[] = [
  {
    mode: 'source',
    label: 'Source',
    activeCls: 'bg-crimson-500 text-white',
    inactiveCls: 'text-ink-400 hover:bg-ink-50',
  },
  {
    mode: 'fixed',
    label: 'Fixed',
    activeCls: 'bg-warn-700 text-white border-l border-warn-100',
    inactiveCls: 'text-ink-400 border-l border-ink-200 hover:bg-ink-50',
  },
  {
    mode: 'partner',
    label: 'Partner',
    activeCls: 'bg-ok-600 text-white border-l border-ok-100',
    inactiveCls: 'text-ink-400 border-l border-ink-200 hover:bg-ink-50',
  },
  {
    mode: 'global',
    label: 'Global',
    activeCls: 'bg-ok-600 text-white border-l border-ok-100',
    inactiveCls: 'text-ink-400 border-l border-ink-200 hover:bg-ink-50',
  },
];

// ─── ModeToggleButtons ────────────────────────────────────────────────────────

export const ModeToggleButtons: React.FC<ModeToggleButtonsProps> = ({ current, onChange }) => (
  <div className="flex flex-shrink-0 rounded overflow-hidden border border-ink-200 text-[10px] font-medium">
    {MODE_CONFIG.map(({ mode, label, activeCls, inactiveCls }) => (
      <button
        key={mode}
        onClick={(e) => {
          e.stopPropagation();
          onChange(mode);
        }}
        className={`px-1.5 py-0.5 ${current === mode ? activeCls : inactiveCls}`}
      >
        {label}
      </button>
    ))}
  </div>
);
