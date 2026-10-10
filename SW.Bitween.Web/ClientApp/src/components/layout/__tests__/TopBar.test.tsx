import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const none = { result: [], totalCount: 0 };

/** A row as GET /workgroups sends it: `RawWorkGroup` in src/api/http/workGroups.ts. */
const group = {
  id: 7,
  name: "Priority lane",
  busMessageName: "priority",
  options: { rabbitMqOptions: { prefetch: 10, priority: 5 } },
  processorNodeCount: 1,
  usedByCount: 0,
};

/** A consumer row as GET /ops/consumers sends it, for one of the group's two lanes. */
const consumer = (lane: "Work" | "Notifications", queueCount: number, retryCount: number, failedCount: number) => ({
  name: "xchangeservice",
  messageName: lane === "Work" ? "7priority" : "7priority-result",
  queueName: `v3.test.bitween.xchangeservice.7priority${lane === "Work" ? "" : "-result"}`,
  lane,
  title: group.name,
  workGroupId: group.id,
  informationTypeId: null,
  totalNodes: 1,
  processingCount: 0,
  queueCount,
  retryCount,
  failedCount,
  priority: 5,
  prefetch: 10,
  incomingRate: 0,
  ackRate: 0,
  isBackpressured: false,
  healthStatus: "Info",
});

const handlers = [
  http.get(apiPath("/workgroups"), () => HttpResponse.json({ result: [group], totalCount: 1 })),
  http.get(apiPath("/subscriptions"), () => HttpResponse.json(none)),
  http.get(apiPath("/audit"), () => HttpResponse.json(none)),
  http.get(apiPath("/ops/summary"), () =>
    HttpResponse.json({
      totalConsumers: 2,
      unhealthyConsumers: 0,
      disconnectedConsumers: 0,
      totalQueueDepth: 0,
      totalRetryBacklog: 0,
      totalDeadLetterBacklog: 0,
      totalIncomingRate: 0,
      totalAckRate: 0,
      lastUpdatedUtc: "2026-09-28T08:00:00Z",
    }),
  ),
  // 4 + 1 + 0 in the work lane, 2 dead in the notifications lane.
  http.get(apiPath("/ops/consumers"), () =>
    HttpResponse.json([consumer("Work", 4, 1, 0), consumer("Notifications", 0, 0, 2)]),
  ),
  http.get(apiPath("/ops/retries"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/deadletters"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/alerts"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/unattendedqueues"), () => HttpResponse.json([])),
];

describe("the top bar", () => {
  it("shows the way here, ending in the thing itself, and names the tab after it", async () => {
    renderApp("/work-groups/7", { handlers });

    const crumbs = within(await screen.findByRole("navigation", { name: "Breadcrumb" }));
    expect(await crumbs.findByText("Priority lane")).toHaveAttribute("aria-current", "page");
    expect(crumbs.getByRole("link", { name: "Work groups" })).toHaveAttribute("href", "/work-groups");
    expect(crumbs.getByText("Configuration")).toBeVisible();
    await waitFor(() => expect(document.title).toBe("Priority lane · Work groups · Bitween"));
  });

  it("shows which environment this is, once one is named", async () => {
    renderApp("/work-groups", {
      handlers,
      config: { theme: { environmentName: "Staging", environmentColor: "#d97706", tabTitle: "Acme Bitween" } },
    });

    expect((await screen.findAllByText("Staging")).length).toBeGreaterThan(0);
    await waitFor(() => expect(document.title).toBe("Work groups · Acme Bitween (Staging)"));
  });
});
