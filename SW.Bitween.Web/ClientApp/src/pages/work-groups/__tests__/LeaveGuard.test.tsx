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

/** The page's own "back to Work groups"; the sidebar has a link of the same name. */
const backLink = () => screen.getAllByRole("link", { name: /Work groups/ })[0];

async function editPrefetch(user: ReturnType<typeof renderApp>["user"]) {
  const prefetch = await screen.findByLabelText("Prefetch");
  await user.clear(prefetch);
  await user.type(prefetch, "20");
}

describe("leaving an editor with unsaved changes", () => {
  it("asks first, and staying keeps the changes", async () => {
    const { user, router } = renderApp("/work-groups/7", { handlers });
    await editPrefetch(user);
    const edited = (screen.getByLabelText("Prefetch") as HTMLInputElement).value;

    await user.click(backLink());
    const dialog = within(screen.getByRole("dialog", { name: "Leave without saving?" }));
    await user.click(dialog.getByRole("button", { name: "Stay on this page" }));

    expect(router.state.location.pathname).toBe("/work-groups/7");
    expect(screen.getByLabelText("Prefetch")).toHaveDisplayValue(edited);

    await user.click(backLink());
    await user.click(screen.getByRole("button", { name: "Leave without saving" }));
    expect(router.state.location.pathname).toBe("/work-groups");
  });

  it("doesn't ask when there is nothing to lose", async () => {
    const { user, router } = renderApp("/work-groups/7", { handlers });
    await screen.findByLabelText("Prefetch");

    await user.click(backLink());
    expect(router.state.location.pathname).toBe("/work-groups");
    expect(screen.queryByRole("dialog", { name: "Leave without saving?" })).not.toBeInTheDocument();
  });

  it("doesn't ask again after the page has dealt with them itself, by deleting", async () => {
    const { user, router } = renderApp("/work-groups/7", {
      handlers: [
        http.post(apiPath("/workgroups/7/delete"), () => new HttpResponse(null, { status: 204 })),
        ...handlers,
      ],
    });
    await editPrefetch(user);

    await user.click(screen.getByRole("button", { name: "Delete" }));
    await user.click(screen.getByRole("button", { name: "Delete work group" }));

    await expect.poll(() => router.state.location.pathname).toBe("/work-groups");
    expect(screen.queryByRole("dialog", { name: "Leave without saving?" })).not.toBeInTheDocument();
  });
});
