/**
 * An exception as Bitween records it is the whole .NET text: type, message, inner exceptions and
 * every stack frame, with the server's own source paths. Shown as is, the one thing an operator
 * needs ("connection refused to 127.0.0.1:9") was a line among forty. This picks it out.
 */
export interface ExceptionSummary {
  /** The message, in plain words: "Connection refused (127.0.0.1:9)". */
  summary: string;
  /** The exception's short type name, "HttpRequestException", when the text names one. */
  kind: string | null;
  /** Whether there is more than the summary to show behind a "details" toggle. */
  hasDetail: boolean;
}

// "System.Net.Http.HttpRequestException: message" or "AdapterInvocationException: message".
const TYPED = /^\s*(?:---> )?((?:[A-Za-z_][\w`]*\.)*([A-Za-z_][\w`]*(?:Exception|Error)))(?::\s*(.*))?$/;

// Wrappers whose own message says nothing: their inner exception is the story.
const WRAPPERS = new Set(["AggregateException", "TargetInvocationException", "TypeInitializationException"]);

export function summarizeException(text: string | null | undefined): ExceptionSummary {
  const all = (text ?? "").replace(/\r\n/g, "\n").trim();
  if (!all) return { summary: "", kind: null, hasDetail: false };

  const lines = all.split("\n");
  const typed = lines
    .map((line) => TYPED.exec(line))
    .filter((m): m is RegExpExecArray => m !== null && !!m[3]?.trim());
  const pick = typed.find((m) => !WRAPPERS.has(m[2])) ?? typed[0];

  if (!pick) {
    // Not a .NET exception: an adapter's own message, or a refusal written as a sentence.
    const first = lines[0].trim();
    return { summary: first, kind: null, hasDetail: lines.length > 1 };
  }
  return {
    summary: pick[3].trim().replace(/\s*See the inner exception for details\.?$/i, ""),
    kind: pick[2],
    hasDetail: lines.length > 1 || all !== pick[3].trim(),
  };
}
