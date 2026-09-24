import { cleanup, screen, waitFor, within } from "@testing-library/react";
import type { UserEvent } from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { expect } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import type { MappingRules } from "../../../lib/nativeMapper/types";

/**
 * The mapping editor, opened from a subscription against a mock of everything it asks the API for.
 *
 * The page reads the subscription (and, through it, the gateways, the recent exchanges and the
 * information type), the partners for the "Preview as" picker, and posts the rules to the preview
 * endpoint whenever they change. The preview here answers whatever the test tells it to, and keeps
 * every request, so a test can say both what the editor asked the server to map and that it shows
 * what came back. What the engine does with the rules is the C# suite's business
 * (SW.Bitween.UnitTests/NativeMapper), not this one's.
 */

// The connection lines between the panels watch their own size, which jsdom cannot measure.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

export const SUBSCRIPTION_ID = 12;

/** A source document with a value, a number, and a list with one entry to filter out. */
export const SAMPLE = {
  order: {
    customer: "Ali",
    net: 100,
    line: [
      { sku: "A1", qty: 2 },
      { sku: "B7", qty: 0 },
    ],
  },
};

/**
 * The partner the preview can run as.
 *
 * Needed for the same reason the e2e suite seeded one: a scheduled job has no partner of its own,
 * so a Partner rule has nothing to resolve against unless one is chosen explicitly.
 */
export const PARTNER = {
  id: 7,
  name: "Mapper Partner",
  properties: { WarehouseCode: "WH-7", SenderId: "BITWEEN-JO" },
};

/** What the editor posted to the preview endpoint, with the rules read back out of their string. */
export interface SentPreview {
  rules: MappingRules;
  sourceDocument: string;
  partnerId: number | null;
}

/** The preview endpoint's answer, as `RawMappingPreviewResponse` in src/api/http/mappers.ts has it. */
export interface PreviewAnswer {
  outputDocument?: string | null;
  ruleErrors?: { target: string; reason: string }[];
  error?: string | null;
}

const noRows = { result: [], totalCount: 0 };

/** What the server labels each target format's output with. */
const CONTENT_TYPES: Record<string, string> = {
  json: "application/json",
  xml: "application/xml",
  csv: "text/csv",
};

/**
 * A mock backend holding one subscription.
 *
 * Saving writes to it, so a remount afterwards reads back what was saved — the same round trip a
 * page reload made against the real server.
 */
