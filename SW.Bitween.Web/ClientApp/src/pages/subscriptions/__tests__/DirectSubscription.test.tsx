import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { summary } from "../../../__tests__/support/metricsFixtures";
import { backend, sub } from "./subscriptionBackend";

/** The week's metrics for subscription 10, with `total` exchanges in it. */
const week = (total: number) =>
  http.get(apiPath("/metrics/subscription"), () =>
    HttpResponse.json({
      id: 10,
      days: 7,
      since: null,
      status: null,
      lastExchangeHour: null,
      quietHours: null,
      usualGapHours: null,
      total: { ...summary({ total, succeeded: total, failed: 0, successRate: 100 }), failedAtMapper: 0, failedAtHandler: 0 },
      hourly: [],
      daily: [],
      nodes: [],
      errors: [],
    }),
  );

function open(total: number) {
  const { handlers } = backend([sub({ type: "ApiCall" })]);
  renderApp("/subscriptions/10", {
    handlers: [week(total), http.get(apiPath("/subscriptions/schedulehealth"), () => HttpResponse.json([])), ...handlers],
  });
}

describe("a direct subscription", () => {
  it("is named by its trigger, not called legacy, and is fed by the calls it got", async () => {
    open(32);

    expect(await screen.findByText("Direct calls · 32 in 7 days")).toBeVisible();
    expect(screen.getAllByText("Direct")[0]).toBeVisible();
    expect(screen.getAllByText("called by id")[0]).toBeVisible();
    expect(screen.queryByText("Legacy")).toBeNull();
    expect(screen.queryByText(/never runs/)).toBeNull();
  });

  it("only says nothing feeds it when nothing called it and no gateway points at it", async () => {
    open(0);

    expect(await screen.findByText("Nothing in 7 days and no gateway points here")).toBeVisible();
  });
});
