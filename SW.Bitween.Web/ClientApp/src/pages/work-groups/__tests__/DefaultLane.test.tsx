import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const none = { result: [], totalCount: 0 };

/** A subscription row as GET /subscriptions sends it, with only what the list reads. */
const subscription = (id: number, workGroupId: number | null) => ({
  id,
  name: `Subscription ${id}`,
  type: 4,
  documentId: 1,
  partnerId: 1,
  workGroupId,
});

const handlers = (subscriptions: unknown[]) => [
  http.get(apiPath("/workgroups"), () => HttpResponse.json(none)),
  http.get(apiPath("/subscriptions"), () =>
    HttpResponse.json({ result: subscriptions, totalCount: subscriptions.length }),
  ),
  http.get(apiPath("/audit"), () => HttpResponse.json(none)),
  http.get(apiPath("/ops/summary"), () =>
    HttpResponse.json({
      totalConsumers: 0,
      unhealthyConsumers: 0,
      disconnectedConsumers: 0,
      totalQueueDepth: 0,
      totalRetryBacklog: 0,
      totalDeadLetterBacklog: 0,
      totalIncomingRate: 0,
      totalAckRate: 0,
      lastUpdatedUtc: "2026-10-13T08:00:00Z",
    }),
  ),
  http.get(apiPath("/ops/consumers"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/retries"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/deadletters"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/alerts"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/unattendedqueues"), () => HttpResponse.json([])),
];

describe("the default lane", () => {
  it("counts the subscriptions that share it and says what a slow partner there does", async () => {
    renderApp("/work-groups", { handlers: handlers([subscription(1, null), subscription(2, null), subscription(3, 7)]) });

    expect(await screen.findByText("2 subscriptions share the default lane.")).toBeVisible();
    expect(screen.getByText(/give its subscriptions a work group of their own/)).toBeVisible();
  });

  it("says nothing when every subscription has a work group", async () => {
    renderApp("/work-groups", { handlers: handlers([subscription(3, 7)]) });

    expect(await screen.findByRole("heading", { name: "Work groups" })).toBeVisible();
    expect(screen.queryByText(/share(s)? the default lane/)).toBeNull();
  });
});
