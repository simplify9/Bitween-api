import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { summary } from "../../../__tests__/support/metricsFixtures";
import { backend, noRows, sub } from "./subscriptionBackend";

const silent = {
  subscriptionId: 10,
  name: "Acme orders in",
  type: "Receiving",
  status: "silent",
  successRate: 100,
  p95Ms: 300,
  lastExchangeHour: "2026-10-10T08:00:00Z",
  quietHours: 26,
  usualGapHours: 1.5,
  daily: [40, 38, 41, 39, 12, 0, 0],
  total: 170,
  failed: 0,
  badResponse: 0,
  retries: 0,
  manualRetries: 0,
  bytes: 100_000,
};

describe("subscription health", () => {
  it("lists the subscriptions in one state, as the dashboard's tile links to", async () => {
    renderApp("/subscriptions?health=silent", {
      handlers: [
        http.get(apiPath("/metrics/subscriptions"), () =>
          HttpResponse.json({ since: null, days: 7, subscriptions: [silent] }),
        ),
        http.get(apiPath("/subscriptions"), () => HttpResponse.json(noRows)),
        http.get(apiPath("/documents"), () => HttpResponse.json(noRows)),
        http.get(apiPath("/partners"), () => HttpResponse.json(noRows)),
        http.get(apiPath("/apigateways"), () => HttpResponse.json(noRows)),
        http.get(apiPath("/busgateways"), () => HttpResponse.json(noRows)),
      ],
    });
    const panel = within((await screen.findByRole("heading", { name: "Gone quiet, last 7 days" })).closest("section")!);
    expect(await panel.findByRole("link", { name: "Acme orders in" })).toHaveAttribute("href", "/subscriptions/10");
  });

  it("shows a subscription's week on its page, and says when it has gone quiet", async () => {
    const { handlers } = backend([sub({ type: "Receiving", receiverId: "NativeHttpReceiver" })]);
    renderApp("/scheduled-jobs/10", {
      handlers: [
        http.get(apiPath("/metrics/subscription"), () =>
          HttpResponse.json({
            id: 10,
            days: 7,
            since: "2026-09-01T00:00:00Z",
            status: "silent",
            lastExchangeHour: "2026-10-10T08:00:00Z",
            quietHours: 26,
            usualGapHours: 1.5,
            total: {
              ...summary({ total: 170, succeeded: 160, failed: 10, successRate: 94.1, p95Ms: 300, p50Ms: 120 }),
              failedAtMapper: 4,
              failedAtHandler: 6,
            },
            hourly: [],
            daily: [],
            nodes: [],
            errors: [
              { fingerprint: "f1", count: 6, lastSeenOn: "2026-10-10T07:00:00Z", sample: "Acme FTP refused the login" },
            ],
          }),
        ),
        http.get(apiPath("/subscriptions/schedulehealth"), () => HttpResponse.json([])),
        http.get(apiPath("/subscriptions/receiveattempts"), () => HttpResponse.json(noRows)),
        ...handlers,
      ],
    });

    const panel = within((await screen.findByRole("heading", { name: "Health, last 7 days" })).closest("section")!);
    expect(panel.getByText("Gone quiet")).toBeVisible();
    expect(panel.getByText(/Nothing has come through for 1d 2h|Nothing has come through for 26h 0m/)).toBeVisible();
    expect(panel.getByText("94.1%")).toBeVisible();
    expect(panel.getByText(/4 in the mapping, 6 in the delivery/)).toBeVisible();
    await waitFor(() => expect(panel.getByText("Acme FTP refused the login")).toBeVisible());
  });
});
