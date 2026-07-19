import { useEffect, useRef, useState, type ReactNode } from "react";
import { X } from "lucide-react";
import { Button, FormError } from "./basics";

/** Centered modal. Closes on Escape and backdrop click. */
export function Dialog({
  title,
  onClose,
  children,
  wide = false,
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
  wide?: boolean;
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-ink-950/40 p-4 pt-[10vh]"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`w-full ${wide ? "max-w-2xl" : "max-w-md"} rounded-2xl bg-white shadow-xl`}
      >
        <div className="flex items-center justify-between border-b border-ink-100 px-5 py-3.5">
          <h2 className="text-[15px] font-semibold text-ink-900">{title}</h2>
          <button
            onClick={onClose}
            aria-label="Close"
            className="rounded-md p-1 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
          >
            <X className="size-4.5" />
          </button>
        </div>
        <div className="px-5 py-4">{children}</div>
      </div>
    </div>
  );
}

/**
 * Confirmation for destructive actions. The action runs here so the
 * dialog can surface its error instead of silently closing.
 */
export function ConfirmDialog({
  title,
  body,
  confirmLabel,
  onConfirm,
  onClose,
}: {
  title: string;
  body: ReactNode;
  confirmLabel: string;
  onConfirm: () => Promise<void>;
  onClose: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const run = async () => {
    setBusy(true);
    setError("");
    try {
      await onConfirm();
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog title={title} onClose={onClose}>
      <div className="space-y-4">
        <div className="text-sm text-ink-600">{body}</div>
        <FormError>{error}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="danger" busy={busy} onClick={run}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </Dialog>
  );
}

/** Minimal dropdown anchored to its trigger. Use side="up" for triggers near the bottom of the viewport. */
export function Menu({
  trigger,
  children,
  align = "right",
  side = "down",
}: {
  trigger: ReactNode;
  children: ReactNode;
  align?: "left" | "right";
  side?: "down" | "up";
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  return (
    <div ref={ref} className="relative">
      <div onClick={() => setOpen((o) => !o)}>{trigger}</div>
      {open && (
        <div
          onClick={() => setOpen(false)}
          className={`absolute ${align === "right" ? "right-0" : "left-0"} ${
            side === "up" ? "bottom-full mb-1.5" : "top-full mt-1.5"
          } z-40 min-w-44 rounded-xl border border-ink-100 bg-white p-1 shadow-lg`}
        >
          {children}
        </div>
      )}
    </div>
  );
}

export function MenuItem({
  onSelect,
  danger = false,
  children,
}: {
  onSelect: () => void;
  danger?: boolean;
  children: ReactNode;
}) {
  return (
    <button
      onClick={onSelect}
      className={`flex w-full items-center gap-2 rounded-lg px-2.5 py-1.5 text-left text-sm ${
        danger ? "text-crimson-700 hover:bg-crimson-50" : "text-ink-700 hover:bg-ink-50"
      }`}
    >
      {children}
    </button>
  );
}
