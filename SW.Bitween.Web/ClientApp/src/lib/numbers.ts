/** 950, 1.2K, 12K, 3.4M: compact from a thousand up, as chart axes and tiles show counts. */
export function compact(n: number): string {
  if (!Number.isFinite(n)) return "-";
  const abs = Math.abs(n);
  if (abs < 1_000) return String(Math.round(n));
  if (abs < 1_000_000) return `${(n / 1_000).toFixed(abs < 10_000 ? 1 : 0).replace(/\.0$/, "")}K`;
  if (abs < 1_000_000_000) return `${(n / 1_000_000).toFixed(abs < 10_000_000 ? 1 : 0).replace(/\.0$/, "")}M`;
  return `${(n / 1_000_000_000).toFixed(1).replace(/\.0$/, "")}B`;
}

/** 512 B, 3.4 KB, 12 MB, 1.2 GB. */
export function bytes(n: number): string {
  if (!Number.isFinite(n) || n <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = n;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toFixed(value < 10 && unit > 0 ? 1 : 0)} ${units[unit]}`;
}

/** 98.5%, or "-" when there is nothing to take a share of. */
export const percent = (rate: number | null | undefined, digits = 1): string =>
  rate === null || rate === undefined ? "-" : `${rate.toFixed(digits).replace(/\.0+$/, "")}%`;

/** Thousands separated, for numbers people read rather than compare at a glance. */
export const whole = (n: number): string => n.toLocaleString("en");
