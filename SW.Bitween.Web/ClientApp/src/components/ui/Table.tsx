import type { ReactNode } from "react";

export interface Column<T> {
  /** Header text. Empty string for action/icon columns. */
  header: string;
  /** Cell renderer. */
  cell: (row: T) => ReactNode;
  /** Extra classes on both header and cells — width, alignment, wrapping. */
  className?: string;
  /** Right-align the column (counts, times, actions). */
  align?: "right";
  /**
   * This column holds free text that may be long (a name, a path, an
   * exception) and should ellipsis rather than widen the table. The cell
   * still needs `truncate` on its own child element to show the ellipsis.
   */
  truncate?: boolean;
}

/**
 * Width strategy, and the reason it isn't just `max-w-0` everywhere:
 * `max-width: 0` lets a cell shrink below its content so `truncate` can bite,
 * but in an auto-layout table several such columns all collapse to their
 * minimum and the row turns into a line of ellipses. So every *other* column
 * gets `w-px whitespace-nowrap` — the standard shrink-to-content trick — which
 * leaves all the slack for the truncating ones to share.
 */
const widthClass = <T,>(c: Column<T>) => (c.truncate ? "max-w-0" : "w-px whitespace-nowrap");

const cellClass = <T,>(c: Column<T>) =>
  `px-3 py-1.5 ${widthClass(c)} ${c.align === "right" ? "text-right" : ""} ${c.className ?? ""}`;

/**
 * The page-level table: a bordered card holding every row of a list page.
 *
 * Density is owned here rather than per page — `px-3 py-1.5` throughout, so
 * a screen shows as many rows as it can rather than padding a handful out to
 * fill it. Never truncate the table itself; wide content scrolls sideways
 * inside the card while the page body stays put.
 */
export function Table<T>({
  columns,
  rows,
  rowKey,
  onRowClick,
  minWidth = "min-w-160",
  footer,
}: {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string | number;
  onRowClick?: (row: T) => void;
  /** Tailwind min-width class — the point at which horizontal scroll kicks in. */
  minWidth?: string;
  /** Rendered under the table inside the same card, e.g. pagination. */
  footer?: ReactNode;
}) {
  return (
    <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
      <table className={`w-full ${minWidth} text-left text-sm`}>
        <thead>
          <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
            {columns.map((c, i) => (
              <th key={i} className={cellClass(c)}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              key={rowKey(row)}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              className={`border-b border-ink-50 last:border-b-0 ${
                onRowClick ? "cursor-pointer hover:bg-ink-50/60" : ""
              }`}
            >
              {columns.map((c, i) => (
                <td key={i} className={cellClass(c)}>
                  {c.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {footer}
    </div>
  );
}

/**
 * The in-panel table — same data-density rules, no card of its own, since it
 * already sits inside a `Panel`. This is what replaced the old icon-and-link
 * lists ("Acme Logistics runs Acme invoice submission"): a sentence per row
 * reads fine at three rows and is useless at thirty, and it wasted the whole
 * right half of the row on nothing.
 */
export function MiniTable<T>({
  columns,
  rows,
  rowKey,
  empty,
  onRowClick,
  fitWidth = false,
}: {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string | number;
  /** Shown instead of the table when there are no rows. */
  empty: ReactNode;
  /** Makes the whole row the way in, as on the page-level table. */
  onRowClick?: (row: T) => void;
  /**
   * Honour `truncate` and stay inside the panel instead of growing past it.
   *
   * Off by default because most callers are 2–4 column lists in the ~360px
   * sidebar, where collapsing a column to ellipsis its text gives a row of
   * "3c8…" — there, growing and scrolling sideways is the better trade. A
   * wide table in the main column is the opposite case: it has the room, and
   * what it pushes out of reach is the action buttons on the right, which
   * nobody thinks to scroll a table sideways to find.
   */
  fitWidth?: boolean;
}) {
  if (rows.length === 0) return <p className="text-sm text-ink-500">{empty}</p>;

  const cell = (c: Column<T>) =>
    `px-1 ${fitWidth ? widthClass(c) : "whitespace-nowrap"} ${c.align === "right" ? "text-right" : ""} ${c.className ?? ""}`;

  return (
    <div className="-mx-1 overflow-x-auto">
      <table className={`w-full ${fitWidth ? "" : "min-w-max"} text-left text-sm`}>
        <thead>
          <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
            {columns.map((c, i) => (
              <th key={i} className={`pb-1 ${cell(c)}`}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              key={rowKey(row)}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              className={`border-b border-ink-50 last:border-b-0 ${
                onRowClick ? "cursor-pointer hover:bg-ink-50/60" : ""
              }`}
            >
              {columns.map((c, i) => (
                <td key={i} className={`py-1.5 ${cell(c)}`}>
                  {c.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
