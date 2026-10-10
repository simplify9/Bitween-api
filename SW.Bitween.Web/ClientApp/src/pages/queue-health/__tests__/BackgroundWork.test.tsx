import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** Queue health › Bitween's own work: the system jobs and the outbox. */
const summary = {
  totalConsumers: 0, unhealthyConsumers: 0, disconnectedConsumers: 0, totalQueueDepth: 0,
  totalRetryBacklog: 0, totalDeadLetterBacklog: 0, criticalAlerts: 0, warningAlerts: 0, generatedAt: "2026-10-10T09:00:00Z",
};

describe("Bitween's own work", () => {
  it("shows each system job's last and next run, a job that isn't scheduled, and the outbox", async () => {
    const soon = new Date(Date.now() + 10 * 60_000).toISOString();
    renderApp("/queue-health", {
      handlers: [
        http.get(apiPath("/ops/summary"), () => HttpResponse.json(summary)),
        ...["/ops/consumers", "/ops/retries", "/ops/deadletters", "/ops/alerts", "/ops/unattendedqueues"].map((p) =>
          http.get(apiPath(p), () => HttpResponse.json([])),
        ),
        http.get(apiPath("/ops/background"), () =>
          HttpResponse.json({
            jobs: [
              { name: "Automatic retries", does: "Re-runs failed exchanges.", cron: "0 * * * * ?", scheduled: true, state: "Normal", lastRanOn: "2026-10-10T08:59:00Z", nextRunOn: soon, runningNow: false },
              { name: "Exchange retention", does: "Deletes old exchanges.", cron: null, scheduled: false, state: "NotScheduled", lastRanOn: null, nextRunOn: null, runningNow: false },
            ],
            outbox: { pending: 4, oldestPendingOn: "2026-10-10T07:00:00Z", failing: 2, lastError: "RabbitMQ.Client.Exceptions.BrokerUnreachableException: None of the specified endpoints were reachable", publishedLastHour: 120 },
          }),
        ),
      ],
    });

    expect(await screen.findByText("Bitween's own work", undefined, { timeout: 5000 })).toBeVisible();
    expect(screen.getByText("Not scheduled")).toBeVisible();
    expect(screen.getByText("Hasn't run yet")).toBeVisible();
    expect(screen.getByText("4 waiting")).toBeVisible();
    expect(screen.getByText(/2 after a failed attempt/)).toBeVisible();
    expect(screen.getByText("None of the specified endpoints were reachable")).toBeVisible();
  });
});
