import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Explain this failure, from the exchange panel. What is masked and sent is the C# suite's
 * business (MaskingTests, FailureExplanationTests); this is the button, the dialog and the answer.
 */

const EXCHANGE_ID = "3f2a9c1e-0000-4000-8000-000000000002";
const NONE = { result: [], totalCount: 0 };

/** GET /xchanges row (`RawXchangeRow`): finished, not successful - failed. */
const FAILED = {
  id: EXCHANGE_ID, subscriptionId: 12, subscriptionName: "Orders in", documentId: 3, documentName: "SHIPMENT_ORDER",
  mapperId: "NativeMapper", status: false, exception: "flag: can't read <text> as a boolean",
  finishedOn: "2026-09-24T08:00:02Z", startedOn: "2026-09-24T08:00:00Z",
  inputFileName: "input.json", outputFileName: null, responseFileName: null,
  inputFileSize: 20, outputFileSize: 0, responseFileSize: 0, inputKey: "in-1", outputKey: null, responseKey: null,
  promotedProperties: {}, retryFor: null, aggregationXchangeId: null, responseBad: null, correlationId: null,
  partnerId: null, scheduledRetryOn: null, hasRetry: false,
};

function openFailed(aiConfigured: boolean, onExplain = () => {}) {
  return renderApp(`/exchanges?ids=${EXCHANGE_ID}`, {
    config: { aiConfigured },
    handlers: [
      http.get(apiPath("/xchanges"), () => HttpResponse.json({ result: [FAILED], totalCount: 1 })),
      http.get(apiPath("/bitweendocs"), () => HttpResponse.json({ key: "in-1", data: '{"order":{"flag":"maybe"}}' })),
      http.get(apiPath("/subscriptions"), () => HttpResponse.json(NONE)),
      http.get(apiPath("/partners"), () => HttpResponse.json(NONE)),
      http.get(apiPath("/documents"), () => HttpResponse.json(NONE)),
      http.get(apiPath("/xchanges/summary"), () =>
        HttpResponse.json({ counts: { processing: 0, success: 0, badResponse: 0, failed: 1 }, buckets: [], bucketMinutes: 0 }),
      ),
      http.post(apiPath("/xchangeexplanations"), async ({ request }) => {
        expect(await request.json()).toEqual({ xchangeId: EXCHANGE_ID });
        onExplain();
        return HttpResponse.json({
          summary: "The flag can't be read as yes or no.",
          cause: "The rule for flag converts to boolean, and the partner sent text.",
          fix: "Add a lookup from the partner's words to true and false.",
          model: "claude-sonnet-5-5",
          sent: 'Failed step: mapping\n{ "order": { "flag": "<text>" } }',
        });
      }),
    ],
  });
}

describe("Explain this failure", { timeout: 20000 }, () => {
  it("explains a failed exchange in three parts and shows exactly what was sent", async () => {
    let asked = 0;
    const { user } = openFailed(true, () => asked++);
    await screen.findByRole("complementary", { name: `Exchange ${EXCHANGE_ID}` });

    await user.click(screen.getByRole("button", { name: /Explain this failure/ }));
    const dialog = await screen.findByRole("dialog", { name: "Explain this failure" });
    // Nothing goes until asked, and the dialog says what will.
    expect(asked).toBe(0);
    expect(within(dialog).getByText(/Every value is/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: /^Explain$/ }));

    expect(await within(dialog).findByText("The flag can't be read as yes or no.")).toBeInTheDocument();
    expect(within(dialog).getByText(/Add a lookup/)).toBeInTheDocument();
    expect(within(dialog).getByText(/claude-sonnet-5-5/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "See what was sent" }));
    expect(within(dialog).getByLabelText("What was sent to the AI provider")).toHaveTextContent('"flag": "<text>"');
  });

  it("says where to turn it on until Settings has a provider key", async () => {
    const { user } = openFailed(false);
    await screen.findByRole("complementary", { name: `Exchange ${EXCHANGE_ID}` });

    await user.click(screen.getByRole("button", { name: /Explain this failure/ }));

    const dialog = await screen.findByRole("dialog", { name: "Explain this failure" });
    expect(within(dialog).getByRole("link", { name: "Open Settings, AI in a new tab" })).toHaveAttribute("target", "_blank");
  });
});
