import { describe, expect, it } from "vitest";
import type { SubscriptionInfo } from "../../../api";
import { buildFlowGraph } from "../model";

const subscription = (id: number, name: string, fields: Partial<SubscriptionInfo> = {}): SubscriptionInfo => ({
  id,
  name,
  type: "Internal",
  partnerIds: [],
  informationTypeId: 1,
  workGroupId: null,
  categoryId: null,
  retryPolicyId: null,
  handlerId: "smtp",
  adapterUses: [],
  responseMessageTypeName: null,
  responseSubscriptionId: null,
  partnerPropKeys: [],
  globals: [],
  ...fields,
});

describe("the flow map's model", () => {
  it("names the subscriptions it leaves off, so the page can list them", () => {
    const graph = buildFlowGraph({
      apiGateways: [],
      busGateways: [],
      informationTypes: [],
      subscriptions: [
        subscription(1, "Nightly export"),
        // Hands its response to another, so both are drawn.
        subscription(2, "Orders in", { responseSubscriptionId: 3 }),
        subscription(3, "Orders out"),
      ],
    });

    expect(graph.omitted.map((s) => s.name)).toEqual(["Nightly export"]);
  });
});
