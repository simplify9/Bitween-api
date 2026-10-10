import { describe, expect, it } from "vitest";
import { namesSomething } from "../subscriptionLabels";

/**
 * Whether promoted properties can stand in for an exchange's identity. An information type can
 * promote paths a payload never filled, and those name every exchange of that type equally — so
 * the callers fall back to the id, which is at least this exchange's own.
 */
describe("namesSomething", () => {
  it("is false when there are no promoted properties at all", () => {
    expect(namesSomething(null)).toBe(false);
    expect(namesSomething({})).toBe(false);
  });

  it("is false when every promoted path resolved to nothing", () => {
    // What the backend sends for a type that promotes three paths the payload did not carry:
    // unresolved values arrive as null rather than as an empty string.
    expect(namesSomething({ merchant: null, orderRef: null, destination: null })).toBe(false);
    expect(namesSomething({ merchant: "", orderRef: "" })).toBe(false);
  });

  it("is true as soon as one carries a value", () => {
    expect(namesSomething({ merchant: "Acme" })).toBe(true);
    // Partial information is still information, so the empty siblings stay on show beside it.
    expect(namesSomething({ merchant: "Acme", orderRef: null })).toBe(true);
    expect(namesSomething({ trackingNo: "0" })).toBe(true);
  });
});
