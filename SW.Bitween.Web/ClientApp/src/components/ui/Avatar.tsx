const TONES = [
  "bg-crimson-100 text-crimson-800",
  "bg-ink-100 text-ink-700",
  "bg-warn-100 text-warn-700",
  "bg-ok-100 text-ok-600",
  "bg-crimson-600 text-white",
  "bg-ink-800 text-ink-100",
];

const initialsOf = (name: string) =>
  name
    .split(/[\s@._-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join("");

const toneOf = (key: string) => {
  let hash = 0;
  for (const ch of key) hash = (hash * 31 + ch.charCodeAt(0)) | 0;
  return TONES[Math.abs(hash) % TONES.length];
};

export function Avatar({
  name,
  size = "md",
  dimmed = false,
}: {
  name: string;
  size?: "sm" | "md" | "lg";
  dimmed?: boolean;
}) {
  const sizeClass =
    size === "sm" ? "size-7 text-[11px]" : size === "lg" ? "size-14 text-lg" : "size-9 text-[13px]";
  return (
    <span
      aria-hidden
      className={`inline-flex shrink-0 items-center justify-center rounded-full font-semibold ${sizeClass} ${
        dimmed ? "bg-ink-100 text-ink-400" : toneOf(name)
      }`}
    >
      {initialsOf(name)}
    </span>
  );
}
