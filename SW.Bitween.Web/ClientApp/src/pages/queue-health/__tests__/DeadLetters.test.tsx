import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Dead letters on Queue health: reading what's there, and sending it back to be tried again.
 * What the broker does with a requeue is SW.Bitween.IntegrationTests' business (DeadLetterRequeueTests).
 */
const QUEUE = "v3.test.bitween.xchangeservice.xchangecreated.bad";

const summary = {
  totalConsumers: 0,
  unhealthyConsumers: 0,
  disconnectedConsumers: 0,
  totalQueueDepth: 0,
  totalRetryBacklog: 0,
  totalDeadLetterBacklog: 3,
  totalIncomingRate: 0,
  totalAckRate: 0,
  lastUpdatedUtc: "2026-10-08T08:00:00Z",
};

/** GET /ops/deadletters: `DeadLetterSummaryView` from SW.Bus. */
const deadLetter = {
  consumerName: "XchangeService",
  messageName: "XchangeCreated",
  deadLetterQueueName: QUEUE,
  deadLetterCount: 3,
  lastExceptionType: "TimeoutException",
  lastExceptionMessage: "The database did not answer",
  lastFailedAt: "2026-10-08T07:55:00Z",
  severity: "Critical",
};

const page = (requeues: unknown[] = []) => [
  http.get(apiPath("/ops/summary"), () => HttpResponse.json(summary)),
  http.get(apiPath("/ops/consumers"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/retries"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/deadletters"), () => HttpResponse.json([deadLetter])),
  http.get(apiPath("/ops/alerts"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/unattendedqueues"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/deadlettermessages"), ({ request }) => {
    expect(new URL(request.url).searchParams.get("queue")).toBe(QUEUE);
    return HttpResponse.json([
      {
        position: 1,
        body: '{"Id":"abc123"}',
        bodyCut: false,
        lastException: "TimeoutException: The database did not answer",
        correlationId: "corr-1",
        routingKey: "xchangecreated",
        exceptionHistory: [],
      },
    ]);
  }),
  http.post(apiPath("/ops/requeuedeadletters"), async ({ request }) => {
    requeues.push(await request.json());
    return HttpResponse.json({ requeued: 3 });
  }),
];

describe("dead letters", () => {
  it("shows what is sitting in the queue", async () => {
    const { user } = renderApp("/queue-health", { handlers: page() });

    await user.click(await screen.findByRole("button", { name: /Show messages/ }));

    const dialog = within(await screen.findByRole("dialog"));
    expect(await dialog.findByText('{"Id":"abc123"}')).toBeVisible();
    expect(dialog.getByText(/Correlation corr-1/)).toBeVisible();
  });

  it("sends them back only after saying what will happen", async () => {
    const requeues: unknown[] = [];
    const { user } = renderApp("/queue-health", { handlers: page(requeues) });

    await user.click(await screen.findByRole("button", { name: /Send back/ }));
    const dialog = within(screen.getByRole("dialog", { name: "Send 3 messages back?" }));
    expect(dialog.getByText(/fresh set of retries/)).toBeVisible();
    expect(requeues).toHaveLength(0);

    await user.click(dialog.getByRole("button", { name: "Send back" }));

    await expect.poll(() => requeues).toEqual([{ queue: QUEUE }]);
    expect(await screen.findByText("3 messages sent back.")).toBeVisible();
  });

  it("offers neither to someone who may only watch the queues", async () => {
    renderApp("/queue-health", {
      as: { permissions: ALL_PERMISSIONS.filter((k) => k !== "monitoring.operate" && k !== "exchanges.view") },
      handlers: page(),
    });

    expect(await screen.findByText("The database did not answer")).toBeVisible();
    expect(screen.queryByRole("button", { name: /Show messages/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Send back/ })).not.toBeInTheDocument();
  });
});
