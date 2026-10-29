import { useEffect, useState } from "react";
import type { ExchangeVolumeBucket } from "../../api";

const total = (b: ExchangeVolumeBucket) => b.processing + b.success + b.badResponse + b.failed;
const pad = (n: number) => String(n).padStart(2, "0");

function tick(iso: string, bucketMinutes: number): string {
  const d = new Date(iso);
  const day = d.toLocaleDateString(undefined, { day: "numeric", month: "short" });
  if (bucketMinutes >= 1440) return day;
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function sliceLabel(b: ExchangeVolumeBucket, bucketMinutes: number): string {
  const d = new Date(b.start);
  const when =
    bucketMinutes >= 1440
      ? d.toLocaleDateString(undefined, { weekday: "short", day: "numeric", month: "short" })
      : `${d.toLocaleDateString(undefined, { day: "numeric", month: "short" })} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  const n = total(b);
  const parts = [`${n.toLocaleString()} exchange${n === 1 ? "" : "s"}`];
  if (b.failed) parts.push(`${b.failed.toLocaleString()} failed`);
  if (b.badResponse) parts.push(`${b.badResponse.toLocaleString()} bad response`);
  if (b.processing) parts.push(`${b.processing.toLocaleString()} processing`);
  return `${when}: ${parts.join(", ")}`;
}

/**
 * How the matching exchanges spread over the time range, by status. Failures cluster - an
 * outage is a column of red - so this is where a spike is noticed, and dragging across it (or
 * clicking one bar) narrows the list to exactly that stretch.
 */
export function VolumeChart({
  buckets,
  bucketMinutes,
  caption,
  endsNow,
  onSelect,
}: {
  buckets: ExchangeVolumeBucket[];
  bucketMinutes: number;
  /** What range the bars cover, said above them. */
  caption: string;
  /** Whether the last bar runs up to the present, so the axis can end on "Now". */
  endsNow: boolean;
  /** A stretch was picked: the start of the first bar to the end of the last. */
  onSelect: (from: Date, to: Date) => void;
}) {
  const [anchor, setAnchor] = useState<number | null>(null);
  const [hover, setHover] = useState<number | null>(null);

  useEffect(() => {
    if (anchor === null) return;
    const up = () => {
      const a = anchor;
      const b = hover ?? anchor;
      setAnchor(null);
      setHover(null);
      const lo = Math.min(a, b);
      const hi = Math.max(a, b);
      onSelect(new Date(buckets[lo].start), new Date(new Date(buckets[hi].start).getTime() + bucketMinutes * 60_000 - 1));
    };
    window.addEventListener("pointerup", up);
    return () => window.removeEventListener("pointerup", up);
  }, [anchor, hover, buckets, bucketMinutes, onSelect]);

  if (buckets.length === 0) return null;

  const max = Math.max(1, ...buckets.map(total));
  const sum = buckets.reduce((n, b) => n + total(b), 0);
  const failed = buckets.reduce((n, b) => n + b.failed, 0);
  const lo = anchor !== null ? Math.min(anchor, hover ?? anchor) : -1;
  const hi = anchor !== null ? Math.max(anchor, hover ?? anchor) : -1;
  const ticks = [0, 0.25, 0.5, 0.75].map((f) => Math.floor(f * (buckets.length - 1)));

  return (
    <section aria-label="Exchange volume over time" className="rounded-xl border border-ink-100 bg-white px-4 pt-3 pb-2.5">
      <div className="mb-2.5 flex flex-wrap items-baseline justify-between gap-2">
        <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1 text-[13px] text-ink-600">
          <span>
            <strong className="text-lg font-semibold text-ink-900 tabular-nums">{sum.toLocaleString()}</strong> exchanges{" "}
            <span className="text-ink-500">{caption}</span>
            {failed > 0 && <span className="text-danger-700">, {failed.toLocaleString()} failed</span>}
          </span>
          <span className="inline-flex items-center gap-1.5"><span className="size-2 rounded-sm bg-ok-200" aria-hidden />Success</span>
          <span className="inline-flex items-center gap-1.5"><span className="size-2 rounded-sm bg-warn-400" aria-hidden />Bad response</span>
          <span className="inline-flex items-center gap-1.5"><span className="size-2 rounded-sm bg-danger-600" aria-hidden />Failed</span>
        </div>
        <span className="hidden text-xs text-ink-500 sm:inline">Drag across the bars to zoom in</span>
      </div>
      <div className="flex h-14 items-end gap-[3px] select-none" onPointerLeave={() => anchor === null && setHover(null)}>
        {buckets.map((b, i) => {
          const h = (n: number) => (n === 0 ? 0 : Math.max(2, Math.round((n / max) * 56)));
          const picked = i >= lo && i <= hi;
          return (
            <button
              key={b.start}
              type="button"
              aria-label={sliceLabel(b, bucketMinutes)}
              title={sliceLabel(b, bucketMinutes)}
              onPointerDown={(e) => {
                e.preventDefault();
                setAnchor(i);
                setHover(i);
              }}
              onPointerEnter={() => setHover(i)}
              onKeyDown={(e) => {
                if (e.key !== "Enter" && e.key !== " ") return;
                e.preventDefault();
                onSelect(new Date(b.start), new Date(new Date(b.start).getTime() + bucketMinutes * 60_000 - 1));
              }}
              className={`flex h-14 min-w-0 flex-1 cursor-pointer flex-col justify-end gap-px rounded-sm ${
                picked ? "bg-crimson-100" : "hover:bg-ink-50"
              }`}
            >
              <span className="block w-full rounded-t-sm bg-danger-600" style={{ height: h(b.failed) }} />
              <span className="block w-full bg-warn-400" style={{ height: h(b.badResponse) }} />
              <span className="block w-full bg-ink-300" style={{ height: h(b.processing) }} />
              <span className="block w-full rounded-b-sm bg-ok-200" style={{ height: h(b.success) }} />
            </button>
          );
        })}
      </div>
      <div className="mt-1.5 flex justify-between text-[11px] text-ink-500 tabular-nums">
        {ticks.map((t) => (
          <span key={t}>{tick(buckets[t].start, bucketMinutes)}</span>
        ))}
        <span>
          {endsNow
            ? "Now"
            : tick(new Date(new Date(buckets[buckets.length - 1].start).getTime() + bucketMinutes * 60_000).toISOString(), bucketMinutes)}
        </span>
      </div>
    </section>
  );
}
