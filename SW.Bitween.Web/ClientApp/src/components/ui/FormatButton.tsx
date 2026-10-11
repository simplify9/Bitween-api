import { useMemo } from "react";
import { WrapText } from "lucide-react";
import { formatDocument } from "../../lib/documentPreview";

/**
 * Lays out the JSON or XML in a box someone types into.
 *
 * A button rather than the Raw/Formatted toggle the exchange drawer has, because that
 * one shows a document nobody can edit. Here the text belongs to whoever typed it, so
 * reflowing it is something they do rather than a way of looking at it. Integrations
 * hand out samples as one long line, and XML arrives that way almost every time.
 *
 * It cannot be undone - a programmatic change is not on the textarea's own undo stack,
 * and a sample is deliberately not on the editor's. What makes that tolerable is how
 * little `formatDocument` does: it adds whitespace, only between two tags that had
 * nothing at all between them, and it refuses outright any document whose whitespace
 * might be content - CDATA, comments, `xml:space="preserve"`. It cannot make that
 * promise for mixed content, which XML gives it no way to recognise: two inline
 * elements written flush against each other read differently with a newline between
 * them. Data-interchange documents do not do that, and this button is for samples and
 * for a payload someone is composing by hand.
 *
 * Absent when there is nothing to gain: while the text is half-typed and unparseable,
 * and again once it is already laid out. That is `formatDocument`'s own answer, so the
 * button is never offered for a document it would refuse or leave alone.
 */
export function FormatButton({
  value,
  onChange,
  className = "",
}: {
  value: string;
  onChange: (formatted: string) => void;
  className?: string;
}) {
  const formatted = useMemo(() => formatDocument(value), [value]);

  if (formatted === null) return null;

  return (
    <button
      type="button"
      onClick={() => onChange(formatted)}
      title="Lay this document out over lines"
      className={
        "flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[11px] font-medium " +
        "text-ink-500 hover:bg-ink-100 hover:text-ink-800 " +
        className
      }
    >
      <WrapText size={12} aria-hidden /> Format
    </button>
  );
}
