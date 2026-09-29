import { screen, within } from "@testing-library/react";
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

describe("a work group's queues", () => {
  it("says its queues go with it, and counts what is still in them", async () => {
    const { user } = renderApp("/work-groups/7", { handlers });

    await user.click(await screen.findByRole("button", { name: "Delete" }));

    const dialog = within(screen.getByRole("dialog", { name: "Delete this work group?" }));
    expect(dialog.getByText(/and its queues will be gone for good/)).toBeVisible();
    expect(await dialog.findByText(/7 messages are still in them and will be deleted/)).toBeVisible();
  });

  it("asks before a new bus message name moves the group to new queues", async () => {
    const saved: unknown[] = [];
    const { user } = renderApp("/work-groups/7", {
      handlers: [
        http.post(apiPath("/workgroups/7"), async ({ request }) => {
          saved.push(await request.json());
          return new HttpResponse(null, { status: 204 });
        }),
        ...handlers,
      ],
    });

    const busName = await screen.findByLabelText("Bus message name");
    await user.clear(busName);
    await user.type(busName, "urgent");
    await user.click(screen.getByRole("button", { name: /save/i }));

    const dialog = within(screen.getByRole("dialog", { name: "Change the bus message name?" }));
    expect(dialog.getByText("urgent")).toBeVisible();
    expect(dialog.getByText(/its current queues are deleted/)).toBeVisible();
    expect(await dialog.findByText(/7 messages are still in them and will be deleted/)).toBeVisible();
    expect(saved).toHaveLength(0);

    await user.click(dialog.getByRole("button", { name: "Change and save" }));

    await expect.poll(() => saved).toHaveLength(1);
    expect(saved[0]).toMatchObject({ busMessageName: "urgent" });
  });

  it("saves other changes without asking", async () => {
    const saved: unknown[] = [];
    const { user } = renderApp("/work-groups/7", {
      handlers: [
        http.post(apiPath("/workgroups/7"), async ({ request }) => {
          saved.push(await request.json());
          return new HttpResponse(null, { status: 204 });
        }),
        ...handlers,
      ],
    });

    const prefetch = await screen.findByLabelText("Prefetch");
    await user.clear(prefetch);
    await user.type(prefetch, "20");
    await user.click(screen.getByRole("button", { name: /save/i }));

    await expect.poll(() => saved).toHaveLength(1);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
