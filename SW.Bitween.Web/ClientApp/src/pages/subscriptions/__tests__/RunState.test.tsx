import { http, HttpResponse } from "msw";
import { screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { backend, sub } from "./subscriptionBackend";

/**
 * The running state in the header: one control for what used to be Pause and Disable, each move
 * still confirmed, and the rare actions behind "More actions".
 */

globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

/** The backend, plus every write in the order it arrived. */
function withPause(subject: ReturnType<typeof sub>) {
  const { handlers, saves } = backend([subject]);
  const writes: string[] = [];
  return {
    writes,
    saves,
    handlers: [
      http.post(apiPath(`/subscriptions/${subject.id}/pause`), () => {
        writes.push("pause");
        return HttpResponse.json(null);
      }),
      http.post(apiPath(`/subscriptions/${subject.id}`), async ({ request }) => {
        const body = (await request.json()) as Record<string, unknown>;
        saves.push(body);
        writes.push(body.inactive ? "off" : "on");
        return new HttpResponse(null, { status: 204 });
      }),
      ...handlers,
    ],
  };
}

describe("a subscription's running state", () => {
  it("says what the current state means", async () => {
    const { handlers } = withPause(sub({ pausedOn: "2026-10-01T10:00:00Z" }));
    renderApp("/subscriptions/10", { handlers });

    expect(await screen.findByRole("radio", { name: "Paused" })).toBeChecked();
    expect(screen.getByText(/Paused: keeps accepting work and holds every exchange/)).toBeVisible();
  });

  it("pauses through the confirm, and nothing before it", async () => {
    const { handlers, writes } = withPause(sub());
    const { user } = renderApp("/subscriptions/10", { handlers });

    await user.click(await screen.findByRole("radio", { name: "Paused" }));
    const dialog = within(await screen.findByRole("dialog", { name: "Pause this subscription?" }));
    expect(writes).toEqual([]);
    await user.click(dialog.getByRole("button", { name: "Pause" }));

    await waitFor(() => expect(writes).toEqual(["pause"]));
  });

  it("from Off to Paused holds first, so nothing slips through between the two writes", async () => {
    const { handlers, writes } = withPause(sub({ inactive: true }));
    const { user } = renderApp("/subscriptions/10", { handlers });

    expect(await screen.findByRole("radio", { name: "Off" })).toBeChecked();
    await user.click(screen.getByRole("radio", { name: "Paused" }));
    await user.click(
      within(await screen.findByRole("dialog", { name: "Turn this subscription on, paused?" })).getByRole("button", {
        name: "Turn on",
      }),
    );

    await waitFor(() => expect(writes).toEqual(["pause", "on"]));
  });

  it("offers the aggregation and Delete behind More actions", async () => {
    const { handlers } = withPause(sub());
    const { user, router } = renderApp("/subscriptions/10", { handlers });

    await user.click(await screen.findByRole("button", { name: "More actions" }));
    expect(await screen.findByRole("menuitem", { name: "Delete" })).toBeVisible();
    await user.click(screen.getByRole("menuitem", { name: "Create an aggregation of this" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/aggregations/new"));
  });
});
