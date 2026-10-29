import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { empty, page, recorded } from "../../../__tests__/support/listPage";

describe("the retry policies list", () => {
  it("lists retry policies and searches them on the server", async () => {
    const policies = recorded(
      "/retrypolicies",
      page([{ id: 4, name: "Carrier backoff", groupCount: 2, usedByCount: 0 }]),
    );
    const { user } = renderApp("/retry-policies", { handlers: [policies.handler, ...empty("/subscriptions")] });

    expect(await screen.findByRole("row", { name: /Carrier backoff/ })).toBeVisible();
    await user.type(screen.getByRole("searchbox"), "backoff");
    await expect.poll(() => policies.asked.some((url) => url.includes("backoff"))).toBe(true);
  });
});
