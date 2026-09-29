import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * An information type's bus queue holds the incoming messages themselves, so the screens say
 * what happens to it: turning the bus off pauses it, and a delete or a rename deletes it.
 */
const empty = { result: [], totalCount: 0 };

/** GET /documents/{id}: `RawDocument` in src/api/http/documents.ts. */
const typeOf = (busEnabled: boolean, busMessageTypeName: string | null = "OrderPlaced") => ({
  id: 5,
  code: "ORDER",
  name: "Order",
  documentFormat: "Json",
  busEnabled,
  busMessageTypeName,
  duplicateInterval: 0,
  disregardsUnfilteredMessages: false,
  promotedProperties: [],
  usedByCount: 0,
  retiredOn: null,
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

/** The type's queue with 4 queued and 2 dead: read while on the bus, left unread while paused. */
const queueHealth = (paused: boolean) => {
  const queueName = "v3.test.bitween.busservice.orderplaced";
  return [
    http.get(apiPath("/ops/summary"), () => HttpResponse.json(summary)),
    http.get(apiPath("/ops/consumers"), () =>
      HttpResponse.json(
        paused
          ? []
          : [
              {
                name: "busservice",
                messageName: "OrderPlaced",
                queueName,
                lane: "FrontDoor",
                title: "Order",
                workGroupId: null,
                informationTypeId: 5,
                totalNodes: 1,
                processingCount: 0,
                queueCount: 4,
                retryCount: 0,
                failedCount: 2,
                priority: 0,
                prefetch: 10,
                incomingRate: 0,
                ackRate: 0,
                isBackpressured: false,
                healthStatus: "Info",
              },
            ],
      ),
    ),
    http.get(apiPath("/ops/retries"), () => HttpResponse.json([])),
    http.get(apiPath("/ops/deadletters"), () => HttpResponse.json([])),
    http.get(apiPath("/ops/alerts"), () => HttpResponse.json([])),
    http.get(apiPath("/ops/unattendedqueues"), () =>
      HttpResponse.json(
        paused
          ? [{ queueName, messages: 4, retryMessages: 0, deadMessages: 2, queues: 3, consumers: 0, informationTypeId: 5 }]
          : [],
      ),
    ),
  ];
};

const openType = (type: ReturnType<typeof typeOf>, saved: unknown[] = []) =>
  renderApp("/information-types/5", {
    handlers: [
      http.get(apiPath("/documents/5"), () => HttpResponse.json(type)),
      http.post(apiPath("/documents/5"), async ({ request }) => {
        saved.push(await request.json());
        return new HttpResponse(null, { status: 204 });
      }),
      ...["/subscriptions", "/busgateways", "/xchanges", "/partners", "/audit"].map((p) =>
        http.get(apiPath(p), () => HttpResponse.json(empty)),
      ),
      ...queueHealth(!type.busEnabled),
    ],
  });

describe("an information type's bus queue", () => {
  it("says a paused type's messages are waiting for it", async () => {
    openType(typeOf(false));

    expect(await screen.findByText(/Paused: messages sent as/)).toHaveTextContent(
      "Paused: messages sent as OrderPlaced wait in its queue",
    );
    expect(await screen.findByText(/6 are waiting now/)).toBeVisible();
  });

  it("pauses rather than forgets when the bus is turned off", async () => {
    const saved: unknown[] = [];
    const { user } = openType(typeOf(true), saved);

    expect(await screen.findByText(/Turning it off pauses it/)).toBeVisible();
    await user.click(screen.getByRole("checkbox", { name: /Available on the message bus/ }));
    expect(screen.getByText(/Saving pauses it/)).toBeVisible();

    await user.click(screen.getByRole("button", { name: /save/i }));

    // No rename to confirm: the name goes with it, so the type resumes on the same queue.
    await expect.poll(() => saved).toHaveLength(1);
    expect(saved[0]).toMatchObject({ busEnabled: false, busMessageTypeName: "OrderPlaced" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("asks before a new name deletes the old queue and what's in it", async () => {
    const saved: unknown[] = [];
    const { user } = openType(typeOf(true), saved);

    const name = await screen.findByLabelText("Bus message type name");
    await user.clear(name);
    await user.type(name, "OrderCreated");
    await user.click(screen.getByRole("button", { name: /save/i }));

    const dialog = within(screen.getByRole("dialog", { name: "Change the bus message type name?" }));
    expect(dialog.getByText(/anything still sent as/)).toHaveTextContent(
      "Messages sent as OrderCreated go to a new queue. The queue for OrderPlaced is deleted",
    );
    expect(await dialog.findByText(/6 messages are still in it and will be deleted/)).toHaveTextContent(
      "can't be recovered",
    );
    expect(saved).toHaveLength(0);

    await user.click(dialog.getByRole("button", { name: "Change and save" }));

    await expect.poll(() => saved).toHaveLength(1);
    expect(saved[0]).toMatchObject({ busEnabled: true, busMessageTypeName: "OrderCreated" });
  });

  it("says a delete takes its queue and messages too", async () => {
    const { user } = openType(typeOf(true));

    await user.click(await screen.findByRole("button", { name: "Delete" }));

    const dialog = within(screen.getByRole("dialog", { name: "Delete this information type?" }));
    expect(dialog.getByText("Its bus queue is deleted too.")).toBeVisible();
    expect(await dialog.findByText(/6 messages are still in it and will be deleted/)).toBeVisible();
  });
});
