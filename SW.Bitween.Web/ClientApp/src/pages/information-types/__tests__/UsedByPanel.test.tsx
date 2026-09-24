import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * A panel list once it runs long: an information type carried by 45 subscriptions. Whether the
 * list still fits its ~360px panel is layout, which only a real browser can measure, so that half
 * stays in e2e/table-layout.spec.ts.
 */
const LONG_NAMES = [
  "Customer Aggregation Trace Out Manifest - Sodexi Cassini EDI",
  "Agent Tracing - Colissimo EDI Daily Reconciliation",
  "Customer_Aggregation_Scan_Out_CUSTOMS_Chronopost_Returns",
];

/** GET /documents/{id}: `RawDocument` in src/api/http/documents.ts. */
const PURCHASE_ORDER = {
  id: 5,
  code: "PURCHASE_ORDER",
  name: "Purchase order",
  documentFormat: "Json",
  busEnabled: false,
  busMessageTypeName: null,
  duplicateInterval: 0,
  disregardsUnfilteredMessages: false,
  promotedProperties: [],
  usedByCount: 45,
  retiredOn: null,
};

const empty = { result: [], totalCount: 0 };

describe("an information type's Used by panel", () => {
  it("pages and filters once it runs long", async () => {
    const { user } = renderApp("/information-types/5", {
      handlers: [
        http.get(apiPath("/documents/5"), () => HttpResponse.json(PURCHASE_ORDER)),
        // The subscriptions carrying the type — `RawSubscriptionRef`.
        http.get(apiPath("/subscriptions"), () =>
          HttpResponse.json({
            result: Array.from({ length: 45 }, (_, i) => ({
              id: 900000 + i,
              name: `${LONG_NAMES[i % LONG_NAMES.length]} ${i}`,
              type: "ApiCall",
            })),
            totalCount: 45,
          }),
        ),
        ...["/busgateways", "/xchanges", "/partners", "/audit"].map((p) =>
          http.get(apiPath(p), () => HttpResponse.json(empty)),
        ),
      ],
    });

    const panel = (await screen.findByRole("heading", { name: "Used by" })).closest("section")!;
    expect(within(panel).getByRole("columnheader", { name: "Type" })).toBeInTheDocument();
    expect(within(panel).getByText("1–10 of 45")).toBeVisible();
    expect(within(panel).getAllByRole("link")).toHaveLength(10);

    // The pager counts what the search matched, not the whole list: 15 of the 45 share this name.
    const box = within(panel).getByPlaceholderText("Search 45 subscriptions");
    await user.type(box, LONG_NAMES[0].slice(0, 20));
    expect(within(panel).getByText("1–10 of 15")).toBeVisible();
    expect(within(panel).queryByText(/of 45$/)).not.toBeInTheDocument();

    // Filtering to one page takes the pager away but leaves the box that got you there.
    await user.type(box, " Trace Out Manifest - Sodexi Cassini EDI 3");
    expect(within(panel).getAllByRole("link")).toHaveLength(5); // 3, 30, 33, 36, 39
    expect(within(panel).queryByText(/\d+–\d+ of \d+/)).not.toBeInTheDocument();
    expect(within(panel).queryByRole("button", { name: "Next" })).not.toBeInTheDocument();
    expect(box).toBeVisible();
  });
});
