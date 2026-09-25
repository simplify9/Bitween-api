import { Badge } from "../../components/ui/basics";

/**
 * What the supervisor last heard on the adapter's heartbeat.
 *
 * "Never started" is a state of its own, not a fault: external bus providers are opt-in per node,
 * so a data source can be perfectly configured and simply not running here. Colouring that as an
 * error would send an operator hunting for something that is not wrong.
 */
export function ConnectionBadge({
  state,
  failures = 0,
}: {
  state: string | null;
  failures?: number;
}) {
  if (!state)
    return (
      <Badge title="No adapter has reported on this connection yet. External bus providers are opt-in per node.">
        Never started
      </Badge>
    );

  // Whole words, or "Disconnected" would match "connected" and read as healthy.
  const tone = /\b(connected|ready|idle|running)\b/i.test(state)
    ? "ok"
    : /starting|draining/i.test(state)
      ? "warn"
      : "danger";

  return (
    <span className="inline-flex items-center gap-1.5">
      <Badge tone={tone as "ok" | "warn" | "danger"}>{state}</Badge>
      {failures > 0 && (
        <span className="text-[11px] text-danger-700" title="Consecutive restarts">
          ×{failures}
        </span>
      )}
    </span>
  );
}
