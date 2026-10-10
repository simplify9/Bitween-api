import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { backend, sub } from "./subscriptionBackend";

globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

/**
 * A scheduled job still marked running by a run that died skips every run that comes due. The page
 * says so, and someone who may operate it can clear the mark.
 */
describe("a scheduled job stuck as running", () => {
  it("says every run is skipped and clears the mark when asked", async () => {
    let stuck = true;
    let cleared = 0;
    const { handlers } = backend([
      sub({ type: "Receiving", isRunning: true, receiverId: "NativeHttpReceiver", schedules: [{ recurrence: "Hourly", days: 0, hours: 0, minutes: 5, backwards: false }] }),
    ]);
    const { user } = renderApp("/subscriptions/10", {
      handlers: [
        http.get(apiPath("/subscriptions/schedulehealth"), () =>
          HttpResponse.json([{ subscriptionId: 10, scheduleCount: 1, triggerCount: 1, state: "Normal", nextFireOn: null, stuck }]),
        ),
        http.post(apiPath("/subscriptions/10/clearrunning"), () => {
          cleared++;
          stuck = false;
          return HttpResponse.json({ id: 10, cleared: true });
        }),
        // A scheduled job's page also lists its recent receive attempts: none here.
        http.get(apiPath("/subscriptions/receiveattempts"), () => HttpResponse.json({ result: [], totalCount: 0 })),
        ...handlers,
      ],
    });

    expect(await screen.findByText("Every scheduled run is being skipped.", undefined, { timeout: 5000 })).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Clear the running mark" }));

    await waitFor(() => expect(cleared).toBe(1));
    await waitFor(() => expect(screen.queryByText("Every scheduled run is being skipped.")).not.toBeInTheDocument());
  });
});
