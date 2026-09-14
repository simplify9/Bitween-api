import { useMemo } from "react";
import { Wand2 } from "lucide-react";
import { parseSample } from "../../lib/nativeMapper/documentTree";
import { useRules, useRulesDispatch } from "../../lib/nativeMapper/RulesEditorContext";
import type { ScaffoldTally } from "../../lib/nativeMapper/scaffold";
import { FormatButton } from "../ui/FormatButton";
import { Popover } from "../ui/Popover";
import { Button } from "../ui/basics";

/**
 * Builds the rules from a sample of the output document.
 *
 * In the toolbar rather than in the panel: at 28px a row, the box and its
 * explanation were costing about a dozen rules' worth of list for something used
 * once per mapping.
 */
export function BuildFromSample() {
  const { targetSample, rules, scaffold } = useRules();
  const dispatch = useRulesDispatch();

  const parsed = useMemo(
    () => parseSample(targetSample, rules.targetFormat, "target", rules.targetCsv),
    [targetSample, rules.targetFormat, rules.targetCsv],
  );

  return (
    <Popover
      // Deliberately not containing "Build the rules": that is the name of the
      // button inside the panel, and one accessible name inside another makes both
      // ambiguous to anything matching by name.
      label="Build from a sample of the output"
      width="w-96"
      button={
        <span className="flex items-center gap-1 rounded-lg border border-ink-200 bg-white px-2 py-1 text-xs font-medium text-ink-600 hover:border-ink-300 hover:bg-ink-50">
          <Wand2 size={12} aria-hidden /> Build from a sample
        </span>
      }
    >
      <div className="space-y-2">
        <p className="text-[11px] text-ink-500">
          Paste what the partner expects. Adds a rule for anything in it that has none yet, leaves
          the rules you already have alone, and fills in the source fields it can identify. Undo
          puts it back.
        </p>

        <textarea
          className="min-h-[140px] w-full resize-y rounded-lg border border-ink-200 bg-ink-50 px-2 py-1.5 font-mono text-[11px] focus:border-crimson-400 focus:outline-none"
          placeholder={'{ "customerName": "", "lines": [{ "code": "" }] }'}
          value={targetSample}
          onChange={(e) => dispatch({ type: "SET_TARGET_SAMPLE", text: e.target.value })}
          aria-label="Sample output document"
        />
        <div className="flex items-center gap-2">
          {parsed.error && <p className="text-[11px] text-danger-700">{parsed.error}</p>}
          <FormatButton
            className="ml-auto"
            value={targetSample}
            onChange={(text) => dispatch({ type: "SET_TARGET_SAMPLE", text })}
          />
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            size="sm"
            disabled={!targetSample.trim() || parsed.error !== null}
            onClick={() => dispatch({ type: "SCAFFOLD_FROM_TARGET" })}
          >
            <Wand2 size={12} /> Build the rules
          </Button>
          {scaffold && <ScaffoldNote tally={scaffold} />}
        </div>

        <p className="text-[11px] text-ink-500">
          Kept with the mapping as a note for the next reader. The mapping never reads it.
        </p>
      </div>
    </Popover>
  );
}

/** What the last press of "Build the rules" did. */
function ScaffoldNote({ tally }: { tally: ScaffoldTally }) {
  if (tally.problem)
    return (
      <p role="alert" className="text-[11px] text-danger-700">
        {tally.problem}
      </p>
    );

  if (tally.created === 0)
    return <p className="text-[11px] text-ink-500">Everything in the sample already has a rule.</p>;

  return (
    <p className="text-[11px] text-ok-700">
      Added {tally.created} {tally.created === 1 ? "rule" : "rules"} · {tally.matched} matched to a
      source field
    </p>
  );
}
