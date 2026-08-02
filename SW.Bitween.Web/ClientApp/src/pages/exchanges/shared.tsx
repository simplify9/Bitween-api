import { useState } from "react";
import { Check, Copy } from "lucide-react";
import type { ExchangeRow, ExchangeStatus } from "../../api";
import { Badge, Button } from "../../components/ui/basics";
import { Checkbox } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";

export const STATUS_LABELS: Record<ExchangeStatus, string> = {
  processing: "Processing",
  success: "Success",
  badResponse: "Bad response",
  failed: "Failed",
};

export function StatusBadge({ status }: { status: ExchangeStatus }) {
  const tone =
    status === "success"
      ? ("ok" as const)
      : status === "failed"
        ? ("danger" as const)
        : status === "badResponse"
          ? ("warn" as const)
          : ("neutral" as const);
  return <Badge tone={tone}>{STATUS_LABELS[status]}</Badge>;
}

// ——— The pipeline journey ———

export type StageState = "done" | "bad" | "failed" | "skipped" | "running" | "notReached";

export interface JourneyStage {
  key: "Input" | "Mapped" | "Handled";
  label: string;
  state: StageState;
  note?: string;
}

/** Derives what happened at each pipeline stage from the row's fields. */
export function journeyStages(x: ExchangeRow): JourneyStage[] {
  const mapped: JourneyStage = x.mapperSkipped
    ? { key: "Mapped", label: "Mapped", state: "skipped", note: "No mapper configured" }
    : x.files.mapped
      ? { key: "Mapped", label: "Mapped", state: "done" }
      : x.status === "processing"
        ? { key: "Mapped", label: "Mapped", state: "running" }
        : x.status === "failed"
          ? { key: "Mapped", label: "Mapped", state: "failed", note: "Failed while mapping" }
          : { key: "Mapped", label: "Mapped", state: "notReached" };

  const handlerReached = !(mapped.state === "failed");
  const handled: JourneyStage = !handlerReached
    ? { key: "Handled", label: "Handled", state: "notReached", note: "Never reached" }
    : x.status === "success"
      ? { key: "Handled", label: "Handled", state: "done" }
      : x.status === "badResponse"
        ? { key: "Handled", label: "Handled", state: "bad", note: "Delivered, but the response reports an error" }
        : x.status === "failed"
          ? { key: "Handled", label: "Handled", state: "failed", note: "Failed while handling" }
          : { key: "Handled", label: "Handled", state: "running" };

  return [{ key: "Input", label: "Received", state: "done" }, mapped, handled];
}

const STRIP_COLORS: Record<StageState, string> = {
  done: "bg-ok-600",
  bad: "bg-warn-400",
  failed: "bg-danger-600",
  skipped: "bg-ink-200",
  running: "bg-ink-400 animate-pulse",
  notReached: "bg-ink-100",
};

/** Compact three-segment pipeline indicator for list rows. */
export function JourneyStrip({ x }: { x: ExchangeRow }) {
  const stages = journeyStages(x);
  return (
    <span className="inline-flex w-24 items-center gap-0.5" aria-hidden>
      {stages.map((s) => (
        <span
          key={s.key}
          title={`${s.label}: ${s.note ?? s.state}`}
          className={`h-1.5 flex-1 first:rounded-l-full last:rounded-r-full ${STRIP_COLORS[s.state]}`}
        />
      ))}
    </span>
  );
}

/**
 * Promoted properties as the row's identity, in place of the exchange id.
 *
 * An id answers "which record is this row" — which you only ever need once
 * you've already found the row. The promoted properties answer "which *order*
 * is this", which is what you were scanning for. Shows the first few and
 * counts the rest; the full set is in the drawer.
 */
export function PromotedProps({
  properties,
  max = 3,
}: {
  properties: Record<string, string> | null;
  max?: number;
}) {
  const entries = Object.entries(properties ?? {});
  if (entries.length === 0) return <span className="text-[13px] text-ink-400">—</span>;
  const shown = entries.slice(0, max);
  const rest = entries.length - shown.length;
  return (
    <span
      className="flex flex-wrap items-center gap-1"
      title={entries.map(([k, v]) => `${k}=${v}`).join("\n")}
    >
      {shown.map(([k, v]) => (
        <code key={k} className="rounded bg-ink-100 px-1.5 py-0.5 font-mono text-[11px] text-ink-700">
          <span className="text-ink-500">{k}=</span>
          {v}
        </code>
      ))}
      {rest > 0 && <span className="text-[11px] text-ink-400">+{rest}</span>}
    </span>
  );
}

/** A full exchange id — mono, never truncated, click to copy. */
export function XchangeId({ id, className = "" }: { id: string; className?: string }) {
  const [copied, setCopied] = useState(false);
  const copy = async (e: React.MouseEvent) => {
    e.stopPropagation();
    await navigator.clipboard.writeText(id);
    setCopied(true);
    setTimeout(() => setCopied(false), 1400);
  };
  return (
    <button
      onClick={copy}
      title="Copy exchange id"
      className={`group inline-flex items-center gap-1 font-mono text-xs text-ink-700 hover:text-ink-900 ${className}`}
    >
      {id}
      {copied ? (
        <Check className="size-3 text-ok-600" aria-hidden />
      ) : (
        <Copy className="size-3 text-ink-300 opacity-0 transition-opacity group-hover:opacity-100" aria-hidden />
      )}
    </button>
  );
}

/**
 * Shared confirm for single and bulk retries — carries the "reset adapter
 * properties" choice that decides whether the retry re-resolves config.
 */
export function RetryDialog({
  count,
  busy,
  onConfirm,
  onClose,
}: {
  count: number;
  busy: boolean;
  onConfirm: (reset: boolean) => void;
  onClose: () => void;
}) {
  const [reset, setReset] = useState(false);
  return (
    <Dialog
      title={count === 1 ? "Retry this exchange?" : `Retry ${count} exchanges?`}
      onClose={onClose}
    >
      <div className="space-y-4">
        <p className="text-sm text-ink-600">
          The original input document{count === 1 ? "" : "s"} will run through the pipeline again as{" "}
          {count === 1 ? "a new exchange" : "new exchanges"}.
          {count > 1 && " Exchanges that already have a pending auto-retry are skipped."}
        </p>
        <Checkbox
          label="Re-resolve adapter properties"
          description="Use the integration's current configuration instead of the values captured when the exchange first ran."
          checked={reset}
          onChange={(e) => setReset(e.target.checked)}
        />
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button variant="primary" busy={busy} onClick={() => onConfirm(reset)}>
            Retry
          </Button>
        </div>
      </div>
    </Dialog>
  );
}
