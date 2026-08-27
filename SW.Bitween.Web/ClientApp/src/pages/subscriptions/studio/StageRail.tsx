import { Fragment } from "react";
import { ChevronRight, type LucideIcon } from "lucide-react";
import { STAGES, type StageId } from "./stages";

export type StageState =
  /** Configured. */
  | "set"
  /** Deliberately empty — the stage is optional and does nothing. */
  | "none"
  /** Empty but required: the pipeline can't run like this. */
  | "missing";

export interface StageFace {
  id: StageId;
  /** What defines this stage right now — usually the chosen adapter. */
  title: string;
  /** The concrete thing it points at: a bucket, a host, the next fire time. */
  detail?: string;
  state: StageState;
  dirty: boolean;
  /**
   * A fault we can actually attribute to *this* step. Only the schedule has one
   * today (the scheduler reports its own trigger state); a subscription-level
   * failure count is not evidence about which step failed, so it stays on the
   * overview rather than being guessed onto Delivery.
   */
  fault?: { label: string; tone: "warn" | "danger"; title: string };
  /**
   * This node explains itself rather than offering configuration — the Trigger
   * on a create page, where the attachment or route is made elsewhere. Drawn
   * dashed and flat so it doesn't promise a form it can't deliver.
   */
  readOnly?: boolean;
}

const titleTone: Record<StageState, string> = {
  set: "text-ink-800",
  none: "text-ink-400",
  missing: "text-danger-700",
};

/** The connector — a continuous track, so the row reads as flow and not as tabs. */
export function Connector() {
  return (
    <span className="flex shrink-0 items-center self-center" aria-hidden>
      <span className="h-0.5 w-6 bg-ink-300" />
      <ChevronRight className="-ml-1.5 size-4 text-ink-300" strokeWidth={3} />
    </span>
  );
}

/**
 * One node of a pipeline diagram.
 *
 * Lives here rather than inside `StageRail` because the bus-gateway studio draws
 * a different graph out of the same nodes. Two hand-rolled copies of this card
 * would drift the moment either studio was touched, and a node that looks
 * subtly different in one place is a node an operator has to re-learn.
 */
export function StageNode({
  face,
  label,
  icon: Icon,
  selected,
  onSelect,
  className = "w-58",
}: {
  /** No `id`: each studio names its own nodes. */
  face: Omit<StageFace, "id">;
  label: string;
  icon: LucideIcon;
  selected: boolean;
  onSelect: () => void;
  className?: string;
}) {
  const border = selected
    ? "border-crimson-400 ring-2 ring-crimson-200"
    : face.fault
      ? face.fault.tone === "danger"
        ? "border-danger-300"
        : "border-warn-300"
      : face.readOnly
        ? "border-dashed border-ink-300"
        : "border-ink-200 hover:border-ink-300";
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-current={selected ? "true" : undefined}
      title={selected ? "Click again to close and show the overview" : undefined}
      className={`shrink-0 cursor-pointer rounded-xl border px-4 py-3.5 text-left transition-shadow ${className} ${border} ${
        face.readOnly ? "bg-ink-50/60" : "bg-white"
      } ${selected ? "shadow-md" : face.readOnly ? "" : "shadow-sm hover:shadow-md"}`}
    >
      <div className="flex items-center gap-2">
        <span
          className={`grid size-7 shrink-0 place-items-center rounded-lg ${
            selected ? "bg-crimson-600 text-white" : "bg-ink-100 text-ink-500"
          }`}
        >
          <Icon className="size-4" aria-hidden />
        </span>
        <span className="min-w-0 flex-1 truncate text-[11px] font-semibold tracking-wide text-ink-400 uppercase">
          {label}
        </span>
        <span className="flex shrink-0 items-center gap-1">
          {face.dirty && (
            <span
              className="size-2 rounded-full bg-warn-700"
              title="Unsaved changes"
              aria-label="Unsaved changes"
            />
          )}
          {face.state === "missing" && (
            <span
              className="size-2 rounded-full bg-danger-600"
              title="Required — not configured"
              aria-label="Required — not configured"
            />
          )}
        </span>
      </div>

      <p className={`mt-2.5 truncate text-[15px] font-semibold ${titleTone[face.state]}`} title={face.title}>
        {face.title}
      </p>
      {/*
        Always rendered so every node keeps the same baseline — and with a
        non-breaking space, not a plain one: `truncate` sets `white-space:
        nowrap`, which collapses an ordinary space away to nothing. The <p> then
        had no line box, and since a <button> centres its content, every node
        without a detail line sat 9px lower than its neighbours.
      */}
      <p className="mt-1 truncate font-mono text-[11px] text-ink-400" title={face.detail}>
        {face.detail ?? " "}
      </p>

      {face.fault && (
        <p
          className={`mt-2 truncate rounded-md px-1.5 py-0.5 text-[11px] font-medium ${
            face.fault.tone === "danger" ? "bg-danger-100 text-danger-800" : "bg-warn-100 text-warn-700"
          }`}
          title={face.fault.title}
        >
          {face.fault.label}
        </p>
      )}
    </button>
  );
}

/**
 * The pipeline, as a diagram on its own canvas.
 *
 * The canvas is the point: on a plain page these nodes read as a row of buttons
 * above the "real" content, and the eye goes straight past them to the tables.
 * Given a surface of their own — grid, depth, room to breathe — they read as
 * the data path, which is what an operator is here to understand first.
 *
 * It is deliberately *not* a pannable canvas. The pipeline is a fixed line of
 * at most five nodes; pan and zoom would cost keyboard access and the ability
 * to link straight to a step, and buy nothing.
 */
export function StageRail({
  faces,
  selected,
  onSelect,
}: {
  faces: StageFace[];
  /** null = the overview. */
  selected: StageId | null;
  onSelect: (stage: StageId | null) => void;
}) {
  return (
    <div
      // Roomy while the pipeline is the subject; tighter once a stage is open,
      // where it is a sticky header over a form that needs the screen.
      className={`sticky top-0 z-20 mb-5 overflow-x-auto rounded-2xl border border-ink-200 bg-ink-50 px-6 transition-[padding] ${
        selected === null ? "py-7" : "py-3.5"
      }`}
      style={{
        backgroundImage: "radial-gradient(var(--color-ink-200) 1px, transparent 1px)",
        backgroundSize: "16px 16px",
      }}
    >
      <div className="flex min-w-max items-stretch justify-center">
        {faces.map((face, i) => (
          <Fragment key={face.id}>
            {i > 0 && <Connector />}
            <StageNode
              face={face}
              label={STAGES[face.id].label}
              icon={STAGES[face.id].icon}
              selected={selected === face.id}
              onSelect={() => onSelect(selected === face.id ? null : face.id)}
            />
          </Fragment>
        ))}
      </div>
    </div>
  );
}
