import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { MirrorItem, MirrorParity, MirrorRehearsal, MirrorSample, MirrorSamples } from "../../../api";
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

const noSamples: MirrorSamples = { state: "none", startedOn: null, finishedOn: null, done: 0, total: 0, error: null, subscriptions: [], families: [] };

const samplesHandler = (answer: MirrorSamples = noSamples) => http.get(apiPath("/mirror/samples"), () => HttpResponse.json(answer));

const noRehearsal: MirrorRehearsal = {
  state: "none", startedOn: null, finishedOn: null, done: 0, total: 0, error: null, prodVersion: null, stagingVersion: null, setupError: null, families: [],
};

const rehearsalHandler = (answer: MirrorRehearsal = noRehearsal) => http.get(apiPath("/mirror/rehearsal"), () => HttpResponse.json(answer));

const sample = (over: Partial<MirrorSample> & Pick<MirrorSample, "prodExchangeId">): MirrorSample => ({
  startedOn: "2026-10-20T10:00:00Z",
  outcome: "success",
  status: null,
  errorTypes: [],
  input: { key: `mirror/samples/x/${over.prodExchangeId}/input`, name: "order.json", format: "json", size: 120, notCopied: null, url: `https://staging.acme.test/api/files/s/${over.prodExchangeId}` },
  output: null,
  response: null,
  partner: null,
  handler: null,
  mapper: null,
  subscription: "Push labels",
  informationType: "Label request",
  parent: null,
  ...over,
});

const taken: MirrorSamples = {
  state: "done",
  startedOn: new Date().toISOString(),
  finishedOn: new Date().toISOString(),
  done: 2,
  total: 2,
  error: null,
  subscriptions: [
    { subscription: "Push labels", prodId: "12", reason: null, families: ["a1b2"] },
    { subscription: "Nightly report", prodId: "13", reason: "Prod has no exchanges of it.", families: [] },
  ],
  families: [
    {
      id: "a1b2",
      entry: "gateway",
      door: "acme-labels",
      partner: "Acme Retail",
      cut: 0,
      members: [
        sample({
          prodExchangeId: "a1b2",
          outcome: "failed",
          status: 500,
          errorTypes: ["HttpRequestException"],
          input: { key: "mirror/samples/x/a1b2/input", name: "ORD_2025-04-11.xml", format: "xml", size: 120, notCopied: null, url: "https://staging.acme.test/api/files/s/in" },
          response: { key: null, name: "label.pdf", format: "pdf", size: 0, notCopied: "PDFs can't be faked.", url: null },
        }),
      ],
    },
  ],
};

