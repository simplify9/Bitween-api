import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { MirrorBring, MirrorFamily, MirrorSubscriptionView, MirrorTest } from "../../../api";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { backend, sub } from "../../subscriptions/__tests__/subscriptionBackend";

const mirror = http.get(apiPath("/mirror"), () =>
  HttpResponse.json({ target: true, sourceUrl: "https://bitween.acme.test", signIn: "token", configured: true }),
);

const view = (over: Partial<MirrorSubscriptionView> = {}): MirrorSubscriptionView => ({
  key: "Acme orders",
  onProd: true,
  prodId: "31",
  prodUrl: "https://bitween.acme.test",
  used: [
    { part: "used", name: "Subscription “Acme orders”", level: "same", prod: "Same", staging: "Same", note: null, fields: null, section: "subscriptions", stagingId: "10", type: "BusGateway" },
    {
      part: "used", name: "Partner “Acme”", level: "info", prod: "1 different", staging: "1 different", note: "Staging's differs from prod's.",
      fields: [{ path: "properties · region", level: "info", prod: "EU", staging: "US" }], section: "partners", stagingId: "4", type: null,
    },
  ],
  toBring: [],
  needsYou: [],
  ...over,
});

const family: MirrorFamily = {
  id: "f1",
  entry: "document",
  door: "Orders",
  partner: null,
  cut: 0,
  unlinked: false,
  members: [
    {
      prodExchangeId: "p1", startedOn: "2026-10-20T08:00:00Z", outcome: "success", status: null, errorTypes: [],
      input: { key: "k1", name: "in.json", format: "json", size: 20, notCopied: null, url: "/files/in" }, output: null, response: null,
      partner: null, handler: null, mapper: null, subscription: "Acme orders", informationType: "Orders", parent: null, rolled: null,
    },
  ],
};

const test = (over: Partial<MirrorTest> = {}): MirrorTest => ({
  state: "idle", busy: null, done: 0, total: 0, error: null, takenOn: null, reason: null, families: [], ranOn: null, runs: [],
  setupError: null, made: [], exchanges: 0, last: null, ...over,
});

