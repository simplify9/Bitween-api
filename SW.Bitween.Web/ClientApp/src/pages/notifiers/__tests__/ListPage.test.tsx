import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { empty, none, page, recorded } from "../../../__tests__/support/listPage";

describe("the notifiers list", () => {
  it("lists notifiers and searches them on the server", async () => {
    const notifiers = recorded(
      "/notifiers",
      page([
        {
          id: 3,
          name: "Ops on failure",
          inactive: false,
          handlerId: null,
          runOnSuccessfulResult: false,
          runOnBadResult: true,
          runOnFailedResult: true,
        },
      ]),
    );
    const { user } = renderApp("/notifiers", {
      handlers: [notifiers.handler, ...empty("/subscriptions"), ...none("/adapters/Catalog")],
    });

    expect(await screen.findByRole("row", { name: /Ops on failure/ })).toBeVisible();
    await user.type(screen.getByRole("searchbox"), "Ops");
    await expect.poll(() => notifiers.asked.some((url) => url.includes("Ops"))).toBe(true);
  });
});
