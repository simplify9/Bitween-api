import { cleanup, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { empty } from "../../../__tests__/support/listPage";

const handlers = empty("/globaladaptervaluessets", "/subscriptions");

const mainNav = async () => within(await screen.findByRole("navigation", { name: "Main" }));

describe("the sidebar", () => {
  const tallWindow = window.innerHeight;
  afterEach(() => {
    window.innerHeight = tallWindow;
  });

  it("leads with the dashboard and shows an administrator Administration from the first visit", async () => {
    renderApp("/global-values", { handlers });
    const nav = await mainNav();

    expect(nav.getAllByRole("link")[0]).toHaveAccessibleName("Dashboard");
    expect(nav.getByRole("link", { name: "Members" })).toBeVisible();
    expect(nav.getByRole("link", { name: "Settings" })).toBeVisible();
  });

  it("remembers a group someone folded", async () => {
    const { user } = renderApp("/global-values", { handlers });
    let nav = await mainNav();

    await user.click(nav.getByRole("button", { name: /Administration/ }));
    expect(nav.queryByRole("link", { name: "Members" })).not.toBeInTheDocument();

    // The next visit, as a reload would be: the folded group stays folded.
    cleanup();
    renderApp("/global-values", { handlers });
    nav = await mainNav();
    expect(nav.getByRole("button", { name: /Administration/ })).toHaveAttribute("aria-expanded", "false");
    expect(nav.queryByRole("link", { name: "Members" })).not.toBeInTheDocument();
  });

  it("starts Administration folded on a window too short for the whole list", async () => {
    window.innerHeight = 600;
    const { user } = renderApp("/global-values", { handlers });
    const nav = await mainNav();

    expect(nav.queryByRole("link", { name: "Members" })).not.toBeInTheDocument();
    await user.click(nav.getByRole("button", { name: /Administration/ }));
    expect(nav.getByRole("link", { name: "Members" })).toBeVisible();
  });

  it("does not show Administration to someone without a permission in it", async () => {
    renderApp("/global-values", { handlers, as: { permissions: ["global-values.view"] } });
    const nav = await mainNav();
    expect(nav.queryByRole("button", { name: /Administration/ })).not.toBeInTheDocument();
  });

  it("opens Administration on one of its own pages", async () => {
    renderApp("/settings", { handlers: [...handlers, ...empty("/settings")] });
    const nav = await mainNav();
    await waitFor(() => expect(nav.getByRole("link", { name: "Settings" })).toHaveAttribute("aria-current", "page"));
  });
});
