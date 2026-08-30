import type { ButtonHTMLAttributes, ReactNode } from "react";
import { Loader2, TriangleAlert } from "lucide-react";

type ButtonVariant = "primary" | "secondary" | "ghost" | "danger";

const buttonStyles: Record<ButtonVariant, string> = {
  primary:
    "bg-crimson-600 text-white hover:bg-crimson-700 active:bg-crimson-800 disabled:bg-ink-200 disabled:text-ink-400",
  secondary:
    "bg-white text-ink-800 border border-ink-200 hover:border-ink-300 hover:bg-ink-50 disabled:text-ink-300",
  ghost: "text-ink-700 hover:bg-ink-100 disabled:text-ink-300",
  danger:
    "bg-white text-danger-700 border border-danger-200 hover:bg-danger-50 hover:border-danger-300 disabled:text-ink-300 disabled:border-ink-200",
};

export function Button({
  variant = "secondary",
  size = "md",
  busy = false,
  className = "",
  children,
  disabled,
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  size?: "sm" | "md";
  busy?: boolean;
}) {
  return (
    <button
      type="button"
      disabled={disabled || busy}
      className={`inline-flex items-center justify-center gap-1.5 rounded-lg font-medium transition-colors cursor-pointer disabled:cursor-not-allowed ${
        size === "sm" ? "h-8 px-3 text-[13px]" : "h-9.5 px-4 text-sm"
      } ${buttonStyles[variant]} ${className}`}
      {...rest}
    >
      {busy && <Loader2 className="size-4 animate-spin" aria-hidden />}
      {children}
    </button>
  );
}

/**
 * `crimson` is brand emphasis ("You", "Unsaved") and follows the theme color.
 * `danger` means something is wrong and stays red — see the danger ramp in index.css.
 */
type BadgeTone = "neutral" | "crimson" | "danger" | "ok" | "warn" | "ink";

const badgeStyles: Record<BadgeTone, string> = {
  neutral: "bg-ink-100 text-ink-700",
  crimson: "bg-crimson-100 text-crimson-800",
  danger: "bg-danger-100 text-danger-800",
  ok: "bg-ok-100 text-ok-600",
  warn: "bg-warn-100 text-warn-700",
  ink: "bg-ink-800 text-ink-100",
};

export function Badge({
  tone = "neutral",
  className = "",
  title,
  children,
}: {
  tone?: BadgeTone;
  className?: string;
  /** Hover explanation — for badges whose one word can't carry the whole meaning. */
  title?: string;
  children: ReactNode;
}) {
  return (
    <span
      title={title}
      className={`inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-xs font-medium whitespace-nowrap ${badgeStyles[tone]} ${className}`}
    >
      {children}
    </span>
  );
}

export function LoadingBlock({ label = "Loading…" }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2 py-16 text-ink-500">
      <Loader2 className="size-4 animate-spin" aria-hidden />
      <span className="text-sm">{label}</span>
    </div>
  );
}

export function EmptyState({
  icon,
  title,
  children,
  action,
}: {
  icon?: ReactNode;
  title: string;
  children?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-ink-200 bg-white px-6 py-14 text-center">
      {icon && <div className="mb-1 text-ink-300 [&>svg]:size-8">{icon}</div>}
      <h3 className="text-sm font-semibold text-ink-800">{title}</h3>
      {children && <p className="max-w-sm text-sm text-ink-500">{children}</p>}
      {action && <div className="mt-3">{action}</div>}
    </div>
  );
}

/** Slim inline banner for a known, expected degraded state (e.g. an optional integration not configured). */
export function InlineNotice({ children }: { children: ReactNode }) {
  return (
    <p className="mb-3 flex items-start gap-1.5 rounded-lg border border-warn-100 bg-warn-100/40 px-3 py-2 text-[13px] text-warn-800">
      <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden />
      <span>{children}</span>
    </p>
  );
}

/** Inline error strip for failed form submissions. */
export function FormError({ children }: { children: ReactNode }) {
  if (!children) return null;
  return (
    <p role="alert" className="rounded-lg bg-danger-50 px-3 py-2 text-sm text-danger-800">
      {children}
    </p>
  );
}
