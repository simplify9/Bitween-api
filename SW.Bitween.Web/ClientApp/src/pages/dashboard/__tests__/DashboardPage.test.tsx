import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { metricsHandlers, overview, summary } from "../../../__tests__/support/metricsFixtures";

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
const dashboard = (subscriptions: ReturnType<typeof subscription>[], metrics = metricsHandlers()) => [
  ...metrics,
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
  http.get(apiPath("/apigateways"), () => HttpResponse.json(none)),
];

describe("the dashboard", () => {
  it("pages subscription health instead of letting it grow without bound", async () => {
    // Fourteen unhealthy subscriptions: eleven failing, then three paused.
    const rows = Array.from({ length: 14 }, (_, i) => subscription(i, i < 11 ? { failures: i + 1 } : { paused: true }));
    const { user } = renderApp("/dashboard", { handlers: dashboard(rows) });

    const heading = await screen.findByRole("heading", { name: "Subscription health" });
    const panel = within(heading.closest("section")!);

    expect(await panel.findByText("1-10 of 14")).toBeVisible();
    expect(panel.getAllByRole("listitem")).toHaveLength(10);
    expect(panel.getByText("Health page 1", { exact: true })).toBeVisible();

    await user.click(panel.getByRole("button", { name: "Next →" }));

    expect(panel.getByText("11-14 of 14")).toBeVisible();
    expect(panel.getAllByRole("listitem")).toHaveLength(4);
    expect(panel.getByText("Health page 11", { exact: true })).toBeVisible();
    expect(panel.getAllByText("Paused")).toHaveLength(3);
    expect(panel.getByRole("button", { name: "Next →" })).toBeDisabled();
  });

  it("shows a new instance where to start, in order, and no success rate before anything has finished", async () => {
    renderApp("/dashboard", { handlers: dashboard([]) });

    const card = within((await screen.findByRole("heading", { name: "Getting started" })).closest("section")!);
    expect(card.getByText("0 of 5 done. Each step needs the one before it.")).toBeVisible();
    // The next step is the one that stands out, and leads to where it's done.
    expect(card.getByRole("link", { name: "Information types" })).toHaveAttribute("href", "/information-types");
    expect(card.getByText("1. Describe a document")).toBeVisible();

    const rate = screen.getByText("Success rate, 7 days").closest("a")!;
    expect(within(rate).getByText("-")).toBeVisible();
  });

  it("can be dismissed for good", async () => {
    const { user } = renderApp("/dashboard", { handlers: dashboard([]) });
    await user.click(await screen.findByRole("button", { name: "Dismiss getting started" }));
    expect(screen.queryByRole("heading", { name: "Getting started" })).not.toBeInTheDocument();
    expect(localStorage.getItem("bitween_getting_started_dismissed")).toBe("1");
    localStorage.removeItem("bitween_getting_started_dismissed");
  });

  it("isn't offered once the instance is set up", async () => {
    renderApp("/dashboard", {
      handlers: [
        http.get(apiPath("/documents"), () =>
          HttpResponse.json({ result: [{ id: 3, name: "Order", code: "ORDER" }], totalCount: 1 }),
        ),
        // A partner with no key yet still counts: one that sends by token never has one.
        http.get(apiPath("/partners"), () =>
          HttpResponse.json({
            result: [{ id: 7, name: "Acme", keys: 0, subscriptionsCount: 1, propertyKeys: [] }],
            totalCount: 1,
          }),
        ),
        ...dashboard([subscription(0)]),
      ],
    });
    expect(await screen.findByRole("heading", { name: "Subscription health" })).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Getting started" })).not.toBeInTheDocument();
  });

  it("reads its numbers from the metrics, and names subscriptions that have gone quiet", async () => {
    const metrics = overview({
      since: "2026-09-01T00:00:00Z",
      last24h: summary({ total: 12_400, succeeded: 12_000, failed: 300, badResponse: 100, p50Ms: 180, p95Ms: 2_400 }),
      previous24h: summary({ total: 11_000 }),
      last7d: summary({ total: 80_000, succeeded: 78_800, successRate: 98.5 }),
      silent: [
        {
          subscriptionId: 41,
          name: "Acme orders in",
          type: "GatewayApiCall",
          lastExchangeHour: "2026-10-10T08:00:00Z",
          quietHours: 26,
          usualGapHours: 1.2,
        },
      ],
    });
    renderApp("/dashboard", {
      handlers: dashboard(
        [],
        metricsHandlers(metrics, {
          since: null,
          insights: [
            {
              kind: "unused-retry-policies",
              severity: "info",
              title: "Retry policies nothing uses",
              detail: "No subscription is assigned them.",
              count: 1,
              items: [{ label: "Old backoff", href: "/retry-policies/4" }],
            },
          ],
        }),
      ),
    });

    const exchanges = (await screen.findByText("Exchanges, last 24 hours")).closest("a")!;
    expect(within(exchanges).getByText("12K")).toBeVisible();
    expect(within(exchanges).getByText(/\+1.4K vs the day before/)).toBeVisible();
    expect(within(screen.getByText("Success rate, 7 days").closest("a")!).getByText("98.5%")).toBeVisible();
    expect(within(screen.getByText("End-to-end time, last 24 hours").closest("a")!).getByText("2.4s")).toBeVisible();

    const quiet = within(screen.getByRole("heading", { name: "Gone quiet" }).closest("section")!);
    expect(quiet.getByRole("link", { name: "Acme orders in" })).toHaveAttribute("href", "/subscriptions/41");
    expect(quiet.getByText(/quiet 1d 2h|quiet 26h 0m/)).toBeVisible();

    const housekeeping = within(screen.getByRole("heading", { name: "Housekeeping" }).closest("section")!);
    expect(await housekeeping.findByRole("link", { name: "Old backoff" })).toHaveAttribute("href", "/retry-policies/4");
  });
});
