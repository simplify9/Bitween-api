import type { Schedule } from "../api";

export const WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

const pad = (n: number) => String(n).padStart(2, "0");

/** "Daily at 02:00", "Hourly at :15", "Weekly on Monday at 08:30"… */
export const scheduleSummary = (s: Schedule): string => {
  const time = `${pad(s.hours)}:${pad(s.minutes)}`;
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
