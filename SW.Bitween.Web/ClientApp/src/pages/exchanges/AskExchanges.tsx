import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2, Sparkles, X } from "lucide-react";
import { api, getAppConfig } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { Button, FormError } from "../../components/ui/basics";
import { countLabel, viewSearch } from "./exchangeFilters";

type Changes = Record<string, string | string[] | null>;

/** A question that was turned into the page's filter, kept while that filter is the page's. */
export interface AskedQuestion {
  question: string;
  /** The filter it became, as `viewSearch` writes it: while the page shows another, it's gone. */
  search: string;
  answer: string;
  notes: string[];
  model: string | null;
  sent: string;
}

/**
 * Ask your exchanges: a question in plain English becomes the page's own filter - its chips, its
 * link, its saved views. Only the question, the time and names go to the AI provider.
 */
export function AskButton({
  searchParams,
  apply,
  onAsked,
}: {
  searchParams: URLSearchParams;
  apply: (changes: Changes) => void;
  onAsked: (asked: AskedQuestion) => void;
}) {
  const config = useQuery({ queryKey: keys.appConfig, queryFn: getAppConfig });
  const canSetUp = useSessionCan("settings.edit");
  const [open, setOpen] = useState(false);
  const [question, setQuestion] = useState("");
  const [busy, setBusy] = useState(false);
  const [seconds, setSeconds] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const controller = useRef<AbortController | null>(null);

  useEffect(() => () => controller.current?.abort(), []);
  useEffect(() => {
    if (!busy) return;
    setSeconds(0);
    const timer = setInterval(() => setSeconds((s) => s + 1), 1000);
    return () => clearInterval(timer);
  }, [busy]);

  async function ask() {
    if (!question.trim() || busy) return;
    controller.current = new AbortController();
    setBusy(true);
    setError(null);
    try {
      const res = await api.askExchanges(question.trim(), controller.current.signal);
      // The answer replaces every filter, as picking a saved view does; the open row stays.
      const target = new URLSearchParams(res.query);
      const changes: Changes = {};
      for (const k of new Set([...searchParams.keys(), ...target.keys()])) if (k !== "open") changes[k] = target.getAll(k);
      apply(changes);
      onAsked({ question: question.trim(), search: viewSearch(target), ...res });
      setOpen(false);
    } catch (e) {
      if (!controller.current.signal.aborted) setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  const configured = !config.data || config.data.aiConfigured;
  const close = () => {
    controller.current?.abort();
    setOpen(false);
    setError(null);
  };

  // A row of its own under the bar rather than a panel off the button: the button moves with the
  // bar's wrapping, and a panel anchored to it ran off the screen's edge on a narrow window.
  return (
    <>
      <button
        type="button"
        onClick={() => (open ? close() : setOpen(true))}
        aria-expanded={open}
        title="Ask a question about your exchanges in plain English, and get the filter for it"
        className="inline-flex h-11 items-center gap-2 rounded-xl border border-ink-200 bg-white px-3 text-[13px] font-medium text-ink-800 hover:bg-ink-50"
      >
        <Sparkles className="size-4 text-ink-500" aria-hidden />
        Ask
      </button>
      {open && (
        <div className="basis-full rounded-xl border border-ink-200 bg-white p-3">
          {!configured ? (
            <div className="space-y-2 text-sm text-ink-700">
              <p>AI isn't set up on this Bitween yet: it needs an AI provider and its API key under Settings, AI.</p>
              {canSetUp ? (
                <a href="/settings?section=AI" target="_blank" rel="noreferrer" className="font-medium link">
                  Open Settings, AI in a new tab
                </a>
              ) : (
                <p className="text-ink-500">Ask an administrator to set it up.</p>
              )}
            </div>
          ) : (
            <form
              className="space-y-2"
              onSubmit={(e) => {
                e.preventDefault();
                void ask();
              }}
              onKeyDown={(e) => e.key === "Escape" && close()}
            >
              <label className="block text-sm font-medium text-ink-900" htmlFor="ask-exchanges">
                Ask your exchanges
              </label>
              <div className="flex flex-wrap items-center gap-2">
                <input
                  id="ask-exchanges"
                  autoFocus
                  value={question}
                  maxLength={500}
                  onChange={(e) => setQuestion(e.target.value)}
                  placeholder="Failed SkyNet orders since Monday"
                  className="h-9 min-w-0 flex-[1_1_16rem] rounded-lg border border-ink-200 px-2.5 text-sm focus:border-ink-400 focus:outline-none"
                />
                {busy ? (
                  <>
                    <span className="inline-flex items-center gap-1.5 text-xs text-ink-500" role="status">
                      <Loader2 className="size-3.5 animate-spin" aria-hidden />
                      Asking… {seconds}s
                    </span>
                    <Button type="button" onClick={() => controller.current?.abort()}>
                      Cancel
                    </Button>
                  </>
                ) : (
                  <Button type="submit" variant="primary" disabled={!question.trim()}>
                    Ask
                  </Button>
                )}
              </div>
              <p className="text-xs text-ink-500">
                Only your question, the time and the names of partners, subscriptions, information types and promoted
                properties are sent to the AI provider. Never an exchange or its data.
              </p>
              {error && <FormError>{error}</FormError>}
            </form>
          )}
        </div>
      )}
    </>
  );
}

/** The answer above the list, with the page's own count where the AI left `{count}`. */
export function AskedLine({
  asked,
  total,
  counting,
  onDismiss,
}: {
  asked: AskedQuestion;
  total: number;
  counting: boolean;
  onDismiss: () => void;
}) {
  const [showSent, setShowSent] = useState(false);
  const count = counting ? "…" : countLabel(total);
  const answer = asked.answer ? asked.answer.replaceAll("{count}", count) : `${count} exchanges match`;

  return (
    <div className="rounded-lg border border-ink-200 bg-ink-50 px-3 py-2 text-sm" role="status" aria-label="Answer">
      <div className="flex items-start gap-2">
        <Sparkles className="mt-0.5 size-4 shrink-0 text-ink-500" aria-hidden />
        <div className="min-w-0 flex-1">
          <p className="font-medium text-ink-900">{answer}</p>
          <p className="text-xs text-ink-500">
            “{asked.question}”{asked.model ? ` · filter written by ${asked.model}, check the chips above` : ""} ·{" "}
            <button
              type="button"
              className="link"
              aria-expanded={showSent}
              onClick={() => setShowSent((s) => !s)}
              title="Exactly what went to the AI provider"
            >
              {showSent ? "Hide what was sent" : "See what was sent"}
            </button>
          </p>
          {asked.notes.length > 0 && (
            <ul className="mt-1 list-disc pl-4 text-xs text-warn-700">
              {asked.notes.map((n) => (
                <li key={n}>{n}</li>
              ))}
            </ul>
          )}
          {showSent && (
            <pre
              aria-label="What was sent to the AI provider"
              className="mt-2 max-h-64 overflow-auto rounded-md bg-white p-2 font-mono text-xs whitespace-pre-wrap text-ink-700"
            >
              {asked.sent}
            </pre>
          )}
        </div>
        <button
          type="button"
          onClick={onDismiss}
          aria-label="Dismiss the answer"
          title="Dismiss the answer"
          className="rounded p-0.5 hover:bg-ink-200"
        >
          <X className="size-3.5" aria-hidden />
        </button>
      </div>
    </div>
  );
}
