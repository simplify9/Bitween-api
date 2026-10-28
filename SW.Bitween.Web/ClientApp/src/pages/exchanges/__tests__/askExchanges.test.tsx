import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Ask your exchanges, on the Exchanges page. What is sent and how the answer is checked is the C#
 * suite's business (ExchangeQueryPromptTests, AskExchangesTests); this is the box, the filter it
 * applies and the answer line.
 */

const NONE = { result: [], totalCount: 0 };

const ANSWER = {
  query: "status=failed&range=24h",
  answer: "{count} failed exchanges in the last day",
  notes: ["There is no partner with id 99."],
  model: "claude-sonnet-5-5",
  sent: "Now: 2026-10-27 14:05\n<question>\nfailed in the last day\n</question>",
};

function openPage(aiConfigured: boolean, searched: string[] = [], answered = ANSWER, path = "/exchanges") {
  return renderApp(path, {
    config: { aiConfigured },
    handlers: [
      http.get(apiPath("/xchanges"), ({ request }) => {
        searched.push(decodeURIComponent(new URL(request.url).search));
        return HttpResponse.json({ result: [], totalCount: 42 });
      }),
      http.get(apiPath("/subscriptions"), () => HttpResponse.json(NONE)),
      http.get(apiPath("/partners"), () => HttpResponse.json(NONE)),
      http.get(apiPath("/documents"), () => HttpResponse.json(NONE)),
      http.get(apiPath("/xchanges/summary"), () =>
        HttpResponse.json({ counts: { processing: 0, success: 0, badResponse: 0, failed: 42 }, buckets: [], bucketMinutes: 0 }),
      ),
      http.post(apiPath("/xchangequeries"), async ({ request }) => {
        const body = (await request.json()) as { question: string; timeZone: string };
        expect(body.question).toBe("failed in the last day");
        expect(body.timeZone).toBeTruthy();
        return HttpResponse.json(answered);
      }),
    ],
  });
}

describe("Ask your exchanges", { timeout: 20000 }, () => {
  it("turns a question into the page's filter and answers with the page's own count", async () => {
    const searched: string[] = [];
    const { user } = openPage(true, searched);

    await user.click(await screen.findByRole("button", { name: "Ask" }));
    await user.type(screen.getByLabelText("Ask your exchanges"), "failed in the last day");
    await user.click(screen.getAllByRole("button", { name: "Ask" }).at(-1)!);

    const answer = await screen.findByRole("status", { name: "Answer" });
    await waitFor(() => expect(answer).toHaveTextContent("42 failed exchanges in the last day"));
    expect(within(answer).getByText("There is no partner with id 99.")).toBeInTheDocument();
    expect(searched.at(-1)).toContain("StatusFilter:1:3");

    await user.click(within(answer).getByRole("button", { name: "See what was sent" }));
    expect(within(answer).getByLabelText("What was sent to the AI provider")).toHaveTextContent("failed in the last day");

    // A filter changed by hand is no longer what was asked, so the answer goes.
    await user.click(screen.getByRole("button", { name: "Clear all filters" }));
    await waitFor(() => expect(screen.queryByRole("status", { name: "Answer" })).not.toBeInTheDocument());
  });

  it("keeps the page's filters when nothing usable came back", async () => {
    const unusable = { ...ANSWER, query: "", answer: "", notes: ["There is no partner with id 99."] };
    const { user, router } = openPage(true, [], unusable, "/exchanges?status=success");

    await user.click(await screen.findByRole("button", { name: "Ask" }));
    await user.type(screen.getByLabelText("Ask your exchanges"), "failed in the last day");
    await user.click(screen.getAllByRole("button", { name: "Ask" }).at(-1)!);

    expect(await screen.findByText("There is no partner with id 99.")).toBeVisible();
    expect(screen.getByLabelText("Ask your exchanges")).toBeVisible();
    expect(screen.queryByRole("status", { name: "Answer" })).not.toBeInTheDocument();
    expect(router.state.location.search).toBe("?status=success");
  });

  it("says where to turn it on until Settings has a provider key", async () => {
    const { user } = openPage(false);

    await user.click(await screen.findByRole("button", { name: "Ask" }));

    expect(screen.getByRole("link", { name: "Open Settings, AI in a new tab" })).toHaveAttribute("target", "_blank");
  });
});
