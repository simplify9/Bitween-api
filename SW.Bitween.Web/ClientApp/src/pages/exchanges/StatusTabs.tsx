import type { ExchangeBreakdown, ExchangeStatus } from "../../api";
import { STATUS_LABELS } from "./journey";
import { COUNT_CAP, STATUSES, countLabel } from "./exchangeFilters";

const DOT: Record<ExchangeStatus | "all", string> = {
  all: "bg-ink-400",
  failed: "bg-danger-600",
  badResponse: "bg-warn-400",
  processing: "bg-ink-300",
  success: "bg-ok-600",
};

const PILL: Record<ExchangeStatus | "all", string> = {
  all: "bg-ink-100 text-ink-700",
  failed: "bg-danger-100 text-danger-800",
  badResponse: "bg-warn-100 text-warn-700",
  processing: "bg-ink-100 text-ink-700",
  success: "bg-ink-100 text-ink-700",
};

/**
 * The status picker, with how many exchanges each status holds under every other filter - so
 * "37 failed" is visible before anyone clicks, which is most of what this page gets opened for.
 *
 * Buttons rather than tabs: each one filters the same list rather than switching between panels.
 */
export function StatusTabs({
  status,
  counts,
  onPick,
  latest,
  onToggleLatest,
}: {
  status: ExchangeStatus | undefined;
  counts: ExchangeBreakdown["counts"] | undefined;
  onPick: (status: ExchangeStatus | null) => void;
  latest: boolean;
  onToggleLatest: () => void;
}) {
  const sum = counts ? STATUSES.reduce((n, s) => n + Math.min(counts[s], COUNT_CAP + 1), 0) : undefined;
  const items: { key: ExchangeStatus | "all"; label: string; count: number | undefined }[] = [
    { key: "all", label: "All", count: sum },
    ...STATUSES.map((s) => ({ key: s, label: STATUS_LABELS[s], count: counts?.[s] })),
  ];

  return (
    <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-b border-ink-100">
      <div role="group" aria-label="Status" className="-mb-px flex flex-wrap gap-1">
        {items.map((it) => {
          const active = it.key === "all" ? !status : status === it.key;
          // Counts that are worth noticing stand out; a large success count is not news.
          const alert = (it.key === "failed" || it.key === "badResponse") && (it.count ?? 0) > 0;
          return (
            <button
              key={it.key}
              type="button"
              aria-pressed={active}
              onClick={() => onPick(it.key === "all" || active ? null : it.key)}
              className={`inline-flex items-center gap-2 border-b-2 px-3 py-2.5 text-sm transition-colors ${
                active ? "border-ink-900 font-semibold text-ink-900" : "border-transparent font-medium text-ink-600 hover:text-ink-900"
              }`}
            >
              <span className={`size-2 rounded-full ${DOT[it.key]}`} aria-hidden />
              {it.label}
              {it.count !== undefined && " "}
              {it.count !== undefined && (
                <span
                  className={`rounded-full px-1.5 py-px text-xs font-semibold tabular-nums ${alert ? PILL[it.key] : PILL.all}`}
                >
                  {countLabel(it.count)}
                </span>
              )}
            </button>
          );
        })}
      </div>

      {/* A switch rather than another status: those pick one of a set, this narrows whatever they
          picked. A chain is one piece of work however many attempts it took, so this is what
          turns a list of failures into a list of things still to deal with. */}
      <button
        type="button"
        aria-pressed={latest}
        onClick={onToggleLatest}
        title="Hide attempts that have since been retried, leaving the newest attempt of each chain - the one that is still the state of that work."
        className="inline-flex items-center gap-2 py-1.5 text-[13px] text-ink-700"
      >
        <span className={`relative inline-block h-[18px] w-[30px] rounded-full transition-colors ${latest ? "bg-crimson-600" : "bg-ink-300"}`} aria-hidden>
          <span className={`absolute top-0.5 size-3.5 rounded-full bg-white transition-all ${latest ? "left-[14px]" : "left-0.5"}`} />
        </span>
        Latest attempt only
      </button>
    </div>
  );
}
