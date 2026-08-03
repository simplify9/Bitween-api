import type { ReactNode } from "react";
import { Button, FormError } from "./basics";

/** A titled card section on a hub page, with optional header action. */
export function Panel({
  title,
  description,
  action,
  children,
  className = "",
}: {
  title: string;
  description?: string;
  action?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={`rounded-xl border border-ink-200 bg-white ${className}`}>
      <div className="flex items-start justify-between gap-3 px-4 pt-3 pb-2">
        <div>
          <h2 className="text-[15px] font-semibold text-ink-900">{title}</h2>
          {description && <p className="mt-0.5 text-[13px] text-ink-500">{description}</p>}
        </div>
        {action}
      </div>
      <div className="px-4 pb-3.5">{children}</div>
    </section>
  );
}

/**
 * A page-title name, editable in place. Looks like plain heading text at
 * rest; hovering reveals it's a field, focusing turns it into one. Used
 * instead of a "Details" panel that would otherwise hold nothing but a
 * Name input, padded out to a full-width card for no reason.
 */
export function EditableTitle({
  value,
  onChange,
  disabled,
  placeholder,
}: {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  placeholder?: string;
}) {
  if (disabled) return <span>{value}</span>;
  return (
    <input
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      aria-label="Name"
      className="-mx-1.5 w-72 max-w-full rounded-md border border-transparent bg-transparent px-1.5 py-0.5 text-[22px] font-semibold tracking-tight text-ink-900 placeholder:text-ink-300 hover:border-ink-200 hover:bg-ink-50 focus:border-crimson-400 focus:bg-white focus:ring-2 focus:ring-crimson-100 focus:outline-none"
    />
  );
}

/** UPPER_SNAKE identity chip, e.g. PURCHASE_ORDER. Code is optional — falls back to `name` when unset. */
export function CodeBadge({
  code,
  name,
  className = "",
}: {
  code?: string;
  name?: string;
  className?: string;
}) {
  const text = code || name;
  if (!text) return null;
  return (
    <code
      className={`inline-block rounded-md bg-ink-800 px-1.5 py-0.5 font-mono text-[11px] font-medium text-ink-100 ${className}`}
    >
      {text}
    </code>
  );
}

/** Sticky bottom bar shown while a detail page has unsaved edits. */
export function UnsavedBar({
  error,
  busy,
  onSave,
  onDiscard,
}: {
  error?: string;
  busy: boolean;
  onSave: () => void;
  onDiscard: () => void;
}) {
  return (
    <div className="fixed inset-x-0 bottom-0 z-30 border-t border-ink-200 bg-white/95 backdrop-blur lg:left-[var(--sidebar-w,15.5rem)]">
      {/* right padding keeps the fixed demo pill clear of the save button */}
      <div className="flex items-center justify-between gap-3 py-2.5 pr-40 pl-4 sm:pl-6">
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-ink-800">Unsaved changes</p>
          <FormError>{error}</FormError>
        </div>
        <div className="flex shrink-0 gap-2">
          <Button onClick={onDiscard}>Discard</Button>
          <Button variant="primary" busy={busy} onClick={onSave}>
            Save changes
          </Button>
        </div>
      </div>
    </div>
  );
}
