import type { ExchangeStateFill } from "../api/types";
import { timeUntil } from "./dates";

/** Where the one-time exchange status fill is, in a few words, for Queue health and the Exchanges page. */
export function describeStateFill(fill: ExchangeStateFill): string {
  switch (fill.phase) {
    case "NotStarted":
      return "starting within a minute";
    case "Filling":
      return fill.estimatedFinishOn ? `${fill.percent}% done, ${timeLeft(fill.estimatedFinishOn)}` : `${fill.percent}% done`;
    case "BuildingIndex":
      return "building the index";
    case "WaitingForQuietHour":
      return `waiting for the quiet hour, ${String(fill.quietHourUtc).padStart(2, "0")}:00 UTC, to build the index`;
    case "Rechecking":
      // Waits until the servers of the earlier version, which don't label exchanges, have stopped.
      return fill.estimatedFinishOn
        ? `waiting for older servers to stop, ${timeLeft(fill.estimatedFinishOn)}`
        : "checking the exchanges that ran meanwhile";
    case "Done":
      return "done";
  }
}

/** "about 34m left", "under a minute left"; "finishing soon" once the estimate has passed. */
function timeLeft(iso: string): string {
  const until = timeUntil(iso);
  if (!until.startsWith("in ")) return "finishing soon";
  const span = until.slice(3);
  return span.startsWith("under") ? `${span} left` : `about ${span} left`;
}

/** What the fill is, for a tooltip beside its progress. */
export const STATE_FILL_EXPLAINED =
  "After an upgrade, Bitween fills in each older exchange's status once, in the background, a minute at a time. " +
  "Until it finishes, the Exchanges page counts failures and exchanges still running the old way, which can take " +
  "a while on a big database. Nothing is lost or wrong meanwhile.";
