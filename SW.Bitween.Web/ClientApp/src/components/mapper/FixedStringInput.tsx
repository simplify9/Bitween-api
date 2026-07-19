import type React from "react";
import { useEffect, useRef, useState } from "react";

// ─── Types ────────────────────────────────────────────────────────────────────

export interface FixedStringInputProps {
  value: string;
  /** Non-array source paths available for variable insertion. */
  sourcePaths: string[];
  onChange: (value: string) => void;
}

// ─── FixedStringInput ─────────────────────────────────────────────────────────
// Fixed mode input for string targets. Lets the user type a literal string and
// embed source field references via a { } picker button, e.g. "Hello {{ name }}".

export const FixedStringInput: React.FC<FixedStringInputProps> = ({ value, sourcePaths, onChange }) => {
  const [pickerRect, setPickerRect] = useState<DOMRect | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const pickerRef = useRef<HTMLDivElement>(null);

  const showPicker = pickerRect !== null;

  const openPicker = () => {
    if (pickerRect) {
      setPickerRect(null);
      return;
    }
    const rect = buttonRef.current?.getBoundingClientRect();
    setPickerRect(rect ?? null);
  };

  const closePicker = () => setPickerRect(null);

  const insertVar = (path: string) => {
    const snippet = `{{ ${path} }}`;
    const input = inputRef.current;
    if (!input) {
      onChange(value + snippet);
      closePicker();
      return;
    }
    const start = input.selectionStart ?? value.length;
    const end = input.selectionEnd ?? value.length;
    const newVal = value.slice(0, start) + snippet + value.slice(end);
    onChange(newVal);
    closePicker();
    requestAnimationFrame(() => {
      input.focus();
      const pos = start + snippet.length;
      input.setSelectionRange(pos, pos);
    });
  };

  // Close picker on outside click
  useEffect(() => {
    if (!showPicker) return;
    const handler = (e: MouseEvent) => {
      if (
        !pickerRef.current?.contains(e.target as Node) &&
        !buttonRef.current?.contains(e.target as Node)
      ) {
        closePicker();
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [showPicker]);

  return (
    <div
      className="flex-1 relative flex items-center min-w-0"
      onClick={(e) => e.stopPropagation()}
    >
      <input
        ref={inputRef}
        className="flex-1 border-0 bg-transparent font-mono text-xs focus:outline-none text-warn-700 min-w-0 placeholder-warn-100"
        placeholder="fixed value… or {{ field }}"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      {sourcePaths.length > 0 && (
        <button
          ref={buttonRef}
          type="button"
          title="Insert source field variable"
          className="flex-shrink-0 font-mono text-[10px] leading-none px-1 py-0.5 rounded text-warn-700 hover:text-warn-700 hover:bg-warn-100 transition-colors"
          onClick={(e) => { e.stopPropagation(); openPicker(); }}
        >
          {'{ }'}
        </button>
      )}
      {showPicker && pickerRect && (
        <div
          ref={pickerRef}
          style={{
            position: 'fixed',
            top: pickerRect.bottom + 2,
            right: window.innerWidth - pickerRect.right,
          }}
          className="z-[200] max-h-48 overflow-y-auto rounded border border-warn-100 bg-white shadow-lg min-w-[160px]"
        >
          {sourcePaths.map((p) => (
            <button
              key={p}
              type="button"
              className="block w-full text-left px-2 py-1 font-mono text-[10px] text-ink-700 hover:bg-warn-100 truncate"
              onMouseDown={(e) => e.preventDefault()}
              onClick={(e) => { e.stopPropagation(); insertVar(p); }}
            >
              {p}
            </button>
          ))}
        </div>
      )}
    </div>
  );
};
