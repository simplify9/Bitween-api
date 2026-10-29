import { useState, type ReactNode } from "react";
import { compact } from "../../lib/numbers";
import { handMoved, niceMax } from "./scale";

export interface BarSeries {
  key: string;
  label: string;
  /** A background class: the mark's color. Text never wears it. */
  className: string;
}

export interface TimePoint {
  at: string;
  values: Record<string, number>;
}

/**
 * Stacked columns over time, one series per outcome, with an optional comparison line (the same
 * hours a week ago). Thin columns with rounded ends and a 2px gap between segments; a tooltip
 * per column on hover; a legend for two or more series; and the same numbers as a table.
 */
export function TimeBars({
  points,
  series,
  compare,
  height = 140,
  label,
  tick,
  every = 1,
  format = compact,
}: {
  points: TimePoint[];
  series: BarSeries[];
  /** A line over the columns, in a neutral ink, e.g. last week's totals at the same positions. */
  compare?: { label: string; values: (number | null)[] };
  height?: number;
  /** For the table and the tooltip: "Oct 9, 14:00". */
  label: (at: string) => string;
  /** The axis label under a column, or nothing. */
  tick: (at: string, index: number) => ReactNode;
  /** Label every nth column. */
  every?: number;
  format?: (n: number) => string;
}) {
  const [hover, setHover] = useState<number | null>(null);
  const totals = points.map((p) => series.reduce((n, s) => n + (p.values[s.key] ?? 0), 0));
  const max = niceMax(Math.max(1, ...totals, ...(compare?.values.map((v) => v ?? 0) ?? [])));
  const y = (v: number) => (v / max) * height;
  const legend = [
    ...series.map((s) => ({
      key: s.key,
      label: s.label,
      mark: <span className={`size-2.5 rounded-[3px] ${s.className}`} />,
    })),
  ];
  if (compare)
    legend.push({ key: "compare", label: compare.label, mark: <span className="h-0.5 w-3.5 rounded bg-ink-400" /> });

  return (
    <figure>
      {legend.length >= 2 && (
        <figcaption className="mb-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-ink-600">
          {legend.map((l) => (
            <span key={l.key} className="flex items-center gap-1.5">
              <span aria-hidden className="flex items-center">
                {l.mark}
              </span>
              {l.label}
            </span>
          ))}
        </figcaption>
      )}
      <div className="relative" style={{ height: height + 20 }} onPointerLeave={() => setHover(null)}>
        {[1, 0.5].map((f) => (
          <div key={f} className="absolute right-0 left-9 border-t border-ink-100" style={{ top: height * (1 - f) }}>
            <span className="absolute -top-2 -left-9 w-7 text-right font-mono text-[10px] text-ink-500 tabular-nums">
              {format(max * f)}
            </span>
          </div>
        ))}
        <div className="absolute inset-x-0 bottom-5 left-9 border-t border-ink-200" />
        <div className="absolute inset-0 left-9 flex items-end gap-px" style={{ height: height + 20 }}>
          {points.map((p, i) => (
            <div
              key={p.at}
              className="relative flex h-full min-w-0 flex-1 flex-col items-center justify-end"
              // A move, not an enter: entering is also what the browser reports when the chart
              // is drawn under a resting mouse.
              onPointerMove={(e) => hover !== i && handMoved(e) && setHover(i)}
              onPointerDown={() => setHover(i)}
            >
              {hover === i && (
                <div
                  role="tooltip"
                  className="pointer-events-none absolute bottom-full z-20 mb-1 w-max rounded-md bg-ink-950 px-2.5 py-1.5 text-[11px] text-ink-100 shadow-lg"
                >
                  <p className="mb-0.5 font-medium text-white">{label(p.at)}</p>
                  {series.map((s) => (
                    <p key={s.key} className="flex items-center gap-1.5 tabular-nums">
                      <span className={`h-0.5 w-2.5 rounded ${s.className}`} aria-hidden />
                      <span className="font-semibold text-white">{(p.values[s.key] ?? 0).toLocaleString("en")}</span>
                      {s.label.toLowerCase()}
                    </p>
                  ))}
                  {compare && compare.values[i] !== null && (
                    <p className="flex items-center gap-1.5 tabular-nums">
                      <span className="h-0.5 w-2.5 rounded bg-ink-400" aria-hidden />
                      <span className="font-semibold text-white">{(compare.values[i] ?? 0).toLocaleString("en")}</span>
                      {compare.label.toLowerCase()}
                    </p>
                  )}
                </div>
              )}
              <div
                className={`flex w-full max-w-6 flex-col-reverse items-stretch gap-0.5 ${
                  hover === i ? "opacity-80" : ""
                }`}
              >
                {series.map((s, k) => {
                  const v = p.values[s.key] ?? 0;
                  if (v <= 0) return null;
                  // The data end is rounded; the baseline end stays square.
                  const top = !series.slice(k + 1).some((above) => (p.values[above.key] ?? 0) > 0);
                  return (
                    <div
                      key={s.key}
                      className={`${s.className} ${top ? "rounded-t-[4px]" : ""}`}
                      style={{ height: Math.max(2, y(v)) }}
                    />
                  );
                })}
                {totals[i] === 0 && <div className="h-px bg-ink-200" />}
              </div>
              <span className="mt-1 h-4 text-[10px] whitespace-nowrap text-ink-500">
                {i % every === 0 ? tick(p.at, i) : ""}
              </span>
            </div>
          ))}
        </div>
        {compare && (
          <svg
            aria-hidden
            className="pointer-events-none absolute top-0 right-0 left-9"
            style={{ height }}
            viewBox={`0 0 ${points.length} ${height}`}
            preserveAspectRatio="none"
          >
            <polyline
              fill="none"
              className="stroke-ink-400"
              strokeWidth={2}
              vectorEffect="non-scaling-stroke"
              strokeLinejoin="round"
              strokeLinecap="round"
              points={compare.values
                .map((v, i) => (v === null ? null : `${i + 0.5},${height - y(v)}`))
                .filter(Boolean)
                .join(" ")}
            />
          </svg>
        )}
      </div>
      <details className="mt-2">
        <summary className="cursor-pointer text-xs font-medium text-ink-500 hover:text-ink-700">View as table</summary>
        <div className="mt-2 max-h-64 overflow-y-auto">
          <table className="text-[13px]">
            <thead>
              <tr className="text-left text-[11px] font-medium tracking-wide text-ink-500 uppercase">
                <th className="py-1 pr-6">When</th>
                {series.map((s) => (
                  <th key={s.key} className="py-1 pr-6 text-right">
                    {s.label}
                  </th>
                ))}
                {compare && <th className="py-1 text-right">{compare.label}</th>}
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {points.map((p, i) => (
                <tr key={p.at}>
                  <td className="py-0.5 pr-6 text-ink-600">{label(p.at)}</td>
                  {series.map((s) => (
                    <td key={s.key} className="py-0.5 pr-6 text-right text-ink-800">
                      {(p.values[s.key] ?? 0).toLocaleString("en")}
                    </td>
                  ))}
                  {compare && <td className="py-0.5 text-right text-ink-800">{compare.values[i] ?? "-"}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  );
}
