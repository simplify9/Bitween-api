const formatter = new Intl.DateTimeFormat("en", { day: "numeric", month: "short", year: "numeric" });

/** Missing/invalid dates (e.g. fields the backend doesn't return) render as a dash. */
const asDate = (iso: string): Date | null => {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? null : d;
};

export const formatDate = (iso: string) => {
  const d = asDate(iso);
  return d ? formatter.format(d) : "-";
};

export const timeAgo = (iso: string): string => {
  const seconds = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 90) return "just now";
  const minutes = seconds / 60;
  if (minutes < 60) return `${Math.round(minutes)}m ago`;
  const hours = minutes / 60;
  if (hours < 24) return `${Math.round(hours)}h ago`;
  const days = hours / 24;
  if (days < 30) return `${Math.round(days)}d ago`;
  return formatDate(iso);
};

/** "in 34m", "in 2h" for the future; "overdue by 3d" once a schedule has been missed a while. */
export const timeUntil = (iso: string): string => {
  const d = asDate(iso);
  if (!d) return "-";
  const seconds = (d.getTime() - Date.now()) / 1000;
  if (seconds > 0) {
    if (seconds < 90) return "in under a minute";
    const minutes = seconds / 60;
    if (minutes < 60) return `in ${Math.round(minutes)}m`;
    const hours = minutes / 60;
    if (hours < 24) return `in ${Math.round(hours)}h`;
    return `in ${Math.round(hours / 24)}d`;
  }
  const overdue = -seconds;
  if (overdue < 30) return "any moment";
  const minutes = overdue / 60;
  if (minutes < 90) return `overdue by ${Math.max(1, Math.round(minutes))}m`;
  const hours = overdue / 3600;
  if (hours < 48) return `overdue by ${Math.round(hours)}h`;
  return `overdue by ${Math.round(overdue / 86400)}d`;
};

const timeFormatter = new Intl.DateTimeFormat("en", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
});

export const formatDateTime = (iso: string) => {
  const d = asDate(iso);
  return d ? timeFormatter.format(d) : "-";
};

/** "14ms", "1.1s", "2m 3s" - from an elapsed millisecond count. */
export const formatDurationMs = (ms: number): string => {
  // NaN compares false against every bound below, so without this an unusable
  // number falls through every branch and is rendered as "NaNh NaNm".
  if (!Number.isFinite(ms)) return "-";
  if (ms < 1000) return `${Math.round(ms)}ms`;
  const seconds = ms / 1000;
  if (seconds < 60) return `${seconds.toFixed(1)}s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ${Math.round(seconds % 60)}s`;
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
};

/**
 * "14ms", "1.1s", "2m 3s" - elapsed time between two instants.
 *
 * Milliseconds, not whole seconds: most exchanges finish inside one, so rounding
 * to the second reported nearly all of them as "0s" - which reads as "no time at
 * all" rather than as a measurement, and hides the difference between a 4ms run
 * and a 900ms one.
 */
export const duration = (fromIso: string, toIso: string): string => {
  // Either end missing or unparseable means there is no elapsed time to state -
  // an em dash, the same as every other field with nothing to show.
  const from = asDate(fromIso);
  const to = asDate(toIso);
  if (!from || !to) return "-";
  return formatDurationMs(Math.max(0, to.getTime() - from.getTime()));
};
