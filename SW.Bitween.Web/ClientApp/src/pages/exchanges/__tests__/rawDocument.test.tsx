import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * The exchange drawer's Raw/Formatted toggle.
 *
 * Laying a payload out and colouring it are pinned in src/lib/__tests__/documentPreview.test.ts
 * and documentHighlight.test.ts; this is about the drawer asking for neither once Raw is on.
 */

const EXCHANGE_ID = "3f2a9c1e-0000-4000-8000-000000000001";

/** One line, no spaces: how a partner sends it. */
const MINIFIED_JSON = `{"order":{"customer":"Ali","line":[{"sku":"A1"}]}}`;

/** What the drawer fetches per stage, keyed by the row's `*Key`. */
const DOCUMENTS: Record<string, string> = {
  "in-1": MINIFIED_JSON,
  "out-1": `{"who":"Ali"}`,
};

/** GET /xchanges: `RawXchangeRow` in src/api/http/exchanges.ts. Received and mapped, not yet handled. */
const EXCHANGE = {
  id: EXCHANGE_ID,
  subscriptionId: 12,
  subscriptionName: "Orders in",
  documentId: 3,
  documentName: "SHIPMENT_ORDER",
  mapperId: "NativeMapper",
  status: null,
  exception: null,
  finishedOn: null,
  startedOn: "2026-09-24T08:00:00Z",
  inputFileName: "input.json",
  outputFileName: "mapped.json",
  responseFileName: null,
  inputFileSize: MINIFIED_JSON.length,
  outputFileSize: 13,
  responseFileSize: 0,
  inputKey: "in-1",
  outputKey: "out-1",
  responseKey: null,
  promotedProperties: {},
  retryFor: null,
  aggregationXchangeId: null,
  responseBad: null,
  correlationId: null,
  partnerId: null,
  scheduledRetryOn: null,
  hasRetry: false,
};

const NONE = { result: [], totalCount: 0 };

function openExchanges(documents = DOCUMENTS) {
  return renderApp(`/exchanges?ids=${EXCHANGE_ID}`, {
    handlers: [
      http.get(apiPath("/xchanges"), () => HttpResponse.json({ result: [EXCHANGE], totalCount: 1 })),
      http.get(apiPath("/bitweendocs"), ({ request }) => {
        const key = new URL(request.url).searchParams.get("documentKey")!;
        return HttpResponse.json({ key, data: documents[key] });
      }),
      // The filters' options, none of which this is about.
      http.get(apiPath("/subscriptions"), () => HttpResponse.json(NONE)),
      http.get(apiPath("/partners"), () => HttpResponse.json(NONE)),
      http.get(apiPath("/documents"), () => HttpResponse.json(NONE)),
    ],
  });
}

describe("an exchange's document", () => {
  it("shows the bytes as they arrived, uncoloured, under Raw", async () => {
    // The Raw toggle's whole promise is that nothing has been done to the document. Colour is a
    // claim about its structure, and the pane was making that claim on both sides of the
    // toggle — including for a payload that never parsed, where the parts a grammar still
    // recognises would come out looking fine.
    const { user } = openExchanges();

    const row = (await screen.findAllByRole("row"))[1];
    await user.click(within(row).getAllByRole("cell").at(-1)!);
    // The drawer opens on the furthest stage with a document, which here is the mapped one.
    // Raw is a promise about what arrived, so ask for that.
    await user.click(screen.getByTitle("Show the Input document"));

    // Formatted is the default, and it parsed, so it is coloured.
    const pane = () => document.querySelector<HTMLElement>(".doc-hl-dark")!;
    await waitFor(() => expect(pane()).toHaveTextContent('"customer"'));
    expect(pane().querySelector(".hljs-attr")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Raw" }));

    // The same document, one line again, and no token spans anywhere in it.
    expect(pane()).toHaveTextContent(MINIFIED_JSON, { normalizeWhitespace: false });
    expect(pane().querySelectorAll("span")).toHaveLength(0);
  });
});
