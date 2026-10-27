import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2, Sparkles } from "lucide-react";
import { api, getAppConfig } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { Button, FormError } from "../../components/ui/basics";
import { Dialog } from "../../components/ui/overlays";

type Explanation = Awaited<ReturnType<typeof api.explainExchange>>;

/**
 * "Explain this failure": the AI provider under Settings, AI reads the failed exchange and says
 * what happened, why, and what to change.
 *
 * Offered to anyone who can see the exchange - it changes nothing. What goes to the provider is
 * masked on the server, every value replaced by its type, and the dialog shows exactly what went.
 */
export function ExplainFailure({ xchangeId }: { xchangeId: string }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button
        size="sm"
        onClick={() => setOpen(true)}
        title="Ask the AI provider under Settings, AI why this failed. Values are masked before anything is sent."
      >
        <Sparkles className="size-3.5" aria-hidden />
        Explain this failure
      </Button>
      {open && <ExplainDialog xchangeId={xchangeId} onClose={() => setOpen(false)} />}
    </>
  );
}

function ExplainDialog({ xchangeId, onClose }: { xchangeId: string; onClose: () => void }) {
  const config = useQuery({ queryKey: keys.appConfig, queryFn: getAppConfig });
  const canSetUp = useSessionCan("settings.edit");
  const [result, setResult] = useState<Explanation | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [seconds, setSeconds] = useState(0);
  const [showSent, setShowSent] = useState(false);
  const running = useRef<AbortController | null>(null);

  useEffect(() => {
    if (!pending) return;
    const started = Date.now();
    setSeconds(0);
    const tick = setInterval(() => setSeconds(Math.floor((Date.now() - started) / 1000)), 1000);
    return () => clearInterval(tick);
  }, [pending]);

  // Stops waiting when the dialog closes, so a late answer has nowhere to land.
  useEffect(() => () => running.current?.abort(), []);

  const explain = async () => {
    const abort = new AbortController();
    running.current = abort;
    setPending(true);
    setError(null);
    try {
      const answer = await api.explainExchange(xchangeId, abort.signal);
      if (!abort.signal.aborted) setResult(answer);
    } catch (e) {
      if (!abort.signal.aborted) setError((e as Error).message);
    } finally {
      if (running.current === abort) {
        running.current = null;
        setPending(false);
      }
    }
  };

  const cancel = () => {
    running.current?.abort();
    running.current = null;
    setPending(false);
  };

  if (config.data && !config.data.aiConfigured)
    return (
      <Dialog title="Explain this failure" onClose={onClose}>
        <div className="space-y-3 text-sm text-ink-700">
          <p>AI isn't set up on this Bitween yet: it needs an AI provider and its API key under Settings, AI.</p>
          {canSetUp ? (
            <a href="/settings?section=AI" target="_blank" rel="noreferrer" className="font-medium link">
              Open Settings, AI in a new tab
            </a>
          ) : (
            <p className="text-ink-500">Ask an administrator to set it up.</p>
          )}
        </div>
      </Dialog>
    );

  return (
    <Dialog title="Explain this failure" wide onClose={onClose}>
      <div className="space-y-3">
        {result ? (
          <>
            <p className="text-[15px] font-medium text-ink-900">{result.summary}</p>
            {result.cause && <Section title="Most likely cause">{result.cause}</Section>}
            {result.fix && <Section title="What to change">{result.fix}</Section>}
            <p className="text-[11px] text-ink-500">
              Written by {result.model ?? "the AI provider"} from the error, the setup and the documents, with
              every value masked. It can be wrong; check before you change anything.
            </p>
            <button
              type="button"
              onClick={() => setShowSent((s) => !s)}
              aria-expanded={showSent}
              className="text-[13px] font-medium link"
            >
              {showSent ? "Hide what was sent" : "See what was sent"}
            </button>
            {showSent && (
              <pre
                aria-label="What was sent to the AI provider"
                className="max-h-80 overflow-auto rounded-lg bg-ink-50 p-3 font-mono text-[11px] whitespace-pre-wrap text-ink-700"
              >
                {result.sent}
              </pre>
            )}
          </>
        ) : (
          <p className="text-sm text-ink-700">
            Sends this exchange's error, which step failed, the subscription's adapters and the names of their
            settings, its retry state, and its documents to the AI provider under Settings, AI. Every value is
            masked first: <code className="font-mono text-[12px]">"name": "Lina"</code> goes as{" "}
            <code className="font-mono text-[12px]">"name": "&lt;text&gt;"</code>. The request is recorded in the
            audit log.
          </p>
        )}

        <FormError>{error}</FormError>

        <div className="flex items-center justify-end gap-2 border-t border-ink-100 pt-3">
          {pending ? (
            <>
              <span role="status" className="mr-auto flex items-center gap-1.5 text-[13px] text-ink-600">
                <Loader2 className="size-4 animate-spin" aria-hidden /> Explaining… {seconds}s
                <span className="text-ink-400">· usually 5-20 seconds</span>
              </span>
              <Button onClick={cancel}>Cancel</Button>
            </>
          ) : (
            <>
              <Button onClick={onClose}>Close</Button>
              {!result && (
                <Button variant="primary" onClick={() => void explain()}>
                  <Sparkles size={14} aria-hidden /> Explain
                </Button>
              )}
            </>
          )}
        </div>
      </div>
    </Dialog>
  );
}

function Section({ title, children }: { title: string; children: string }) {
  return (
    <div>
      <h3 className="text-[13px] font-semibold text-ink-800">{title}</h3>
      <p className="mt-0.5 text-sm whitespace-pre-line text-ink-700">{children}</p>
    </div>
  );
}
