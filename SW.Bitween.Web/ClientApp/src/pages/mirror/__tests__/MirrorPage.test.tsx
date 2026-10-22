import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { MirrorItem, MirrorParity } from "../../../api";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

const status = (over = {}) =>
  http.get(apiPath("/mirror"), () =>
    HttpResponse.json({ target: true, sourceUrl: "https://bitween.acme.test", signIn: "token", configured: true, ...over }),
  );

const item = (over: Partial<MirrorItem> & Pick<MirrorItem, "key" | "state">): MirrorItem => ({
  fields: [],
  reason: null,
  refused: false,
  stagingId: null,
  prodId: null,
  type: null,
  ...over,
});

const parity: MirrorParity = {
  checkedOn: new Date().toISOString(),
  prod: { url: "https://bitween.acme.test", version: "10.0.110", exchanges: 125000 },
  staging: { url: "https://staging.acme.test", version: "10.0.115", exchanges: 40 },
  verdict: { ready: false, matching: 40, total: 43, needsYou: 1, toCopy: 1, notCovered: 2 },
  notes: [
    { id: "adapter:acme.mapper", group: "needsYou", text: "Upload the adapter acme.mapper, version 1.4.2, on the Adapters page.", section: null, key: null, action: "adapters-installed" },
    { id: "copy", group: "copy", text: "Copying updates 1 item that differs.", section: null, key: null, action: null },
    {
      id: "refused:subscriptions:Order intake",
      group: "notCovered",
      text: "Staging couldn't create prod's subscription “Order intake”, so the test won't cover it. Bitween said: nope.",
      section: "subscriptions",
      key: "Order intake",
      action: null,
    },
    { id: "waiting:subscriptions:SkyNet returns", group: "notCovered", text: "Prod's subscription “SkyNet returns” isn't on staging yet.", section: "subscriptions", key: "SkyNet returns", action: null },
    { id: "addresses", group: "onPurpose", text: "1 address prod uses leads to a test address on staging, which goes nowhere.", section: null, key: null, action: "addresses" },
  ],
  kinds: [
    {
      section: "subscriptions",
      noun: "subscription",
      plural: "subscriptions",
      inSync: 40,
      differs: 1,
      missing: 2,
      extra: 1,
      skipped: null,
      items: [
        item({ key: "Push labels", state: "differs", fields: ["handler"], stagingId: "77", prodId: "12", type: "Internal" }),
        item({ key: "Order intake", state: "missing", refused: true, reason: "Staging couldn't create it. Bitween said: nope.", prodId: "31", type: "GatewayApiCall" }),
        item({ key: "SkyNet returns", state: "waiting", reason: "Waits for the adapter acme.mapper 1.4.2 on staging.", prodId: "32", type: "Receiving" }),
        item({ key: "zz old test", state: "extra", reason: "Only on staging.", stagingId: "90", type: "Internal" }),
      ],
    },
    { section: "connections", noun: "connection", plural: "connections", inSync: 0, differs: 0, missing: 0, extra: 0, skipped: "Prod's release doesn't have these.", items: [] },
  ],
  adapters: [{ id: "acme.mapper", source: "custom", prodVersion: "1.4.2", stagingVersion: null, state: "missing", usedBy: 3 }],
  addresses: [
    { mock: "m1a2b3c4d.mirror.invalid", kind: "https", usedBy: [{ section: "subscriptions", noun: "subscription", key: "Push labels", where: "handler · Url" }] },
  ],
  fakedKeys: [{ id: "partner:timeZone", label: "Partner property timeZone", count: 4, kept: false }],
  unknown: [],
};

const parityHandler = (answer: MirrorParity | { error: string } = parity) => http.get(apiPath("/mirror/parity"), () => HttpResponse.json(answer));

