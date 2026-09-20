import { describe, expect, it } from "vitest";
import type { ExchangeFileRef, ExchangeRow, ExchangeStatus } from "../../../api";
import { journeyStages } from "../shared";

/**
 * A stage's name is a claim about what happened to the document. The cards used to be named
 * "Mapped" and "Handled" whatever state they were in, so a Processing exchange read as though
 * both stages were behind it — "Mapped" sitting beside a Running badge.
 */
const A_FILE = { name: "doc.json", size: 12 } as ExchangeFileRef;

/** Only the three fields `journeyStages` reads; the rest of the row does not reach it. */
const row = (status: ExchangeStatus, over: { mapperSkipped?: boolean; mapped?: boolean } = {}) =>
  ({
    status,
    mapperSkipped: over.mapperSkipped ?? false,
    files: { input: A_FILE, mapped: over.mapped ? A_FILE : null, handled: null },
  }) as ExchangeRow;

/** The stage keys are stable; only the words shown to a reader move. */
const labels = (x: ExchangeRow) => journeyStages(x).map((s) => `${s.label}:${s.state}`);

describe("journeyStages labels", () => {
  it("names a stage in the past tense only once it is behind us", () => {
    expect(labels(row("success", { mapped: true }))).toEqual([
      "Received:done",
      "Mapped:done",
      "Handled:done",
    ]);
  });

  it("does not claim a Processing exchange was mapped or handled", () => {
    expect(labels(row("processing"))).toEqual([
      "Received:done",
      "Mapping:running",
      "Handling:running",
    ]);
  });

  it("does not claim a skipped mapping stage was mapped", () => {
    // The note already says "No mapper configured"; the title must not contradict it.
    expect(labels(row("processing", { mapperSkipped: true }))).toEqual([
      "Received:done",
      "Mapping:skipped",
      "Handling:running",
    ]);
  });

  it("does not claim a handler was reached when mapping failed first", () => {
    expect(labels(row("failed"))).toEqual([
      "Received:done",
      "Mapped:failed",
      "Handling:notReached",
    ]);
  });

  it("keeps the past tense for a delivery the response rejected", () => {
    // badResponse means it was handled — the far end just did not like it.
    expect(labels(row("badResponse", { mapped: true }))).toEqual([
      "Received:done",
      "Mapped:done",
      "Handled:bad",
    ]);
  });
});
