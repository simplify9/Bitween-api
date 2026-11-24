import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const ACME = { id: 7, name: "Acme Freight" };
const EVERYTHING = [
  "portal-subscriptions.view",
  "portal-exchanges.view",
  "portal-exchange-files.view",
  "portal-exchange-errors.view",
];

const failed = {
  id: "x-failed-1",
  startedOn: "2026-11-24T10:00:00Z",
  finishedOn: "2026-11-24T10:00:02Z",
  status: "failed",
  documentName: "Shipment",
  subscriptionId: 3,
  subscriptionName: "Acme webhook",
  error: "The remote server returned 500",
  files: [{ kind: "input", name: "in.json", size: 120, url: "https://bitween.test/api/files/seal/in" }],
};

/** The portal's API, recording which exchange searches were asked for. */
function portal(rows: unknown[] = [failed]) {
  const searches: string[] = [];
  const handlers = [
    http.get(apiPath("/portal/summary"), () => HttpResponse.json({ partnerName: ACME.name, running: 1, succeeded: 40, failed: 2 })),
    http.get(apiPath("/portal/subscriptions"), () =>
      HttpResponse.json([
        { id: 3, name: "Acme webhook", type: "Internal", documentName: "Shipment", paused: false },
        { id: 4, name: "Acme pickups", type: "Receiving", documentName: "Pickup", paused: true },
      ]),
    ),
    http.get(apiPath("/portal/xchanges"), ({ request }) => {
      searches.push(new URL(request.url).search);
      return HttpResponse.json({ result: rows, hasMore: false });
    }),
  ];
  return { handlers, searches };
}

describe("the partner portal", () => {
  it("is where a partner's person lands, whatever page they asked for", async () => {
    const { router } = renderApp("/exchanges", { as: { partner: ACME, permissions: EVERYTHING }, handlers: portal().handlers });

    expect(await screen.findByRole("heading", { name: ACME.name })).toBeVisible();
    expect(router.state.location.pathname).toBe("/portal");
    // None of the app's own navigation is there to be tried.
    expect(screen.queryByRole("navigation")).not.toBeInTheDocument();
  });

  it("sends our own people back to the app", async () => {
    const { router } = renderApp("/portal", { handlers: portal().handlers });
    await screen.findByRole("navigation");
    expect(router.state.location.pathname).not.toBe("/portal");
  });

  it("shows their exchanges and subscriptions, and an exchange's error and files", async () => {
    const { handlers, searches } = portal();
    const { user } = renderApp("/portal", { as: { partner: ACME, permissions: EVERYTHING }, handlers });

    const subscriptions = within((await screen.findByRole("heading", { name: "Subscriptions" })).closest("section")!);
    expect(await subscriptions.findByText("Acme pickups")).toBeVisible();
    expect(subscriptions.getByText("Paused")).toBeVisible();
    expect(screen.getByText("40")).toBeVisible();

    await user.click(await screen.findByRole("row", { name: /Acme webhook/ }));
    const dialog = within(screen.getByRole("dialog", { name: "Exchange" }));
    expect(dialog.getByText(/The remote server returned 500/)).toBeVisible();
    expect(dialog.getByRole("link", { name: "Download Received" })).toHaveAttribute("href", failed.files[0].url);
    await user.click(dialog.getByRole("button", { name: /close/i }));

    await user.click(screen.getByRole("radio", { name: "Failed" }));
    await screen.findByRole("row", { name: /Acme webhook/ });
    expect(searches.at(-1)).toContain("status=failed");
  });

  it("shows only what the role allows", async () => {
    renderApp("/portal", {
      as: { partner: ACME, permissions: ["portal-exchanges.view"] },
      handlers: portal([{ ...failed, error: null, files: null }]).handlers,
    });

    expect(await screen.findByRole("row", { name: /Acme webhook/ })).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Subscriptions" })).not.toBeInTheDocument();
  });

  it("says so when the role allows nothing", async () => {
    renderApp("/portal", { as: { partner: ACME, permissions: [] }, handlers: portal().handlers });
    expect(await screen.findByText("Nothing to show yet")).toBeVisible();
  });
});
