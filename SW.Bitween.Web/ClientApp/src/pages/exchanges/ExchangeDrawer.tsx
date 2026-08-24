import { useState, type ReactNode } from "react";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRight, Check, Copy, FileText, Play, RotateCcw } from "lucide-react";
import { api, ApiRequestError, type ExchangeDocStage, type ExchangeRow } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { Badge, Button } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { formatDateTime, duration, timeUntil } from "../../lib/dates";
import { RetryDialog, journeyStages, type JourneyStage } from "./shared";

const STAGE_TONES: Record<JourneyStage["state"], { ring: string; badge: ReactNode }> = {
  done: { ring: "border-ok-100", badge: <Badge tone="ok">Done</Badge> },
  bad: { ring: "border-warn-100", badge: <Badge tone="warn">Bad response</Badge> },
  failed: { ring: "border-danger-200", badge: <Badge tone="danger">Failed</Badge> },
  skipped: { ring: "border-ink-100 border-dashed", badge: <Badge>Skipped</Badge> },
  running: { ring: "border-ink-200", badge: <Badge tone="ink">Running</Badge> },
  notReached: { ring: "border-ink-100", badge: <Badge>Not reached</Badge> },
};

const kb = (bytes: number) => (bytes < 1024 ? `${bytes} B` : `${(bytes / 1024).toFixed(1)} KB`);

function CopyButton({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      onClick={async () => {
        await navigator.clipboard.writeText(value);
        setCopied(true);
        setTimeout(() => setCopied(false), 1400);
      }}
      title={`Copy ${label}`}
      className="rounded-md p-1 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
    >
      {copied ? <Check className="size-3.5 text-ok-600" /> : <Copy className="size-3.5" />}
    </button>
  );
}

function MetaItem({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-[11px] font-medium tracking-wide text-ink-400 uppercase">{label}</dt>
      <dd className="mt-0.5 text-[13px] text-ink-800">{children}</dd>
    </div>
  );
}

const STAGE_ORDER: ExchangeDocStage[] = ["Input", "Mapped", "Handled"];

/**
 * The expanded exchange row: the pipeline journey with per-stage documents,
 * the failure (when there is one), full metadata and the retry actions.
 */
