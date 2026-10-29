/**
 * Colouring a JSON or XML document for reading.
 *
 * Sits beside `documentPreview.ts` and does the other half of the same job: that one
 * decides where the lines go, this one decides what colour the words are. Both are
 * pure, so both are tested without a browser.
 */

import hljs from "highlight.js/lib/core";
import jsonLanguage from "highlight.js/lib/languages/json";
import xmlLanguage from "highlight.js/lib/languages/xml";

// Only these two grammars, so the bundle carries the formats the mapper knows and
// none of the other hundred highlight.js ships with.
hljs.registerLanguage("json", jsonLanguage);
hljs.registerLanguage("xml", xmlLanguage);

/**
 * Which grammar to colour with, or null when the text is neither.
 *
 * A `format` of `null` is a caller saying not to colour this at all, which is different
 * from leaving it out - that one means nothing declared a format, so sniff for one.
 */
export function grammarFor(text: string, format?: string | null): "json" | "xml" | null {
  if (format === null) return null;
  if (format === "json" || format === "xml") return format;

  // Nothing declared one - an exchange payload arrives as bytes and a filename. The
  // first character settles it, the same way `formatDocument` decides how to lay out.
  const first = text.trimStart()[0];
  if (first === "{" || first === "[") return "json";
  if (first === "<") return "xml";
  return null;
}

/**
 * `text` as HTML with the tokens wrapped in `hljs-*` spans, or `null` when there is
 * nothing to colour - in which case the caller shows the text as it came.
 *
 * The returned HTML is escaped: every `<`, `>` and `&` in the document comes back as
 * an entity, and the only real tags are the spans this adds. That is what makes it
 * safe to render, and it is the point worth testing - this is the one place in the
 * app that turns a partner's bytes into HTML.
 */
export function colourDocument(text: string, format?: string | null): string | null {
  const grammar = grammarFor(text, format);
  if (grammar === null) return null;

  try {
    return hljs.highlight(text, { language: grammar, ignoreIllegals: true }).value;
  } catch {
    // A grammar that gives up leaves the document readable rather than blank.
    return null;
  }
}
