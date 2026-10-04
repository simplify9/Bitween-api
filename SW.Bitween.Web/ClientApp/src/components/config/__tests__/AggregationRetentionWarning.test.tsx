import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { Schedule } from "../../../api";
import { apiPath } from "../../../__tests__/support/renderApp";
import { server } from "../../../__tests__/support/server";
import { AggregationRetentionWarning } from "../AggregationRetentionWarning";

const monthly: Schedule = { recurrence: "Monthly", days: 1, hours: 2, minutes: 0, backwards: false };

const show = (schedules: Schedule[]) =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AggregationRetentionWarning schedules={schedules} />
    </QueryClientProvider>,
  );

/**
 * Retention removes exchanges whether or not an aggregation has rolled them up, so the aggregation's
 * schedule is where someone has to hear that theirs runs too seldom for it.
 */
describe("AggregationRetentionWarning", () => {
  it("says so when exchanges are removed before the aggregation rolls them up", async () => {
    let sent: unknown;
    server.use(
      http.post(apiPath("/retention/aggregation"), async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ warning: "This aggregation can go up to 31 days between runs." });
      }),
    );

    show([monthly]);

    expect(await screen.findByText(/up to 31 days between runs/)).toBeInTheDocument();
    expect(sent).toEqual({ schedules: [monthly] });
  });

  it("shows nothing while the schedule keeps up", async () => {
    let asked = false;
    server.use(
      http.post(apiPath("/retention/aggregation"), () => {
        asked = true;
        return HttpResponse.json({ warning: null });
      }),
    );

    const { container } = show([monthly]);

    await waitFor(() => expect(asked).toBe(true));
    expect(container).toBeEmptyDOMElement();
  });

  it("doesn't ask while there's no schedule", () => {
    // The server refuses anything it wasn't told about, so asking here would fail the test.
    const { container } = show([]);
    expect(container).toBeEmptyDOMElement();
  });
});
