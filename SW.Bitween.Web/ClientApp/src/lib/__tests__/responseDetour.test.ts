import { describe, expect, it } from "vitest";
import { feederOf, newResponsePath, returnPath, safeReturn } from "../responseDetour";

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

describe("the subscription a response subscription is being made for", () => {
  it("travels to the create page when it is saved, so its paths can be offered", () => {
    const path = newResponsePath("/subscriptions/10?stage=response&draftKept=1", 10);
    expect(path).toBe(
      "/response-subscriptions/new?return=%2Fsubscriptions%2F10%3Fstage%3Dresponse%26draftKept%3D1&feeder=10",
    );
    expect(feederOf(new URLSearchParams(path.split("?")[1]))).toBe(10);
  });

  it("is absent from a page that isn't saved yet, and anything else reads as none", () => {
    expect(newResponsePath("/scheduled-jobs/new")).toBe("/response-subscriptions/new?return=%2Fscheduled-jobs%2Fnew");
    expect(feederOf(new URLSearchParams("return=%2Fx"))).toBeNull();
    expect(feederOf(new URLSearchParams("feeder=abc"))).toBeNull();
    expect(feederOf(new URLSearchParams("feeder=-3"))).toBeNull();
  });
});