describe("prod mirror", () => {
  it("asks for prod's address and a login before anything else", async () => {
    renderApp("/mirror", { handlers: [status({ configured: false, sourceUrl: null, signIn: null, target: false })] });

    expect(await screen.findByRole("button", { name: "Connect to prod" })).toBeVisible();
    // Not a mirror: copying is off, and the page says how to turn it on.
    expect(screen.getByText(/isn't marked as a mirror/)).toBeVisible();
  });

  it("answers first, then says what needs you, what the test can't cover and what differs on purpose", async () => {
    renderApp("/mirror", { handlers: [status(), parityHandler()] });

    const verdict = await screen.findByRole("status", { name: "Readiness" });
    expect(within(verdict).getByText("Not ready to test yet")).toBeVisible();
    expect(within(verdict).getByText(/40 of prod's 43 items as prod has them\. 1 thing needs you, and 1 item is left to copy/)).toBeVisible();

    const needsYou = screen.getByRole("region", { name: "Needs you" });
    expect(within(needsYou).getByRole("link", { name: "Open Adapters" })).toHaveAttribute("href", "/adapters?tab=installed");

    // An item staging couldn't create opens on prod, where it is, and not on staging, where it isn't.
    const notCovered = screen.getByRole("region", { name: "Not covered by the test" });
    const refused = within(notCovered).getByText(/couldn't create prod's subscription “Order intake”/).closest("li")!;
    expect(within(refused).getByRole("link", { name: /On prod/ })).toHaveAttribute("href", expect.stringMatching(/^https:\/\/bitween\.acme\.test\/.*31$/));
    expect(within(refused).queryByRole("link", { name: "On staging" })).toBeNull();

    expect(screen.getByRole("region", { name: "Different on staging, on purpose" })).toBeVisible();
  });

  it("opens the section a note points at", async () => {
    const { user } = renderApp("/mirror", { handlers: [status(), parityHandler()] });

    // Folded until asked for, so the page leads with what to do.
    await screen.findByRole("status", { name: "Readiness" });
    expect(screen.queryByText("m1a2b3c4d.mirror.invalid")).toBeNull();
    await user.click(screen.getByRole("button", { name: "See the test addresses" }));
    expect(screen.getByText("m1a2b3c4d.mirror.invalid")).toBeVisible();
  });

  it("lists each kind's items with a way to each, on staging and on prod", async () => {
    const { user } = renderApp("/mirror", { handlers: [status(), parityHandler()] });

    await user.click(await screen.findByRole("button", { name: /^subscriptions: 40 same/ }));
    const row = screen.getByText("Push labels").parentElement!;
    expect(within(row).getByRole("link", { name: "On staging" })).toHaveAttribute("href", expect.stringMatching(/\/77$/));
    expect(within(row).getByRole("link", { name: /On prod/ })).toHaveAttribute("href", expect.stringMatching(/^https:\/\/bitween\.acme\.test\/.*12$/));
    expect(screen.getByTitle("Staging tried and couldn't make it")).toHaveTextContent("Couldn't copy");
    expect(screen.getByText("SkyNet returns")).toBeVisible();

    // A kind prod's release doesn't have is said, not counted as missing.
    await user.click(screen.getByRole("button", { name: "connections: not read from prod" }));
    expect(screen.getByText("Prod's release doesn't have these.")).toBeVisible();
  });

  it("copies, and says what staging couldn't make", async () => {
    let asked: unknown = null;
    const ready: MirrorParity = { ...parity, verdict: { ...parity.verdict, ready: true, needsYou: 0, toCopy: 0, matching: 41 }, notes: [] };
    const { user } = renderApp("/mirror", {
      handlers: [
        status(),
        parityHandler(),
        http.post(apiPath("/mirror/copy"), async ({ request }) => {
          asked = await request.json();
          return HttpResponse.json({
            created: 1,
            updated: 1,
            removed: 0,
            adapterSteps: 0,
            failed: [{ section: "subscriptions", noun: "subscription", key: "Order intake", error: "nope." }],
            after: ready,
          });
        }),
      ],
    });

    await user.click(await screen.findByRole("button", { name: "Copy now" }));

    expect(await screen.findByText(/Copied: created 1, updated 1, removed 0/)).toBeVisible();
    expect(screen.getByText(/Staging couldn't make this one/)).toBeVisible();
    expect(asked).toEqual({ removeExtras: false });
    expect(await screen.findByText("Ready to test the upgrade")).toBeVisible();
  });

  it("removes what only staging has only when ticked, and asks first", async () => {
    let asked: unknown = null;
    const { user } = renderApp("/mirror", {
      handlers: [
        status(),
        parityHandler(),
        http.post(apiPath("/mirror/copy"), async ({ request }) => {
          asked = await request.json();
          return HttpResponse.json({ created: 0, updated: 0, removed: 1, adapterSteps: 0, failed: [], after: parity });
        }),
      ],
    });

    await user.click(await screen.findByRole("checkbox", { name: /Also remove the 1 item/ }));
    await user.click(screen.getByRole("button", { name: "Copy now" }));
    const dialog = await screen.findByRole("dialog", { name: "Copy and remove" });
    await user.click(within(dialog).getByRole("button", { name: "Copy and remove" }));

    expect(await screen.findByText(/removed 1/)).toBeVisible();
    expect(asked).toEqual({ removeExtras: true });
  });

  it("says why prod can't be compared, with a way to fix the connection", async () => {
    renderApp("/mirror", {
      handlers: [status(), parityHandler({ error: "Prod runs r8 or older. The mirror copies prods on r10.0 or later; move this client to r10 first." })],
    });

    expect(await screen.findByText(/Prod runs r8 or older/)).toBeVisible();
    expect(screen.getByRole("button", { name: "Change the connection" })).toBeVisible();
  });
});
