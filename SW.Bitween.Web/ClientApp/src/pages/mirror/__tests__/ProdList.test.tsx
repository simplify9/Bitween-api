import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { MirrorListed } from "../../../api";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { backend, sub } from "../../subscriptions/__tests__/subscriptionBackend";

/**
 * The lists on a mirror: a Prod column saying how each row's test with prod's traffic went, a filter
 * on it the list pages through, and testing several from the list. What prod has and how each test
 * ran is MirrorListTests; this is what a list makes of it.
 */
const mirror = http.get(apiPath("/mirror"), () =>
  HttpResponse.json({ target: true, sourceUrl: "https://bitween.acme.test", signIn: "token", configured: true }),
);
const prodKinds = http.get(apiPath("/mirror/prodkinds"), () =>
  HttpResponse.json({ subscriptions: { all: 2 }, apiGateways: 1, busGateways: 0, error: null }),
);

const listed = (over: Partial<MirrorListed> = {}): MirrorListed => ({
  items: [
    { id: 10, key: "Acme orders", type: "BusGateway", verdict: "same", ranOn: "2026-10-24T08:00:00Z", tested: 1, of: 1 },
    { id: 11, key: "Globex orders", type: "BusGateway", verdict: null, ranOn: null, tested: 0, of: 1 },
  ],
  testing: null,
  waiting: [],
  error: null,
  ...over,
});

const row = async (name: string) => within(await screen.findByRole("row", { name: new RegExp(name) }));

describe("a list on a mirror", () => {
  it("says how each row's test went, filters by it, and tests the picked ones one after another", async () => {
    let current = listed();
    const searched: string[][] = [];
    const tested: unknown[] = [];
    let stopped = 0;
    const subjects = [sub(), sub({ id: 11, name: "Globex orders" }), sub({ id: 12, name: "Staging only" })];
    const { handlers } = backend(subjects);
    const { user } = renderApp("/subscriptions", {
      handlers: [
        mirror,
        prodKinds,
        http.get(apiPath("/mirror/listed"), () => HttpResponse.json(current)),
        http.get(apiPath("/subscriptions"), ({ request }) => {
          searched.push(new URL(request.url).searchParams.getAll("filter"));
          return HttpResponse.json({ result: subjects, totalCount: subjects.length });
        }),
        http.post(apiPath("/mirror/testmany"), async ({ request }) => {
          tested.push(await request.json());
          current = listed({ testing: "Globex orders", waiting: ["Acme orders"] });
          return HttpResponse.json({ subscriptions: ["Globex orders"] });
        }),
        http.post(apiPath("/mirror/teststop"), () => {
          stopped++;
          current = listed({ testing: "Globex orders" });
          return HttpResponse.json({ dropped: 1 });
        }),
        ...handlers,
      ],
    });

    expect(await screen.findByRole("columnheader", { name: "Prod" })).toBeVisible();
    expect(await (await row("Acme orders")).findByText("Same")).toBeVisible();
    expect((await row("Globex orders")).getByText("Not tested")).toBeVisible();
    expect((await row("Staging only")).getByTitle("Prod has nothing of this name")).toBeVisible();

    await user.click(screen.getByRole("combobox", { name: "Filter by how it stands against prod" }));
    await user.click(await screen.findByRole("option", { name: "Not tested" }));
    await waitFor(() => expect(searched.some((f) => f.includes("Mirror:1:untested"))).toBe(true));

    await user.click(screen.getByRole("button", { name: /Test from prod/ }));
    const dialog = within(await screen.findByRole("dialog", { name: "Test subscriptions from prod" }));
    await user.click(dialog.getByRole("checkbox", { name: "Pick “Globex orders”" }));
    await user.click(dialog.getByRole("button", { name: "Test 1" }));
    await waitFor(() => expect(tested).toEqual([{ section: "subscriptions", ids: [11] }]));

    expect(await screen.findByText(/Testing “Globex orders” with prod's traffic; 1 more waits its turn/)).toBeVisible();
    expect(await (await row("Globex orders")).findByText("Testing…")).toBeVisible();
    expect((await row("Acme orders")).getByText("Waiting")).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Stop after this one" }));
    await waitFor(() => expect(stopped).toBe(1));
    expect(await screen.findByText("Testing “Globex orders” with prod's traffic.")).toBeVisible();
  });

  it("says a gateway's test is that of the subscriptions it leads to, and how many were tested", async () => {
    renderApp("/api-gateways", {
      handlers: [
        mirror,
        prodKinds,
        http.get(apiPath("/mirror/listed"), () =>
          HttpResponse.json(listed({ items: [{ id: 15, key: "Orders", type: null, verdict: "check", ranOn: "2026-10-24T08:00:00Z", tested: 1, of: 2 }] })),
        ),
        http.get(apiPath("/apigateways"), () =>
          HttpResponse.json({
            result: [{ id: 15, name: "Orders", urlName: "orders", inactive: false, partnersCount: 0, partners: [] }],
            totalCount: 1,
          }),
        ),
        ...["/subscriptions", "/documents", "/partners"].map((p) => http.get(apiPath(p), () => HttpResponse.json({ result: [], totalCount: 0 }))),
      ],
    });

    const gateway = await row("Orders");
    expect(await gateway.findByText("Check")).toHaveAttribute("title", expect.stringContaining("of the 2 subscriptions it leads to that prod has too, 1 tested"));
    expect(gateway.getByText("1 of 2")).toBeVisible();
  });

  it("shows nothing of prod on a Bitween that isn't a mirror", async () => {
    const { handlers } = backend([sub()]);
    renderApp("/subscriptions", { handlers });

    expect(await row("Acme orders")).toBeTruthy();
    expect(screen.queryByRole("columnheader", { name: "Prod" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Test from prod/ })).not.toBeInTheDocument();
  });
});
