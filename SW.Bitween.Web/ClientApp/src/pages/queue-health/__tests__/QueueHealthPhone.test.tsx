import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { onAPhone } from "../../../__tests__/support/phone";

/** A lane as GET /ops/consumers sends it (`RawConsumer` in src/api/http/queueHealth.ts). */
const consumer = {
  name: "SubscriptionConsumer",
  messageName: "orders",
  queueName: "v3.prod.bitween.xchangeservice.subscriptionconsumer.orders",
  lane: "Work",
  title: "Orders",
  workGroupId: 4,
  informationTypeId: null,
  totalNodes: 2,
  processingCount: 3,
  queueCount: 120,
  retryCount: 5,
  failedCount: 7,
  priority: 0,
  prefetch: 16,
  concurrency: 4,
  incomingRate: 2,
  ackRate: 1,
  isBackpressured: false,
  healthStatus: "Warning",
};

const summary = {
  totalConsumers: 1,
  unhealthyConsumers: 1,
  disconnectedConsumers: 0,
  totalQueueDepth: 120,
  totalRetryBacklog: 5,
  totalDeadLetterBacklog: 7,
  totalIncomingRate: 2,
  totalAckRate: 1,
  lastUpdatedUtc: "2026-09-28T08:00:00Z",
};

const handlers = [
  http.get(apiPath("/ops/summary"), () => HttpResponse.json(summary)),
  http.get(apiPath("/ops/background"), () =>
    HttpResponse.json({ jobs: [], outbox: { pending: 0, oldestPendingOn: null, failing: 0, lastError: null, publishedLastHour: 0 } }),
  ),
  http.get(apiPath("/ops/consumers"), () => HttpResponse.json([consumer])),
  http.get(apiPath("/ops/retries"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/deadletters"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/alerts"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/unattendedqueues"), () => HttpResponse.json([])),
];

describe("queue health on a phone", () => {
  onAPhone();

  it("shows each lane as a card rather than a twelve-column table", async () => {
    renderApp("/queue-health", { handlers });

    const work = within(await screen.findByRole("region", { name: "Work" }));
    expect(screen.queryByRole("columnheader", { name: "Prefetch" })).not.toBeInTheDocument();
    expect(work.getByRole("link", { name: "Orders" })).toHaveAttribute("href", "/work-groups/4");
    expect(work.getByText(consumer.queueName)).toBeInTheDocument();
    expect(work.getByText("Warning")).toBeInTheDocument();
    expect(work.getByText("Queued").nextSibling).toHaveTextContent("120");
    expect(work.getByText("Dead").nextSibling).toHaveTextContent("7");
  });
});
