import { useRef, useState } from "react";
import { compact } from "../../lib/numbers";
import { niceMax } from "./scale";

export interface LineSeries {
  key: string;
  label: string;
  /** The line's color, as a hex the stroke takes. */
  color: string;
}

/**
 * Lines over time on one axis, with a crosshair that snaps to the nearest point and lists every
 * series there. 2px lines, a legend for two or more series, and a table of the same numbers.
 */
export function Lines({
  points,
  series,
  height = 140,
  label,
  tick,
  format = compact,
}: {
  points: { at: string; values: Record<string, number> }[];
  series: LineSeries[];
  height?: number;
  label: (at: string) => string;
  /** Up to five evenly spaced axis labels are shown. */
  tick: (at: string) => string;
  format?: (n: number) => string;
}) {
  const box = useRef<HTMLDivElement>(null);
  const [hover, setHover] = useState<number | null>(null);
  const max = niceMax(Math.max(1, ...points.flatMap((p) => series.map((s) => p.values[s.key] ?? 0))));
  const n = Math.max(1, points.length - 1);
  const x = (i: number) => (i / n) * 1000;
  const y = (v: number) => height - (v / max) * height;
  const ticks = points.length <= 1 ? [0] : [0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(f * n));

  const onMove = (e: React.PointerEvent) => {
    const r = box.current?.getBoundingClientRect();
    if (!r || points.length === 0) return;
    setHover(Math.max(0, Math.min(points.length - 1, Math.round(((e.clientX - r.left) / r.width) * n))));
  };

  return (
    <figure>
      {series.length >= 2 && (
        <figcaption className="mb-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-ink-600">
          {series.map((s) => (
            <span key={s.key} className="flex items-center gap-1.5">
              <span aria-hidden className="h-0.5 w-3.5 rounded" style={{ backgroundColor: s.color }} />
              {s.label}
            </span>
          ))}
        </figcaption>
      )}
      <div className="relative pl-9" style={{ height: height + 20 }}>
        {[1, 0.5].map((f) => (
          <div key={f} className="absolute right-0 left-9 border-t border-ink-100" style={{ top: height * (1 - f) }}>
            <span className="absolute -top-2 -left-9 w-7 text-right font-mono text-[10px] text-ink-500 tabular-nums">
              {format(max * f)}
            </span>
          </div>
        ))}
        <div
          ref={box}
          className="relative h-full"
          onPointerMove={onMove}
          onPointerLeave={() => setHover(null)}
          style={{ height }}
        >
          <div className="absolute inset-x-0 bottom-0 border-t border-ink-200" />
          <svg
            aria-hidden
            className="absolute inset-0 h-full w-full overflow-visible"
            viewBox={`0 0 1000 ${height}`}
            preserveAspectRatio="none"
          >
            {series.map((s) => (
              <polyline
                key={s.key}
                fill="none"
                stroke={s.color}
                strokeWidth={2}
                vectorEffect="non-scaling-stroke"
                strokeLinejoin="round"
                strokeLinecap="round"
                points={points.map((p, i) => `${x(i)},${y(p.values[s.key] ?? 0)}`).join(" ")}
              />
            ))}
            {hover !== null && (
              <line
                x1={x(hover)}
                x2={x(hover)}
                y1={0}
                y2={height}
                className="stroke-ink-300"
                strokeWidth={1}
                vectorEffect="non-scaling-stroke"
              />
            )}
          </svg>
          {hover !== null && points[hover] && (
            <div
              role="tooltip"
              className="pointer-events-none absolute top-0 z-20 w-max rounded-md bg-ink-950 px-2.5 py-1.5 text-[11px] text-ink-100 shadow-lg"
              style={
                hover / n > 0.6
                  ? { right: `${100 - (hover / n) * 100}%`, marginRight: 8 }
                  : { left: `${(hover / n) * 100}%`, marginLeft: 8 }
              }
            >
              <p className="mb-0.5 font-medium text-white">{label(points[hover].at)}</p>
              {series.map((s) => (
                <p key={s.key} className="flex items-center gap-1.5 tabular-nums">
                  <span className="h-0.5 w-2.5 rounded" style={{ backgroundColor: s.color }} aria-hidden />
                  <span className="font-semibold text-white">
                    {(points[hover].values[s.key] ?? 0).toLocaleString("en")}
                  </span>
                  {s.label.toLowerCase()}
                </p>
              ))}
            </div>
          )}
        </div>
        <div className="relative mt-1 h-4">
          {ticks.map((i) =>
            points[i] ? (
              <span
                key={i}
                className="absolute -translate-x-1/2 text-[10px] whitespace-nowrap text-ink-500"
                style={{ left: `${(i / n) * 100}%` }}
              >
                {tick(points[i].at)}
              </span>
            ) : null,
          )}
        </div>
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
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {points.map((p) => (
                <tr key={p.at}>
                  <td className="py-0.5 pr-6 text-ink-600">{label(p.at)}</td>
                  {series.map((s) => (
                    <td key={s.key} className="py-0.5 pr-6 text-right text-ink-800">
                      {(p.values[s.key] ?? 0).toLocaleString("en")}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  );
}
