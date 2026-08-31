import { useMemo, useState, type ReactNode } from "react";
import { Search } from "lucide-react";
import { Button } from "./basics";

export interface Column<T> {
  /** Header text. Empty string for action/icon columns. */
  header: string;
  /**
   * What the column means, shown on hovering the header.
   *
   * Required reading for any header whose value is derived, abbreviated or otherwise not
   * self-explanatory — "Reliability" reading `3/10` told nobody anything, and the header is
   * what a person reads before they think to hover a cell. Plain words, not the formula.
   */
  headerTitle?: string;
  /** Cell renderer. */
  cell: (row: T) => ReactNode;
  /** Extra classes on both header and cells — width, alignment, wrapping. */
  className?: string;
  /** Right-align the column (counts, times, actions). */
  align?: "right";
  /**
   * This column holds free text that may be long (a path, an exception, a
   * joined list of keys) and should ellipsis rather than widen the table. The
   * cell still needs `truncate` on its own child element to show the ellipsis,
   * and a `title` carrying the whole string.
   *
   * Only for text nobody needs in full at a glance. A name is never that —
   * use `wrap`.
   */
  truncate?: boolean;
  /**
   * This column holds a name, and a name has to be readable in full: a list of
   * “Customer Aggregation Trace Out - AL…” rows tells nobody which row is
   * theirs. The column takes the table's slack the way `truncate` does, but
   * wraps onto as many lines as the name needs instead of clipping it.
   */
  wrap?: boolean;
}

/**
 * Width strategy, and the reason it isn't just `max-w-0` everywhere:
 * `max-width: 0` lets a cell shrink below its content so `truncate` can bite,
 * but in an auto-layout table several such columns all collapse to their
 * minimum and the row turns into a line of ellipses. So every *other* column
 * gets `w-px whitespace-nowrap` — the standard shrink-to-content trick — which
 * leaves all the slack for the truncating ones to share.
 *
 * `wrap` columns take that same slack but carry no `max-w-0`, so the table
 * hands them width in proportion to how long their text really is and they
 * wrap inside it. `wrap-anywhere` rather than `break-words`: only the former
 * feeds mid-token breaks into intrinsic sizing, and a name with no spaces in it
 * would otherwise set the column's min-content and widen the table anyway. Both kinds also carry a floor, because the slack being
 * shared is what collapsed three columns into a grid of ellipses: below the
 * floor the card scrolls sideways instead of shaving every column at once.
 */
const widthClass = <T,>(c: Column<T>, compact = false) =>
  c.wrap
    ? `${compact ? "min-w-20" : "min-w-26"} wrap-anywhere`
    : c.truncate
      ? `max-w-0 ${compact ? "min-w-16" : "min-w-20"}`
      : "w-px whitespace-nowrap";

const cellClass = <T,>(c: Column<T>) =>
  `px-2 py-1.5 ${widthClass(c)} ${c.align === "right" ? "text-right" : ""} ${c.className ?? ""}`;

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
              <th key={i} className={cellClass(c)} title={c.headerTitle}>
                <span className={c.headerTitle ? "cursor-help decoration-ink-300 decoration-dotted underline-offset-4 hover:underline" : undefined}>
                  {c.header}
                </span>
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
 *
 * It fits its panel rather than growing past it. It used to do the opposite —
 * grow and scroll sideways — on the reasoning that squeezing a column in a
 * ~360px panel gives a row of "3c8…". `wrap` retired that reasoning: a narrow
 * column can now be read in full over two lines, and growing was costing more
 * than it saved, because what it pushed out of sight was the Status and When
 * columns on the right, which nobody thinks to scroll a panel sideways to find.
 */
export function MiniTable<T>({
  columns,
  rows,
  rowKey,
  empty,
  onRowClick,
  pageSize = 10,
  search,
}: {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string | number;
  /** Shown instead of the table when there are no rows. */
  empty: ReactNode;
  /** Makes the whole row the way in, as on the page-level table. */
  onRowClick?: (row: T) => void;
  /**
   * Rows per page. The pager appears only once there are more than this many,
   * so the panels the server caps at eight rows never grow one.
   */
  pageSize?: number;
  /**
   * Makes the list filterable, on the same rule as the pager: a list worth
   * searching is a list too long to read. `noun` names what is in it, because
   * "Search 45 rows" tells nobody what they are about to search.
   *
   * Only for lists the server hands over whole — filtering one that is already
   * just the latest eight reads as a search over everything, and isn't.
   */
  search?: { text: (row: T) => string; noun: string };
}) {
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(0);

  const matches = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!search || q === "") return rows;
    return rows.filter((r) => search.text(r).toLowerCase().includes(q));
  }, [rows, query, search]);

  if (rows.length === 0) return <p className="text-sm text-ink-500">{empty}</p>;

  // The box stays put once the list is long enough to have earned it, so
  // filtering down to three rows doesn't take away the thing that got you
  // there. The pager is the opposite: it goes when there is one page left.
  const searchable = search !== undefined && rows.length > pageSize;
  const paged = matches.length > pageSize;
  // A filter can shorten the list past the page you were on, which would leave
  // the panel empty with rows still in it.
  const start = Math.min(page, Math.max(0, Math.ceil(matches.length / pageSize) - 1)) * pageSize;
  const shown = paged ? matches.slice(start, start + pageSize) : matches;

  const cell = (c: Column<T>) =>
    `px-1 ${widthClass(c, true)} ${c.align === "right" ? "text-right" : ""} ${c.className ?? ""}`;

  return (
    <div className="space-y-2">
      {searchable && (
        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-ink-400" />
          <input
            type="search"
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setPage(0);
            }}
            placeholder={`Search ${rows.length} ${search.noun}`}
            aria-label={`Search ${search.noun}`}
            className="h-8 w-full rounded-lg border border-ink-200 bg-white pr-2.5 pl-8 text-[13px] placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
          />
        </div>
      )}

      {matches.length === 0 ? (
        <p className="py-1 text-sm text-ink-500">No {search?.noun ?? "rows"} match “{query.trim()}”.</p>
      ) : (
        <div className="-mx-1 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
                {columns.map((c, i) => (
                  <th key={i} className={`pb-1 ${cell(c)}`} title={c.headerTitle}>
                    <span className={c.headerTitle ? "cursor-help decoration-ink-300 decoration-dotted underline-offset-4 hover:underline" : undefined}>
                      {c.header}
                    </span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {shown.map((row) => (
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
      )}

      {paged && (
        <div className="flex items-center justify-between gap-2 pt-0.5 text-[12px] text-ink-500">
          <span className="tabular-nums">
            {start + 1}–{Math.min(start + pageSize, matches.length)} of {matches.length}
          </span>
          <span className="flex gap-1.5">
            <Button size="sm" disabled={start === 0} onClick={() => setPage(Math.max(0, page - 1))}>
              Previous
            </Button>
            <Button
              size="sm"
              disabled={start + pageSize >= matches.length}
              onClick={() => setPage(page + 1)}
            >
              Next
            </Button>
          </span>
        </div>
      )}
    </div>
  );
}
