import { useState } from "react";
import { Copy } from "lucide-react";
import { useRules } from "../../lib/nativeMapper/rulesEditorHooks";
import { HighlightedDocument } from "../ui/HighlightedDocument";

/**
 * What a partner would receive.
 *
 * The document comes back from the server, produced by the same read/map/write an
 * exchange runs. Nothing is evaluated in the browser, so this cannot drift from
 * production the way the old preview did.
 */
export function PreviewPanel({ isPreviewing }: { isPreviewing: boolean }) {
  const { previewOutput, previewError, ruleErrors, sourceSample, rules } = useRules();
  const [copied, setCopied] = useState<"no" | "done" | "failed">("no");

  const failedCount = Object.keys(ruleErrors).length;

  return (
    <div className="flex h-full flex-col overflow-hidden bg-ink-50">
      <div className="flex flex-shrink-0 items-center gap-2 border-b border-ink-200 bg-white px-3 py-2">
        <span className="text-xs font-semibold tracking-wide text-ink-600 uppercase">Preview</span>
        <span className="text-xs text-ink-500">— what a partner would receive</span>
        {isPreviewing && (
          <span className="flex-shrink-0 animate-pulse text-[10px] text-ink-500">Working…</span>
        )}
        {previewOutput && (
          <button
            type="button"
            className="ml-auto flex items-center gap-1 rounded border border-ink-200 px-2 py-0.5 text-xs text-ink-500 transition hover:text-ink-700"
            // A browser may refuse this — no permission, or no clipboard at all — and an
            // unhandled rejection is not how a reader should find that out.
            onClick={() => {
              const say = (state: "done" | "failed") => {
                setCopied(state);
                setTimeout(() => setCopied("no"), 2000);
              };
              navigator.clipboard?.writeText(previewOutput).then(
                () => say("done"),
                () => say("failed"),
              );
            }}
            title="Copy the preview to the clipboard"
          >
            <Copy size={12} />{" "}
            {copied === "failed" ? "Could not copy" : copied === "done" ? "Copied" : "Copy"}
          </button>
        )}
      </div>

      {previewError && (
        <div className="flex-shrink-0 border-b border-warn-100 bg-warn-100 px-3 py-2">
          <p className="font-mono text-xs text-warn-700">{previewError}</p>
        </div>
      )}

      {failedCount > 0 && (
        <div className="flex-shrink-0 border-b border-danger-100 bg-danger-50 px-3 py-2">
          <p className="mb-1 text-xs font-semibold text-danger-700">
            {failedCount === 1
              ? "1 rule could not be applied, so no document is produced:"
              : `${failedCount} rules could not be applied, so no document is produced:`}
          </p>
          <ul className="space-y-0.5">
            {Object.entries(ruleErrors).map(([target, reason]) => (
              <li key={target} className="font-mono text-[11px] text-danger-700">
                {target}: {reason}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="flex-1 overflow-auto">
        {previewOutput === null ? (
          <div className="flex h-full items-center justify-center px-4 text-center">
            <p className="text-xs text-ink-500">
              {sourceSample.trim()
                ? "Add a rule to see the document it produces."
                : "Paste a sample document on the left to see the preview."}
            </p>
          </div>
        ) : (
          <HighlightedDocument
            text={previewOutput}
            format={rules.targetFormat}
            className="px-3 py-2 text-xs leading-5 text-ink-700"
          />
        )}
      </div>
    </div>
  );
}
