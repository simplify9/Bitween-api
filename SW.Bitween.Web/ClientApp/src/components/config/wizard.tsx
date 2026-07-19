import { useState, type ReactNode } from "react";
import { Link } from "react-router";
import { ArrowLeft, Check, Plus, SquarePen } from "lucide-react";
import { Button, FormError } from "../ui/basics";

/**
 * Draft state that survives the routed create/edit detours of the
 * return-flow: persisted in sessionStorage, cleared on submit.
 */
export function usePersistentDraft<T>(key: string, initial: T) {
  const [draft, setDraft] = useState<T>(() => {
    try {
      const raw = sessionStorage.getItem(key);
      if (raw) return JSON.parse(raw) as T;
    } catch {
      // corrupted draft — start fresh
    }
    return initial;
  });

  const update = (patch: Partial<T>) =>
    setDraft((d) => {
      const next = { ...d, ...patch };
      sessionStorage.setItem(key, JSON.stringify(next));
      return next;
    });

  const clear = () => sessionStorage.removeItem(key);

  return [draft, update, clear] as const;
}

/**
 * Full-page guided flow. URL-routed (survives refresh as a page; step
 * state is local). Steps render as numbered chips with the current one
 * highlighted and finished ones checked.
 */
export function WizardShell({
  title,
  subtitle,
  backTo,
  backLabel,
  steps,
  current,
  children,
}: {
  title: string;
  subtitle?: string;
  backTo: string;
  backLabel: string;
  steps: string[];
  current: number;
  children: ReactNode;
}) {
  return (
    <div className="pb-10">
      <Link
        to={backTo}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {backLabel}
      </Link>

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">{title}</h1>
      {subtitle && <p className="mt-1 text-sm text-ink-500">{subtitle}</p>}

      <ol className="mt-5 flex flex-wrap items-center gap-2">
        {steps.map((label, i) => (
          <li key={label} className="flex items-center gap-2">
            {i > 0 && <span className="h-px w-5 bg-ink-200" aria-hidden />}
            <span
              className={`flex items-center gap-1.5 rounded-full px-3 py-1 text-[13px] font-medium ${
                i === current
                  ? "bg-ink-900 text-white"
                  : i < current
                    ? "bg-ok-100 text-ok-600"
                    : "bg-ink-100 text-ink-500"
              }`}
            >
              {i < current ? <Check className="size-3.5" /> : <span>{i + 1}.</span>}
              {label}
            </span>
          </li>
        ))}
      </ol>

      <div className="mt-6 max-w-3xl rounded-xl border border-ink-200 bg-white p-5">{children}</div>
    </div>
  );
}

/** Back/continue footer for a wizard step. */
export function StepNav({
  onBack,
  onNext,
  nextLabel = "Continue",
  nextDisabled = false,
  busy = false,
  error,
}: {
  onBack?: () => void;
  onNext: () => void;
  nextLabel?: string;
  nextDisabled?: boolean;
  busy?: boolean;
  error?: string;
}) {
  return (
    <div className="mt-5 space-y-3 border-t border-ink-100 pt-4">
      <FormError>{error}</FormError>
      <div className="flex justify-between">
        {onBack ? <Button onClick={onBack}>Back</Button> : <span />}
        <Button variant="primary" disabled={nextDisabled} busy={busy} onClick={onNext}>
          {nextLabel}
        </Button>
      </div>
    </div>
  );
}

/** A selectable row card — the wizard's pick-one pattern. */
export function OptionCard({
  selected,
  onSelect,
  title,
  subtitle,
  right,
  editHref,
}: {
  selected: boolean;
  onSelect: () => void;
  title: string;
  subtitle?: string;
  right?: ReactNode;
  /** Detour link to view/edit this option on its own page (return-flow). */
  editHref?: string;
}) {
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onSelect}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onSelect();
        }
      }}
      aria-pressed={selected}
      className={`flex w-full cursor-pointer items-center gap-3 rounded-xl border px-4 py-3 text-left transition-colors ${
        selected
          ? "border-crimson-400 bg-crimson-50/50 ring-2 ring-crimson-100"
          : "border-ink-200 bg-white hover:border-ink-300 hover:bg-ink-50"
      }`}
    >
      <span
        aria-hidden
        className={`flex size-4 shrink-0 items-center justify-center rounded-full border ${
          selected ? "border-crimson-600 bg-crimson-600" : "border-ink-300 bg-white"
        }`}
      >
        {selected && <span className="size-1.5 rounded-full bg-white" />}
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-medium text-ink-900">{title}</span>
        {subtitle && <span className="block truncate text-[13px] text-ink-500">{subtitle}</span>}
      </span>
      {right}
      {editHref && (
        <Link
          to={editHref}
          onClick={(e) => e.stopPropagation()}
          title="Open — you can come back and continue"
          aria-label={`Open ${title}`}
          className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
        >
          <SquarePen className="size-3.5" />
        </Link>
      )}
    </div>
  );
}

/** "Add new …" — routes to the entity's create page (return-flow detour). */
export function CreateLinkCard({ to, title, subtitle }: { to: string; title: string; subtitle?: string }) {
  return (
    <Link
      to={to}
      className="flex w-full items-center gap-3 rounded-xl border border-dashed border-ink-300 bg-white px-4 py-3 text-left transition-colors hover:border-crimson-400 hover:bg-crimson-50/40"
    >
      <span className="flex size-4 shrink-0 items-center justify-center text-ink-400" aria-hidden>
        <Plus className="size-4" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-medium text-ink-900">{title}</span>
        {subtitle && <span className="block truncate text-[13px] text-ink-500">{subtitle}</span>}
      </span>
    </Link>
  );
}
