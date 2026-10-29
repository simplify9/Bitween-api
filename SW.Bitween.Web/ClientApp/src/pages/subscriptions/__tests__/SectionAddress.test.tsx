import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { backend, sub } from "./subscriptionBackend";

describe("a subscription's address", () => {
  it("moves a scheduled job opened at /subscriptions/:id to its own section, keeping the query", async () => {
    const { handlers } = backend([sub({ type: "Receiving", receiverId: "NativeHttpReceiver" })]);
    const { router } = renderApp("/subscriptions/10?stage=source", {
      handlers: [
        http.get(apiPath("/subscriptions/schedulehealth"), () => HttpResponse.json([])),
        http.get(apiPath("/subscriptions/receiveattempts"), () => HttpResponse.json({ result: [], totalCount: 0 })),
        ...handlers,
      ],
    });

    await waitFor(() => expect(router.state.location.pathname).toBe("/scheduled-jobs/10"));
    expect(router.state.location.search).toBe("?stage=source");
    // The sidebar shows where you are; the way back names the list too.
    await waitFor(() =>
      expect(
        screen.getAllByRole("link", { name: "Scheduled jobs" }).some((l) => l.getAttribute("aria-current") === "page"),
      ).toBe(true),
    );
  });
});
