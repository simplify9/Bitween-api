import type { ReactNode } from "react";
import { clickableRow, clickableRowClass } from "./clickableRow";

/** One number or short value on a card, read as a label over its value. */
export interface CardFact {
  label: string;
  value: ReactNode;
  /** Colours the value the way the table colours its cell: amber for retrying, red for dead. */
  tone?: "warn" | "danger" | "muted";
  title?: string;
}

/**
 * What a table row says when it is a card on a phone. Not every column: a card that repeats all
 * eleven of them is the table again, one cell per line. The name, where it stands, the full
 * technical name for anyone matching it against a log, and the few numbers people check.
 */
export interface RowCardSpec {
  title: ReactNode;
  /** Badges, top right. */
  status?: ReactNode;
  /** A line of plain words under the title: what it is, who it belongs to. */
  subtitle?: ReactNode;
  /**
   * The full technical name - a queue, an email, a code. Small mono that wraps anywhere, because
   * these have no spaces and a phone is narrower than most of them.
   */
  technical?: ReactNode;
  facts?: CardFact[];
  /** Under everything: a last error, an action. */
  footer?: ReactNode;
}

const toneClass: Record<NonNullable<CardFact["tone"]>, string> = {
  warn: "font-medium text-warn-700",
  danger: "font-medium text-danger-700",
  muted: "text-ink-500",
};

export function RowCard({ spec, onClick }: { spec: RowCardSpec; onClick?: () => void }) {
  return (
    <div
      {...(onClick ? clickableRow(onClick) : {})}
      className={`flex flex-col gap-2 px-3.5 py-3 ${onClick ? `cursor-pointer active:bg-ink-50/60 ${clickableRowClass}` : ""}`}
    >
      <div className="flex items-start justify-between gap-2">
        <span className="min-w-0 font-medium wrap-anywhere text-ink-900">{spec.title}</span>
        {spec.status && <span className="flex shrink-0 flex-wrap justify-end gap-1">{spec.status}</span>}
      </div>
      {spec.subtitle && <div className="-mt-1.5 text-[13px] text-ink-600">{spec.subtitle}</div>}
      {spec.technical && <code className="block font-mono text-[11px] wrap-anywhere text-ink-500">{spec.technical}</code>}
      {spec.facts && spec.facts.length > 0 && (
        <dl className="grid grid-cols-3 gap-x-3 gap-y-1.5 tabular-nums">
          {spec.facts.map((f) => (
            <div key={f.label} className="min-w-0" title={f.title}>
              <dt className="text-[11px] tracking-wide text-ink-500 uppercase">{f.label}</dt>
              <dd className={`text-sm wrap-anywhere ${f.tone ? toneClass[f.tone] : "text-ink-800"}`}>{f.value}</dd>
            </div>
          ))}
        </dl>
      )}
      {spec.footer}
    </div>
  );
}

/**
 * Cards for the rows of a list, one under another in a single bordered card, so a phone reads the
 * same box the table sits in on a desk. Dividers rather than gaps: a list of forty jobs as forty
 * floating boxes spends a third of the screen on margins.
 */
export function CardList<T>({
  rows,
  rowKey,
  card,
  onRowClick,
  label,
  footer,
  bare = false,
}: {
  rows: T[];
  rowKey: (row: T) => string | number;
  card: (row: T) => RowCardSpec;
  onRowClick?: (row: T) => void;
  label?: string;
  footer?: ReactNode;
  /** Already inside a panel: no border or background of its own. */
  bare?: boolean;
}) {
  return (
    <div className={bare ? "" : "overflow-hidden rounded-xl border border-ink-200 bg-white"}>
      <ul aria-label={label} className="divide-y divide-ink-100">
        {rows.map((row) => (
          <li key={rowKey(row)}>
            <RowCard spec={card(row)} onClick={onRowClick ? () => onRowClick(row) : undefined} />
          </li>
        ))}
      </ul>
      {footer}
    </div>
  );
}
