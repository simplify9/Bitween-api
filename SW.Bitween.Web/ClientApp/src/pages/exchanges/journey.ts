import type { ExchangeRow, ExchangeStatus } from "../../api";
import type { JourneyStage, StageState } from "./shared";

/**
 * Past tense only once the stage is actually behind us. "Mapped" beside a Running badge
 * claims something that has not happened yet - while it is in flight, or was skipped, or
 * was never reached, the stage is named for the work rather than for a result it lacks.
 */
function stageLabel(key: JourneyStage["key"], state: StageState): string {
  if (key === "Input") return "Received";
  const finished = state === "done" || state === "bad" || state === "failed";
  if (key === "Mapped") return finished ? "Mapped" : "Mapping";
  return finished ? "Handled" : "Handling";
}


export const STATUS_LABELS: Record<ExchangeStatus, string> = {
  processing: "Processing",
  success: "Success",
  badResponse: "Bad response",
  failed: "Failed",
};

/**
 * Whether the delivery went through but the stage's file could not be stored, as the result's
 * warning says. The stage happened; only its document is missing.
 */
export function fileNotStored(x: Pick<ExchangeRow, "warning">, file: "output" | "response"): boolean {
  return !!x.warning?.includes(`the ${file} file could not be stored`);
}

/** Derives what happened at each pipeline stage from the row's fields. */
export function journeyStages(x: ExchangeRow): JourneyStage[] {
  const mapped: Omit<JourneyStage, "label"> = x.mapperSkipped
    ? { key: "Mapped", state: "skipped", note: "No mapper configured" }
    : x.files.mapped
      ? { key: "Mapped", state: "done" }
      : fileNotStored(x, "output")
        ? { key: "Mapped", state: "done", note: "Mapped and delivered; the file could not be stored" }
      : x.status === "processing"
        ? { key: "Mapped", state: "running", note: "Mapping now" }
        : x.status === "failed"
          ? { key: "Mapped", state: "failed", note: "Failed while mapping" }
          : { key: "Mapped", state: "notReached" };

  const handlerReached = !(mapped.state === "failed");
  const handled: Omit<JourneyStage, "label"> = !handlerReached
    ? { key: "Handled", state: "notReached", note: "Never reached" }
    : // One stage runs at a time: while mapping is underway the handler has not started, and
      // showing both as running put two spinners on one piece of work.
      mapped.state === "running"
      ? { key: "Handled", state: "notReached", note: "Not reached yet" }
      : x.status === "success"
      ? fileNotStored(x, "response")
        ? { key: "Handled", state: "done", note: "Delivered; the response file could not be stored" }
        : { key: "Handled", state: "done" }
      : x.status === "badResponse"
        ? { key: "Handled", state: "bad", note: "Delivered, but the response reports an error" }
        : x.status === "failed"
          ? { key: "Handled", state: "failed", note: "Failed while handling" }
          : { key: "Handled", state: "running", note: "Handling now" };

  return [{ key: "Input", state: "done" } as Omit<JourneyStage, "label">, mapped, handled].map(
    (s) => ({ ...s, label: stageLabel(s.key, s.state) }),
  );
}

/** One phrase for where an exchange got to, under its three-segment strip. */
export function journeySummary(x: ExchangeRow): string {
  const [, mapped, handled] = journeyStages(x);
  if (mapped.state === "failed") return "Failed while mapping";
  if (handled.state === "failed") return "Failed while handling";
  if (handled.state === "bad") return "Delivered, error reply";
  if (mapped.state === "running") return "Mapping now";
  if (handled.state === "running") return "Handling now";
  return mapped.state === "skipped" ? "No mapper, handled" : "Mapped and handled";
}
