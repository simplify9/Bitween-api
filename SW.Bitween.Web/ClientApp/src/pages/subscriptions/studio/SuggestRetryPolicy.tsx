import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import { api, getAppConfig } from "../../../api";
import { keys } from "../../../api/queryKeys";
import type { RetrySuggestion } from "../../../api/types";
import { useSessionCan } from "../../../auth/useSessionCan";
import { Dialog } from "../../../components/ui/overlays";
import { Button, FormError } from "../../../components/ui/basics";

type Group = RetrySuggestion["groups"][number];

const seconds = (ms?: number) => {
  const s = Math.round((ms ?? 0) / 1000);
  return s >= 120 ? `${Math.round(s / 60)} min` : `${s}s`;
};

function delayLabel(d: NonNullable<Group["budget"]>["delayStrategy"]) {
  if (d.type === "fixed") return `every ${seconds(d.delayMs)}`;
  if (d.type === "linear") return `${seconds(d.initialDelayMs)}, then ${seconds(d.incrementMs)} longer each time`;
  return `${seconds(d.initialDelayMs)}, doubling${d.multiplier && d.multiplier !== 2 ? ` (x${d.multiplier})` : ""} up to ${seconds(d.maxDelayMs)}`;
}

const matcherLabel = (m: Group["matchers"][number]) =>
  m.type === "contains"
    ? `contains "${m.value}"`
    : m.type === "regex"
      ? `matches /${m.pattern}/`
      : m.type === "exceptionType"
        ? `is a ${m.value}`
        : `${m.path} ${m.value ?? ""}`.trim();

/**
 * Suggest a retry policy with AI, from this subscription's own failures. Nothing is saved until
 * "Save as a new policy", and then the new policy is only picked here: the subscription saves as
 * it always does.
 */
