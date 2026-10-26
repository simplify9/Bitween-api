import { useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2, Sparkles } from "lucide-react";
import { api, getAppConfig } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { parseSample } from "../../lib/nativeMapper/documentTree";
import { changedRows, differencesByRow, rowFor, ruleCount } from "../../lib/nativeMapper/aiTally";
import type { AiTally } from "../../lib/nativeMapper/rulesReducer";
import { useRules, useRulesDispatch } from "../../lib/nativeMapper/rulesEditorHooks";
import { fromWire, toWire } from "../../lib/nativeMapper/serialize";
import { sideOptions, type MappingRules } from "../../lib/nativeMapper/types";
import { FormatButton } from "../ui/FormatButton";
import { Popover } from "../ui/Popover";
import { Dialog } from "../ui/overlays";
import { Button, FormError } from "../ui/basics";

/**
 * Asks the AI provider under Settings, AI for the rules that turn the source sample into
 * the output sample, and puts them in place of the rules there are.
 *
 * Replaces rather than merges: the model sees both samples whole, so its rules are a
 * complete answer, and mixing them with half of someone else's would be neither. The
 * note it leaves says what was replaced, with Undo beside it.
 *
 * Opens a dialog rather than acting at once, because it needs both samples and the
 * output one otherwise lives inside "Build from a sample" - and because it is the place
 * to say what is sent, and to say it before anything is.
 */