export function mapperBackend({
  mapperProperties = {},
  preview = () => ({ outputDocument: "{}" }),
}: {
  /** What the subscription is stored with, as the editor's `MappingRules`/`SourceSample` keys. */
  mapperProperties?: Record<string, string>;
  preview?: (sent: SentPreview) => PreviewAnswer;
} = {}) {
  const stored = { mapperId: null as string | null, mapperProperties: { ...mapperProperties } };
  const previews: SentPreview[] = [];
  const saves: { mapperId: string | null; mapperProperties: Record<string, string> }[] = [];

  // RawSubscription in src/api/http/subscriptions.ts: a scheduled job with no partner and, until
  // something is saved, no mapper — which is what opens the new editor.
  const subscription = () => ({
    id: SUBSCRIPTION_ID,
    name: "Mapper cases",
    documentId: 3,
    partnerId: null,
    aggregationForId: null,
    type: "Receiving",
    handlerId: "NativeHttpHandler",
    mapperId: stored.mapperId,
    receiverId: "NativeHttpReceiver",
    dataSourceId: null,
    validatorId: null,
    inactive: false,
    temporary: false,
    categoryId: null,
    handlerProperties: [{ key: "Url", value: "https://example.com/post" }],
    mapperProperties: Object.entries(stored.mapperProperties).map(([key, value]) => ({ key, value })),
    receiverProperties: [{ key: "Url", value: "https://example.com/feed" }],
    validatorProperties: [],
    documentFilter: [],
    matchExpression: null,
    workGroupId: null,
    retryPolicyId: null,
    customRetryPolicy: null,
    schedules: [],
    responseSubscriptionId: null,
    responseMessageTypeName: null,
    receiveOn: null,
    aggregateOn: null,
    pausedOn: null,
    isRunning: false,
    consecutiveFailures: 0,
    lastException: null,
  });

  const handlers = [
    http.get(apiPath(`/subscriptions/${SUBSCRIPTION_ID}`), () => HttpResponse.json(subscription())),
    http.post(apiPath(`/subscriptions/${SUBSCRIPTION_ID}`), async ({ request }) => {
      const body = (await request.json()) as {
        mapperId: string | null;
        mapperProperties: { key: string; value: string }[];
      };
      const saved = {
        mapperId: body.mapperId,
        mapperProperties: Object.fromEntries(body.mapperProperties.map((kv) => [kv.key, kv.value])),
      };
      saves.push(saved);
      stored.mapperId = saved.mapperId;
      stored.mapperProperties = saved.mapperProperties;
      return new HttpResponse(null, { status: 204 });
    }),

    // What getSubscription gathers alongside the row itself.
    http.get(apiPath("/apigateways"), () => HttpResponse.json(noRows)),
    http.get(apiPath("/busgateways"), () => HttpResponse.json(noRows)),
    http.get(apiPath("/xchanges"), () => HttpResponse.json(noRows)),
    http.get(apiPath("/subscriptions"), () => HttpResponse.json(noRows)),
    http.get(apiPath("/documents/3"), () =>
      HttpResponse.json({
        id: 3,
        code: "SHIPMENT_ORDER",
        name: "Shipment order",
        documentFormat: "Json",
        busEnabled: false,
        busMessageTypeName: null,
        duplicateInterval: 0,
        disregardsUnfilteredMessages: false,
        promotedProperties: [],
        usedByCount: 1,
        retiredOn: null,
      }),
    ),

    // The list sends property names only; the values need the partner itself.
    http.get(apiPath("/partners"), () =>
      HttpResponse.json({
        result: [
          {
            id: PARTNER.id,
            name: PARTNER.name,
            subscriptionsCount: 0,
            keys: 0,
            propertyKeys: Object.keys(PARTNER.properties),
          },
        ],
        totalCount: 1,
      }),
    ),
    http.get(apiPath(`/partners/${PARTNER.id}`), () =>
      HttpResponse.json({
        name: PARTNER.name,
        apiCredentials: [],
        adapterProperties: PARTNER.properties,
        secretProperties: [],
      }),
    ),

    http.post(apiPath("/mappingpreviews"), async ({ request }) => {
      const body = (await request.json()) as {
        mappingRules: string;
        sourceDocument: string;
        partnerId: number | null;
      };
      const sent: SentPreview = {
        rules: JSON.parse(body.mappingRules) as MappingRules,
        sourceDocument: body.sourceDocument,
        partnerId: body.partnerId ?? null,
      };
      previews.push(sent);
      const reply = preview(sent);
      return HttpResponse.json({
        outputDocument: reply.outputDocument ?? null,
        // The server names the type of what it wrote, which follows the format the rules ask for.
        contentType: reply.outputDocument ? (CONTENT_TYPES[sent.rules.targetFormat] ?? null) : null,
        ruleErrors: reply.ruleErrors ?? [],
        error: reply.error ?? null,
      });
    }),
  ];

  return {
    handlers,
    saves,
    previews,
    /** What the editor last asked to have mapped. */
    lastPreview: () => previews[previews.length - 1],
  };
}

export type MapperBackend = ReturnType<typeof mapperBackend>;

/** Mounts the subscription's mapping editor, without waiting for anything. */
export const mountEditor = (backend: MapperBackend) =>
  renderApp(`/subscriptions/${SUBSCRIPTION_ID}/mapper`, { handlers: backend.handlers });

/** Opens the subscription's mapping editor and waits for it to finish loading. */
export async function openEditor(backend: MapperBackend) {
  const app = mountEditor(backend);
  await screen.findByRole("button", { name: "Save" }, { timeout: 5000 });
  return app;
}

/** Opens the editor and pastes a source sample into it. */
export async function openWithSample(backend: MapperBackend, sample: unknown = SAMPLE) {
  const app = await openEditor(backend);
  await fill(
    app.user,
    screen.getByRole("textbox", { name: "Sample source document" }),
    typeof sample === "string" ? sample : JSON.stringify(sample, null, 2),
  );
  return app;
}

/**
 * Replaces a box's text in one change, the way Playwright's fill() did.
 *
 * Typed a key at a time, every keystroke would be its own step on the undo stack — and the
 * keyboard test is precisely about one undo taking back one change.
 */
