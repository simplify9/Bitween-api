import { useMemo } from "react";
import { colourDocument } from "../../lib/documentHighlight";

/**
 * A JSON or XML document, coloured, for reading.
 *
 * Read-only on purpose. Beautifying puts a document on separate lines; colour is what
 * lets an eye land on the right one, and the places that pays for itself are the ones
 * nobody types into - the mapped output, and the payload someone opens to work out why
 * an exchange failed.
 */
export function HighlightedDocument({
  text,
  format,
  onInk = false,
  className = "",
}: {
  text: string;
  /**
   * The declared format, when something declares one. Left out, it is sniffed; passed
   * as `null`, the document is shown plain.
   */
  format?: string | null;
  /** Set on a dark ground, where the same token roles need lifting to stay legible. */
  onInk?: boolean;
  className?: string;
}) {
  const coloured = useMemo(() => colourDocument(text, format), [text, format]);

  const shared =
    `font-mono whitespace-pre-wrap ${onInk ? "doc-hl-dark" : "doc-hl"} ${className}`;

  // Plain text when there is nothing to colour, so an unrecognised document still reads.
  if (coloured === null) return <pre className={shared}>{text}</pre>;

  // Safe because `colourDocument` escapes everything that came from the document; the
  // only tags in here are the spans it added.
  return <pre className={shared} dangerouslySetInnerHTML={{ __html: coloured }} />;
}
