import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { backend, sub } from "./subscriptionBackend";

/**
 * Suggest a retry policy with AI, from the subscription page. What is sent and how the suggestion
 * is checked is the C# suite's business (RetryPolicyPromptTests, RetrySuggestionTests); this is the
 * dialog, saving the suggestion as a policy, and picking it.
 */

globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

const GROUPS = [
  {
    name: "Outages",
    priority: 0,
    appliesTo: ["Error"],
    action: "Allow",
    matchers: [{ type: "exceptionType", value: "System.Net.Http.HttpRequestException" }],
    budget: {
      maxAttemptsPerError: 3,
      maxAttemptsTotal: 50,
      whenExhausted: "Hold",
      delayStrategy: { type: "exponential", initialDelayMs: 30000, multiplier: 2, maxDelayMs: 600000 },
    },
    notes: "Connection failures pass by themselves.",
  },
];

describe("suggesting a retry policy", { timeout: 20000 }, () => {
  it("shows what would change, saves the suggestion as a new policy and picks it", async () => {
    const created: unknown[] = [];
    const { handlers, saves } = backend([sub()]);
    const { user } = renderApp("/subscriptions/10", {
      config: { aiConfigured: true },
      handlers: [
        http.post(apiPath("/retrysuggestions"), async ({ request }) => {
          expect(await request.json()).toEqual({ subscriptionId: 10 });
          return HttpResponse.json({
            name: "Retry outages",
            summary: "Retries connection failures, stops the rest.",
            groups: GROUPS,
            coverage: [
              {
                error: "System.Net.Http.HttpRequestException: Connection refused (<url>)",
                count: 40, retried: 10, fixedByRetry: 9,
                now: "Not retried: no retry policy",
                suggested: 'Retried up to 3 times by "Outages"',
              },
            ],
            failures: 40, days: 30, model: "claude-sonnet-5-5", sent: "<failures>...</failures>",
          });
        }),
        http.post(apiPath("/retrypolicies"), async ({ request }) => {
          created.push(await request.json());
          return HttpResponse.json(9);
        }),
        http.get(apiPath("/retrypolicies"), () =>
          HttpResponse.json({ result: created.length ? [{ id: 9, name: "Retry outages", groupCount: 1, usedByCount: 0 }] : [], totalCount: created.length }),
        ),
        ...handlers,
      ],
    });

    await user.click(await screen.findByRole("button", { name: /Suggest with AI/ }));
    const dialog = await screen.findByRole("dialog", { name: "Suggest a retry policy" });
    await user.click(within(dialog).getByRole("button", { name: "Suggest" }));

    expect(await within(dialog).findByText("Retries connection failures, stops the rest.")).toBeVisible();
    expect(within(dialog).getByText(/is a System\.Net\.Http\.HttpRequestException · up to 3 per exchange, 50 in all/)).toBeVisible();
    expect(within(dialog).getByText("9 of 10")).toBeVisible();
    expect(within(dialog).getByText('Retried up to 3 times by "Outages"')).toBeVisible();

    await user.click(within(dialog).getByRole("button", { name: "Save as a new policy" }));

    // Saved exactly as the server wrote it: nothing, like the hold, is lost on the way.
    await waitFor(() => expect(created).toEqual([{ name: "Retry outages", groups: GROUPS }]));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].retryPolicyId).toBe(9);
  });

  it("says where to turn it on until Settings has a provider key", async () => {
    const { handlers } = backend([sub()]);
    const { user } = renderApp("/subscriptions/10", { config: { aiConfigured: false }, handlers });

    await user.click(await screen.findByRole("button", { name: /Suggest with AI/ }));

    const dialog = await screen.findByRole("dialog", { name: "Suggest a retry policy" });
    expect(within(dialog).getByRole("link", { name: "Open Settings, AI in a new tab" })).toHaveAttribute("target", "_blank");
  });
});
