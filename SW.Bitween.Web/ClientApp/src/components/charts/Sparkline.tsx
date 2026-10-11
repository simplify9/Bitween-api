/**
 * A trend in a table cell: a line in a quiet ink, the latest point in the accent. Read with the
 * number beside it; the label says what it shows for a screen reader.
 */
export function Sparkline({
  values,
  label,
  width = 72,
  height = 20,
}: {
  values: number[];
  label: string;
  width?: number;
  height?: number;
}) {
  if (values.length === 0) return null;
  const max = Math.max(1, ...values);
  const n = Math.max(1, values.length - 1);
  const pts = values.map((v, i) => [(i / n) * (width - 4) + 2, height - 2 - (v / max) * (height - 4)] as const);
  const [lx, ly] = pts[pts.length - 1];
  return (
    <svg role="img" aria-label={label} width={width} height={height} className="shrink-0 overflow-visible">
      <title>{label}</title>
      <polyline
        fill="none"
        className="stroke-ink-300"
        strokeWidth={1.5}
        strokeLinejoin="round"
        strokeLinecap="round"
        points={pts.map(([px, py]) => `${px},${py}`).join(" ")}
      />
      <circle cx={lx} cy={ly} r={2.5} className="fill-crimson-600 stroke-white" strokeWidth={1.5} />
    </svg>
  );
}
