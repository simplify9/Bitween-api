import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * The exchanges page's controls: the status counts, the volume chart, the query bar and its
 * filters, saved views, and the exchange that opens beside the list.
 */

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`;

/** GET /xchanges: `RawXchangeRow` in src/api/http/exchanges.ts. */
const exchange = (n: number, over: Record<string, unknown> = {}) => ({
  id: id(n),
  subscriptionId: 16,
  subscriptionName: "Push orders",
  documentId: 5,
  documentName: "PURCHASE_ORDER",
  mapperId: "NativeMapper",
  status: false,
  exception: "Connection refused",
  finishedOn: "2026-09-20T10:00:02Z",
  startedOn: "2026-09-20T10:00:00Z",
  inputFileName: null,
  outputFileName: null,
  responseFileName: null,
  inputFileSize: 0,
  outputFileSize: 0,
  responseFileSize: 0,
  inputKey: null,
  outputKey: null,
  responseKey: null,
  promotedProperties: { orderNo: `SO-${n}` },
  retryFor: null,
  aggregationXchangeId: null,
  responseBad: null,
  correlationId: null,
  partnerId: null,
  scheduledRetryOn: null,
  hasRetry: false,
  ...over,
});

const bucket = (start: string, failed = 0) => ({ start, processing: 0, success: 10, badResponse: 0, failed });

/** The list, the breakdown and the pickers, recording what each was asked. */
function backend({
  rows = 3,
  total = rows,
  counts = { processing: 1, success: 120, badResponse: 2, failed: 7 },
  buckets = [bucket("2026-10-10T12:00:00Z"), bucket("2026-10-10T13:00:00Z", 4)],
}: {
  rows?: number;
  total?: number | ((q: URLSearchParams) => number);
  counts?: Record<string, number>;
  buckets?: ReturnType<typeof bucket>[];
} = {}) {
  const listed: URLSearchParams[] = [];
  const summarised: URLSearchParams[] = [];
  const handlers = [
    http.get(apiPath("/xchanges"), ({ request }) => {
      const q = new URL(request.url).searchParams;
      listed.push(q);
      const count = typeof total === "number" ? total : total(q);
      return HttpResponse.json({
        result: Array.from({ length: Math.min(rows, count) }, (_, i) => exchange(i)),
        totalCount: count,
      });
    }),
    http.get(apiPath("/xchanges/summary"), ({ request }) => {
      const q = new URL(request.url).searchParams;
      summarised.push(q);
      return HttpResponse.json({ counts, buckets: q.getAll("filter").some((f) => f.startsWith("StartedOn:6")) ? buckets : [], bucketMinutes: 60 });
    }),
    http.get(apiPath("/subscriptions"), () => HttpResponse.json({ result: [{ id: 16, name: "Push orders" }], totalCount: 1 })),
    http.get(apiPath("/partners"), () => HttpResponse.json({ result: [], totalCount: 0 })),
    http.get(apiPath("/documents"), () => HttpResponse.json({ result: [], totalCount: 0 })),
  ];
  return { handlers, listed, summarised };
}

const filters = (q: URLSearchParams | undefined) => q?.getAll("filter") ?? [];

describe("status counts", () => {
  it("shows each status's count under the other filters, and asks for them without the status", async () => {
    const api = backend();
    const { user, router } = renderApp("/exchanges?status=failed&subscriptionId=16", { handlers: api.handlers });

    const failed = await screen.findByRole("button", { name: /^Failed 7$/ });
    expect(failed).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: /^All 130$/ })).toHaveAttribute("aria-pressed", "false");
    // The counts are what the tabs choose between, so the status itself is not part of the question.
    expect(filters(api.summarised[0])).toEqual(["SubscriptionId:1:16"]);

    await user.click(screen.getByRole("button", { name: /^Bad response 2$/ }));
    expect(router.state.location.search).toMatch(/status=badResponse/);
    await user.click(screen.getByRole("button", { name: /^All/ }));
    expect(router.state.location.search).not.toMatch(/status=/);
  });

  it("says 10,000+ past the cap", async () => {
    renderApp("/exchanges", { handlers: backend({ counts: { processing: 0, success: 10_001, badResponse: 0, failed: 0 } }).handlers });
    expect(await screen.findByRole("button", { name: /^Success 10,000\+$/ })).toBeVisible();
  });
});

describe("the volume chart", () => {
  it("covers the last 24 hours when the list has no time filter, and says so", async () => {
    const api = backend();
    renderApp("/exchanges", { handlers: api.handlers });
    expect(await screen.findByText("in the last 24 hours")).toBeVisible();
    // Two questions: the counts over all time, the chart over the last day.
    await waitFor(() => expect(api.summarised.some((q) => filters(q).some((f) => f.startsWith("StartedOn:6:")))).toBe(true));
    expect(api.summarised.some((q) => filters(q).length === 0)).toBe(true);
  });

  it("narrows the list to a bar that is clicked", async () => {
    const api = backend();
    const { user, router } = renderApp("/exchanges?range=24h", { handlers: api.handlers });

    const chart = await screen.findByRole("region", { name: "Exchange volume over time" });
    const bars = within(chart).getAllByRole("button");
    expect(bars).toHaveLength(2);
    expect(bars[1]).toHaveAccessibleName(/14 exchanges, 4 failed/);
    await user.pointer([{ keys: "[MouseLeft>]", target: bars[1] }, { keys: "[/MouseLeft]" }]);

    const search = new URLSearchParams(router.state.location.search);
    expect(search.get("range")).toBeNull();
    expect(new Date(search.get("from")!).toISOString()).toBe(new Date("2026-10-10T13:00:00Z").toISOString());
  });
});

describe("the query bar", () => {
  it("opens a pasted exchange id beside the list", async () => {
    const api = backend();
    const { user, router } = renderApp("/exchanges", { handlers: api.handlers });

    const box = await screen.findByRole("combobox", { name: "Search exchanges" });
    await user.type(box, id(1));
    expect(screen.getByRole("option", { name: /Exchange/ })).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{Enter}");

    const search = new URLSearchParams(router.state.location.search);
    expect(search.get("ids")).toBe(id(1));
    expect(search.get("open")).toBe(id(1));
    expect(await screen.findByRole("complementary", { name: `Exchange ${id(1)}` })).toBeVisible();
  });

  it("searches error text from the same box", async () => {
    const api = backend();
    const { user } = renderApp("/exchanges", { handlers: api.handlers });

    await user.type(await screen.findByRole("combobox", { name: "Search exchanges" }), "refused");
    await user.click(screen.getByRole("option", { name: /Error message contains/ }));

    await waitFor(() => expect(filters(api.listed.at(-1))).toEqual(["ErrorContains:4:refused"]));
    // And it shows as a chip that can be taken off again.
    await user.click(screen.getByRole("button", { name: "Remove filter Error" }));
    await waitFor(() => expect(filters(api.listed.at(-1))).toEqual([]));
  });

  it("adds a state filter from the menu", async () => {
    const api = backend();
    const { user, router } = renderApp("/exchanges", { handlers: api.handlers });

    await user.click(await screen.findByRole("button", { name: "Filter" }));
    await user.click(within(screen.getByRole("menu", { name: "Add a filter" })).getByRole("menuitem", { name: "Auto-retry scheduled" }));

    expect(router.state.location.search).toMatch(/scheduled=1/);
    await waitFor(() => expect(filters(api.listed.at(-1))).toEqual(["HasScheduledRetry:1:true"]));
    expect(screen.getByRole("button", { name: "Remove filter Auto-retry scheduled" })).toBeVisible();
  });

  it("edits a filter from its chip", async () => {
    const api = backend();
    const { user, router } = renderApp("/exchanges?correlationId=abc", { handlers: api.handlers });

    await user.click(await screen.findByRole("button", { name: "Edit filter: Correlation id is abc" }));
    const input = screen.getByRole("textbox", { name: "Correlation id is" });
    await user.clear(input);
    await user.type(input, "flow-7{Enter}");
    expect(new URLSearchParams(router.state.location.search).get("correlationId")).toBe("flow-7");
  });

  it("picks a time range", async () => {
    const api = backend();
    const { user, router } = renderApp("/exchanges", { handlers: api.handlers });

    await user.click(await screen.findByRole("button", { name: "Time: Any time" }));
    await user.click(screen.getByRole("button", { name: "Last hour" }));
    expect(router.state.location.search).toMatch(/range=1h/);
    await waitFor(() => expect(filters(api.listed.at(-1)).some((f) => f.startsWith("StartedOn:6:"))).toBe(true));
    expect(screen.getByRole("button", { name: "Time: Last hour" })).toBeVisible();
  });
});

describe("saved views", () => {
  it("applies a built-in view and saves the current filters as one of your own", async () => {
    const api = backend();
    const { user, router } = renderApp("/exchanges?correlationId=abc", { handlers: api.handlers });

    await user.click(await screen.findByRole("button", { name: "Saved views: Custom" }));
    await user.click(screen.getByRole("button", { name: "Save current filters as a view" }));
    await user.type(screen.getByRole("textbox", { name: "View name" }), "Flow abc{Enter}");
    expect(JSON.parse(localStorage.getItem("bitween.exchanges.views")!)).toEqual([{ name: "Flow abc", search: "correlationId=abc" }]);
    expect(screen.getByRole("button", { name: "Saved views: Flow abc" })).toBeVisible();

    await user.click(within(screen.getByRole("menu", { name: "Saved views" })).getByRole("menuitem", { name: /Needs attention/ }));
    const search = new URLSearchParams(router.state.location.search);
    expect(search.get("status")).toBe("failed");
    expect(search.get("latest")).toBe("1");
    expect(search.get("correlationId")).toBeNull();
  });
});

describe("the open exchange", () => {
  it("opens beside the list, narrows the list, and closes on Escape", async () => {
    const api = backend();
    const { user, router } = renderApp("/exchanges", { handlers: api.handlers });

    expect(await screen.findByRole("columnheader", { name: "Pipeline" })).toBeVisible();
    await user.click(screen.getAllByRole("row")[2]);

    expect(await screen.findByRole("complementary", { name: `Exchange ${id(1)}` })).toBeVisible();
    expect(router.state.location.search).toMatch(`open=${id(1)}`);
    expect(screen.queryByRole("columnheader", { name: "Pipeline" })).not.toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("complementary", { name: /^Exchange / })).not.toBeInTheDocument();
  });

  it("keeps the route and the pipeline in the narrowed list, folded into the columns that remain", async () => {
    const api = backend();
    const { user } = renderApp("/exchanges", { handlers: api.handlers });

    await user.click((await screen.findAllByRole("row"))[2]);
    await screen.findByRole("complementary", { name: `Exchange ${id(1)}` });

    const row = screen.getAllByRole("row")[1];
    expect(screen.getByRole("columnheader", { name: "Status and pipeline" })).toBeVisible();
    expect(within(row).getByText(/· Push orders/)).toBeVisible();
    expect(within(row).getByText("Failed while mapping")).toBeVisible();
  });

  it("shows a stage in progress as one, in the list and in the open exchange", async () => {
    const running = { status: null, finishedOn: null, exception: null, inputKey: "in-1", inputFileName: "in.json", inputFileSize: 4 };
    const api = backend({ rows: 0 });
    const { user } = renderApp("/exchanges", {
      handlers: [
        http.get(apiPath("/xchanges"), () => HttpResponse.json({ result: [exchange(0, running)], totalCount: 1 })),
        http.get(apiPath("/bitweendocs"), () => HttpResponse.json({ key: "in-1", data: "{}" })),
        ...api.handlers.slice(1),
      ],
    });

    const row = (await screen.findAllByRole("row"))[1];
    expect(within(row).getByText("Mapping now")).toBeVisible();
    // The strip's running segment is the striped, moving one.
    expect(row.querySelector(".journey-running")).toBeInTheDocument();

    await user.click(row);
    const panel = await screen.findByRole("complementary", { name: `Exchange ${id(0)}` });
    expect(within(panel).getByText("In progress")).toBeVisible();
    expect(within(panel).getByText("Mapping now. Its document shows here when it finishes.")).toBeVisible();
  });

  it("moves through the list with J and K, and ticks the open one with X", async () => {
    const api = backend();
    const { user } = renderApp("/exchanges", { handlers: api.handlers });
    await screen.findAllByRole("checkbox", { name: /^Select (?!all\b)/ });

    await user.keyboard("j");
    expect(await screen.findByRole("complementary", { name: `Exchange ${id(0)}` })).toBeVisible();
    await user.keyboard("j");
    expect(await screen.findByRole("complementary", { name: `Exchange ${id(1)}` })).toBeVisible();
    await user.keyboard("k");
    expect(await screen.findByRole("complementary", { name: `Exchange ${id(0)}` })).toBeVisible();

    await user.keyboard("x");
    expect(screen.getByRole("checkbox", { name: `Select ${id(0)}` })).toBeChecked();
  });

  it("stays closed once closed, when a link to one exchange opened it", async () => {
    const api = backend({ rows: 1 });
    const { user, router } = renderApp(`/exchanges?ids=${id(0)}`, { handlers: api.handlers });

    expect(await screen.findByRole("complementary", { name: `Exchange ${id(0)}` })).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Close exchange" }));
    expect(screen.queryByRole("complementary", { name: /^Exchange / })).not.toBeInTheDocument();
    expect(router.state.location.search).toMatch(/open=-/);
  });
});

describe("pausing", () => {
  it("counts what arrives while paused instead of moving the rows", async () => {
    const api = backend({ total: (q) => (filters(q).some((f) => f.startsWith("StartedOn:6:")) ? 4 : 3) });
    const { user } = renderApp("/exchanges", { handlers: api.handlers });

    await user.click(await screen.findByRole("button", { name: "Pause live updates" }));
    expect(screen.getByRole("combobox", { name: "Refresh interval" })).toHaveValue("0");
    expect(await screen.findByRole("button", { name: /4 new exchanges since you paused/ })).toBeVisible();
    // Asked from just after the newest row on screen.
    const asked = api.listed.find((q) => filters(q).some((f) => f.startsWith("StartedOn:6:")))!;
    expect(filters(asked)).toContain("StartedOn:6:2026-09-20T10:00:00.001Z");
  });
});

describe("refresh", () => {
  it("asks for the list and the counts again at once, even paused", async () => {
    const api = backend();
    const { user } = renderApp("/exchanges", { handlers: api.handlers });

    // Paused, nothing else asks: every request after the click is the button's.
    await user.click(await screen.findByRole("button", { name: "Pause live updates" }));
    await screen.findByText("SO-0");
    const before = { listed: api.listed.length, summarised: api.summarised.length };

    await user.click(screen.getByRole("button", { name: "Refresh" }));
    await waitFor(() => expect(api.listed.length).toBeGreaterThan(before.listed));
    expect(api.summarised.length).toBeGreaterThan(before.summarised);
  });
});

describe("promoted property", () => {
  it("sends the picked key apart from the value, so any part of that key's value matches", async () => {
    const api = backend();
    renderApp("/exchanges?propertyKey=orderNo&property=1001", { handlers: api.handlers });

    await waitFor(() => expect(api.listed.length).toBeGreaterThan(0));
    const sent = filters(api.listed.at(-1)!);
    expect(sent).toContain("PromotedPropertyKey:1:orderNo");
    expect(sent).toContain("PromotedPropertiesRaw:4:1001");
  });
});
