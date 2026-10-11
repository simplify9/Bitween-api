import { describe, it, expect } from "vitest";
import { colourDocument, grammarFor } from "../documentHighlight";

/**
 * Colouring a document for reading.
 *
 * The escaping tests matter most: this is the one place in the app that turns a
 * partner's bytes into HTML, and the payload it renders arrived from outside.
 */
describe("choosing a grammar", () => {
  it("believes a declared format", () => {
    expect(grammarFor("{}", "json")).toBe("json");
    expect(grammarFor("<a/>", "xml")).toBe("xml");
  });

  it("reads the first character when nothing declared one", () => {
    // An exchange payload arrives as bytes and a filename, with no format beside it.
    expect(grammarFor('  {"a":1}')).toBe("json");
    expect(grammarFor("\n[1,2]")).toBe("json");
    expect(grammarFor("  <order/>")).toBe("xml");
  });

  it("colours nothing when the caller passes null", () => {
    // How the exchange drawer's Raw view asks for the bytes as they arrived. Different
    // from leaving the format out, which means sniff for one.
    expect(grammarFor('{"a":1}', null)).toBeNull();
    expect(colourDocument("<order/>", null)).toBeNull();
  });

  it("gives up rather than guessing", () => {
    // A CSV, a fixed-width record, a stack trace. Colouring one of those with a
    // grammar it does not follow produces confident nonsense.
    expect(grammarFor("ref,qty\nA1,2")).toBeNull();
    expect(grammarFor("")).toBeNull();
    expect(grammarFor("   ")).toBeNull();
  });

  it("prefers a declared format over how the text looks", () => {
    // A JSON sample pasted into a box the mapping calls XML should look wrong, which
    // is exactly the mistake worth catching before the mapping runs.
    expect(grammarFor('{"a":1}', "xml")).toBe("xml");
  });
});

describe("colouring a document", () => {
  it("wraps the parts of an XML document in their own spans", () => {
    const html = colourDocument('<order ref="A1"><qty>2</qty></order>', "xml")!;

    expect(html).toContain("hljs-name");
    expect(html).toContain("hljs-attr");
    expect(html).toContain("hljs-string");
  });

  it("wraps the parts of a JSON document in their own spans", () => {
    const html = colourDocument('{"ref":"A1","qty":2,"ok":true}', "json")!;

    expect(html).toContain("hljs-attr");
    expect(html).toContain("hljs-string");
    expect(html).toContain("hljs-number");
    expect(html).toContain("hljs-literal");
  });

  it("escapes everything that came from the document", () => {
    // The whole safety argument in one test. A payload carrying markup has to come
    // back as characters - the only real tags in the output are the spans we added.
    const html = colourDocument("<x><script>alert(1)</script></x>", "xml")!;

    expect(html).not.toContain("<script");
    expect(html).toContain("&lt;");
    expect(html).toContain("script");

    const json = colourDocument('{"note":"<img src=x onerror=alert(1)>"}', "json")!;
    expect(json).not.toContain("<img");
    expect(json).toContain("&lt;img");
  });

  it("escapes an ampersand rather than leaving a live entity", () => {
    const html = colourDocument("<a>Ben &amp; Co</a>", "xml")!;

    // `&amp;` in the document must reach the page as the five characters it is, not
    // as an ampersand that a later reader mistakes for the real thing.
    expect(html).toContain("&amp;amp;");
  });

  it("returns nothing for a document it has no grammar for", () => {
    expect(colourDocument("ref,qty\nA1,2")).toBeNull();
    expect(colourDocument("", "json")).toBe("");
  });

  it("colours a document that is not quite valid rather than giving up", () => {
    // Samples are pasted half-finished all the time, and losing the colour at the
    // moment the document is hardest to read is the wrong way round.
    const html = colourDocument('{"ref": "A1", "qty":', "json");

    expect(html).not.toBeNull();
    expect(html).toContain("hljs-attr");
  });
});
