import { describe, expect, it } from "vitest";
import { returnPath, safeReturn } from "../responseDetour";

describe("the way back from creating a response subscription", () => {
  it("only goes somewhere inside the app", () => {
    expect(safeReturn("/subscriptions/10?stage=response")).toBe("/subscriptions/10?stage=response");
    // `?return=` is a link anyone could hand you.
    expect(safeReturn("https://evil.example/")).toBeNull();
    expect(safeReturn("//evil.example/")).toBeNull();
    expect(safeReturn("/\\evil.example/")).toBeNull();
    expect(safeReturn(null)).toBeNull();
  });

  it("carries the new response subscription, whatever the page's own query", () => {
    expect(returnPath("/scheduled-jobs/new", 7)).toBe("/scheduled-jobs/new?pickedResponse=7");
    expect(returnPath("/subscriptions/10?stage=response&draftKept=1", 7)).toBe(
      "/subscriptions/10?stage=response&draftKept=1&pickedResponse=7",
    );
    expect(returnPath("/scheduled-jobs/new?draftKept=1", null)).toBe("/scheduled-jobs/new?draftKept=1");
  });
});
