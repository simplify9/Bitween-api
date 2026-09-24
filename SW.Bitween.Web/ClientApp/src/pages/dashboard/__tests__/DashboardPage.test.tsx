import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const none = { result: [], totalCount: 0 };

/** A subscription row as GET /subscriptions sends it: `RawSubscription` in src/api/http/subscriptions.ts. */
const subscription = (i: number, { failures = 0, paused = false } = {}) => ({
  id: 900000 + i,
  name: `Health page ${i + 1}`,
  documentId: 3,
  partnerId: null,
  aggregationForId: null,
  type: "Receiving",
  handlerId: "NativeHttpHandler",
  mapperId: null,
  receiverId: "NativeHttpReceiver",
  dataSourceId: null,
  validatorId: null,
  inactive: false,
  temporary: false,
  categoryId: null,
  handlerProperties: [],
  mapperProperties: [],
  receiverProperties: [],
  validatorProperties: [],
  documentFilter: [],
  matchExpression: null,
  workGroupId: null,
  retryPolicyId: null,
  customRetryPolicy: null,
  schedules: [],
  responseSubscriptionId: null,
  responseMessageTypeName: null,
  receiveOn: null,
  aggregateOn: null,
  pausedOn: paused ? "2026-09-24T08:00:00Z" : null,
  isRunning: false,
  consecutiveFailures: failures,
  lastException: failures ? "Request failed with status NotFound" : null,
});

/** Everything the dashboard reads, with `subscriptions` as the rows behind the health panel. */
const dashboard = (subscriptions: ReturnType<typeof subscription>[]) => [
  http.get(apiPath("/xchanges"), () => HttpResponse.json(none)),
  http.get(apiPath("/delayedretries"), () => HttpResponse.json(none)),
  http.get(apiPath("/dashboard/retrysummary"), () =>
    HttpResponse.json({ retriesLast7Days: { finished: 0, succeeded: 0 }, failingChains: [] }),
  ),
  http.get(apiPath("/ops/alerts"), () => HttpResponse.json([])),
  http.get(apiPath("/subscriptions"), () =>
    HttpResponse.json({ result: subscriptions, totalCount: subscriptions.length }),
  ),
  // Read alongside the rows to name what each one uses.
  http.get(apiPath("/documents"), () => HttpResponse.json(none)),
  http.get(apiPath("/partners"), () => HttpResponse.json(none)),
];

describe("the dashboard", () => {
  it("pages subscription health instead of letting it grow without bound", async () => {
    // Fourteen unhealthy subscriptions: eleven failing, then three paused.
    const rows = Array.from({ length: 14 }, (_, i) =>
      subscription(i, i < 11 ? { failures: i + 1 } : { paused: true }),
    );
    const { user } = renderApp("/dashboard", { handlers: dashboard(rows) });

    const heading = await screen.findByRole("heading", { name: "Subscription health" });
    const panel = within(heading.closest("section")!);

    expect(await panel.findByText("1–10 of 14")).toBeVisible();
    expect(panel.getAllByRole("listitem")).toHaveLength(10);
    expect(panel.getByText("Health page 1", { exact: true })).toBeVisible();

    await user.click(panel.getByRole("button", { name: "Next →" }));

    expect(panel.getByText("11–14 of 14")).toBeVisible();
    expect(panel.getAllByRole("listitem")).toHaveLength(4);
    expect(panel.getByText("Health page 11", { exact: true })).toBeVisible();
    expect(panel.getAllByText("Paused")).toHaveLength(3);
    expect(panel.getByRole("button", { name: "Next →" })).toBeDisabled();
  });
});
