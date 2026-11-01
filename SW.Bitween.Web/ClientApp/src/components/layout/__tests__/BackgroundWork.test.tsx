import { act, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { MirrorBring, MirrorProdKinds, MirrorProdList } from "../../../api";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { empty, none, page } from "../../../__tests__/support/listPage";
import { backend, raw, sub } from "../../../pages/subscriptions/__tests__/subscriptionBackend";

/** The SignalR connection, faked: each test says what the server tells it. */
type Change = { id: string; status: string; done?: number; total?: number; summary?: string };
const hub = vi.hoisted(() => ({ changed: null as null | ((change: Change) => void) }));
vi.mock("@microsoft/signalr", () => ({
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return {
        on: (_: string, handler: (change: Change) => void) => {
          hub.changed = handler;
        },
        onreconnected() {},
        onclose() {},
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
      };
    }
  },
  HttpTransportType: { WebSockets: 1 },
  LogLevel: { None: 6 },
}));

const tell = (change: Change) => act(() => hub.changed!(change));

/** A staging that mirrors prod, with an aggregations page that offers to bring prod's. */
const mirror = [
  http.get(apiPath("/mirror"), () =>
    HttpResponse.json({ target: true, sourceUrl: "https://bitween.acme.test", signIn: "token", configured: true }),
  ),
  http.get(apiPath("/mirror/prodkinds"), () =>
    HttpResponse.json<MirrorProdKinds>({ subscriptions: { all: 5, Aggregation: 2 }, apiGateways: 0, busGateways: 0, error: null }),
  ),
  http.get(apiPath("/subscriptions"), () => HttpResponse.json(page([raw({ id: 8, name: "Returns rollup", type: "Aggregation" })]))),
  ...empty("/documents", "/partners", "/workgroups", "/retrypolicies"),
  ...none("/subscriptions/lastruns", "/subscriptions/schedulehealth"),
];

const prodList: MirrorProdList = {
  section: "subscriptions",
  noun: "subscription",
  plural: "subscriptions",
  days: 7,
  trafficNote: null,
  items: [
    {
      key: "Orders rollup",
      prodId: "31",
      type: "Aggregation",
      detail: "Acme",
      state: "missing",
      stagingId: null,
      reason: null,
      traffic: { total: 1204, failed: 3, lastOn: null, daily: [100, 200, 150, 180, 190, 200, 184] },
    },
  ],
};

/** What bringing "Orders rollup" comes to, or - `applied` - brought. */
const brought = (applied: boolean): MirrorBring => ({
  applied,
  pieces: [
    { section: "subscriptions", noun: "subscription", key: "Orders rollup", state: "new", why: "Picked.", reason: null, stagingId: applied ? "41" : null, type: "Aggregation" },
  ],
  needsYou: [],
  failed: [],
});

/** Prod's list, which the server carries on in the background as `id`. */
function slowProdList(id: string, asked: { prefer: string | null; label: string }[] = []) {
  return [
    http.get(apiPath("/mirror/prod"), ({ request }) => {
      asked.push({
        prefer: request.headers.get("Prefer"),
        label: decodeURIComponent(request.headers.get("X-Bitween-Label") ?? ""),
      });
      return HttpResponse.json({ id, status: "Running" }, { status: 202 });
    }),
    http.get(apiPath(`/background/${id}/result`), () => HttpResponse.json(prodList)),
  ];
}