describe("a subscription against prod", () => {
  it("says what differs and what's left to do, and brings what it needs", async () => {
    const asked: unknown[] = [];
    const { handlers } = backend([sub()]);
    const { user } = renderApp("/subscriptions/10", {
      handlers: [
        mirror,
        http.get(apiPath("/mirror/subscription"), () =>
          HttpResponse.json(view({
            toBring: [{ section: "partners", noun: "partner", key: "Globex", state: "new", why: "Picked.", reason: null, stagingId: null, type: null }],
            needsYou: [
              { id: "adapter:acme.mapper", group: "needsYou", text: "Upload the adapter acme.mapper on the Adapters page.", section: null, key: null, action: "adapters-installed" },
              {
                id: "fill:subscriptions:Acme orders:handler/properties/Url", group: "fill",
                text: "Fill in handler · Url of the subscription “Acme orders” with the address of your test system.",
                section: "subscriptions", key: "Acme orders", action: null, stagingId: "10", prodId: "31", type: "BusGateway",
                field: "handler · Url", noun: "subscription",
              },
              { id: "faked", group: "faked", text: "Faked in the same shape as prod's: the properties of “Acme”.", section: null, key: null, action: null },
            ],
          })),
        ),
        http.get(apiPath("/mirror/test"), () => HttpResponse.json(test())),
        http.post(apiPath("/mirror/bring"), async ({ request }) => {
          asked.push(await request.json());
          return HttpResponse.json<MirrorBring>({
            applied: false,
            pieces: [{ section: "partners", noun: "partner", key: "Globex", state: "new", why: "The subscription “Acme orders” uses it.", reason: null, stagingId: null, type: null }],
            needsYou: [],
            failed: [],
          });
        }),
        ...handlers,
      ],
    });

    expect(await screen.findByText("Prod: to do")).toBeVisible();
    expect(await screen.findByText("Partner “Acme”")).toBeVisible();
    expect(screen.getByText(/1 item differs from prod's, of the 2 it uses/)).toBeVisible();
    expect(screen.getByText(/Upload the adapter acme\.mapper/)).toBeVisible();
    // An address prod's has, left for staging's own: where to fill it in, and where to look it up.
    const fill = within(screen.getByText("handler · Url").closest("li")!);
    expect(fill.getByText("Stops tests")).toBeVisible();
    expect(fill.getByRole("link", { name: "Fill it on staging" })).toHaveAttribute("href", expect.stringContaining("/10"));
    expect(fill.getByRole("link", { name: /See it on prod/ })).toHaveAttribute("href", expect.stringMatching(/^https:\/\/bitween\.acme\.test\/.*31/));
    expect(screen.getByText(/Faked in the same shape as prod's/)).toBeVisible();
    expect(screen.getByText(/the partner “Globex”/)).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Bring it from prod" }));
    const dialog = await screen.findByRole("dialog", { name: "Bring what it needs from prod" });
    expect(await within(dialog).findByText("Comes from prod")).toBeVisible();
    expect(asked).toEqual([{ section: "subscriptions", keys: ["Acme orders"], apply: false }]);
  });

  it("takes prod's arrivals, runs them, and clears what they made", async () => {
    let current = test();
    const { handlers } = backend([sub()]);
    const { user } = renderApp("/subscriptions/10", {
      handlers: [
        mirror,
        http.get(apiPath("/mirror/subscription"), () => HttpResponse.json(view())),
        http.get(apiPath("/mirror/test"), () => HttpResponse.json(current)),
        http.post(apiPath("/mirror/testtake"), () => HttpResponse.json((current = test({ takenOn: new Date().toISOString(), families: [family] })))),
        http.post(apiPath("/mirror/testrun"), () =>
          HttpResponse.json((current = {
            ...current,
            ranOn: new Date().toISOString(),
            exchanges: 2,
            made: [{ correlation: "rehearsal-1", startedOn: new Date().toISOString() }],
            runs: [{ familyId: "f1", correlation: "rehearsal-1", ranOn: new Date().toISOString(), verdict: "same", notRun: null, diffs: [], members: [] }],
          })),
        ),
        http.post(apiPath("/mirror/testclear"), () =>
          HttpResponse.json((current = {
            ...current, ranOn: null, runs: [], made: [], exchanges: 0,
            last: { verdict: "same", ranOn: new Date().toISOString(), same: 1, check: 0, different: 0, notRun: 0 },
          })),
        ),
        ...handlers,
      ],
    });

    await user.click(await screen.findByRole("button", { name: "Take prod's arrivals" }));
    expect(await screen.findByText(/1 arrival from prod, taken/)).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Run the test" }));
    expect(await screen.findByText(/1 same\./)).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Clear test data (2)" }));
    await user.click(within(await screen.findByRole("dialog", { name: "Clear what the test made?" })).getByRole("button", { name: "Clear" }));
    expect(await screen.findByRole("button", { name: "Run the test" })).toBeVisible();
    expect(screen.queryByRole("button", { name: /Clear test data/ })).not.toBeInTheDocument();
    // How it went stays, for the lists.
    expect(screen.getByText(/1 same\./)).toBeVisible();
    expect(screen.getByText("What it made was cleared.")).toBeVisible();
  });

  it("won't run while an address it needs is still empty", async () => {
    const fill = {
      id: "fill:subscriptions:Acme orders:handler/properties/Url", group: "fill" as const,
      text: "Fill in handler · Url of the subscription “Acme orders” with the address of your test system.",
      section: "subscriptions", key: "Acme orders", action: null, stagingId: "10", prodId: "31", type: "BusGateway",
    };
    const { handlers } = backend([sub()]);
    renderApp("/subscriptions/10", {
      handlers: [
        mirror,
        http.get(apiPath("/mirror/subscription"), () => HttpResponse.json(view({ needsYou: [fill] }))),
        http.get(apiPath("/mirror/test"), () => HttpResponse.json(test({ takenOn: new Date().toISOString(), families: [family] }))),
        ...handlers,
      ],
    });

    expect(await screen.findByRole("button", { name: "Run the test" })).toBeDisabled();
    expect(screen.getByText(/1 address or secret it needs is still empty/)).toBeVisible();
  });

  it("asks first before running an adapter that doesn't come with Bitween", async () => {
    const ran: unknown[] = [];
    const taken = test({ takenOn: new Date().toISOString(), families: [family] });
    const { handlers } = backend([sub()]);
    const { user } = renderApp("/subscriptions/10", {
      handlers: [
        mirror,
        http.get(apiPath("/mirror/subscription"), () => HttpResponse.json(view({ customAdapters: ["acme.labels"] }))),
        http.get(apiPath("/mirror/test"), () => HttpResponse.json(taken)),
        http.post(apiPath("/mirror/testrun"), async ({ request }) => {
          ran.push(await request.json());
          return HttpResponse.json(taken);
        }),
        ...handlers,
      ],
    });

    await user.click(await screen.findByRole("button", { name: "Run the test" }));
    const ask = within(await screen.findByRole("dialog", { name: "Run it with these adapters?" }));
    expect(ask.getByText(/acme\.labels/)).toBeVisible();
    expect(ran).toEqual([]);
    await user.click(ask.getByRole("button", { name: "Run the test" }));
    await waitFor(() => expect(ran).toEqual([{ id: 10 }]));
  });

  it("says when prod has no subscription of its name", async () => {
    const { handlers } = backend([sub()]);
    renderApp("/subscriptions/10", {
      handlers: [mirror, http.get(apiPath("/mirror/subscription"), () => HttpResponse.json(view({ onProd: false, used: [] }))), ...handlers],
    });

    expect(await screen.findByText(/Prod has no subscription “Acme orders”/)).toBeVisible();
    expect(screen.getByText("Only on staging")).toBeVisible();
  });
});
