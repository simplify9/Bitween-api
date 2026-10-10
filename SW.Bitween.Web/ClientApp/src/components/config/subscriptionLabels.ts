import { type SubscriptionType, type QueueSeverity, type ScheduleHealth } from "../../api";

/**
 * Display names for subscription types; Internal and ApiCall are legacy.
 *
 * `Receiving` reads "Scheduled job", not the backend's "Receiver" — it is the
 * same thing the sidebar and its own page call a scheduled job, and one entity
 * with two names in the same screen is just a puzzle for the reader.
 */
export const SUBSCRIPTION_TYPE_LABELS: Record<SubscriptionType, string> = {
  Receiving: "Scheduled job",
  GatewayApiCall: "API gateway",
  BusGateway: "Bus gateway",
  Internal: "Internal",
  ApiCall: "API call",
  Aggregation: "Aggregation",
  Response: "Response",
};

export const isLegacyType = (type: SubscriptionType) =>
  type === "Internal" || type === "ApiCall";

/**
 * A fault the scheduler itself reports, which contradicts whatever the
 * subscription's own badges say — "Active" with no trigger behind it is still a
 * job that never runs. Shared by the scheduled-jobs table and the pipeline rail
 * so the two can't drift apart.
 */
export function scheduleFault(
  health: ScheduleHealth | undefined,
): { label: string; tone: "warn" | "danger"; title: string } | null {
  if (!health) return null;

  if (health.stuck)
    return {
      label: "Stuck",
      tone: "danger",
      title:
        "Flagged as running with nothing executing — every later run is being skipped. Usually a run that was killed rather than failing.",
    };

  switch (health.state) {
    case "Missing":
      return {
        label: "Not scheduled",
        tone: "danger",
        title:
          "The scheduler has no trigger for this schedule — it will never fire.",
      };
    case "Error":
      return {
        label: "Trigger error",
        tone: "danger",
        title:
          "The scheduler put this trigger in an error state; it will not fire again until fixed.",
      };
    case "Paused":
      return {
        label: "Trigger paused",
        tone: "warn",
        title:
          "Paused inside the scheduler — this is not the subscription's own pause.",
      };
    case "Blocked":
      return {
        label: "Blocked",
        tone: "warn",
        title:
          "A previous run is still going and this job doesn't allow overlap, so fires are being held.",
      };
    case "Complete":
      return {
        label: "Schedule ended",
        tone: "warn",
        title:
          "The schedule has run to completion and has no future fire times.",
      };
    default:
      return null;
  }
}

/**
 * What a lane's severity is actually reporting — the three sources this fans out
 * to (Work groups, Queue health, the live stats strip) used to each spell out the
 * same three words with no explanation of what put a lane there.
 *
 * Matches `AlertEvaluator` in SW.Bus: critical is no running consumer or a
 * backlog past its critical threshold; warning is a backlog past its warning
 * threshold, a queue depth past the backpressure threshold, or messages arriving
 * with essentially nothing being acknowledged.
 */
export function queueHealthTitle(severity: QueueSeverity): string {
  switch (severity) {
    case "critical":
      return "No consumer is running for this lane, or a backlog has passed its critical threshold.";
    case "warning":
      return "A backlog, queue depth or the incoming-vs-acknowledged rate has passed its warning threshold.";
    default:
      return "No active alerts for this lane.";
  }
}

/**
 * Recent traffic for a hub page. Every field the row already carries is a
 * column — the old version spent a whole line on an id and left the rest of
 * the width empty, so what the exchange actually *was* never made it to the
 * screen.
 */
/**
 * Promoted properties only name an exchange when at least one of them carries a value. An
 * information type can promote three paths that a payload never filled, and
 * "merchant= orderRef= destination=" then names every exchange of that type equally — so a
 * caller with room for one identity is better off showing the id.
 */
export const namesSomething = (properties: Record<string, string | null> | null) =>
  properties != null && Object.values(properties).some((v) => v != null && v !== "");
