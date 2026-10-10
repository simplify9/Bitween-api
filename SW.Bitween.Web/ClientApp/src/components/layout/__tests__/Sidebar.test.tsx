import { screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { empty } from "../../../__tests__/support/listPage";

const handlers = empty("/globaladaptervaluessets", "/subscriptions");

describe("the sidebar", () => {
  it("leads with the dashboard and keeps Administration folded until it is wanted", async () => {
    const { user } = renderApp("/global-values", { handlers });
    const nav = within(await screen.findByRole("navigation", { name: "Main" }));

    expect(nav.getAllByRole("link")[0]).toHaveAccessibleName("Dashboard");
    expect(nav.queryByRole("link", { name: "Members" })).not.toBeInTheDocument();

    await user.click(nav.getByRole("button", { name: /Administration/ }));
    expect(nav.getByRole("link", { name: "Members" })).toBeVisible();
  });

  it("opens Administration on one of its own pages", async () => {
    renderApp("/settings", { handlers: [...handlers, ...empty("/settings")] });
    const nav = within(await screen.findByRole("navigation", { name: "Main" }));
    await waitFor(() => expect(nav.getByRole("link", { name: "Settings" })).toHaveAttribute("aria-current", "page"));
  });
});
