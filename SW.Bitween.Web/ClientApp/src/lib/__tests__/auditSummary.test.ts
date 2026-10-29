import { describe, expect, it } from "vitest";
import type { TrailEntry } from "../../api";
import { actorName, collapseMachineRuns, isMachineWrite, isRun, summarizeChange } from "../auditSummary";

const entry = (over: Partial<TrailEntry>): TrailEntry => ({
  id: Math.random().toString(36),
  on: "2026-09-20T10:00:00Z",
  action: "Modified",
  by: "#scheduled-job",
  entityName: "Subscription",
  entityKey: "16",
  correlationId: "c",
  changes: [{ property: "ReceiveOn", old: null, new: "2026-09-20T11:00:00Z" }],
  ...over,
});

describe("audit summaries", () => {
  it("names at most three chosen fields of a creation, leaving blanks and the system's own out", () => {
    const summary = summarizeChange("Subscription", "Added", [
      { property: "Id", old: null, new: 16 },
      { property: "AggregateOn", old: null, new: null },
      { property: "IsRunning", old: null, new: false },
      { property: "ReceiveOn", old: null, new: "2026-09-20T11:00:00Z" },
      { property: "Temporary", old: null, new: true },
      { property: "HandlerId", old: null, new: "http" },
      { property: "Name", old: null, new: "Push orders" },
      { property: "Description", old: null, new: "To the ERP" },
    ]);
    expect(summary.shown).toEqual(["Name", "Delivery adapter", "Temporary"]);
    expect(summary.more).toBe(1);
  });

  it("names a machine-kept field when it is all that changed", () => {
    const summary = summarizeChange("Subscription", "Modified", entry({}).changes);
    expect(summary.shown).toEqual(["Next run"]);
  });

  it("tells machines from members", () => {
    expect(isMachineWrite({ by: "#scheduled-job" })).toBe(true);
    expect(isMachineWrite({ by: "System" })).toBe(true);
    // A member whose account is gone is still a person.
    expect(isMachineWrite({ by: "#42" })).toBe(false);
    expect(isMachineWrite({ by: "Ada", byUserId: "7" })).toBe(false);
    expect(actorName({ by: "#scheduled-job" })).toBe("Scheduler");
    expect(actorName({ by: "#bus-consumer" })).toBe("Bus consumer");
    expect(actorName({ by: "#42" })).toBe("#42");
  });

  it("folds back-to-back identical machine writes, and nothing else", () => {
    const rows = collapseMachineRuns([
      entry({}),
      entry({}),
      entry({}),
      entry({ by: "Ada", byUserId: "7" }),
      entry({}),
      entry({ entityKey: "17" }),
    ]);
    expect(rows.map((r) => (isRun(r) ? r.entries.length : 1))).toEqual([3, 1, 1, 1]);
  });
});