export async function fill(user: UserEvent, box: HTMLElement, text: string) {
  await user.clear(box);
  if (text) await user.paste(text);
}

/** Unmounts the editor and opens it again from what the mock backend now holds, like a reload. */
export async function reopen(backend: MapperBackend) {
  cleanup();
  return openEditor(backend);
}

const last = <T,>(items: T[]): T => items[items.length - 1];

/** Adds a field at the top level, leaving it selected and unassigned. */
export async function addNamedRule(user: UserEvent, name: string) {
  await user.click(screen.getByRole("button", { name: "Add a field" }));
  await fill(user, last(screen.getAllByRole("textbox", { name: "Output field name" })), name);
}

/** Adds a field at the top level and points it at a path. */
export async function addPathRule(user: UserEvent, name: string, path: string) {
  await addNamedRule(user, name);
  await fill(user, last(screen.getAllByRole("combobox", { name: "Source field" })), path);
}

/** Adds a field at the top level whose value is a literal. */
export async function addFixedRule(user: UserEvent, name: string, value: string) {
  await addNamedRule(user, name);
  await user.click(last(screen.getAllByRole("radio", { name: "Fixed" })));
  await fill(user, last(screen.getAllByRole("textbox", { name: "Fixed value" })), value);
}

/** Adds a list at the top level over a source path, and returns its rules group. */
export async function addList(user: UserEvent, name: string, over: string) {
  await user.click(screen.getByRole("button", { name: "Add a list" }));
  await fill(user, last(screen.getAllByRole("textbox", { name: "Output list name" })), name);
  await user.selectOptions(last(screen.getAllByRole("combobox", { name: "Source list" })), `p:${over}`);
  return listGroup(name);
}

/** The rows inside a list, which is where its own add buttons live. */
export const listGroup = (name: string) =>
  screen.getByRole("group", { name: `Rules for the list ${name}` });

/** Adds a field inside a list, pointed at a path on the entry. */
export async function addListField(user: UserEvent, list: HTMLElement, addTo: string, name: string, path: string) {
  await user.click(within(list).getByRole("button", { name: `Add a field to ${addTo}` }));
  await fill(user, last(within(list).getAllByRole("textbox", { name: "Output field name" })), name);
  await fill(user, last(within(list).getAllByRole("combobox", { name: "Source field" })), path);
}

/** Opens a row's detail panel, which is where the transform, type and lookup live. */
export async function openDetail(user: UserEvent, name: string) {
  await user.click(screen.getByRole("button", { name: `Details for ${name}` }));
}

/**
 * Runs `set` with the format panel open, and closes it afterwards.
 *
 * What the mapping reads and writes sits behind a summary chip rather than in the toolbar row, so
 * changing a format means opening the panel first.
 */
export async function withFormats(user: UserEvent, set: () => Promise<unknown>) {
  await user.click(screen.getByRole("button", { name: "What this mapping reads and writes" }));
  await screen.findByLabelText("From format");
  await set();
  await user.keyboard("{Escape}");
}

/**
 * What a suggest box offers, which is a hint and not a limit.
 *
 * Focused first: only the box being typed in carries a suggestion list, so that a big mapping does
 * not put every row's copy of every path into the page at once.
 */
export async function suggestionsFor(user: UserEvent, box: HTMLElement): Promise<string[]> {
  if (document.activeElement !== box) await user.click(box);
  const list = document.getElementById(box.getAttribute("list") ?? "");
  return [...(list?.querySelectorAll("option") ?? [])].map((o) => o.value);
}

/** The mapped document as the preview panel shows it. */
export const preview = () => document.querySelector("pre");

/**
 * Waits for the editor to have asked the server to map rules shaped like `rules`.
 *
 * Matched as `toMatchObject` does, so `rules` names only what the test is about — which is why it
 * is not typed as the rules themselves, whose required keys it deliberately leaves out.
 */
export async function expectSent(backend: MapperBackend, rules: object) {
  await waitFor(() => expect(backend.lastPreview()?.rules).toMatchObject(rules), { timeout: 3000 });
}

/** Waits for the preview panel to show `text` from the server's answer. */
export async function expectPreview(text: string) {
  await waitFor(() => expect(preview()).toHaveTextContent(text), { timeout: 3000 });
}
