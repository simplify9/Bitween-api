import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { backend as subscriptionBackend, sub } from "./subscriptionBackend";

const ACME_MAPPING = {
  partnerId: 7,
  partnerName: "Acme Freight",
  mappingRules: '{"version":1,"fields":[{"target":["reference"],"from":{"kind":"path","path":"orderId"}}]}',
  changedOn: "2026-11-24T10:00:00Z",
  changedBy: "Pat at Acme",
  setAside: false,
  inUse: true,
  waitingChangeId: null,
};

/** The subscription page, with Acme mapping it their own way. */
function backend() {
  const removed: unknown[] = [];
  const handlers = [
    http.get(apiPath("/subscriptions/10/partnermappings"), () => HttpResponse.json(removed.length ? [] : [ACME_MAPPING])),
    http.post(apiPath("/subscriptions/10/removepartnermapping"), async ({ request }) => {
      removed.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
    ...subscriptionBackend([sub()]).handlers,
  ];
  return { handlers, removed };
}

describe("partners with their own mapping, on the subscription's page", () => {
  it("are listed with who changed it, their rules can be read, and they can be put back on ours", async () => {
    const { handlers, removed } = backend();
    const { user } = renderApp("/subscriptions/10?stage=transformation", { handlers });

    expect(await screen.findByText("Partners with their own mapping", {}, { timeout: 10000 })).toBeVisible();
    expect(screen.getByText(/by Pat at Acme/)).toBeVisible();

    await user.click(screen.getByRole("button", { name: "See rules" }));
    expect(within(screen.getByRole("dialog", { name: "Acme Freight's mapping" })).getByText(/reference/)).toBeVisible();
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: /close/i }));

    await user.click(screen.getByRole("button", { name: "Remove" }));
    await user.click(screen.getByRole("button", { name: "Remove their mapping" }));
    await waitFor(() => expect(removed).toEqual([{ partnerId: 7 }]));
    await waitFor(() => expect(screen.queryByText("Partners with their own mapping")).not.toBeInTheDocument());
  });

  it("can't be removed without the right to edit the subscription", async () => {
    renderApp("/subscriptions/10?stage=transformation", {
      as: { permissions: ALL_PERMISSIONS.filter((p) => p !== "subscriptions.edit") },
      handlers: backend().handlers,
    });
    expect(await screen.findByRole("button", { name: "See rules" }, { timeout: 10000 })).toBeVisible();
    expect(screen.queryByRole("button", { name: "Remove" })).not.toBeInTheDocument();
  });

  it("says when they couldn't be read, with a way to try again", async () => {
    renderApp("/subscriptions/10?stage=transformation", {
      handlers: [
        http.get(apiPath("/subscriptions/10/partnermappings"), () => new HttpResponse(null, { status: 500 })),
        ...backend().handlers,
      ],
    });
    expect(await screen.findByRole("button", { name: /try again/i }, { timeout: 10000 })).toBeVisible();
  });

  it("show a change waiting for approval, and the history with an earlier version to put back", async () => {
    const putBack: number[] = [];
    const version = (id: number, status: string, requestedOn: string) => ({
      ...ACME_MAPPING, id, subscriptionId: 10, subscriptionName: "Orders", status, backToOurs: false,
      requestedOn, requestedBy: "Pat at Acme", decidedOn: null, decidedBy: null, note: null,
      canPutBack: status !== "waiting",
    });
    const { user } = renderApp("/subscriptions/10?stage=transformation", {
      handlers: [
        http.get(apiPath("/subscriptions/10/partnermappings"), () =>
          HttpResponse.json([{ ...ACME_MAPPING, waitingChangeId: 43 }]),
        ),
        http.get(apiPath("/partnerchanges/history"), () =>
          HttpResponse.json([
            version(43, "waiting", "2026-11-25T09:00:00Z"),
            version(42, "live", "2026-11-24T10:00:00Z"),
            version(41, "live", "2026-11-23T10:00:00Z"),
          ]),
        ),
        http.post(apiPath("/partnerchanges/:id/putback"), ({ params }) => {
          putBack.push(Number(params.id));
          return new HttpResponse(null, { status: 204 });
        }),
        ...backend().handlers,
      ],
    });

    expect(await screen.findByText("Waiting", {}, { timeout: 10000 })).toBeVisible();
    expect(screen.getByRole("button", { name: "Review" })).toBeVisible();

    await user.click(screen.getByRole("button", { name: "History" }));
    const history = await screen.findByRole("dialog", { name: "Acme Freight's mapping history" });
    // The version in use now has nothing to put back; only the older one does.
    const putBackButtons = await within(history).findAllByRole("button", { name: "Use this version again" });
    expect(putBackButtons).toHaveLength(1);
    await user.click(putBackButtons[0]);
    await user.click(screen.getByRole("button", { name: "Use this version" }));
    await waitFor(() => expect(putBack).toEqual([41]));
  });
});
