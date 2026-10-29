import { describe, expect, it } from "vitest";
import { payloadProblem } from "../payloadCheck";

describe("payloadProblem", () => {
  it("says why JSON or XML doesn't parse, and passes what does", () => {
    expect(payloadProblem('{"a": [', "Json")).toMatch(/^This isn't valid JSON: /);
    expect(payloadProblem('{"a": [1]}', "Json")).toBeNull();
    expect(payloadProblem("<order><id>1</order>", "Xml")).toMatch(/^This isn't valid XML: /);
    expect(payloadProblem("<order><id>1</id></order>", "Xml")).toBeNull();
  });

  it("takes other formats, and an unknown one, as they come", () => {
    expect(payloadProblem("a,b\n1,2", "Csv")).toBeNull();
    expect(payloadProblem("{not json", null)).toBeNull();
    expect(payloadProblem("   ", "Json")).toBeNull();
  });
});
