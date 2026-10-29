import type { KeyboardEvent } from "react";

/**
 * What a table row that opens something needs so a keyboard can open it too: a tab stop, and
 * Enter or Space to activate. Keys pressed inside a control in the row (a checkbox, a button)
 * stay that control's.
 */
export function clickableRow(onActivate: () => void) {
  return {
    tabIndex: 0,
    onClick: onActivate,
    onKeyDown: (e: KeyboardEvent<HTMLElement>) => {
      if (e.target !== e.currentTarget || (e.key !== "Enter" && e.key !== " ")) return;
      e.preventDefault();
      onActivate();
    },
  };
}

/** The focus ring a clickable row shows, drawn inside it so the table's overflow can't clip it. */
export const clickableRowClass = "focus-visible:outline-none focus-visible:bg-focus-100/60";
