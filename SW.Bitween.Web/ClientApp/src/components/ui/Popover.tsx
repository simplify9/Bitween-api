import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";

const GAP = 6;
const MARGIN = 8;

/**
 * A small card anchored under its trigger.
 *
 * Portalled with fixed coordinates rather than positioned `absolute` inside the
 * trigger, because every list table is an `overflow-x-auto` scroll container
 * and a scroll container clips on *both* axes — an in-flow panel would be cut
 * off by the row it belongs to.
 *
 * Closes on Esc, on an outside click, and on any scroll that isn't the panel's
 * own: a fixed panel can't follow its trigger, so it must not outlive the
 * position it was measured at.
 */
export function Popover({
  button,
  label,
  children,
  width = "w-72",
}: {
  /** Rendered inside the trigger button. */
  button: ReactNode;
  /** Accessible name for the trigger. */
  label: string;
  /** Panel contents. */
  children: ReactNode;
  /** Tailwind width class for the panel. */
  width?: string;
}) {
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState({ top: 0, left: 0 });
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  useLayoutEffect(() => {
    const trigger = triggerRef.current;
    const panel = panelRef.current;
    if (!open || !trigger || !panel) return;

    const anchor = trigger.getBoundingClientRect();
    const { width: w, height: h } = panel.getBoundingClientRect();
    // Below the trigger unless that would run off the bottom, then above it.
    const below = anchor.bottom + GAP;
    const top = below + h > window.innerHeight - MARGIN ? Math.max(MARGIN, anchor.top - GAP - h) : below;
    const left = Math.min(Math.max(MARGIN, anchor.left), window.innerWidth - w - MARGIN);
    setPos({ top, left });
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const inside = (target: EventTarget | null) =>
      panelRef.current?.contains(target as Node) || triggerRef.current?.contains(target as Node);

    const onDown = (e: MouseEvent) => !inside(e.target) && setOpen(false);
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    const onScroll = (e: Event) => !panelRef.current?.contains(e.target as Node) && setOpen(false);

    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    document.addEventListener("scroll", onScroll, true);
    window.addEventListener("resize", onScroll);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("scroll", onScroll, true);
      window.removeEventListener("resize", onScroll);
    };
  }, [open]);

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        aria-label={label}
        aria-expanded={open}
        // The row underneath is usually clickable; opening the panel is not
        // a request to navigate.
        onClick={(e) => {
          e.stopPropagation();
          setOpen((o) => !o);
        }}
        className="cursor-pointer rounded text-ink-400 underline-offset-2 hover:text-crimson-700 hover:underline"
      >
        {button}
      </button>
      {open &&
        createPortal(
          <div
            ref={panelRef}
            // The portal escapes the DOM tree but NOT React's: a click in here
            // still bubbles to whatever contains <Popover>, which for a table
            // cell is the row — clicking a link in the panel would follow the
            // row's destination instead of the link's.
            onClick={(e) => e.stopPropagation()}
            style={{ top: pos.top, left: pos.left }}
            className={`fixed z-50 ${width} max-h-80 overflow-y-auto rounded-xl border border-ink-100 bg-white p-2 shadow-lg`}
          >
            {children}
          </div>,
          document.body,
        )}
    </>
  );
}
