import { cleanup, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";

const ACME = { id: 7, name: "Acme Freight" };
const LOOK = ["portal-exchanges.view", "portal-exchange-files.view", "portal-exchange-errors.view", "portal-subscriptions.view"];
const RETRY = [...LOOK, "portal-exchanges.operate"];

/** A row as the portal's search answers it: our team's row, stripped on the server. */
const failed = {
  id: "00000000-0000-4000-8000-000000000001",
  subscriptionId: 3,
  subscriptionName: "Acme webhook",
  documentId: 5,
  documentName: "SHIPMENT",
  mapperId: "mapper",
  status: false,
  responseBad: null,
  exception: "The remote server returned 500",
  startedOn: "2026-11-24T10:00:00Z",
  finishedOn: "2026-11-24T10:00:02Z",
  inputFileName: "in.json",
  inputFileSize: 12,
  inputUrl: "https://bitween.test/api/files/seal/in",
  outputFileSize: 0,
  responseFileSize: 0,
  promotedProperties: { orderNo: "A-1" },
  retryFor: null,
  hasRetry: false,
  aggregationXchangeId: null,
  correlationId: null,
  partnerId: 7,
  scheduledRetryOn: null,
};

/** The portal's API, recording what the Exchanges page asked of it. */
function portal(rows: unknown[] = [failed]) {
  const asked: string[] = [];
  const retried: unknown[] = [];
  const handlers = [
    http.get(apiPath("/portal/xchangesearch"), ({ request }) => {
      asked.push(new URL(request.url).search);
      return HttpResponse.json({ result: rows, totalCount: rows.length });
    }),
    http.get(apiPath("/portal/xchangesummary"), () =>
      HttpResponse.json({ counts: { processing: 0, success: 0, badResponse: 0, failed: rows.length }, buckets: [], bucketMinutes: 0 }),
    ),
    http.get(apiPath("/portal/subscriptions"), () =>
      HttpResponse.json([{ id: 3, name: "Acme webhook", type: "Internal", documentName: "SHIPMENT", paused: false }]),
    ),
    http.get(apiPath("/portal/informationtypes"), () =>
      HttpResponse.json([{ id: 5, name: "SHIPMENT", code: null, promotedPropertyKeys: ["orderNo"] }]),
    ),
    http.get("https://bitween.test/api/files/seal/in", () => HttpResponse.text('{"orderNo":"A-1"}')),
    http.post(apiPath("/portal/retry"), async ({ request }) => {
      retried.push(await request.json());
      return HttpResponse.json({ xchangeId: "00000000-0000-4000-8000-000000000002" });
    }),
  ];
  return { handlers, asked, retried };
}

describe("the partner portal, in the app's own frame", () => {
  it("lands a partner's person on their Exchanges, with only their pages in the sidebar", async () => {
    const { handlers, asked } = portal();
    const { router } = renderApp("/exchanges", { as: { partner: ACME, permissions: RETRY }, handlers });

    const nav = await screen.findByRole("navigation", { name: "Main" });
    await waitFor(() => expect(router.state.location.pathname).toBe("/portal"));
    expect(within(nav).getByRole("link", { name: "Exchanges" })).toHaveAttribute("href", "/portal");
    expect(within(nav).getByRole("link", { name: "Subscriptions" })).toBeVisible();
    expect(within(nav).queryByRole("link", { name: "Partners" })).not.toBeInTheDocument();
    expect(await screen.findByText("Acme webhook")).toBeVisible();
    expect(asked.length).toBeGreaterThan(0);
  });

  it("takes a partner's person from a look-alike address to the portal", async () => {
    for (const path of ["/portal-old", "/portal/nothing-here"]) {
      const { router } = renderApp(path, { as: { partner: ACME, permissions: RETRY }, handlers: portal().handlers });
      await waitFor(() => expect(router.state.location.pathname).toBe("/portal"));
      cleanup();
    }
  });

  it("sends a partner's person whose role has no Exchanges to the first page it does open", async () => {
    const { router } = renderApp("/", {
      as: { partner: ACME, permissions: ["portal-mappings.view"] },
      handlers: [http.get(apiPath("/portal/mappings"), () => HttpResponse.json([]))],
    });
    await waitFor(() => expect(router.state.location.pathname).toBe("/portal/mappings"));
    expect(screen.queryByText(/don't have access/i)).not.toBeInTheDocument();
  });

  it("keeps our own people out of the portal, and its pages out of their sidebar", async () => {
    const { router } = renderApp("/portal", { handlers: portal().handlers, as: { permissions: ALL_PERMISSIONS } });
    const nav = await screen.findByRole("navigation", { name: "Main" });
    await waitFor(() => expect(router.state.location.pathname).not.toBe("/portal"));
    expect(within(nav).queryByText("Partner portal")).not.toBeInTheDocument();
  });

  it("opens an exchange with its document, error and Retry, and none of our tools", async () => {
    const { handlers, retried } = portal();
    const { user } = renderApp("/portal", { as: { partner: ACME, permissions: RETRY }, handlers });

    await user.click((await screen.findAllByText("Acme webhook"))[0]);
    expect((await screen.findAllByText(/The remote server returned 500/)).length).toBeGreaterThan(1);
    expect(await screen.findByText(/"orderNo"/)).toBeVisible();
    // No links into our pages, no bulk selection, no AI.
    expect(screen.queryByRole("link", { name: "Acme webhook" })).not.toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Explain/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Ask/i })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Retry/ }));
    // Always the current settings: no choice to make.
    const dialog = await screen.findByRole("dialog", { name: "Retry this exchange?" });
    expect(within(dialog).queryByRole("checkbox")).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Retry" }));
    await waitFor(() => expect(retried).toEqual([{ xchangeId: failed.id }]));
  });

  it("offers no Retry to a role that may only look, and no filter on errors it can't read", async () => {
    const { user } = renderApp("/portal", {
      as: { partner: ACME, permissions: ["portal-exchanges.view"] },
      handlers: portal([{ ...failed, exception: null }]).handlers,
    });
    await user.click((await screen.findAllByText("Acme webhook"))[0]);
    await screen.findByText("Exchange id");
    expect(screen.queryByRole("button", { name: /Retry/ })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Filter" }));
    const menu = screen.getByRole("menu", { name: "Add a filter" });
    expect(within(menu).getByRole("menuitem", { name: "Subscription" })).toBeVisible();
    expect(within(menu).queryByRole("menuitem", { name: "Partner" })).not.toBeInTheDocument();
    expect(within(menu).queryByRole("menuitem", { name: /Error message/ })).not.toBeInTheDocument();
  });
});
