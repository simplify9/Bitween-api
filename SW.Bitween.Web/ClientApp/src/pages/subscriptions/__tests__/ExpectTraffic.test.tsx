import { screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";
import { backend, sub } from "./subscriptionBackend";

/**
 * Expect traffic on a subscription's overview: what a save sends, that a save touching something
 * else leaves it as it was, and the notice once the server has found it quiet. When it goes quiet
 * is the server's business.
 */

// The picker's dropdown watches its anchor's size when it closes, which jsdom cannot measure.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

const RIYADH_WORKWEEK = {
  everyMinutes: 15,
  days: ["Sun", "Mon", "Tue", "Wed", "Thu"],
  from: "08:00",
  to: "22:00",
  timeZone: "Asia/Riyadh",
};

describe("expect traffic", () => {
  it("is off until switched on, and saves what was entered", async () => {
    const { handlers, saves } = backend([sub()]);
    const { user } = renderApp("/subscriptions/10", { handlers });

    const toggle = await screen.findByRole("checkbox", { name: "Expect traffic regularly" });
    expect(toggle).not.toBeChecked();
    expect(screen.queryByRole("spinbutton", { name: /Expect traffic every/ })).not.toBeInTheDocument();

    await user.click(toggle);
    const every = screen.getByRole("spinbutton", { name: /Expect traffic every/ });
    await user.clear(every);
    await user.type(every, "15");
    const days = screen.getByRole("group", { name: "Days" });
    for (const d of ["Sun", "Mon", "Tue", "Wed", "Thu"])
      await user.click(within(days).getByRole("button", { name: d }));
    await user.type(screen.getByLabelText("From"), "08:00");
    await user.type(screen.getByLabelText("To"), "22:00");
    await user.type(screen.getByLabelText("Time zone"), "Asia/Riyadh");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].expectTraffic).toEqual(RIYADH_WORKWEEK);
    expect(saves[0]).not.toHaveProperty("quietSince");
  });

  it("survives a save that changes something else", async () => {
    const { handlers, saves } = backend([sub({ expectTraffic: RIYADH_WORKWEEK })]);
    const { user } = renderApp("/subscriptions/10", { handlers });

    await user.click(await screen.findByRole("button", { name: "Disable" }));
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Disable" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].inactive).toBe(true);
    expect(saves[0].expectTraffic).toEqual(RIYADH_WORKWEEK);
  });

  it("says when the subscription has gone quiet", async () => {
    const { handlers } = backend([sub({ expectTraffic: RIYADH_WORKWEEK, quietSince: "2026-10-08T09:00:00Z" })]);
    renderApp("/subscriptions/10", { handlers });

    expect(await screen.findByText(/Quiet since .*: no exchanges within the expected 15 minutes/)).toBeVisible();
  });

  it("saves null once switched off", async () => {
    const { handlers, saves } = backend([sub({ expectTraffic: RIYADH_WORKWEEK })]);
    const { user } = renderApp("/subscriptions/10", { handlers });

    const toggle = await screen.findByRole("checkbox", { name: "Expect traffic regularly" });
    expect(toggle).toBeChecked();
    await user.click(toggle);
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].expectTraffic).toBeNull();
  });
});
