const formatter = new Intl.DateTimeFormat("en", { day: "numeric", month: "short", year: "numeric" });

export const formatDate = (iso: string) => formatter.format(new Date(iso));

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

/** "in 34m", "in 2h" — for things scheduled in the future. */
export const timeUntil = (iso: string): string => {
  const seconds = (new Date(iso).getTime() - Date.now()) / 1000;
  if (seconds <= 0) return "any moment";
  if (seconds < 90) return "in under a minute";
  const minutes = seconds / 60;
  if (minutes < 60) return `in ${Math.round(minutes)}m`;
  const hours = minutes / 60;
  if (hours < 24) return `in ${Math.round(hours)}h`;
  return `in ${Math.round(hours / 24)}d`;
};

const timeFormatter = new Intl.DateTimeFormat("en", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
});

export const formatDateTime = (iso: string) => timeFormatter.format(new Date(iso));

/** "12s", "1m 42s" — elapsed time between two instants. */
export const duration = (fromIso: string, toIso: string): string => {
  const seconds = Math.max(0, Math.round((new Date(toIso).getTime() - new Date(fromIso).getTime()) / 1000));
  if (seconds < 60) return `${seconds}s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ${seconds % 60}s`;
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
};
