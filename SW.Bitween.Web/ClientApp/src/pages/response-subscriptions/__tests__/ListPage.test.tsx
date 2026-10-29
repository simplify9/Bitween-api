import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { empty, page, recorded } from "../../../__tests__/support/listPage";
import { raw } from "../../subscriptions/__tests__/subscriptionBackend";

describe("the response subscriptions list", () => {
  it("lists response subscriptions and searches them on the server", async () => {
    const subscriptions = recorded("/subscriptions", page([raw({ id: 6, name: "Carrier reply", type: "Response" })]));
    const { user } = renderApp("/response-subscriptions", {
      handlers: [subscriptions.handler, ...empty("/documents", "/partners", "/workgroups", "/retrypolicies")],
    });

    expect(await screen.findByRole("row", { name: /Carrier reply/ })).toBeVisible();
    await user.type(screen.getByRole("searchbox"), "Carrier");
    await expect.poll(() => subscriptions.asked.some((url) => url.includes("Carrier"))).toBe(true);
  });
});
