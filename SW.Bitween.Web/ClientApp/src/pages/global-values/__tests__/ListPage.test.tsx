import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { empty, page, recorded } from "../../../__tests__/support/listPage";

describe("the global values list", () => {
  it("lists value sets and filters them by name", async () => {
    const sets = recorded(
      "/globaladaptervaluessets",
      page([
        { id: "acme", name: "Acme endpoints", values: { Url: "https://acme.test" } },
        { id: "beta", name: "Beta endpoints", values: {} },
      ]),
    );
    const { user } = renderApp("/global-values", { handlers: [sets.handler, ...empty("/subscriptions")] });

    expect(await screen.findByRole("row", { name: /Acme endpoints/ })).toBeVisible();
    await user.type(screen.getByRole("searchbox"), "Beta");
    await expect.poll(() => screen.queryByRole("row", { name: /Acme endpoints/ })).toBeNull();
    expect(screen.getByRole("row", { name: /Beta endpoints/ })).toBeVisible();
  });
});
