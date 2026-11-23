import { describe, expect, it } from "vitest";
import type { ExchangeStateFill } from "../../api/types";
import { describeStateFill } from "../exchangeStateFill";

const fill = (over: Partial<ExchangeStateFill>): ExchangeStateFill => ({
  phase: "Filling",
  percent: 0,
  estimatedFinishOn: null,
  quietHourUtc: 0,
  ...over,
});
const inMinutes = (m: number) => new Date(Date.now() + m * 60_000).toISOString();

describe("the exchange status fill, in words", () => {
  it("gives the share done and, once there is a pace, the time left", () => {
    expect(describeStateFill(fill({ percent: 12 }))).toBe("12% done");
    expect(describeStateFill(fill({ percent: 45, estimatedFinishOn: inMinutes(40) }))).toBe("45% done, about 40m left");
    expect(describeStateFill(fill({ percent: 98, estimatedFinishOn: inMinutes(0.5) }))).toBe("98% done, under a minute left");
    expect(describeStateFill(fill({ percent: 98, estimatedFinishOn: inMinutes(-5) }))).toBe("98% done, finishing soon");
  });

  it("names each later step", () => {
    expect(describeStateFill(fill({ phase: "NotStarted" }))).toBe("starting within a minute");
    expect(describeStateFill(fill({ phase: "BuildingIndex" }))).toBe("building the index");
    expect(describeStateFill(fill({ phase: "WaitingForQuietHour", quietHourUtc: 23 }))).toBe(
      "waiting until 23:00 UTC to build the index",
    );
    expect(describeStateFill(fill({ phase: "Rechecking" }))).toBe("checking the exchanges that ran meanwhile");
    expect(describeStateFill(fill({ phase: "Rechecking", estimatedFinishOn: inMinutes(40) }))).toBe(
      "waiting for older servers to stop, about 40m left",
    );
    expect(describeStateFill(fill({ phase: "Done", percent: 100 }))).toBe("done");
  });
});
