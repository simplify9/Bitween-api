// Minimal Scriban/mustache highlighting for CodeMirror 6.
// Highlights `{{ ... }}` regions (delimiters, keywords, comments, values) without
// pulling in a full grammar. Colors mirror the cheat-sheet under the editor:
// brand crimson for values/braces, warn for control keywords, ink for comments.
import { StreamLanguage, HighlightStyle, syntaxHighlighting } from '@codemirror/language';
import type { StreamParser } from '@codemirror/language';
import { Prec } from '@codemirror/state';
import { tags as t } from '@lezer/highlight';

const KEYWORDS = /^(for|end|if|else|in|while|func|ret|break|continue|capture|case|when|do|with|wrap|tablerow)\b/;

// Exported for unit testing (see __tests__/scribanLanguage.test.ts).
export const scribanStreamParser: StreamParser<{ inTag: boolean }> = {
  startState: () => ({ inTag: false }),
  token(stream, state) {
    if (!state.inTag) {
      if (stream.match(/^\{\{-?/)) {
        state.inTag = true;
        return 'brace';
      }
      // Plain text between tags: advance to the next `{{` (always consuming ≥1 char).
      while (!stream.eol() && !stream.match(/^\{\{/, false)) stream.next();
      return null;
    }
    // Inside a `{{ ... }}` tag.
    if (stream.match(/^-?\}\}/)) {
      state.inTag = false;
      return 'brace';
    }
    if (stream.peek() === '#') {
      while (!stream.eol() && !stream.match(/^-?\}\}/, false)) stream.next();
      return 'comment';
    }
    if (stream.match(KEYWORDS)) return 'keyword';
    if (stream.match(/^"(?:[^"\\]|\\.)*"?/)) return 'string';
    if (stream.match(/^'(?:[^'\\]|\\.)*'?/)) return 'string';
    if (stream.match(/^\d+(?:\.\d+)?/)) return 'number';
    if (stream.match(/^[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*/)) return 'variable';
    if (stream.match(/^[-+*/%=!<>|&.?:,()[\]]+/)) return 'operator';
    stream.next();
    return null;
  },
  tokenTable: {
    brace: t.brace,
    keyword: t.keyword,
    comment: t.lineComment,
    string: t.string,
    number: t.number,
    variable: t.variableName,
    operator: t.operator,
  },
};

const scribanParser = StreamLanguage.define(scribanStreamParser);

// Colors come from the design tokens rather than literals so a re-branded tenant
// (Theme.PrimaryColor rewrites every --color-crimson-*) keeps the editor and the
// cheat-sheet under it in step — they are a matched pair, the sheet is what tells
// the user what each color means.
const scribanHighlight = HighlightStyle.define([
  { tag: t.brace, color: 'var(--color-crimson-600)', fontWeight: 'bold' }, // {{ }}
  { tag: t.variableName, color: 'var(--color-crimson-600)' }, // var.path
  { tag: t.keyword, color: 'var(--color-warn-700)', fontWeight: 'bold' }, // for / if / end
  { tag: t.lineComment, color: 'var(--color-ink-400)', fontStyle: 'italic' },
  { tag: t.string, color: 'var(--color-ok-600)' },
  { tag: t.number, color: 'var(--color-ok-800)' },
  { tag: t.operator, color: 'var(--color-ink-500)' },
]);

// Prec.highest so our colors win over basic-setup's default highlight style.
export const scribanLanguage = [scribanParser, Prec.highest(syntaxHighlighting(scribanHighlight))];