export function SuggestRetryPolicy({
  subscriptionId,
  onClose,
  onCreated,
}: {
  subscriptionId: number;
  onClose: () => void;
  onCreated: (id: number) => void;
}) {
  const config = useQuery({ queryKey: keys.appConfig, queryFn: getAppConfig });
  const canSetUp = useSessionCan("settings.edit");
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [suggestion, setSuggestion] = useState<RetrySuggestion | null>(null);
  const [name, setName] = useState("");
  const [showSent, setShowSent] = useState(false);
  const [saving, setSaving] = useState(false);
  const controller = useRef<AbortController | null>(null);

  useEffect(() => () => controller.current?.abort(), []);
  useEffect(() => {
    if (!busy) return;
    setElapsed(0);
    const timer = setInterval(() => setElapsed((s) => s + 1), 1000);
    return () => clearInterval(timer);
  }, [busy]);

  async function suggest() {
    controller.current = new AbortController();
    setBusy(true);
    setError(null);
    try {
      const res = await api.suggestRetryPolicy(subscriptionId, controller.current.signal);
      setSuggestion(res);
      setName(res.name);
    } catch (e) {
      if (!controller.current.signal.aborted) setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  async function save() {
    if (!suggestion || !name.trim()) return;
    setSaving(true);
    setError(null);
    try {
      const id = await api.saveSuggestedRetryPolicy(name.trim(), suggestion.groups);
      await queryClient.invalidateQueries({ queryKey: keys.retryPolicies.list });
      onCreated(id);
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setSaving(false);
    }
  }

  if (config.data && !config.data.aiConfigured)
    return (
      <Dialog title="Suggest a retry policy" onClose={onClose}>
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
    <Dialog title="Suggest a retry policy" onClose={onClose} wide>
      {!suggestion ? (
        <div className="space-y-3 text-sm text-ink-700">
          <p>
            Proposes which of this subscription's failures to retry, how often and how soon, from its failures in the
            last 30 days. Nothing changes until you save the suggestion as a new policy.
          </p>
          <p className="text-xs text-ink-500">
            Sent to the AI provider: each kind of error as one line with every value masked, how often it happened and how
            often a retry fixed it, and the current policy's rules. Never a document or a response.
          </p>
          {error && <FormError>{error}</FormError>}
          <div className="flex items-center justify-end gap-2">
            {busy ? (
              <>
                <span className="mr-auto inline-flex items-center gap-1.5 text-xs text-ink-500" role="status">
                  <Loader2 className="size-3.5 animate-spin" aria-hidden />
                  Suggesting… {elapsed}s · usually 10-30 seconds
                </span>
                <Button onClick={() => controller.current?.abort()}>Cancel</Button>
              </>
            ) : (
              <Button variant="primary" onClick={() => void suggest()}>
                Suggest
              </Button>
            )}
          </div>
        </div>
      ) : (
        <div className="space-y-4 text-sm text-ink-700">
          {suggestion.summary && <p className="text-ink-900">{suggestion.summary}</p>}

          <section aria-label="Suggested groups" className="space-y-2">
            {suggestion.groups.map((g, i) => (
              <div key={i} className="rounded-lg border border-ink-200 px-3 py-2">
                <div className="flex items-center gap-2">
                  <span className="font-medium text-ink-900">{g.name}</span>
                  <span
                    className={`rounded px-1.5 text-[11px] font-medium ${g.action === "Block" ? "bg-danger-100 text-danger-700" : "bg-ok-100 text-ok-700"}`}
                    title={g.action === "Block" ? "Failures this group matches are not retried" : "Failures this group matches are retried"}
                  >
                    {g.action === "Block" ? "Stops" : "Retries"}
                  </span>
                </div>
                <p className="text-xs text-ink-600">
                  When the error {g.matchers.map(matcherLabel).join(" or ")}
                  {g.budget &&
                    ` · up to ${g.budget.maxAttemptsPerError} per exchange` +
                      (g.budget.maxAttemptsTotal === null ? "" : `, ${g.budget.maxAttemptsTotal} in all until it next succeeds`) +
                      ` · waits ${delayLabel(g.budget.delayStrategy)}`}
                </p>
                {g.notes && <p className="mt-0.5 text-xs text-ink-500">{g.notes}</p>}
              </div>
            ))}
          </section>

          <section aria-label="What changes">
            <p className="mb-1 font-medium text-ink-900">
              What changes, on its {suggestion.failures.toLocaleString()} failures in the last {suggestion.days} days
            </p>
            <div className="overflow-x-auto rounded-lg border border-ink-200">
              <table className="w-full text-left text-xs">
                <thead className="bg-ink-50 text-ink-500">
                  <tr>
                    <th className="px-2 py-1.5 font-medium" title="Each kind of error, masked as it was sent">Error</th>
                    <th className="px-2 py-1.5 text-right font-medium" title="How many times it happened">Count</th>
                    <th className="px-2 py-1.5 text-right font-medium" title="Of those retried, how many the retry fixed">
                      Fixed by retry
                    </th>
                    <th className="px-2 py-1.5 font-medium" title="What the policy in use does with it">Now</th>
                    <th className="px-2 py-1.5 font-medium" title="What the suggested policy would do, tried on the real error">
                      Suggested
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {suggestion.coverage.map((c, i) => (
                    <tr key={i} className="border-t border-ink-100 align-top">
                      <td className="max-w-72 px-2 py-1.5 font-mono break-words text-ink-800">{c.error}</td>
                      <td className="px-2 py-1.5 text-right tabular-nums">{c.count.toLocaleString()}</td>
                      <td className="px-2 py-1.5 text-right tabular-nums">
                        {c.retried === 0 ? "-" : `${c.fixedByRetry} of ${c.retried}`}
                      </td>
                      <td className="px-2 py-1.5">{c.now}</td>
                      <td className="px-2 py-1.5 text-ink-900">{c.suggested}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <p className="text-xs text-ink-500">
            {suggestion.model ? `Written by ${suggestion.model}. ` : ""}
            <button type="button" className="link" aria-expanded={showSent} onClick={() => setShowSent((s) => !s)}>
              {showSent ? "Hide what was sent" : "See what was sent"}
            </button>
          </p>
          {showSent && (
            <pre
              aria-label="What was sent to the AI provider"
              className="max-h-64 overflow-auto rounded-md bg-ink-50 p-2 font-mono text-xs whitespace-pre-wrap"
            >
              {suggestion.sent}
            </pre>
          )}

          {error && <FormError>{error}</FormError>}
          <div className="flex flex-wrap items-end justify-end gap-2 border-t border-ink-100 pt-3">
            <label className="mr-auto flex min-w-60 flex-1 flex-col gap-1 text-xs text-ink-600">
              Policy name
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="h-9 rounded-lg border border-ink-200 px-2.5 text-sm text-ink-900 focus:border-ink-400 focus:outline-none"
              />
            </label>
            <Button onClick={() => void suggest()} disabled={busy || saving}>
              {busy ? `Suggesting… ${elapsed}s` : "Suggest again"}
            </Button>
            <Button variant="primary" onClick={() => void save()} disabled={!name.trim() || saving || busy}>
              {saving ? "Saving…" : "Save as a new policy"}
            </Button>
          </div>
          <p className="text-right text-xs text-ink-500">
            It's then picked for this subscription; save the subscription to start using it.
          </p>
        </div>
      )}
    </Dialog>
  );
}
