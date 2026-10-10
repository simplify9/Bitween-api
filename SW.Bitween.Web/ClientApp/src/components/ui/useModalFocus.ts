import { useEffect, type RefObject } from "react";

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * What a modal owes a keyboard: focus moves into it when it opens, Tab can't wander out to the
 * page behind it, and when it closes focus goes back to whatever opened it.
 *
 * A field that asked for autoFocus keeps it. Otherwise the panel itself takes focus — not its
 * first button, which is usually Close — so a screen reader announces the dialog by its label.
 * The panel needs tabIndex={-1} for that.
 */
export function useModalFocus(ref: RefObject<HTMLElement | null>) {
  useEffect(() => {
    const panel = ref.current;
    if (!panel) return;
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    if (!panel.contains(document.activeElement)) panel.focus({ preventScroll: true });

    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Tab") return;
      // A dialog opened from inside this one sits in this one's tree; its own trap has it.
      e.stopPropagation();
      const items = [...panel.querySelectorAll<HTMLElement>(FOCUSABLE)].filter(
        // Skips controls that are there but not shown (a collapsed section's). Where it isn't
        // implemented, as in jsdom, everything counts as shown.
        (el) => el.checkVisibility?.() ?? true,
      );
      if (items.length === 0) {
        e.preventDefault();
        return;
      }
      const first = items[0];
      const last = items[items.length - 1];
      const at = document.activeElement;
      if (e.shiftKey && (at === first || at === panel)) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && at === last) {
        e.preventDefault();
        first.focus();
      }
    };
    panel.addEventListener("keydown", onKey);
    return () => {
      panel.removeEventListener("keydown", onKey);
      if (opener?.isConnected) opener.focus({ preventScroll: true });
    };
  }, [ref]);
}
