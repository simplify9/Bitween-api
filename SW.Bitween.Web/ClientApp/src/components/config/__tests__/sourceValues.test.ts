import { describe, expect, it } from "vitest";
import type { InformationType, SubscriptionInfo } from "../../../api";
import { sourceKindsFor } from "../../../lib/nativeMapper/types";
import { sourceDocument, feederSubscriptions, lackingPath } from "../sourceValues";

/**
 * A subscription fed by deliveries is offered the paths in each feeder's last received document,
 * and warned about a path one of them lacked.
 */
describe("sourceDocument", () => {
  const source = sourceDocument(true, [
    { id: 1, name: "Send orders", xchangeId: "a", paths: [{ path: "order.number", example: "SO-1" }] },
    {
      id: 2,
      name: "Send returns",
      xchangeId: "b",
      paths: [
        { path: "order.number", example: "RT-9" },
        { path: "order.reason", example: "damaged" },
      ],
    },
  ]);

  it("offers every path any feeder's last document had, once, with the first example found and whose it is", () => {
    expect(source.paths).toEqual([
      { path: "order.number", example: "SO-1", from: "Send orders" },
      { path: "order.reason", example: "damaged", from: "Send returns" },
    ]);
  });

  it("names the feeders whose last document lacked a path, matching it exactly", () => {
    expect(lackingPath(source, "order.number")).toEqual([]);
    expect(lackingPath(source, "order.reason").map((f) => f.name)).toEqual(["Send orders"]);
    expect(lackingPath(source, "Order.Number").map((f) => f.name)).toEqual(["Send orders", "Send returns"]);
  });

  it("warns about nothing while no feeder has run", () => {
    expect(lackingPath(sourceDocument(true, []), "order.number")).toEqual([]);
  });
});

describe("sourceKindsFor", () => {
  it("offers the original document only on a subscription fed by deliveries", () => {
    expect(sourceKindsFor(false).map((k) => k.value)).not.toContain("source");
    expect(sourceKindsFor(true, true).map((k) => k.value)).toEqual([
      "path",
      "fixed",
      "partner",
      "global",
      "count",
      "source",
    ]);
  });
});

const sub = (s: Partial<SubscriptionInfo>): SubscriptionInfo =>
  ({ responseMessageTypeName: null, responseSubscriptionId: null, ...s }) as SubscriptionInfo;

/**
 * Who feeds a subscription: whatever hands a response subscription its response, or whatever
 * publishes its response as the message a bus gateway reads.
 */
describe("feederSubscriptions", () => {
  const types = [{ id: 7, busMessageTypeName: "OrderAcked" }] as InformationType[];
  const subscriptions = [
    sub({ id: 1, informationTypeId: 10, responseSubscriptionId: 50 }),
    sub({ id: 2, informationTypeId: 11, responseMessageTypeName: "orderacked" }),
    sub({ id: 3, informationTypeId: 12, responseMessageTypeName: "OrderShipped" }),
    // The gateway's own subscription publishing the message it reads feeds nothing new.
    sub({ id: 60, informationTypeId: 7, responseMessageTypeName: "OrderAcked" }),
  ];

  it("finds a response subscription's feeders by what they hand their response to", () => {
    expect(feederSubscriptions({ type: "Response", id: 50 }, subscriptions, types)).toEqual([1]);
  });

  it("finds a new response subscription fed by nothing", () => {
    expect(feederSubscriptions({ type: "Response", id: null }, subscriptions, types)).toEqual([]);
  });

  it("finds a bus gateway subscription's by the message they publish, ignoring case as the bus does", () => {
    expect(
      feederSubscriptions({ type: "BusGateway", id: 60, informationTypeId: 7 }, subscriptions, types),
    ).toEqual([2]);
  });

  it("finds none for any other type", () => {
    expect(feederSubscriptions({ type: "Internal", id: 50 }, subscriptions, types)).toEqual([]);
  });
});
