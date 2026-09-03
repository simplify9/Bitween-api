import { describe, it, expect } from 'vitest';
import { StringStream } from '@codemirror/language';
import { scribanStreamParser } from '../scribanLanguage';

interface Tok {
  text: string;
  style: string | null;
}

// Drive the StreamLanguage tokenizer the same way CodeMirror does: one StringStream
// per line, sharing a single state object so `inTag` carries across lines.
function tokenize(src: string): Tok[] {
  const state = scribanStreamParser.startState!(2);
  const out: Tok[] = [];
  for (const line of src.split('\n')) {
    const stream = new StringStream(line, 2, 2);
    while (!stream.eol()) {
      stream.start = stream.pos;
      const style = scribanStreamParser.token!(stream, state);
      if (stream.pos === stream.start) stream.pos++; // guard: never stall
      out.push({ text: stream.string.slice(stream.start, stream.pos), style: style ?? null });
    }
  }
  return out;
}

const styleOf = (toks: Tok[], text: string) => toks.find((t) => t.text === text)?.style;

describe('scriban tokenizer', () => {
  it('marks braces and a variable path inside a tag', () => {
    const toks = tokenize('{{ user.name }}');
    expect(styleOf(toks, '{{')).toBe('brace');
    expect(styleOf(toks, 'user.name')).toBe('variable');
    expect(styleOf(toks, '}}')).toBe('brace');
  });

  it('handles trim delimiters and control keywords', () => {
    const toks = tokenize('{{- for x in items -}}');
    expect(styleOf(toks, '{{-')).toBe('brace');
    expect(styleOf(toks, 'for')).toBe('keyword');
    expect(styleOf(toks, 'in')).toBe('keyword');
    expect(styleOf(toks, 'items')).toBe('variable');
    expect(styleOf(toks, '-}}')).toBe('brace');
  });

  it('treats # to the end of the tag as a comment', () => {
    const toks = tokenize('{{ # a comment }}');
    const comment = toks.find((t) => t.style === 'comment');
    expect(comment).toBeDefined();
    expect(comment!.text).toContain('# a comment');
    // the closing braces are still recognised after the comment
    expect(styleOf(toks, '}}')).toBe('brace');
  });

  it('recognises quoted strings and operators', () => {
    const toks = tokenize('{{ x = "hello world" }}');
    expect(styleOf(toks, '"hello world"')).toBe('string');
    expect(styleOf(toks, '=')).toBe('operator');
  });

  it('keeps tag state across multiple lines', () => {
    const toks = tokenize('{{ for x\nin y }}');
    expect(styleOf(toks, 'for')).toBe('keyword'); // line 1, still open
    expect(styleOf(toks, 'in')).toBe('keyword'); // line 2, tag continued
    expect(styleOf(toks, '}}')).toBe('brace'); // line 2, tag closed
  });

  it('does not style plain text outside tags', () => {
    const toks = tokenize('plain text {{ v }}');
    expect(styleOf(toks, 'plain text ')).toBeNull();
    expect(styleOf(toks, 'v')).toBe('variable');
  });
});