export function GenerateWithAi({ partnerId }: { partnerId: number | null }) {
  const { ai } = useRules();
  const [open, setOpen] = useState(false);
  // Kept while the editor is open, so Fix with AI asks with the same notes.
  const [instructions, setInstructions] = useState("");
  const run = useAiRun(partnerId, instructions, () => setOpen(false));

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        title="Write every rule from a source sample and an output sample"
        className="flex items-center gap-1 rounded-lg border border-ink-200 bg-white px-2 py-1 text-xs font-medium text-ink-600 hover:border-ink-300 hover:bg-ink-50"
      >
        {run.pending && !open ? (
          <Loader2 size={12} className="animate-spin" aria-hidden />
        ) : (
          <Sparkles size={12} aria-hidden />
        )}
        {run.pending && !open ? `Generating… ${run.seconds}s` : "Generate with AI"}
      </button>

      {/* Closing the dialog leaves the request running; this is where it can still be stopped. */}
      {run.pending && !open && (
        <button type="button" onClick={run.cancel} className="text-[11px] font-medium link">
          Cancel
        </button>
      )}

      {run.error && !run.pending && !open && (
        <span role="alert" className="max-w-80 truncate text-[11px] text-danger-700" title={run.error}>
          {run.error}
        </span>
      )}

      {ai && !run.pending && <AiNote tally={ai} onFix={() => run.start(true)} />}

      {open && (
        <AiDialog
          instructions={instructions}
          onInstructions={setInstructions}
          run={run}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}

type AiRun = ReturnType<typeof useAiRun>;

/**
 * One request to the server at a time, with the seconds it has taken so far and a way to
 * stop waiting for it.
 *
 * Cancelling stops the editor waiting, not the model: the server finishes the call it
 * started, and its answer is dropped.
 */
function useAiRun(partnerId: number | null, instructions: string, onApplied: () => void) {
  const { rules, sourceSample, targetSample } = useRules();
  const dispatch = useRulesDispatch();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [seconds, setSeconds] = useState(0);
  const running = useRef<AbortController | null>(null);
  // What the editor holds now, as opposed to what this render's closure saw when a run started.
  const latest = useRef({ rules, sourceSample, targetSample });
  useEffect(() => {
    latest.current = { rules, sourceSample, targetSample };
  }, [rules, sourceSample, targetSample]);

  useEffect(() => {
    if (!pending) return;
    const started = Date.now();
    setSeconds(0);
    const tick = setInterval(() => setSeconds(Math.floor((Date.now() - started) / 1000)), 1000);
    return () => clearInterval(tick);
  }, [pending]);

  const start = async (refine: boolean) => {
    running.current?.abort();
    const abort = new AbortController();
    running.current = abort;
    const started = Date.now();
    setPending(true);
    setError(null);
    try {
      const result = await api.suggestMappingRules(
        {
          mappingRules: JSON.stringify(toWire(rules)),
          sourceDocument: sourceSample,
          targetDocument: targetSample,
          partnerId,
          refine,
          instructions: instructions.trim() || undefined,
        },
        abort.signal,
      );
      if (abort.signal.aborted) return;
      // The answer was checked against what was sent. Anything edited since - a rule, either
      // sample - would be overwritten, and the note would vouch for a sample that is gone.
      const now = latest.current;
      if (now.rules !== rules || now.sourceSample !== sourceSample || now.targetSample !== targetSample) {
        setError("The mapping or a sample changed while the AI was working, so its answer was dropped. Generate again.");
        return;
      }
      const next = fromWire(JSON.parse(result.mappingRules) as MappingRules);
      dispatch({
        type: "APPLY_AI_RULES",
        rules: next,
        tally: {
          matchesTarget: result.matchesTarget,
          differences: result.differences,
          byRow: differencesByRow(next, result.differences),
          changed: changedRows(rules, next),
          before: ruleCount(rules),
          after: ruleCount(next),
          model: result.model,
          seconds: Math.max(1, Math.round((Date.now() - started) / 1000)),
        },
      });
      onApplied();
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

  return { start, cancel, pending, error, seconds };
}

function AiDialog({
  instructions,
  onInstructions,
  run,
  onClose,
}: {
  instructions: string;
  onInstructions: (text: string) => void;
  run: AiRun;
  onClose: () => void;
}) {
  const config = useQuery({ queryKey: keys.appConfig, queryFn: getAppConfig });
  const canSetUp = useSessionCan("settings.edit");
  const { rules, sourceSample, targetSample } = useRules();
  const dispatch = useRulesDispatch();

  const source = useMemo(
    () => parseSample(sourceSample, rules.sourceFormat, "source", rules.sourceCsv, sideOptions(rules, "source")),
    [sourceSample, rules],
  );
  const target = useMemo(
    () => parseSample(targetSample, rules.targetFormat, "target", rules.targetCsv, sideOptions(rules, "target")),
    [targetSample, rules],
  );
  const ready = sourceSample.trim() !== "" && targetSample.trim() !== "" && !source.error && !target.error;
  const existing = ruleCount(rules);

  if (config.data && !config.data.aiConfigured)
    return (
      <Dialog title="Generate the rules with AI" onClose={onClose}>
        <div className="space-y-3 text-sm text-ink-700">
          <p>AI isn't set up on this Bitween yet: it needs an AI provider and its API key under Settings, AI.</p>
          {canSetUp ? (
            // A new tab, so the mapping being built here isn't left behind unsaved.
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
    <Dialog title="Generate the rules with AI" wide onClose={onClose}>
      <div className="space-y-3">
        <div className="grid gap-3 sm:grid-cols-2">
          <SampleBox
            label="Source sample"
            hint="A document as it arrives."
            value={sourceSample}
            error={source.error}
            onChange={(text) => dispatch({ type: "SET_SOURCE_SAMPLE", text })}
          />
          <SampleBox
            label="Output sample"
            hint="What that document should become. Blank values are fine."
            value={targetSample}
            error={target.error}
            onChange={(text) => dispatch({ type: "SET_TARGET_SAMPLE", text })}
          />
        </div>

        <label className="block">
          <span className="text-[13px] font-medium text-ink-800">
            Instructions <span className="font-normal text-ink-500">- optional</span>
          </span>
          <textarea
            value={instructions}
            onChange={(e) => onInstructions(e.target.value)}
            placeholder="e.g. Status codes: P = Pending, S = Shipped, C = Cancelled. Bronze customers aren't VIP."
            className="mt-1 min-h-[64px] w-full resize-y rounded-lg border border-ink-200 px-2 py-1.5 text-[13px] focus:border-focus-400 focus:outline-none"
          />
          <span className="text-[11px] text-ink-500">
            For what the samples don't show, such as codes the sample doesn't happen to contain.
          </span>
        </label>

        <p className="text-[11px] text-ink-500">
          Sent to the AI provider under Settings, AI: both samples, these instructions, and the names -
          never the values - of this partner's properties and the global values.
        </p>
        {existing > 0 && (
          <p className="text-[11px] text-warn-700">
            Replaces the {existing} {existing === 1 ? "rule" : "rules"} there {existing === 1 ? "is" : "are"}. Undo
            puts {existing === 1 ? "it" : "them"} back.
          </p>
        )}

        <FormError>{run.error}</FormError>

        <div className="flex items-center justify-end gap-2 border-t border-ink-100 pt-3">
          {run.pending ? (
            <>
              <span role="status" className="mr-auto flex items-center gap-1.5 text-[13px] text-ink-600">
                <Loader2 className="size-4 animate-spin" aria-hidden /> Generating… {run.seconds}s
                <span className="text-ink-400">· usually 10-30 seconds</span>
              </span>
              <Button onClick={run.cancel}>Cancel</Button>
            </>
          ) : (
            <>
              <Button onClick={onClose}>Close</Button>
              <Button variant="primary" disabled={!ready} onClick={() => void run.start(false)}>
                <Sparkles size={14} aria-hidden /> Generate
              </Button>
            </>
          )}
        </div>
      </div>
    </Dialog>
  );
}

function SampleBox({
  label,
  hint,
  value,
  error,
  onChange,
}: {
  label: string;
  hint: string;
  value: string;
  error: string | null;
  onChange: (text: string) => void;
}) {
  return (
    <div className="flex min-w-0 flex-col">
      <label className="text-[13px] font-medium text-ink-800">
        {label}
        <textarea
          value={value}
          onChange={(e) => onChange(e.target.value)}
          spellCheck={false}
          className="mt-1 h-48 w-full resize-y rounded-lg border border-ink-200 bg-ink-50 px-2 py-1.5 font-mono text-[11px] font-normal focus:border-focus-400 focus:outline-none"
        />
      </label>
      <div className="flex items-start gap-2">
        <p className={`text-[11px] ${error ? "text-danger-700" : "text-ink-500"}`}>{error ?? hint}</p>
        <FormatButton className="ml-auto" value={value} onChange={onChange} />
      </div>
    </div>
  );
}

/**
 * What the last generation did: how close it came, what it replaced, which model took how
 * long - and Undo, right there, since it replaced every rule.
 */
function AiNote({ tally, onFix }: { tally: AiTally; onFix: () => void }) {
  const dispatch = useRulesDispatch();
  const { rules } = useRules();
  const count = tally.differences.length;

  /** Brings a difference's rule into view and selects it, when there is one to point at. */
  const show = (target: string) => {
    const row = rowFor(rules, target);
    if (!row) return;
    document
      .querySelector(`[data-error-key="${row.key.replace(/["\\]/g, "\\$&")}"]`)
      ?.scrollIntoView?.({ block: "center", behavior: "smooth" });
    if (row.isField) dispatch({ type: "SELECT", id: row.id });
  };

  return (
    <span className="flex flex-wrap items-center gap-x-1.5 text-[11px] text-ink-500">
      {tally.matchesTarget ? (
        <span className="font-medium text-ok-700">Matches the output sample</span>
      ) : (
        <Popover
          label="Where the generated rules miss the output sample"
          width="w-96"
          closeOnScroll={false}
          button={
            <span className="font-medium text-warn-700 underline decoration-dotted">
              {count === 0
                ? "Some rules fail - see the preview"
                : `${count}${count >= 20 ? "+" : ""} ${count === 1 ? "difference" : "differences"} from the output sample`}
            </span>
          }
        >
          <div className="space-y-2">
            {count > 0 && (
              <ul className="max-h-64 space-y-0.5 overflow-auto text-[11px] text-ink-700">
                {tally.differences.map((d) => (
                  <li key={`${d.target}\u0000${d.reason}`}>
                    <button
                      type="button"
                      onClick={() => show(d.target)}
                      title="Show the rule"
                      className="w-full rounded px-1.5 py-1 text-left hover:bg-ink-50"
                    >
                      <span className="font-mono text-ink-900">{d.target}</span>: {d.reason}
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <p className="text-[11px] text-ink-500">
              The rows are marked too. Fix asks the AI to correct the rules against these.
            </p>
            <Button size="sm" onClick={onFix}>
              <Sparkles size={12} /> Fix with AI
            </Button>
          </div>
        </Popover>
      )}
      <span title="Undo puts the rules there were back">
        ·{" "}
        {tally.before === 0
          ? `Wrote ${tally.after} ${tally.after === 1 ? "rule" : "rules"}`
          : `Replaced ${tally.before} ${tally.before === 1 ? "rule" : "rules"} with ${tally.after}`}
      </span>
      {tally.model && (
        <span title="The model that wrote the rules, and how long it took">
          · {tally.model} · {tally.seconds}s
        </span>
      )}
      <button
        type="button"
        onClick={() => dispatch({ type: "UNDO" })}
        aria-label="Undo the generated rules"
        title="Put back the rules there were"
        className="font-medium link"
      >
        Undo
      </button>
    </span>
  );
}
