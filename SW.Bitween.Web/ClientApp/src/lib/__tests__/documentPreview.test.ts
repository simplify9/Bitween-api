import { describe, expect, it } from "vitest";
import { formatDocument } from "../documentPreview";

describe("formatDocument", () => {
  it("lays a minified object out over lines", () => {
    expect(formatDocument('{"Shipment":{"Uid":"STF.365673","Flags":2}}')).toBe(
      '{\n  "Shipment": {\n    "Uid": "STF.365673",\n    "Flags": 2\n  }\n}',
    );
  });

  it("indents nested elements", () => {
    expect(formatDocument("<a><b>1</b><c/></a>")).toBe("<a>\n  <b>1</b>\n  <c/>\n</a>");
  });

  it("leaves text sitting between elements where it is", () => {
    expect(formatDocument("<p>hello <b>there</b></p>")).toBe("<p>hello <b>there</b>\n</p>");
  });

  /**
   * The property that matters more than tidy output: reflowing a payload must
   * never lose or move a character of it, however odd the markup is. Malformed
   * XML comes back strangely indented and completely intact.
   */
  it.each(['<a><b>1</b><c/></a>', '<a><b></a>', '<?xml version="1.0"?><a><b/></a>'])(
    "changes nothing but whitespace between tags: %s",
    (src) => {
      expect(formatDocument(src)!.replace(/\s*\n\s*/g, "")).toBe(src);
    },
  );

  // Everything below comes back null, and the caller then shows the payload
  // exactly as it arrived.
  it("declines a payload that is neither JSON nor markup", () => {
    expect(formatDocument("UNB+UNOA:1+SENDER+RECEIVER+260831:1200+1'")).toBeNull();
  });

  it("declines malformed JSON", () => {
    expect(formatDocument('{"Uid":"STF.365673"')).toBeNull();
  });

  it("declines markup carrying CDATA, which a newline would corrupt", () => {
    expect(formatDocument("<a><![CDATA[keep > <this]]></a>")).toBeNull();
  });

  it("declines markup carrying a comment", () => {
    expect(formatDocument("<a><!-- note --><b/></a>")).toBeNull();
  });

  it("declines what is already laid out, so the toggle stays hidden", () => {
    expect(formatDocument('{\n  "Uid": "STF.365673"\n}')).toBeNull();
  });

  it("declines an empty payload", () => {
    expect(formatDocument("   ")).toBeNull();
  });
});
