/**
 * The last few things opened — a subscription, a partner — for the command palette to offer
 * before anything is typed. Kept in this browser only; losing it costs nothing.
 */
export interface Visit {
  path: string;
  title: string;
  /** What kind of page it is: "Scheduled job", "Partner". */
  kind: string;
}

const KEY = "bitween-recent";
const KEEP = 8;

export function recentVisits(): Visit[] {
  try {
    const raw = JSON.parse(localStorage.getItem(KEY) ?? "[]") as unknown;
    return Array.isArray(raw) ? (raw as Visit[]).filter((v) => v && typeof v.path === "string") : [];
  } catch {
    return [];
  }
}

export function recordVisit(visit: Visit) {
  try {
    const rest = recentVisits().filter((v) => v.path !== visit.path);
    localStorage.setItem(KEY, JSON.stringify([visit, ...rest].slice(0, KEEP)));
  } catch {
    // Storage refused (private mode, full): the palette just has nothing recent to offer.
  }
}
