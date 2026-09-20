import { useState } from "react";
import { ArrowRight, Check, Copy } from "lucide-react";
import type { BulkRetryPlan, ExchangeRow, ExchangeStatus } from "../../api";
import { Badge, Button } from "../../components/ui/basics";
import { PromotedProps, namesSomething } from "../../components/config/shared";
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

/**
 * Past tense only once the stage is actually behind us. "Mapped" beside a Running badge
 * claims something that has not happened yet — while it is in flight, or was skipped, or
 * was never reached, the stage is named for the work rather than for a result it lacks.
 */
function stageLabel(key: JourneyStage["key"], state: StageState): string {
  if (key === "Input") return "Received";
  const finished = state === "done" || state === "bad" || state === "failed";
  if (key === "Mapped") return finished ? "Mapped" : "Mapping";
  return finished ? "Handled" : "Handling";
}

/** Derives what happened at each pipeline stage from the row's fields. */
export function journeyStages(x: ExchangeRow): JourneyStage[] {
  const mapped: Omit<JourneyStage, "label"> = x.mapperSkipped
    ? { key: "Mapped", state: "skipped", note: "No mapper configured" }
    : x.files.mapped
      ? { key: "Mapped", state: "done" }
      : x.status === "processing"
        ? { key: "Mapped", state: "running" }
        : x.status === "failed"
          ? { key: "Mapped", state: "failed", note: "Failed while mapping" }
          : { key: "Mapped", state: "notReached" };

  const handlerReached = !(mapped.state === "failed");
  const handled: Omit<JourneyStage, "label"> = !handlerReached
    ? { key: "Handled", state: "notReached", note: "Never reached" }
    : x.status === "success"
      ? { key: "Handled", state: "done" }
      : x.status === "badResponse"
        ? { key: "Handled", state: "bad", note: "Delivered, but the response reports an error" }
        : x.status === "failed"
          ? { key: "Handled", state: "failed", note: "Failed while handling" }
          : { key: "Handled", state: "running" };

  return [{ key: "Input", state: "done" } as Omit<JourneyStage, "label">, mapped, handled].map(
    (s) => ({ ...s, label: stageLabel(s.key, s.state) }),
  );
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
 * How the plan lists name an exchange: the promoted properties the exchange list names it by,
 * and enough of the id to tell two of them apart.
 *
 * The id stays because a substitution puts two exchanges side by side, and an information type
 * whose promoted paths resolved to nothing gives both of them the same chips — "trackingNo= →
 * trackingNo=" says which fields exist and nothing about which exchanges these are.
 */
function ExchangeIdentity({
  id,
  properties,
}: {
  id: string;
  properties: Record<string, string | null> | null;
}) {
  // Properties that carry no values name nothing, so they are left out entirely rather than
  // shown as a row of empty chips next to an identical row of empty chips.
  if (!namesSomething(properties))
    return (
      <span className="min-w-0 truncate font-mono text-[11px] text-ink-500" title={id}>
        {id}
      </span>
    );

  return (
    <span className="flex min-w-0 items-center gap-1.5">
      <PromotedProps properties={properties} max={2} />
      <span className="shrink-0 font-mono text-[11px] text-ink-400" title={id}>
        {id.slice(0, 8)}…
      </span>
    </span>
  );
}

/**
 * Shared confirm for single and bulk retries — carries the "reset adapter properties" choice
 * that decides whether the retry re-resolves config.
 *
 * A bulk retry also passes the `plan` the server worked out for the same selection, because a
 * selection is rarely just itself: exchanges already retried hand over to their newest attempt,
 * ones that have since succeeded drop out, and two selections in one chain come to the same
 * attempt. All of that is shown before anyone commits, since a retry cannot be taken back.
 */
export function RetryDialog({
  count,
  plan,
  planLoading = false,
  busy,
  onConfirm,
  onResetChange,
  onClose,
}: {
  count: number;
  /** Bulk retries only — a single retry has nothing to resolve. */
  plan?: BulkRetryPlan | null;
  planLoading?: boolean;
  busy: boolean;
  onConfirm: (reset: boolean) => void;
  /**
   * Bulk retries only: the plan depends on this choice — re-resolving properties is impossible
   * for an exchange whose subscription is gone — so the caller has to be able to ask again.
   */
  onResetChange?: (reset: boolean) => void;
  onClose: () => void;
}) {
  const [reset, setReset] = useState(false);
  const bulk = count !== 1;
  const nothingToDo = plan != null && !plan.overLimit && plan.willRetry === 0;

  return (
    <Dialog
      title={count === 1 ? "Retry this exchange?" : `Retry ${count.toLocaleString()} exchanges?`}
      onClose={onClose}
    >
      <div className="space-y-4">
        {plan?.overLimit ? (
          <p className="text-sm text-ink-600">
            {plan.selected.toLocaleString()} exchanges match this filter, which is more than the{" "}
            {plan.limit.toLocaleString()} a single retry will carry out. Narrow the filter — by
            status, partner or date — and retry the rest after.
          </p>
        ) : (
          <>
            <p className="text-sm text-ink-600">
              {bulk && planLoading
                ? "Working out what will run…"
                : plan
                  ? plan.willRetry === 0
                    ? "Nothing here can be retried."
                    : `${plan.willRetry.toLocaleString()} ${
                        plan.willRetry === 1 ? "exchange" : "exchanges"
                      } will run again — the original input document goes back through the pipeline as a new exchange.`
                  : `The original input document${count === 1 ? "" : "s"} will run through the pipeline again as ${
                      count === 1 ? "a new exchange" : "new exchanges"
                    }.`}
            </p>

            {plan != null && plan.substituted.length > 0 && (
              <div className="rounded-lg border border-ink-200 bg-ink-50/60 px-3 py-2">
                <p className="text-[13px] text-ink-700">
                  <strong className="font-semibold">{plan.substituted.length.toLocaleString()}</strong> of
                  these {plan.substituted.length === 1 ? "has" : "have"} already been retried. An
                  exchange is only retried once, so{" "}
                  {plan.substituted.length === 1 ? "its newest attempt runs" : "their newest attempts run"}{" "}
                  instead.
                </p>
                <details className="mt-1.5">
                  <summary className="cursor-pointer text-xs font-medium text-ink-500 hover:text-ink-700">
                    Show which
                  </summary>
                  <ul className="mt-1.5 max-h-40 space-y-1.5 overflow-y-auto">
                    {plan.substituted.map((s) => (
                      <li key={s.selectedId} className="flex items-center gap-1.5">
                        <ExchangeIdentity id={s.selectedId} properties={plan.properties[s.selectedId] ?? null} />
                        <ArrowRight className="size-3 shrink-0 text-ink-400" aria-hidden />
                        <ExchangeIdentity id={s.retryId} properties={plan.properties[s.retryId] ?? null} />
                      </li>
                    ))}
                  </ul>
                </details>
              </div>
            )}

            {plan != null && plan.skipped.length > 0 && (
              <div className="rounded-lg border border-ink-200 bg-ink-50/60 px-3 py-2">
                <p className="text-[13px] text-ink-700">
                  <strong className="font-semibold">{plan.skipped.length.toLocaleString()}</strong>{" "}
                  will be skipped.
                </p>
                <details className="mt-1.5">
                  <summary className="cursor-pointer text-xs font-medium text-ink-500 hover:text-ink-700">
                    Show why
                  </summary>
                  <ul className="mt-1.5 max-h-40 space-y-1 overflow-y-auto">
                    {plan.skipped.map((s) => (
                      <li key={s.id} className="flex flex-wrap items-center gap-1.5 text-[11px] text-ink-600">
                        <ExchangeIdentity id={s.id} properties={plan.properties[s.id] ?? null} />
                        <span>— {s.reason}</span>
                      </li>
                    ))}
                  </ul>
                </details>
              </div>
            )}

            <Checkbox
              label="Re-resolve adapter properties"
              description="Use the subscription's current configuration instead of the values captured when the exchange first ran."
              checked={reset}
              onChange={(e) => {
                setReset(e.target.checked);
                onResetChange?.(e.target.checked);
              }}
            />
          </>
        )}

        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>
            {plan?.overLimit || nothingToDo ? "Close" : "Cancel"}
          </Button>
          {!plan?.overLimit && !nothingToDo && (
            <Button variant="primary" busy={busy} disabled={planLoading} onClick={() => onConfirm(reset)}>
              Retry
            </Button>
          )}
        </div>
      </div>
    </Dialog>
  );
}
