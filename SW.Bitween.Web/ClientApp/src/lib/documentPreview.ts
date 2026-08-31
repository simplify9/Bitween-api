/**
 * Turning an exchange's payload into something readable.
 *
 * Integrations send minified JSON and single-line XML, and the drawer used to
 * print that verbatim: one 4KB line, which is both unreadable and — because the
 * drawer lives in a table cell that sizes to its content — wide enough to
 * stretch every other row on the page.
 */

/** Two spaces per level, matching the mapper's editors. */
const INDENT = "  ";

/**
 * `text` laid out over multiple lines, or `null` when it isn't a format we can
 * lay out — in which case the caller shows it as it came.
 *
 * Never throws and never guesses: anything that doesn't parse cleanly comes
 * back as `null` rather than as a mangled approximation of itself, because a
 * payload people are reading to diagnose a failure has to be the payload.
 */
export function formatDocument(text: string): string | null {
  const trimmed = text.trim();
  if (trimmed === "") return null;

  const first = trimmed[0];
  if (first === "{" || first === "[") return formatJson(trimmed);
  if (first === "<") return formatXml(trimmed);
  return null;
}

function formatJson(text: string): string | null {
  try {
    const formatted = JSON.stringify(JSON.parse(text), null, 2);
    // A scalar or an already-formatted document gains nothing from the toggle.
    return formatted === undefined || formatted === text ? null : formatted;
  } catch {
    return null;
  }
}

function formatXml(text: string): string | null {
  // A newline landing inside either of these would change the data rather than
  // its shape, and a payload someone is reading to diagnose a failure has to
  // survive being displayed. Neither is common enough to be worth handling.
  if (text.includes("<![CDATA[") || text.includes("<!--")) return null;

  // Split only where one tag butts *literally* against the next, with nothing
  // between them. Whitespace sitting between two tags is a text node — in mixed
  // content it is character data the reader came here to see — so it is left
  // exactly where it is, even though that means those tags don't get reflowed.
  // Indenting still inserts whitespace, but only at boundaries that held none.
  const lines = text.replace(/></g, ">\n<").split("\n");
  if (lines.length === 1) return null;

  let depth = 0;
  return lines
    .map((line) => {
      if (line.startsWith("</")) depth = Math.max(0, depth - 1);
      const indented = INDENT.repeat(depth) + line;
      // Only a line that is exactly one opening tag indents what follows it: a
      // line carrying its own closing tag, a self-closing tag and a declaration
      // are all already finished.
      if (/^<[^!?/][^>]*>$/.test(line) && !line.endsWith("/>")) depth += 1;
      return indented;
    })
    .join("\n");
}
