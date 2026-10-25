import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { MirrorBring, MirrorProdKinds, MirrorProdList } from "../../../api";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { empty, none, page } from "../../../__tests__/support/listPage";
import { raw } from "../../subscriptions/__tests__/subscriptionBackend";

const mirror = http.get(apiPath("/mirror"), () =>
  HttpResponse.json({ target: true, sourceUrl: "https://bitween.acme.test", signIn: "token", configured: true }),
);

const kinds = (subscriptions: Record<string, number>) =>
  http.get(apiPath("/mirror/prodkinds"), () =>
    HttpResponse.json<MirrorProdKinds>({ subscriptions, apiGateways: 0, busGateways: 0, error: null }),
  );

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
      detail: "Acme · rolls up “Orders in”",
      state: "missing",
      stagingId: null,
      reason: null,
      traffic: { total: 1204, failed: 3, lastOn: new Date().toISOString(), daily: [100, 200, 150, 180, 190, 200, 184] },
    },
    {
      key: "Returns rollup",
      prodId: "32",
      type: "Aggregation",
      detail: "Acme",
      state: "inSync",
      stagingId: "8",
      reason: null,
      traffic: { total: 12, failed: 0, lastOn: null, daily: [2, 2, 2, 2, 2, 1, 1] },
    },
  ],
};

const brought = (applied: boolean): MirrorBring => ({
  applied,
  pieces: [
    { section: "subscriptions", noun: "subscription", key: "Orders rollup", state: "new", why: "Picked.", reason: null, stagingId: applied ? "41" : null, type: "Aggregation" },
    { section: "subscriptions", noun: "subscription", key: "Orders in", state: "new", why: "The subscription “Orders rollup” rolls up its exchanges.", reason: null, stagingId: applied ? "42" : null, type: "Internal" },
    { section: "partners", noun: "partner", key: "Acme", state: "kept", why: "The subscription “Orders in” uses it.", reason: null, stagingId: "3", type: null },
  ],
  needsYou: [
    { id: "adapter:acme.mapper", group: "needsYou", text: "Upload the adapter acme.mapper on the Adapters page.", section: null, key: null, action: "adapters-installed" },
  ],
  failed: [],
});

/** The aggregations page's own lookups, answered with one aggregation of staging's. */
const aggregationsPage = [
  http.get(apiPath("/subscriptions"), () => HttpResponse.json(page([raw({ id: 8, name: "Returns rollup", type: "Aggregation" })]))),
  ...empty("/documents", "/partners", "/workgroups", "/retrypolicies"),
  ...none("/subscriptions/lastruns", "/subscriptions/schedulehealth"),
];

describe("bringing prod's items from a list page", () => {
  it("lists prod's busiest first, shows everything that comes, and brings it", async () => {
    const asked: unknown[] = [];
    const { user } = renderApp("/aggregations", {
      handlers: [
        mirror,
        kinds({ all: 5, Aggregation: 2 }),
        http.get(apiPath("/mirror/prod"), ({ request }) => {
          expect(new URL(request.url).search).toBe("?section=subscriptions&type=Aggregation");
          return HttpResponse.json(prodList);
        }),
        http.post(apiPath("/mirror/bring"), async ({ request }) => {
          const body = (await request.json()) as { apply: boolean };
          asked.push(body);
          return HttpResponse.json(brought(body.apply));
        }),
        ...aggregationsPage,
      ],
    });

    await user.click(await screen.findByRole("button", { name: "Bring from prod" }));
    const dialog = await screen.findByRole("dialog", { name: "Bring aggregations from prod" });
    expect(await within(dialog).findByText("1,204")).toBeVisible();
    expect(within(dialog).getByText("3 failed")).toBeVisible();
    expect(within(dialog).getByText("Not on staging")).toBeVisible();

    await user.click(within(dialog).getByRole("checkbox", { name: "Pick “Orders rollup”" }));
    await user.click(within(dialog).getByRole("button", { name: "See what comes" }));
    expect(await within(dialog).findByText("Comes from prod")).toBeVisible();
    expect(within(dialog).getByText("The subscription “Orders rollup” rolls up its exchanges.")).toBeVisible();
    expect(within(dialog).getByText(/Upload the adapter acme\.mapper/)).toBeVisible();
    expect(asked).toEqual([{ section: "subscriptions", keys: ["Orders rollup"], apply: false }]);

    await user.click(within(dialog).getByRole("button", { name: "Bring 2 items" }));
    expect(await within(dialog).findByText("Brought 2 items")).toBeVisible();
    expect(within(dialog).getByRole("link", { name: "Orders rollup" })).toHaveAttribute("href", "/aggregations/41");
    expect(asked).toHaveLength(2);
    expect(asked[1]).toEqual({ section: "subscriptions", keys: ["Orders rollup"], apply: true });
  });

  it("isn't offered where prod has none of what the page lists", async () => {
    let read = false;
    renderApp("/aggregations", {
      handlers: [
        mirror,
        http.get(apiPath("/mirror/prodkinds"), () => {
          read = true;
          return HttpResponse.json<MirrorProdKinds>({ subscriptions: { all: 3, Receiving: 3 }, apiGateways: 0, busGateways: 0, error: null });
        }),
        ...aggregationsPage,
      ],
    });

    expect(await screen.findByRole("row", { name: /Returns rollup/ })).toBeVisible();
    await expect.poll(() => read).toBe(true);
    expect(screen.queryByRole("button", { name: "Bring from prod" })).not.toBeInTheDocument();
  });
});
