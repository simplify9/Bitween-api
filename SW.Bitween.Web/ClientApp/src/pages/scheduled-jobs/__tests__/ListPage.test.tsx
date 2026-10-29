import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { empty, none, page, recorded } from "../../../__tests__/support/listPage";
import { raw } from "../../subscriptions/__tests__/subscriptionBackend";

describe("the scheduled jobs list", () => {
  it("lists scheduled jobs and searches them on the server", async () => {
    const subscriptions = recorded(
      "/subscriptions",
      page([raw({ id: 7, name: "Nightly stock pull", type: "Receiving" })]),
    );
    const { user } = renderApp("/scheduled-jobs", {
      handlers: [
        subscriptions.handler,
        ...empty("/documents", "/partners", "/workgroups", "/retrypolicies"),
        ...none("/subscriptions/lastruns", "/subscriptions/schedulehealth"),
      ],
    });

    expect(await screen.findByRole("row", { name: /Nightly stock pull/ })).toBeVisible();
    await user.type(screen.getByRole("searchbox"), "stock");
    await expect.poll(() => subscriptions.asked.some((url) => url.includes("stock"))).toBe(true);
  });
});