describe("prod mirror", () => {
  it("asks for prod's address and a login before anything else", async () => {
    renderApp("/mirror", { handlers: [status({ configured: false, sourceUrl: null, signIn: null, target: false })] });

    expect(await screen.findByRole("button", { name: "Connect to prod" })).toBeVisible();
    // Not a mirror: copying is off, and the page says how to turn it on.
    expect(screen.getByText(/isn't marked as a mirror/)).toBeVisible();
  });

  it("answers first, then says what needs you, what the test can't cover and what differs on purpose", async () => {
    renderApp("/mirror", { handlers: [status(), parityHandler(), samplesHandler(), rehearsalHandler()] });

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
    const { user } = renderApp("/mirror", { handlers: [status(), parityHandler(), samplesHandler(), rehearsalHandler()] });

    // Folded until asked for, so the page leads with what to do.
    await screen.findByRole("status", { name: "Readiness" });
    expect(screen.queryByText("m1a2b3c4d.mirror.invalid")).toBeNull();
    await user.click(screen.getByRole("button", { name: "See the test addresses" }));
    expect(screen.getByText("m1a2b3c4d.mirror.invalid")).toBeVisible();
  });

  it("lists each kind's items with a way to each, on staging and on prod", async () => {
    const { user } = renderApp("/mirror", { handlers: [status(), parityHandler(), samplesHandler(), rehearsalHandler()] });

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
        samplesHandler(),
        rehearsalHandler(),
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
        samplesHandler(),
        rehearsalHandler(),
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

  it("takes samples, follows the taking and lists each arrival with its faked files and its exchanges on prod", async () => {
    let samples: MirrorSamples = noSamples;
    let asked = 0;
    const { user } = renderApp("/mirror", {
      handlers: [
        status(),
        parityHandler(),
        rehearsalHandler(),
        http.get(apiPath("/mirror/samples"), () => HttpResponse.json(samples)),
        http.post(apiPath("/mirror/takesamples"), () => {
          asked++;
          samples = { ...noSamples, state: "running", done: 1, total: 2 };
          return HttpResponse.json(samples);
        }),
      ],
    });

    expect(await screen.findByText(/^No samples yet\./)).toBeVisible();
    // Nothing to rehearse until there are samples.
    expect(screen.getByRole("button", { name: "Rehearse" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Take samples" }));
    expect(await screen.findByText(/Taking samples: 1 of 2 subscriptions read/)).toBeVisible();
    expect(asked).toBe(1);

    // The taking ends; the page follows it on its own.
    samples = taken;
    expect(await screen.findByText(/1 arrival from prod, 1 exchange in all, covering 1 of prod's 2 subscriptions/, {}, { timeout: 4000 })).toBeVisible();

    await user.click(screen.getByRole("button", { name: "See the samples" }));
    const row = screen.getByRole("button", { name: /^acme-labels, .*: Not run$/ }).closest("li")!;
    expect(within(row).getByTitle("A partner's call through an API gateway")).toHaveTextContent("Gateway call");
    expect(within(row).getByText("from Acme Retail")).toBeVisible();
    expect(within(row).getByRole("link", { name: /On prod/ })).toHaveAttribute("href", "https://bitween.acme.test/exchanges?ids=a1b2");

    await user.click(within(row).getByRole("button", { name: /^acme-labels/ }));
    expect(within(row).getByText("Not rehearsed yet.")).toBeVisible();
    expect(within(row).getByText("Failed 500")).toBeVisible();
    expect(within(row).getByText("HttpRequestException")).toBeVisible();
    expect(within(row).getByRole("link", { name: "In (xml)" })).toHaveAttribute("href", "https://staging.acme.test/api/files/s/in");
    expect(within(row).getByTitle("PDFs can't be faked.")).toHaveTextContent("Answer: not kept (pdf)");

    // A subscription without samples says why, when asked.
    await user.click(screen.getByRole("button", { name: /1 subscription has no traffic on prod/ }));
    expect(screen.getByText("Prod has no exchanges of it.")).toBeVisible();
  });

  it("rehearses, then lists each arrival and the exchanges it led to, prod beside staging, what changed first", async () => {
    const now = new Date().toISOString();
    const withFamilies: MirrorSamples = {
      ...taken,
      subscriptions: [
        { subscription: "Push labels", prodId: "12", reason: null, families: ["f1", "f2"] },
        { subscription: "Send confirmation", prodId: "15", reason: null, families: ["f1"] },
        { subscription: "Nightly roll-up", prodId: "14", reason: null, families: ["f3"] },
      ],
      families: [
        { id: "f1", entry: "gateway", door: "acme-orders", partner: "Acme Retail", cut: 0,
          members: [sample({ prodExchangeId: "p1" }), sample({ prodExchangeId: "p1b", subscription: "Send confirmation", parent: "p1", startedOn: "2026-10-20T10:00:05Z" })] },
        { id: "f2", entry: "document", door: "Label request", partner: null, cut: 0,
          members: [sample({ prodExchangeId: "p2", subscription: null, startedOn: "2026-10-19T10:00:00Z" }), sample({ prodExchangeId: "p2b", startedOn: "2026-10-19T10:00:01Z" })] },
        { id: "f3", entry: "aggregation", door: "Nightly roll-up", partner: null, cut: 0, members: [sample({ prodExchangeId: "p3", subscription: "Nightly roll-up" })] },
      ],
    };
    type Diff = MirrorRehearsal["families"][number]["diffs"][number];
    const diff = (over: Partial<Diff> & Pick<Diff, "part" | "name" | "level">): Diff => ({
      prod: null, staging: null, note: null, fields: null, section: null, stagingId: null, type: null, ...over,
    });
    const member = (over: Partial<MirrorRehearsal["families"][number]["members"][number]> & { prodExchangeId: string | null; subscription: string }) => ({
      stagingExchangeId: null, ranOn: now, outcome: null, status: null, errorTypes: [], verdict: null, notRun: null, output: null, response: null, diffs: [],
      ...over,
    }) as MirrorRehearsal["families"][number]["members"][number];
    const rehearsed: MirrorRehearsal = {
      state: "done", startedOn: now, finishedOn: now, done: 3, total: 3, error: null, prodVersion: "10.0.110", stagingVersion: "10.0.115", setupError: null,
      families: [
        {
          familyId: "f1", correlation: "rehearsal-abc", ranOn: now, verdict: "different", notRun: null,
          diffs: [
            diff({ part: "entry", name: "How it came in", level: "same", prod: "A call to the gateway “acme-orders” from “Acme Retail”", staging: "Taken: staging answered 202" }),
            diff({ part: "family", name: "Bitween", level: "info", prod: "10.0.110", staging: "10.0.115", note: "The release being tried." }),
          ],
          members: [
            member({ prodExchangeId: "p1", subscription: "Push labels", stagingExchangeId: "s1", outcome: "success", verdict: "same",
              diffs: [diff({ part: "result", name: "Result", level: "same", prod: "Worked", staging: "Worked" })] }),
            member({ prodExchangeId: "p1b", subscription: "Send confirmation", stagingExchangeId: "s1b", outcome: "failed", errorTypes: ["JsonException"], verdict: "different",
              diffs: [
                diff({ part: "result", name: "Result", level: "different", prod: "Worked", staging: "Failed · JsonException", note: "It worked on prod and doesn't on staging." }),
                diff({ part: "output", name: "What it sent", level: "different", prod: "JSON · 3 fields", staging: "JSON · 2 fields", note: "1 field missing.",
                  fields: [{ path: "order.ref", level: "different", prod: "\"XKQ-1\"", staging: "Missing" }] }),
                diff({ part: "input", name: "What was handed on to it", level: "same", prod: "JSON · 4 fields", staging: "JSON · 4 fields" }),
              ] }),
          ],
        },
        {
          familyId: "f2", correlation: "rehearsal-def", ranOn: now, verdict: "same", notRun: null,
          diffs: [diff({ part: "entry", name: "How it came in", level: "same", prod: "A document of “Label request”", staging: "The same document, routed by staging" })],
          members: [
            member({ prodExchangeId: "p2", subscription: "Label request", stagingExchangeId: "s2", outcome: "success", verdict: "same", diffs: [diff({ part: "result", name: "Result", level: "same", prod: "Worked", staging: "Worked" })] }),
            member({ prodExchangeId: "p2b", subscription: "Push labels", stagingExchangeId: "s2b", outcome: "success", verdict: "same", diffs: [diff({ part: "result", name: "Result", level: "same", prod: "Worked", staging: "Worked" })] }),
          ],
        },
        { familyId: "f3", correlation: null, ranOn: now, verdict: null, notRun: "Staging had nothing to roll up: none of the exchanges it aggregates worked in this rehearsal.", diffs: [], members: [] },
      ],
    };
    let rehearsal: MirrorRehearsal = noRehearsal;
    let asked = 0;
    const { user } = renderApp("/mirror", {
      handlers: [
        status(),
        parityHandler(),
        samplesHandler(withFamilies),
        http.get(apiPath("/mirror/rehearsal"), () => HttpResponse.json(rehearsal)),
        http.post(apiPath("/mirror/rehearse"), () => {
          asked++;
          rehearsal = { ...noRehearsal, state: "running", done: 1, total: 3 };
          return HttpResponse.json(rehearsal);
        }),
      ],
    });

    expect(await screen.findByText("Not rehearsed yet.")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Rehearse" }));
    expect(await screen.findByText(/Rehearsing: 1 of 3 arrivals run/)).toBeVisible();
    expect(asked).toBe(1);

    // It ends; the page follows it on its own.
    rehearsal = rehearsed;
    expect(await screen.findByText(/2 arrivals ran \(1 different, 1 the same\), 1 didn't\. Staging runs 10\.0\.115, prod 10\.0\.110\./, {}, { timeout: 4000 })).toBeVisible();

    // The list opens on what differs: each arrival with every exchange it led to, on staging and on prod.
    await user.click(screen.getByRole("button", { name: "See them" }));
    expect(screen.getByRole("radio", { name: /^Different \(1\)/ })).toBeChecked();
    const row = screen.getByRole("button", { name: /^acme-orders, .*: Different$/ }).closest("li")!;
    expect(within(row).getByRole("link", { name: "Staging's exchanges" })).toHaveAttribute("href", "/exchanges?correlationId=rehearsal-abc");
    expect(within(row).getByRole("link", { name: /On prod/ })).toHaveAttribute("href", "https://bitween.acme.test/exchanges?ids=p1%2Cp1b");

    // Opened: how it came in, then its exchanges, the one that differs opened, what changed first.
    await user.click(within(row).getByRole("button", { name: /^acme-orders, .*: Different$/ }));
    const entry = within(row).getByRole("list", { name: "How it came in" });
    expect(within(entry).getByRole("button", { name: /How it came in/ })).toHaveTextContent(/A call to the gateway “acme-orders” from “Acme Retail”\s*→\s*Taken: staging answered 202/);
    expect(within(entry).getByRole("button", { name: /Bitween/ })).toHaveTextContent(/10\.0\.110\s*→\s*10\.0\.115/);
    const pushLabels = within(row).getByRole("button", { name: "Push labels: Same" }).closest("li")!;
    expect(within(pushLabels).getByRole("link", { name: "Staging's exchange" })).toHaveAttribute("href", "/exchanges?ids=s1");
    expect(within(row).getByText("It worked on prod and doesn't on staging.")).toBeVisible();
    const changed = within(row).getByRole("region", { name: "What changed" });
    expect(within(changed).getByRole("button", { name: /Result/ })).toHaveTextContent(/Worked\s*→\s*Failed · JsonException/);
    await user.click(within(changed).getByRole("button", { name: /What it sent/ }));
    expect(within(changed).getByText("order.ref")).toBeVisible();
    expect(within(row).getByRole("region", { name: "Same as prod" })).toHaveTextContent("What was handed on to it");

    // One that didn't run says why.
    await user.click(screen.getByRole("radio", { name: /^Not run \(1\)/ }));
    await user.click(screen.getByRole("button", { name: /^Nightly roll-up, .*: Not run$/ }));
    expect(screen.getByText(/Staging had nothing to roll up/)).toBeVisible();
  });

  it("lists the calls staging answered, each with what it answered last", async () => {
    const withCalls: MirrorParity = {
      ...parity,
      notes: [...parity.notes, { id: "calls", group: "onPurpose", text: "Staging answers calls to test addresses itself.", section: null, key: null, action: "calls" }],
    };
    const { user } = renderApp("/mirror", {
      handlers: [
        status(),
        parityHandler(withCalls),
        samplesHandler(),
        rehearsalHandler(),
        http.get(apiPath("/mirror/calls"), () =>
          HttpResponse.json({
            catching: true,
            recent: [],
            hosts: [
              { host: "m1a2b3c4d.mirror.invalid", test: true, calls: 3, lastAt: new Date().toISOString(), lastWhy: "Prod's answer to “Push labels”, faked.", usedBy: ["Push labels"] },
              { host: "api.zohoapis.eu", test: false, calls: 1, lastAt: new Date().toISOString(), lastWhy: "Nothing of prod's to answer with, so an empty reply.", usedBy: [] },
            ],
          }),
        ),
      ],
    });

    await user.click(await screen.findByRole("button", { name: "See the calls it answered" }));
    expect(await screen.findByText("m1a2b3c4d.mirror.invalid")).toBeVisible();
    expect(screen.getByText("Prod's answer to “Push labels”, faked.")).toBeVisible();
    expect(screen.getByTitle(/Staging's setup doesn't name it/)).toHaveTextContent("Not in staging's setup");
  });

  it("says why prod can't be compared, with a way to fix the connection", async () => {
    renderApp("/mirror", {
      handlers: [status(), samplesHandler(), rehearsalHandler(), parityHandler({ error: "Prod runs r8 or older. The mirror copies prods on r10.0 or later; move this client to r10 first." })],
    });

    expect(await screen.findByText(/Prod runs r8 or older/)).toBeVisible();
    expect(screen.getByRole("button", { name: "Change the connection" })).toBeVisible();
  });
});
