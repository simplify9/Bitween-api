import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** A lane as GET /ops/unattendedqueues sends it: `UnattendedQueueView` in Ops/UnattendedQueues.cs. */
const lane = (queueName: string, { messages = 0, retryMessages = 0, deadMessages = 0, consumers = 0 } = {}) => ({
  queueName,
  messages,
  retryMessages,
  deadMessages,
  queues: 3,
  consumers,
});

const summary = {
  totalConsumers: 0,
  unhealthyConsumers: 0,
  disconnectedConsumers: 0,
  totalQueueDepth: 0,
  totalRetryBacklog: 0,
  totalDeadLetterBacklog: 0,
  totalIncomingRate: 0,
  totalAckRate: 0,
  lastUpdatedUtc: "2026-09-28T08:00:00Z",
};

/** Everything the page polls, with `unattended` as the leftover lanes. */
const queueHealth = (unattended: ReturnType<typeof lane>[]) => [
  http.get(apiPath("/ops/summary"), () => HttpResponse.json(summary)),
  http.get(apiPath("/ops/consumers"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/retries"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/deadletters"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/alerts"), () => HttpResponse.json([])),
  http.get(apiPath("/ops/unattendedqueues"), () => HttpResponse.json(unattended)),
];

const panel = async () =>
  within((await screen.findByRole("heading", { name: "Nobody is reading these" })).closest("section")!);

/** Answers the delete as the backend would: everything asked for is gone. */
const deletes = (asked: unknown[]) =>
  http.post(apiPath("/ops/deleteunattendedqueues"), async ({ request }) => {
    const body = (await request.json()) as { queueNames: string[] };
    asked.push(body);
    return HttpResponse.json({ deleted: body.queueNames, skipped: [] });
  });

describe("unattended queues", () => {
  it("deletes the ticked lanes only after confirming what they hold", async () => {
    const asked: unknown[] = [];
    const { user } = renderApp("/queue-health", {
      handlers: [
        ...queueHealth([
          lane("v3.test.bitween.xchangeservice.7gone", { messages: 12, deadMessages: 3 }),
          lane("v3.test.bitween.xchangeservice.8gone", { retryMessages: 2 }),
          lane("v3.test.bitween.xchangeservice.9gone"),
        ]),
        deletes(asked),
      ],
    });

    const p = await panel();
    await user.click(p.getByRole("checkbox", { name: "Select v3.test.bitween.xchangeservice.7gone" }));
    await user.click(p.getByRole("checkbox", { name: "Select v3.test.bitween.xchangeservice.8gone" }));
    await user.click(p.getByRole("button", { name: "Delete selected…" }));

    const dialog = within(screen.getByRole("dialog", { name: "Delete 2 lanes?" }));
    expect(dialog.getByText(/2 lanes \(6 queues\)/)).toBeVisible();
    expect(dialog.getByText(/They still hold 17 messages/)).toHaveTextContent("12 queued, 2 retrying and 3 dead");
    expect(asked).toHaveLength(0);

    await user.click(dialog.getByRole("button", { name: "Delete 2 lanes" }));

    await expect.poll(() => asked).toEqual([
      { queueNames: ["v3.test.bitween.xchangeservice.7gone", "v3.test.bitween.xchangeservice.8gone"] },
    ]);
    expect(await p.findByText("Deleted 2 lanes.")).toBeVisible();
  });

  it("selects every lane that can go, leaving out one something still reads", async () => {
    const asked: unknown[] = [];
    const { user } = renderApp("/queue-health", {
      handlers: [
        ...queueHealth([
          lane("v3.test.bitween.xchangeservice.7gone"),
          lane("v3.test.bitween.xchangeservice.8held", { consumers: 1 }),
        ]),
        deletes(asked),
      ],
    });

    const p = await panel();
    expect(p.getByText("In use")).toBeVisible();
    expect(p.queryByRole("checkbox", { name: "Select v3.test.bitween.xchangeservice.8held" })).not.toBeInTheDocument();

    await user.click(p.getByRole("checkbox", { name: "Select every lane that can be deleted" }));
    await user.click(p.getByRole("button", { name: "Delete selected…" }));
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Delete queues" }));

    await expect.poll(() => asked).toEqual([{ queueNames: ["v3.test.bitween.xchangeservice.7gone"] }]);
  });

  it("says which lanes it had to skip", async () => {
    const { user } = renderApp("/queue-health", {
      handlers: [
        ...queueHealth([lane("v3.test.bitween.xchangeservice.7gone")]),
        http.post(apiPath("/ops/deleteunattendedqueues"), () =>
          HttpResponse.json({
            deleted: [],
            skipped: [{ queueName: "v3.test.bitween.xchangeservice.7gone", reason: "Something is still reading it." }],
          }),
        ),
      ],
    });

    const p = await panel();
    await user.click(p.getByRole("checkbox", { name: "Select v3.test.bitween.xchangeservice.7gone" }));
    await user.click(p.getByRole("button", { name: "Delete selected…" }));
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Delete queues" }));

    expect(await p.findByText(/Skipped 1/)).toBeVisible();
    expect(p.getByText(/Something is still reading it/)).toBeVisible();
  });

  it("shows no selection to someone who can only look", async () => {
    renderApp("/queue-health", {
      as: { permissions: ALL_PERMISSIONS.filter((k) => k !== "monitoring.operate") },
      handlers: queueHealth([lane("v3.test.bitween.xchangeservice.9gone")]),
    });

    const p = await panel();
    expect(p.getByText("v3.test.bitween.xchangeservice.9gone")).toBeVisible();
    expect(p.queryByRole("checkbox")).not.toBeInTheDocument();
  });
});
