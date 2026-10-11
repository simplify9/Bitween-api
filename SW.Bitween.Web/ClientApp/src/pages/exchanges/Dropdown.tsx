import { useEffect, useRef, useState, type ReactNode } from "react";

/**
 * A panel under its trigger that stays open while it is being used - the query bar's menus hold
 * forms, so unlike `Menu` a click inside must not close it. Closes on Escape and an outside click.
 *
 * A click in a Headless UI portal counts as inside: the pickers in these panels (`SearchSelect`)
 * float their option lists into one, and treating that as outside closed the panel on the
 * mousedown, before the option's click could land.
 */
export function Dropdown({
  trigger,
  children,
  align = "left",
  width = "w-80",
  open: controlledOpen,
  onOpenChange,
}: {
  trigger: (props: { open: boolean; toggle: () => void }) => ReactNode;
  children: (close: () => void) => ReactNode;
  align?: "left" | "right";
  width?: string;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}) {
  const [ownOpen, setOwnOpen] = useState(false);
  const open = controlledOpen ?? ownOpen;
  const setOpen = (o: boolean) => {
    setOwnOpen(o);
    onOpenChange?.(o);
  };
  const ref = useRef<HTMLDivElement>(null);
  const setOpenRef = useRef(setOpen);
  setOpenRef.current = setOpen;

  useEffect(() => {
    if (!open) return;
    const inside = (target: EventTarget | null) => {
      const el = target as Element | null;
      return !!(ref.current?.contains(el) || el?.closest?.("#headlessui-portal-root"));
    };
    const onDown = (e: MouseEvent) => !inside(e.target) && setOpenRef.current(false);
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      // The page underneath also listens for Escape (it closes the open exchange).
      e.stopPropagation();
      setOpenRef.current(false);
    };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey, true);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey, true);
    };
  }, [open]);

  return (
    <div ref={ref} className="relative">
      {trigger({ open, toggle: () => setOpen(!open) })}
      {open && (
        <div
          className={`absolute top-full z-40 mt-1.5 ${align === "right" ? "right-0" : "left-0"} ${width} max-w-[calc(100vw-2rem)] rounded-xl border border-ink-200 bg-white shadow-lg`}
        >
          {children(() => setOpen(false))}
        </div>
      )}
    </div>
  );
}