describe("work carried on in the background", () => {
  it("shows in the header while it runs, and fills its window in when it's ready", async () => {
    const asked: { prefer: string | null; label: string }[] = [];
    renderApp("/aggregations?bring=1", { handlers: [...slowProdList("bg-1", asked), ...mirror] });

    // Twice: the desktop top bar and the phone's, which only CSS tells apart.
    await screen.findAllByRole("button", { name: "Background work: 1 running" });
    expect(asked[0]).toEqual({ prefer: "respond-async, wait=3", label: "Bring from prod: prod's subscriptions" });

    tell({ id: "bg-1", status: "Done" });

    const dialog = await screen.findByRole("dialog", { name: "Bring aggregations from prod" });
    expect(await within(dialog).findByText("1,204")).toBeVisible();
    // Finished on the page that asked, which filled in: nothing left for the header to say.
    await waitFor(() => expect(screen.queryAllByRole("button", { name: /^Background work/ })).toHaveLength(0));
  });

  it("says so when it finishes while you're on another page, and Open goes back to its window", async () => {
    const { user, router } = renderApp("/aggregations?bring=1", {
      handlers: [...slowProdList("bg-2"), ...mirror, ...empty("/apigateways", "/busgateways", "/xchanges")],
    });
    await screen.findAllByRole("button", { name: "Background work: 1 running" });

    await act(() => router.navigate("/partners"));
    tell({ id: "bg-2", status: "Done" });

    const notice = await screen.findByRole("status");
    expect(notice).toHaveTextContent("Bring from prod: prod's subscriptions is ready");
    // A progress push that arrives after it finished is old news: it stays ready.
    tell({ id: "bg-2", status: "Running", done: 1, total: 2 });
    expect(await screen.findAllByRole("button", { name: "Background work: 1 ready" })).not.toHaveLength(0);
    await user.click(within(notice).getByRole("button", { name: "Open" }));

    await waitFor(() => expect(router.state.location.search).toBe("?bring=1"));
    const dialog = await screen.findByRole("dialog", { name: "Bring aggregations from prod" });
    expect(await within(dialog).findByText("1,204")).toBeVisible();
  });

  it("says when a bring whose window closed is done, and Open shows what it brought", async () => {
    const { user, router } = renderApp("/aggregations?bring=1", {
      handlers: [
        ...mirror,
        http.get(apiPath("/mirror/prod"), () => HttpResponse.json(prodList)),
        http.post(apiPath("/mirror/bring"), async ({ request }) => {
          const { apply } = (await request.json()) as { apply: boolean };
          return apply ? HttpResponse.json({ id: "bg-5", status: "Running" }, { status: 202 }) : HttpResponse.json(brought(false));
        }),
        http.get(apiPath("/background/bg-5/result"), () => HttpResponse.json(brought(true))),
      ],
    });
    const dialog = await screen.findByRole("dialog", { name: "Bring aggregations from prod" });
    await user.click(await within(dialog).findByRole("checkbox", { name: "Pick “Orders rollup”" }));
    await user.click(within(dialog).getByRole("button", { name: "See what comes" }));
    await user.click(await within(dialog).findByRole("button", { name: "Bring 1 item" }));
    await screen.findAllByRole("button", { name: "Background work: 1 running" });

    // Closed while it brings, on the page it was asked from: nothing there will show what it brought.
    await user.click(within(dialog).getAllByRole("button", { name: "Close" })[0]);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    tell({ id: "bg-5", status: "Done" });

    const notice = await screen.findByRole("status");
    expect(notice).toHaveTextContent("Bringing 1 subscription from prod is ready");
    await user.click(within(notice).getByRole("button", { name: "Open" }));

    const shown = await screen.findByRole("dialog", { name: "Brought from prod" });
    expect(await within(shown).findByText("Brought 1 item")).toBeVisible();
    expect(router.state.location.search).toBe("?brought=bg-5");
    await user.click(within(shown).getByRole("button", { name: "Done" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(router.state.location.search).toBe("");
  });

  it("says so too when that window closed on the page itself, where nothing else would show it", async () => {
    const { handlers } = backend([sub()]);
    const { user, router } = renderApp("/subscriptions/10", {
      handlers: [
        ...mirror,
        http.get(apiPath("/mirror/subscription"), () =>
          HttpResponse.json({
            key: "Acme orders", onProd: true, prodId: "31", prodUrl: "https://bitween.acme.test", used: [], needsYou: [],
            toBring: [{ section: "partners", noun: "partner", key: "Globex", state: "new", why: "Picked.", reason: null, stagingId: null, type: null }],
          }),
        ),
        http.get(apiPath("/mirror/test"), () =>
          HttpResponse.json({
            state: "idle", busy: null, done: 0, total: 0, error: null, takenOn: null, reason: null, families: [], ranOn: null, runs: [],
            setupError: null, made: [], exchanges: 0, last: null,
          }),
        ),
        http.post(apiPath("/mirror/bring"), async ({ request }) => {
          const { apply } = (await request.json()) as { apply: boolean };
          return apply ? HttpResponse.json({ id: "bg-6", status: "Running" }, { status: 202 }) : HttpResponse.json(brought(false));
        }),
        http.get(apiPath("/background/bg-6/result"), () => HttpResponse.json(brought(true))),
        ...handlers,
      ],
    });
    await user.click(await screen.findByRole("button", { name: "Bring it from prod" }));
    const dialog = await screen.findByRole("dialog", { name: "Bring what it needs from prod" });
    await user.click(await within(dialog).findByRole("button", { name: "Bring 1 item" }));
    await screen.findAllByRole("button", { name: "Background work: 1 running" });
    await user.click(within(dialog).getAllByRole("button", { name: "Close" })[0]);
    tell({ id: "bg-6", status: "Done" });

    const notice = await screen.findByRole("status");
    await user.click(within(notice).getByRole("button", { name: "Open" }));
    const shown = await screen.findByRole("dialog", { name: "Brought from prod" });
    expect(await within(shown).findByText("Brought 1 item")).toBeVisible();
    expect(router.state.location.pathname).toBe("/subscriptions/10");
  });

  it("shows how far work that counts its steps has got, and Cancel asks the server to stop it", async () => {
    let cancelled = false;
    const { user } = renderApp("/aggregations?bring=1", {
      handlers: [
        ...slowProdList("bg-3"),
        http.delete(apiPath("/background/bg-3"), () => {
          cancelled = true;
          return new HttpResponse(null, { status: 204 });
        }),
        ...mirror,
      ],
    });
    await screen.findAllByRole("button", { name: "Background work: 1 running" });
    tell({ id: "bg-3", status: "Running", done: 1200, total: 5000, summary: "1,200 retried, 0 skipped" });

    await user.click(screen.getAllByRole("button", { name: "Background work: 1 running" })[0]);
    const list = screen.getByRole("list", { name: "Background work" });
    expect(within(list).getByText(/Running · 1,200 of 5,000/)).toBeVisible();
    await user.click(within(list).getByRole("button", { name: "Cancel" }));

    await waitFor(() => expect(cancelled).toBe(true));
    tell({ id: "bg-3", status: "Cancelled", done: 1200, total: 5000, summary: "1,200 retried, 0 skipped" });
  });

  it("is only what's asked to: an ordinary page's reads are answered as they always were", async () => {
    const asked: (string | null)[] = [];
    renderApp("/partners", {
      handlers: [
        http.get(apiPath("/partners"), ({ request }) => {
          asked.push(request.headers.get("Prefer"));
          return HttpResponse.json(page([{ id: 3, name: "Acme Retail", subscriptionsCount: 0, keys: 1, propertyKeys: [] }]));
        }),
        ...empty("/subscriptions", "/apigateways", "/busgateways", "/xchanges"),
      ],
    });

    expect(await screen.findByText("Acme Retail")).toBeVisible();
    expect(asked).toEqual([null]);
  });
});
