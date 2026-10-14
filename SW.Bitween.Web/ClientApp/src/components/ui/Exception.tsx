import { summarizeException } from "../../lib/exceptionSummary";
import { TriangleAlert } from "lucide-react";
import { CopyButton } from "./CopyButton";

/**
 * A failure, as an operator needs it: the message in plain words first, and the .NET trace (with
 * the server's source paths) folded away behind "Technical details", to copy into a bug report.
 */
export function ExceptionBlock({ text, title = "What went wrong" }: { text: string; title?: string }) {
  const { summary, kind, hasDetail } = summarizeException(text);
  return (
    <div className="rounded-lg bg-danger-50 px-3 py-2.5">
      <div className="mb-1 flex items-center justify-between gap-2">
        <p className="text-[11px] font-medium tracking-wide text-danger-700 uppercase">{title}</p>
        <CopyButton value={text} label="exception" />
      </div>
      <p className="text-[13px] leading-relaxed break-words text-danger-800">{summary}</p>
      {kind && <p className="mt-0.5 font-mono text-[11px] text-danger-700">{kind}</p>}
      {hasDetail && (
        <details className="mt-2">
          <summary className="cursor-pointer text-[12px] font-medium text-danger-700">Technical details</summary>
          <pre className="mt-1.5 max-h-60 overflow-auto font-mono text-[11px] leading-relaxed whitespace-pre-wrap break-all text-danger-800">
            {text}
          </pre>
        </details>
      )}
    </div>
  );
}

/**
 * One line of a failure for a list or a card; the whole text is on hover. Pass `icon` wherever the
 * line is colored as a problem, so it never reads as bare red text (red is also the brand).
 */
export function ExceptionLine({ text, className = "", icon = false }: { text: string; className?: string; icon?: boolean }) {
  const { summary } = summarizeException(text);
  if (!icon)
    return (
      <span className={`block min-w-0 truncate ${className}`} title={text}>
        {summary}
      </span>
    );
  return (
    <span className={`flex min-w-0 items-center gap-1 ${className}`} title={text}>
      <TriangleAlert className="size-3.5 shrink-0" aria-hidden />
      <span className="min-w-0 truncate">{summary}</span>
    </span>
  );
}
