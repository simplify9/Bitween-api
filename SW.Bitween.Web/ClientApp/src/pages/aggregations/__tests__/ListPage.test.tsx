import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { empty, none, page, recorded } from "../../../__tests__/support/listPage";
import { raw } from "../../subscriptions/__tests__/subscriptionBackend";

describe("the aggregations list", () => {
  it("lists aggregations and searches them on the server", async () => {
    const subscriptions = recorded(
      "/subscriptions",
      page([raw({ id: 5, name: "Orders rollup", type: "Aggregation" })]),
    );
    const { user } = renderApp("/aggregations", {
      handlers: [
        subscriptions.handler,
        ...empty("/documents", "/partners", "/workgroups", "/retrypolicies"),
        ...none("/subscriptions/lastruns", "/subscriptions/schedulehealth"),
      ],
    });

    expect(await screen.findByRole("row", { name: /Orders rollup/ })).toBeVisible();
    await user.type(screen.getByRole("searchbox"), "rollup");
    await expect.poll(() => subscriptions.asked.some((url) => url.includes("rollup"))).toBe(true);
  });
});
