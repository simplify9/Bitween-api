import { describe, expect, it } from "vitest";
import { notificationSlot, notificationsIncomplete, withPickedChannel } from "../notificationChannels";

const row = (channelId: number) => ({ channelId, onFailure: true, onBadResult: false, onSuccess: false, onQuiet: false });

describe("a channel made from a notifications row", () => {
  it("lands in the row it was made from", () => {
    expect(withPickedChannel([row(1), row(0)], notificationSlot(1), 9)).toEqual([row(1), row(9)]);
  });

  it("gets a row of its own when that row is gone, or the trip wasn't from a row", () => {
    expect(withPickedChannel([row(1)], notificationSlot(4), 9)).toEqual([row(1), row(9)]);
    expect(withPickedChannel([], "policy", 9)).toEqual([row(9)]);
  });
});

describe("a notifications list the server would refuse", () => {
  it("is one with a row missing its channel or every outcome", () => {
    expect(notificationsIncomplete([row(1)])).toBe(false);
    expect(notificationsIncomplete([row(0)])).toBe(true);
    expect(notificationsIncomplete([{ ...row(1), onFailure: false }])).toBe(true);
    // Going quiet is not about any one exchange, so it can be a row's only tick.
    expect(notificationsIncomplete([{ ...row(1), onFailure: false, onQuiet: true }])).toBe(false);
  });
});
