import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";

const RULES = '{"version":1,"fields":[{"target":["reference"],"from":{"kind":"path","path":"orderId"}}]}';
const OURS = '{"version":1,"fields":[{"target":["id"],"from":{"kind":"path","path":"orderId"}}]}';

const CHANGE = {
  id: 41,
  subscriptionId: 10,
  subscriptionName: "Orders webhook",
  partnerId: 7,
  partnerName: "Acme Freight",
  status: "waiting",
  backToOurs: false,
  requestedOn: "2026-11-25T09:00:00Z",
  requestedBy: "Pat at Acme",
  decidedOn: null,
  decidedBy: null,
  note: null,
};

/** One change waiting, recording what was decided. */
function backend(problem: string | null = null) {
  const approved: unknown[] = [];
  const rejected: unknown[] = [];
  const decided = () => approved.length + rejected.length > 0;
  const handlers = [
    http.get(apiPath("/partnerchanges"), ({ request }) =>
      HttpResponse.json(new URL(request.url).searchParams.get("waiting") === "true" && !decided() ? [CHANGE] : []),
    ),
    http.get(apiPath("/partnerchanges/count"), () => HttpResponse.json(decided() ? 0 : 1)),
    http.get(apiPath("/partnerchanges/41"), () =>
      HttpResponse.json({ ...CHANGE, mappingRules: RULES, currentRules: OURS, currentIsOurs: true, problem }),
    ),
    http.post(apiPath("/partnerchanges/41/approve"), async ({ request }) => {
      approved.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
    http.post(apiPath("/partnerchanges/41/reject"), async ({ request }) => {
      rejected.push(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
  ];
  return { handlers, approved, rejected };
}

describe("partner changes waiting for approval", () => {
  it("are listed, counted in the sidebar, and approved beside what the partner gets now", async () => {
    const { handlers, approved } = backend();
    const { user } = renderApp("/partner-changes", { handlers });

    const nav = await screen.findByRole("navigation", { name: "Main" });
    expect(await within(nav).findByTitle("1 waiting for approval")).toBeVisible();

    await user.click(await screen.findByText("Acme Freight"));
    const dialog = await screen.findByRole("dialog", { name: "Acme Freight: Orders webhook" });
    expect(await within(dialog).findByTestId("rules-compare")).toBeVisible();
    expect(within(dialog).getByText("Used for them now: the subscription's")).toBeVisible();

    await user.click(within(dialog).getByRole("button", { name: "Approve" }));
    await waitFor(() => expect(approved).toHaveLength(1));
    expect(await screen.findByText("Nothing waits for approval")).toBeVisible();
    await waitFor(() => expect(within(nav).queryByTitle(/waiting for approval/)).not.toBeInTheDocument());
  });

  it("are rejected with a reason the partner sees", async () => {
    const { handlers, rejected } = backend();
    const { user } = renderApp("/partner-changes", { handlers });

    await user.click(await screen.findByText("Acme Freight"));
    const dialog = await screen.findByRole("dialog");
    await user.type(await within(dialog).findByPlaceholderText("The partner sees this"), "Keep the id field");
    await user.click(within(dialog).getByRole("button", { name: "Reject" }));
    await waitFor(() => expect(rejected).toEqual([{ note: "Keep the id field" }]));
  });

  it("can't be approved when they no longer fit ours", async () => {
    const { handlers } = backend("Your mapping can't read these internal values: company.region.");
    const { user } = renderApp("/partner-changes", { handlers });

    await user.click(await screen.findByText("Acme Freight"));
    const dialog = await screen.findByRole("dialog");
    expect(await within(dialog).findByText(/can't read these internal values/)).toBeVisible();
    expect(within(dialog).getByRole("button", { name: "Approve" })).toBeDisabled();
  });

  it("are only looked at by a role that can't decide", async () => {
    const { user } = renderApp("/partner-changes", {
      as: { permissions: ALL_PERMISSIONS.filter((p) => p !== "partner-changes.operate") },
      handlers: backend().handlers,
    });

    await user.click(await screen.findByText("Acme Freight"));
    const dialog = await screen.findByRole("dialog");
    await within(dialog).findByTestId("rules-compare");
    expect(within(dialog).queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
  });
});
