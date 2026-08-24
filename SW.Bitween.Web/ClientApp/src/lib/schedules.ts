import type { Schedule } from "../api";

export const WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

const pad = (n: number) => String(n).padStart(2, "0");

/** "Daily at 02:00 UTC", "Hourly at :15", "Weekly on Monday at 08:30 UTC"… — hour/minute are always UTC. */
export const scheduleSummary = (s: Schedule): string => {
  const time = `${pad(s.hours)}:${pad(s.minutes)} UTC`;
  const tail = s.backwards ? " (counted from the end)" : "";
  switch (s.recurrence) {
    case "Hourly":
      return `Hourly at :${pad(s.minutes)}${tail}`;
    case "Daily":
      return `Daily at ${time}${tail}`;
    case "Weekly":
      return `Weekly on ${WEEKDAYS[s.days] ?? `day ${s.days}`} at ${time}${tail}`;
    case "Monthly":
      return `Monthly on day ${s.days} at ${time}${tail}`;
  }
};

export const schedulesSummary = (list: Schedule[]): string =>
  list.length === 0
    ? "No schedule"
    : list.length === 1
      ? scheduleSummary(list[0])
      : `${scheduleSummary(list[0])} · +${list.length - 1} more`;

/**
 * Translates a schedule's UTC hour/minute (and, for Weekly/Monthly, day) into the
 * viewer's own browser timezone, so editors don't have to do the mental conversion.
 * Uses fixed reference dates purely as a vehicle for the time-of-day/day-of-week math —
 * Dec 31 2023 was a Sunday, so WEEKDAYS[0] lines up with s.days=0 after the UTC->local shift.
 */
export const localTimePreview = (s: Schedule): string => {
  const refHour = s.recurrence === "Hourly" ? 12 : s.hours;
  const utcDate =
    s.recurrence === "Weekly"
      ? new Date(Date.UTC(2023, 11, 31 + s.days, refHour, s.minutes))
      : s.recurrence === "Monthly"
        ? new Date(Date.UTC(2024, 0, s.days, refHour, s.minutes))
        : new Date(Date.UTC(2024, 0, 1, refHour, s.minutes));

  const time = utcDate.toLocaleTimeString([], { hour: "numeric", minute: "2-digit", timeZoneName: "short" });

  switch (s.recurrence) {
    case "Hourly":
      return `:${pad(utcDate.getMinutes())} your local time`;
    case "Weekly":
      return `${WEEKDAYS[utcDate.getDay()]} ${time}`;
    case "Monthly":
      return `day ${utcDate.getDate()}, ${time}`;
    default:
      return time;
  }
};
