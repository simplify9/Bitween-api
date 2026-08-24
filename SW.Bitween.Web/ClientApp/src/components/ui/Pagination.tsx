import { Button } from "./basics";

/**
 * The "Showing X–Y of Z" + Previous/Next footer, lifted out of the Exchanges
 * page so every other table gets the same server-paged behavior without
 * hand-rolling it again. Fixed page size, no jump-to-page — matches the one
 * table that already did this.
 */
export function Pagination({
  offset,
  limit,
  total,
  onOffsetChange,
}: {
  offset: number;
  limit: number;
  total: number;
  onOffsetChange: (offset: number) => void;
}) {
  if (total === 0) return null;
  return (
    <div className="flex items-center justify-between border-t border-ink-100 px-4 py-2.5 text-[13px] text-ink-500">
      <span>
        Showing {offset + 1}–{Math.min(offset + limit, total)} of {total}
      </span>
      <span className="flex gap-1.5">
        <Button size="sm" disabled={offset === 0} onClick={() => onOffsetChange(Math.max(0, offset - limit))}>
          Previous
        </Button>
        <Button size="sm" disabled={offset + limit >= total} onClick={() => onOffsetChange(offset + limit)}>
          Next
        </Button>
      </span>
    </div>
  );
}