export function ExchangeDrawer({ x }: { x: ExchangeRow }) {
  const stages = journeyStages(x);
  const canOperate = useSessionCan("exchanges.operate");
  const queryClient = useQueryClient();

  const files: Record<ExchangeDocStage, ExchangeRow["files"]["input"]> = {
    Input: x.files.input,
    Mapped: x.files.mapped,
    Handled: x.files.handled,
  };

  // Default to the furthest stage that produced a document.
  const [stage, setStage] = useState<ExchangeDocStage>(
    [...STAGE_ORDER].reverse().find((s) => files[s]?.key) ?? "Input",
  );
  const activeKey = files[stage]?.key ?? null;
  const {
    data: activeContent,
    isLoading: activeLoading,
    isError: activeErrored,
  } = useQuery({
    queryKey: ["exchange-document", activeKey],
    queryFn: () => api.getExchangeDocument(activeKey!),
    enabled: activeKey !== null,
  });

  const [confirming, setConfirming] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [startedId, setStartedId] = useState<string | null>(null);

  const invalidate = () => void queryClient.invalidateQueries({ queryKey: ["exchanges"] });

  const retry = useMutation({
    mutationFn: (reset: boolean) => api.retryExchange(x.id, { reset }),
    onSuccess: ({ id }) => {
      setConfirming(false);
      setStartedId(id);
      invalidate();
    },
    onError: (e) => {
      setConfirming(false);
      setActionError(e instanceof ApiRequestError ? e.message : "The retry could not be started.");
    },
  });

  const [confirmingRunNow, setConfirmingRunNow] = useState(false);
  const runNow = useMutation({
    mutationFn: () => api.runScheduledRetryNow(x.id),
    onSuccess: () => {
      setStartedId("scheduled");
      invalidate();
      void queryClient.invalidateQueries({ queryKey: ["scheduled-retries"] });
    },
  });

  return (
    <div className="space-y-4 border-l-2 border-ink-100 py-1 pl-4">
      {/* — journey — */}
      <div className="flex flex-wrap items-stretch gap-1.5">
        {stages.map((s, i) => {
          const tone = STAGE_TONES[s.state];
          const file = files[s.key];
          const active = !!file?.key && s.key === stage;
          return (
            <div key={s.key} className="flex items-stretch gap-1.5">
              {i > 0 && <ArrowRight className="size-3.5 self-center text-ink-300" aria-hidden />}
              <button
                disabled={!file?.key}
                onClick={() => file?.key && setStage(s.key)}
                title={file?.key ? `Show the ${s.key} document` : (s.note ?? "No document")}
                className={`w-44 rounded-xl border bg-white px-3 py-2 text-left transition-colors ${tone.ring} ${
                  file?.key ? "cursor-pointer hover:bg-ink-50" : "cursor-default opacity-80"
                } ${active ? "ring-2 ring-crimson-100" : ""}`}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="text-[13px] font-semibold text-ink-800">{s.label}</span>
                  {tone.badge}
                </div>
                <div className="mt-1 flex items-center gap-1 text-[11px] text-ink-500">
                  {file ? (
                    <>
                      <FileText className="size-3 shrink-0 text-ink-300" aria-hidden />
                      <span className="truncate font-mono">{file.name}</span>
                      <span className="shrink-0 text-ink-400">· {kb(file.size)}</span>
                    </>
                  ) : (
                    <span className="text-ink-400">{s.note ?? "No document"}</span>
                  )}
                </div>
              </button>
            </div>
          );
        })}
      </div>

      {/* — active stage document — */}
      {activeKey && (
        <pre className="max-h-56 overflow-auto rounded-lg bg-ink-950 px-3 py-2.5 font-mono text-[11px] leading-relaxed text-ink-100">
          {activeLoading ? "Loading…" : activeErrored ? "Failed to load this document." : activeContent}
        </pre>
      )}

      {/* — failure — */}
      {x.exception && (
        <div className="rounded-lg bg-danger-50 px-3 py-2.5">
          <div className="mb-1 flex items-center justify-between">
            <p className="text-[11px] font-medium tracking-wide text-danger-700 uppercase">Exception</p>
            <CopyButton value={x.exception} label="exception" />
          </div>
          <pre className="max-h-40 overflow-auto font-mono text-[11px] leading-relaxed whitespace-pre-wrap text-danger-800">
            {x.exception}
          </pre>
        </div>
      )}

      {/* — metadata — */}
      <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3 lg:grid-cols-4">
        {/* The id lives here rather than in the row: it identifies a record you
            have already found, so it earns its place next to the other
            copy-and-paste metadata, not in the scanning path. */}
        <MetaItem label="Exchange id">
          <span className="inline-flex items-center gap-0.5">
            <span className="font-mono text-xs break-all text-ink-700">{x.id}</span>
            <CopyButton value={x.id} label="exchange id" />
          </span>
        </MetaItem>
        <MetaItem label="Started">{formatDateTime(x.startedOn)}</MetaItem>
        <MetaItem label="Finished">
          {x.finishedOn ? (
            <>
              {formatDateTime(x.finishedOn)}
              <span className="text-ink-400"> · {duration(x.startedOn, x.finishedOn)}</span>
            </>
          ) : (
            "Still processing"
          )}
        </MetaItem>
        <MetaItem label="Correlation id">
          {x.correlationId ? (
            <span className="inline-flex items-center gap-0.5">
              <Link
                to={`/exchanges?correlationId=${encodeURIComponent(x.correlationId)}`}
                title="Show every exchange with this correlation id"
                className="font-mono text-xs break-all text-ink-700 hover:text-crimson-700 hover:underline"
              >
                {x.correlationId}
              </Link>
              <CopyButton value={x.correlationId} label="correlation id" />
            </span>
          ) : (
            <span className="text-ink-400">—</span>
          )}
        </MetaItem>
        <MetaItem label="Related">
          <Link
            to={`/exchanges?ids=${encodeURIComponent(x.id)}`}
            className="text-[13px] font-medium text-ink-700 hover:text-crimson-700 hover:underline"
          >
            Retries &amp; aggregation family
          </Link>
        </MetaItem>
        {x.retryFor && (
          <MetaItem label="Retry of">
            <Link
              to={`/exchanges?ids=${encodeURIComponent(x.retryFor)}`}
              className="font-mono text-xs text-ink-700 hover:text-crimson-700 hover:underline"
            >
              {x.retryFor}
            </Link>
          </MetaItem>
        )}
        {x.aggregationXchangeId && (
          <MetaItem label="Aggregated into">
            <Link
              to={`/exchanges?ids=${encodeURIComponent(x.aggregationXchangeId)}`}
              className="font-mono text-xs text-ink-700 hover:text-crimson-700 hover:underline"
            >
              {x.aggregationXchangeId}
            </Link>
          </MetaItem>
        )}
        {x.promotedProperties && Object.keys(x.promotedProperties).length > 0 && (
          <MetaItem label="Promoted properties">
            <span className="flex flex-wrap gap-1">
              {Object.entries(x.promotedProperties).map(([k, v]) => (
                <code key={k} className="rounded bg-ink-100 px-1.5 py-0.5 font-mono text-[11px] text-ink-700">
                  {k}={v}
                </code>
              ))}
            </span>
          </MetaItem>
        )}
      </dl>

      {/* — actions — */}
      {canOperate && (
        <div className="flex flex-wrap items-center gap-2 border-t border-ink-100 pt-3">
          {startedId ? (
            <p className="text-[13px] text-ok-600">
              ✓ Retry started{startedId !== "scheduled" && <> — new exchange is at the top of the list</>}
            </p>
          ) : x.scheduledRetryOn ? (
            <>
              <Badge tone="warn">Auto-retry {timeUntil(x.scheduledRetryOn)}</Badge>
              <span className="text-[13px] text-ink-500">
                scheduled for {formatDateTime(x.scheduledRetryOn)}
              </span>
              <Button size="sm" onClick={() => setConfirmingRunNow(true)}>
                <Play className="size-3.5" aria-hidden />
                Run now
              </Button>
            </>
          ) : (
            (x.status === "failed" || x.status === "badResponse") && (
              <Button size="sm" onClick={() => setConfirming(true)}>
                <RotateCcw className="size-3.5" aria-hidden />
                Retry…
              </Button>
            )
          )}
          {actionError && <p className="text-[13px] text-danger-700">{actionError}</p>}
        </div>
      )}

      {confirming && (
        <RetryDialog
          count={1}
          busy={retry.isPending}
          onConfirm={(reset) => retry.mutate(reset)}
          onClose={() => setConfirming(false)}
        />
      )}

      {confirmingRunNow && (
        <ConfirmDialog
          title="Run this retry now?"
          body={`The scheduled auto-retry for ${x.id} runs immediately instead of waiting for its slot.`}
          confirmLabel="Run now"
          onConfirm={() => runNow.mutateAsync()}
          onClose={() => setConfirmingRunNow(false)}
        />
      )}
    </div>
  );
}
