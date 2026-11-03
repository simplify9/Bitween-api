import { act, cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** The SignalR connection background work is told about over, faked: each test says what it hears. */
type Change = { id: string; status: string; done?: number; total?: number };
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

/**
 * The exchanges list: selecting across a whole filter, narrowing to the newest attempt of each
 * chain, and the promoted-property chips each row is named by.
 */

/** GET /xchanges: `RawXchangeRow` in src/api/http/exchanges.ts. A failed exchange. */
const exchange = (n: number, promotedProperties: Record<string, string | null> | null = null) => ({
  id: `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`,
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
  promotedProperties,
  retryFor: null,
  aggregationXchangeId: null,
  responseBad: null,
  correlationId: null,
  partnerId: null,
  scheduledRetryOn: null,
  hasRetry: false,
});

const PAGE = 25;

/**
 * The list endpoint, answering `total` matches a page at a time, and keeping every query it was
 * asked. `total` may depend on the query, the way a filter changes the count. It answers the summary
 * too, where the page reads its total from: every match counted as failed, which is what these lists
 * show.
 */
function exchanges({
  total,
  properties = null,
}: {
  total: number | ((q: URLSearchParams) => number);
  properties?: Record<string, string | null> | null;
}) {
  const asked: URLSearchParams[] = [];
  const handler = http.get(/\/api\/xchanges(\/summary)?(\?|$)/, ({ request }) => {
    const url = new URL(request.url);
    const q = url.searchParams;
    const count = typeof total === "number" ? total : total(q);
    if (url.pathname.endsWith("/summary"))
      return HttpResponse.json({ counts: { processing: 0, success: 0, badResponse: 0, failed: count }, buckets: [], bucketMinutes: 0 });
    asked.push(q);
    const page = Number(q.get("page") ?? 0);
    const result = Array.from({ length: Math.max(0, Math.min(PAGE, count - page * PAGE)) }, (_, i) =>
      exchange(page * PAGE + i, properties),
    );
    return HttpResponse.json({ result, totalCount: count });
  });
  return { handler, asked };
}

/** The pickers above the list, and the counts and chart beside it. Nothing in them matters here. */
const filterOptions = [
  ...["/partners", "/subscriptions", "/documents"].map((p) =>
    http.get(apiPath(p), () => HttpResponse.json({ result: [], totalCount: 0 })),
  ),
  http.get(apiPath("/xchanges/summary"), () =>
    HttpResponse.json({ counts: { processing: 0, success: 0, badResponse: 0, failed: 0 }, buckets: [], bucketMinutes: 0 }),
  ),
];

const rowCheckboxes = () => screen.getAllByRole("checkbox", { name: /^Select (?!all\b)/ });

describe("the exchanges list", () => {
  /**
   * A selection has to be able to mean "everything this filter matches", or a 200-exchange
   * recovery is 8 pages of ticking boxes. Stops at the confirm - what it says is the point, and
   * running it would retry the whole filter.
   */
  describe("select all matching", () => {
    /** Ticks the whole page, takes up the offer to go further, then opens the confirm. */
    async function selectAllMatching(user: ReturnType<typeof renderApp>["user"]) {
      await user.click(await screen.findByRole("checkbox", { name: "Select all on this page" }));
      await user.click(screen.getByRole("button", { name: "Select all 60 matching this filter" }));
    }

    it("covers the whole filter, and the confirm says what will run", async () => {
      const list = exchanges({ total: 60 });
      const previews: unknown[] = [];
      const { user } = renderApp("/exchanges?status=failed", {
        handlers: [
          list.handler,
          ...filterOptions,
          http.post(apiPath("/xchanges/bulkretrypreview"), async ({ request }) => {
            previews.push(await request.json());
            return HttpResponse.json({
              selected: 59,
              willRetry: 59,
              limit: 500,
              overLimit: false,
              substituted: [],
              skipped: [],
              properties: {},
            });
          }),
        ],
      });

      await selectAllMatching(user);
      // The bar's count, not the tabs' - they show the filter's 60 as well.
      const selection = () => within(screen.getByText(/everything this filter matches/).parentElement!);
      expect(screen.getByText(/everything this filter matches/)).toBeVisible();
      expect(selection().getByText("60")).toBeVisible();

      // Unticking a row in this mode records an exclusion rather than dropping out of it.
      await user.click(rowCheckboxes()[0]);
      expect(screen.getByText(/1 unticked/)).toBeVisible();
      expect(selection().getByText("59")).toBeVisible();
      expect(screen.getByText(/everything this filter matches/)).toBeVisible();

      // The confirm describes the selection the server resolved, not the rows on screen.
      await user.click(screen.getByRole("button", { name: "Retry selected…" }));
      const dialog = await screen.findByRole("dialog");
      expect(within(dialog).getByText("Retry 59 exchanges?")).toBeVisible();
      expect(await within(dialog).findByText(/^59 exchanges will run again/)).toBeVisible();
      // What it asked about is the filter itself, minus the row unticked - not 24 ids off one page.
      expect(previews).toEqual([
        { filter: "filter=StatusFilter%3A1%3A3", excludeIds: [exchange(0).id], reason: "Bulk retry", reset: false },
      ]);

      // Nothing is retried from here: the retry endpoint has no handler, so asking it would fail.
      await user.click(within(dialog).getByRole("button", { name: "Cancel" }));
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    it("stays up while the next page loads, and what acts on it waits for the number", async () => {
      let pageIn!: () => void;
      const nextPage = new Promise<void>((resolve) => (pageIn = resolve));
      const { user } = renderApp("/exchanges?status=failed", {
        handlers: [
          // Holds the second page, then leaves it to the list's own handler.
          http.get(/\/api\/xchanges(\?|$)/, async ({ request }) => {
            if (new URL(request.url).searchParams.get("page") === "1") await nextPage;
          }),
          exchanges({ total: 60 }).handler,
          ...filterOptions,
        ],
      });

      await selectAllMatching(user);
      const selection = () => within(screen.getByText(/everything this filter matches/).parentElement!);
      expect(selection().getByText("60")).toBeVisible();

      // The first page's rows are still up: the footer's total and the selection's wait with them.
      await user.click(screen.getByRole("button", { name: "Next" }));
      expect(await selection().findByTitle("Still counting how many match")).toBeVisible();
      expect(screen.getAllByTitle("Still counting how many match")).toHaveLength(2);
      expect(screen.getByRole("button", { name: "Retry selected…" })).toBeDisabled();
      expect(screen.getByRole("button", { name: "Export files" })).toBeDisabled();

      pageIn();
      expect(await selection().findByText("60")).toBeVisible();
      expect(screen.getByRole("button", { name: "Retry selected…" })).toBeEnabled();
    });

    it.each([
      {
        answer: "past what rounds can take, it refuses",
        plan: { selected: 6000, willRetry: 0, limit: 500, maxSelection: 5000, overLimit: true },
        says: /more than the 5,000 one retry can work through/,
      },
      {
        answer: "with nothing retryable, it says so",
        plan: { selected: 60, willRetry: 0, limit: 500, maxSelection: 5000, overLimit: false },
        says: /^Nothing here can be retried\.$/,
      },
    ])("$answer, and offers only Close", async ({ plan, says }) => {
      const { user } = renderApp("/exchanges?status=failed", {
        handlers: [
          exchanges({ total: 60 }).handler,
          ...filterOptions,
          http.post(apiPath("/xchanges/bulkretrypreview"), () =>
            HttpResponse.json({ ...plan, substituted: [], skipped: [], properties: {} }),
          ),
        ],
      });

      await selectAllMatching(user);
      await user.click(screen.getByRole("button", { name: "Retry selected…" }));
      const dialog = await screen.findByRole("dialog");
      expect(await within(dialog).findByText(says)).toBeVisible();
      expect(within(dialog).queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();

      // By text, not accessible name: the dialog's own × is also called "Close".
      await user.click(within(dialog).getByText("Close", { selector: "button" }));
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    /**
     * A big retry goes as one request for the whole selection, asked to carry on in the background:
     * the dialog shows how far it has got, as the header does, and can be closed meanwhile.
     */
    describe("a big retry, in the background", () => {
      const targets = [11, 12, 13, 14, 15].map((n) => exchange(n).id);
      const plan = (extra: Record<string, unknown> = {}) => ({
        selected: 59,
        willRetry: 5,
        limit: 2,
        maxSelection: 50000,
        overLimit: false,
        targets,
        substituted: [],
        skipped: [{ id: exchange(16).id, reason: "It succeeded, so there is nothing to retry." }],
        properties: {},
        ...extra,
      });
      const preview = http.post(apiPath("/xchanges/bulkretrypreview"), () => HttpResponse.json(plan()));

      /** The retry, carried on in the background as `bulk-1`, answering `answer` once it's collected. */
      function background(answer: Record<string, unknown>) {
        const sent: { prefer: string | null; body: Record<string, unknown> }[] = [];
        return {
          sent,
          handlers: [
            http.post(apiPath("/xchanges/bulkretry"), async ({ request }) => {
              sent.push({ prefer: request.headers.get("Prefer"), body: (await request.json()) as Record<string, unknown> });
              return HttpResponse.json({ id: "bulk-1", status: "Running" }, { status: 202 });
            }),
            http.get(apiPath("/background/bulk-1/result"), () => HttpResponse.json(answer)),
          ],
        };
      }

      it("sends the whole selection once, shows how far it has got, then says what ran", async () => {
        const retry = background(plan());
        const { user } = renderApp("/exchanges?status=failed", {
          handlers: [exchanges({ total: 60 }).handler, ...filterOptions, preview, ...retry.handlers],
        });

        await selectAllMatching(user);
        await user.click(screen.getByRole("button", { name: "Retry selected…" }));
        const dialog = await screen.findByRole("dialog");
        expect(await within(dialog).findByText(/A retry this size runs in the background/)).toBeVisible();

        await user.click(within(dialog).getByRole("button", { name: "Retry" }));
        await waitFor(() => expect(retry.sent).toHaveLength(1));
        expect(retry.sent[0].prefer).toBe("respond-async, wait=3");
        expect(retry.sent[0].body).toMatchObject({ filter: expect.any(String), reset: false });

        tell({ id: "bulk-1", status: "Running", done: 2, total: 5 });
        expect(await within(dialog).findByText(/2 of 5 done…/)).toBeVisible();
        expect(within(dialog).getByRole("progressbar", { name: "How far it has got" })).toHaveAttribute("aria-valuenow", "2");

        tell({ id: "bulk-1", status: "Done", done: 5, total: 5 });
        expect(await screen.findByText("5 retries started, 1 skipped.")).toBeVisible();
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });

      it("closed, carries on; cancelled from the header, says how far it got", async () => {
        const retry = background(plan({ willRetry: 2, stoppedAfter: 2, targets: [] }));
        const { user } = renderApp("/exchanges?status=failed", {
          handlers: [exchanges({ total: 60 }).handler, ...filterOptions, preview, ...retry.handlers],
        });

        await selectAllMatching(user);
        await user.click(screen.getByRole("button", { name: "Retry selected…" }));
        const dialog = await screen.findByRole("dialog");
        await within(dialog).findByText(/A retry this size runs in the background/);
        await user.click(within(dialog).getByRole("button", { name: "Retry" }));
        tell({ id: "bulk-1", status: "Running", done: 2, total: 5 });
        // The dialog's own Close, which stops being "Cancel" once it's under way - not the corner's.
        await user.click((await within(dialog).findAllByRole("button", { name: "Close" })).find((b) => b.textContent === "Close")!);
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

        tell({ id: "bulk-1", status: "Cancelled", done: 2, total: 5 });
        expect(
          await screen.findByText("2 retries started, 1 skipped. Cancelled after 2; the rest were not retried."),
        ).toBeVisible();
        expect(retry.sent).toHaveLength(1);
      });
    });
  });

  /**
   * A chain is one piece of work however many attempts it took, so the list needs to be able to
   * show the newest attempt of each - otherwise a chain retried nine times fills nine rows, none
   * of which is the current state of anything.
   *
   * Which rows that leaves is the server's: Exchange_search_can_return_only_the_newest_attempt_of_each_chain
   * in RetryChainTests. What's left here is the toggle, and what it asks for.
   */
  it("shows the rows at once, and how many match once the counts are in", async () => {
    let countsIn!: () => void;
    const counted = new Promise<void>((resolve) => (countsIn = resolve));
    const list = exchanges({ total: 3 });
    renderApp("/exchanges", {
      handlers: [
        http.get(apiPath("/xchanges/summary"), async () => {
          await counted;
          return HttpResponse.json({ counts: { processing: 0, success: 0, badResponse: 0, failed: 3 }, buckets: [], bucketMinutes: 0 });
        }),
        list.handler,
        ...filterOptions,
      ],
    });

    // The rows don't wait for the count, and don't ask for one.
    expect(await screen.findByText("Showing 1-3")).toBeVisible();
    expect(screen.getByTitle("Still counting how many match")).toBeVisible();
    expect(list.asked[0].get("count")).toBe("false");

    countsIn();
    expect(await screen.findByText("Showing 1-3 of 3")).toBeVisible();
  });

  it("shows no number for a status still being counted, and still shows the rest", async () => {
    const summary = (success: number) =>
      http.get(apiPath("/xchanges/summary"), () =>
        HttpResponse.json({
          counts: { processing: null, success, badResponse: 0, failed: 3 },
          buckets: [],
          bucketMinutes: 0,
          counting: true,
        }),
      );
    const tabs = async () => within(await screen.findByRole("group", { name: "Status" }));

    renderApp("/exchanges", { handlers: [summary(10_001), exchanges({ total: 3 }).handler, ...filterOptions] });
    expect(await (await tabs()).findByRole("button", { name: "Failed 3" })).toBeVisible();
    expect((await tabs()).getByRole("button", { name: "Processing" })).toBeVisible();
    // The others already pass the cap, so all of them together is still "more than".
    expect((await tabs()).getByRole("button", { name: "All 10,000+" })).toBeVisible();

    cleanup();
    renderApp("/exchanges", { handlers: [summary(5), exchanges({ total: 3 }).handler, ...filterOptions] });
    // Under the cap, the one missing could hold any number, so all of them together has none either.
    expect(await (await tabs()).findByRole("button", { name: "Failed 3" })).toBeVisible();
    expect((await tabs()).getByRole("button", { name: "All" })).toBeVisible();
  });

  it("asks again while a status is still being counted, with live refresh paused too", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      let counted = false;
      renderApp("/exchanges", {
        handlers: [
          http.get(apiPath("/xchanges/summary"), () =>
            HttpResponse.json({
              counts: { processing: counted ? 7 : null, success: 0, badResponse: 0, failed: 3 },
              buckets: [],
              bucketMinutes: 0,
              counting: !counted,
            }),
          ),
          exchanges({ total: 3 }).handler,
          ...filterOptions,
        ],
      });
      const tabs = within(await screen.findByRole("group", { name: "Status" }));
      expect(await tabs.findByRole("button", { name: "Failed 3" })).toBeVisible();

      // Paused, nothing else asks again: only that the count isn't in yet.
      fireEvent.change(screen.getByRole("combobox", { name: "Refresh interval" }), { target: { value: "0" } });
      counted = true;
      await act(() => vi.advanceTimersByTimeAsync(15_000));
      expect(await tabs.findByRole("button", { name: "Processing 7" })).toBeVisible();
    } finally {
      vi.useRealTimers();
    }
  });

  it("says when the list couldn't be read, rather than that nothing matches, and reads it again on Try again", async () => {
    let timedOut = true;
    const list = exchanges({ total: 3 });
    const { user } = renderApp("/exchanges?status=failed&latest=1", {
      handlers: [
        http.get(/\/api\/xchanges(\?|$)/, () =>
          timedOut
            ? HttpResponse.json(
                { DATABASE_TIMED_OUT: ["The database took too long to answer. Asking for less, such as a shorter time range, may help."] },
                { status: 504 },
              )
            : undefined,
        ),
        list.handler,
        ...filterOptions,
      ],
    });

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("Couldn't load the exchanges")).toBeVisible();
    expect(within(alert).getByText(/The database took too long to answer/)).toBeVisible();
    expect(screen.queryByText("No exchanges match")).not.toBeInTheDocument();

    timedOut = false;
    await user.click(within(alert).getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("Showing 1-3 of 3")).toBeVisible();
  });

  it("can show only the newest attempt of each chain", async () => {
    const latest = (q: URLSearchParams) => q.getAll("filter").includes("LatestOnly:1:true");
    const list = exchanges({ total: (q) => (latest(q) ? 30 : 40) });
    const { user, router } = renderApp("/exchanges?status=failed", { handlers: [list.handler, ...filterOptions] });

    expect(await screen.findByText(/Showing 1-25 of 40/)).toBeVisible();
    const pill = screen.getByRole("button", { name: "Latest attempt only" });
    expect(pill).toHaveAttribute("aria-pressed", "false");
    expect(latest(list.asked.at(-1)!)).toBe(false);

    await user.click(pill);
    expect(router.state.location.search).toMatch(/latest=1/);
    expect(pill).toHaveAttribute("aria-pressed", "true");
    expect(await screen.findByText(/Showing 1-25 of 30/)).toBeVisible();
    // Narrowing whatever the status picked, not replacing it.
    expect(list.asked.at(-1)!.getAll("filter")).toEqual(["StatusFilter:1:3", "LatestOnly:1:true"]);

    // And it survives a reload, since it lives in the URL like every other filter.
    const { pathname, search } = router.state.location;
    cleanup();
    list.asked.length = 0;
    renderApp(pathname + search, { handlers: [list.handler, ...filterOptions] });
    expect(await screen.findByRole("button", { name: "Latest attempt only" })).toHaveAttribute("aria-pressed", "true");
    expect(await screen.findByText(/Showing 1-25 of 30/)).toBeVisible();
    expect(latest(list.asked[0])).toBe(true);
  });

  it("opens promoted properties in a panel, not just a tooltip", async () => {
    // Ten properties, one value too long for a chip, and a null - the value shape that used to
    // take the page down on paging.
    const properties = {
      "Trace Code": "SHOR020",
      "Agent Code": null,
      "First Time": "True",
      CreatedBy: "madebydaily.shopify.com",
      "Order Ref": "SO-2026-0088341-RETURN-LINE-2",
      Weight: "2.4kg",
      Destination: "FR-75011",
      Service: "EXPRESS",
      Attempt: "3",
      Manifest: "M-88214",
    };
    const { user } = renderApp("/exchanges", {
      handlers: [exchanges({ total: 3, properties }).handler, ...filterOptions],
    });

    await user.click((await screen.findAllByRole("button", { name: "Show all 10 promoted properties" }))[0]);

    // Every property, in full - including the one too long to have fitted a chip.
    expect(screen.getByText("10 promoted properties")).toBeVisible();
    expect(screen.getByText("SO-2026-0088341-RETURN-LINE-2")).toBeVisible();

    // Opening the panel is not a request to expand the row underneath it.
    expect(screen.queryByText("Exchange id")).not.toBeInTheDocument();

    // user-event stands in a clipboard for the page; this reads back what the page wrote to it.
    await user.click(screen.getByRole("button", { name: "Copy all" }));
    const copied = await navigator.clipboard.readText();
    expect(copied).toContain("Order Ref=SO-2026-0088341-RETURN-LINE-2");
    expect(copied.split("\n")).toHaveLength(10);
  });

  it("pages through rows whose promoted values are null", async () => {
    // A promoted path that resolved to nothing arrives as null, not "".
    const list = exchanges({ total: 40, properties: { "Agent Code": null, "Trace Code": null, "First Time": "True" } });
    const { user } = renderApp("/exchanges", { handlers: [list.handler, ...filterOptions] });

    expect(await screen.findByText(/Showing 1-25 of 40/)).toBeVisible();
    await user.click(screen.getAllByRole("button", { name: "Next" })[0]);

    expect(await screen.findByText(/Showing 26-40 of 40/)).toBeVisible();
    expect(list.asked.at(-1)?.get("page")).toBe("1");
    expect(screen.getAllByText("True").length).toBeGreaterThan(0);
    expect(screen.queryByText("Unexpected Application Error")).not.toBeInTheDocument();
  });

  /**
   * The files of a selection as one zip. Only reading is involved, so someone who can see exchanges
   * but not retry them can still tick rows and export.
   */
  describe("export files", () => {
    /** The question asked before each download: how many exchanges come without their files. */
    const exportCheck = (withoutFiles = 0, count = 2, keptDays: number | null = 30) =>
      http.post(apiPath("/xchanges/export/check"), () => HttpResponse.json({ count, withoutFiles, keptDays }));

    afterEach(() => vi.restoreAllMocks());

    /**
     * The export, which the server carries on in the background as `exp-1` - the UI asks it to at once -
     * keeping every request it was sent; done, it's downloaded from the link the server makes for it.
     */
    function exportInBackground(exported: unknown[]) {
      return [
        http.post(apiPath("/xchanges/export"), async ({ request }) => {
          exported.push(await request.json());
          return HttpResponse.json({ id: "exp-1", status: "Running" }, { status: 202 });
        }),
        http.post(apiPath("/background/exp-1/link"), () =>
          HttpResponse.json({ url: "/api/background/exp-1/file?expires=1&seal=s" }),
        ),
      ];
    }

    it("lets a viewer download the ticked rows' files, without offering a retry", async () => {
      const exported: unknown[] = [];
      const followed: string[] = [];
      vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
        followed.push(new URL(this.href).pathname);
      });

      const { user } = renderApp("/exchanges", {
        as: { permissions: ["exchanges.view"] },
        handlers: [exchanges({ total: 3 }).handler, ...filterOptions, exportCheck(), ...exportInBackground(exported)],
      });

      await screen.findAllByRole("checkbox", { name: /^Select (?!all\b)/ });
      await user.click(rowCheckboxes()[0]);
      await user.click(rowCheckboxes()[2]);

      expect(screen.queryByRole("button", { name: "Retry selected…" })).not.toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: "Export files" }));
      await vi.waitFor(() => expect(exported).toEqual([{ ids: [exchange(0).id, exchange(2).id] }]));

      // Downloaded from the link once it's done, not held in the page's memory first.
      tell({ id: "exp-1", status: "Done" });
      await vi.waitFor(() => expect(followed).toEqual(["/api/background/exp-1/file"]));
    });

    it("over an unfiltered list, select all matching still sends a filter that matches everything", async () => {
      const exported: unknown[] = [];
      vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

      const { user } = renderApp("/exchanges", {
        handlers: [exchanges({ total: 30 }).handler, ...filterOptions, exportCheck(0, 30), ...exportInBackground(exported)],
      });

      await user.click(await screen.findByRole("checkbox", { name: "Select all on this page" }));
      await user.click(screen.getByRole("button", { name: "Select all 30 matching this filter" }));
      await user.click(screen.getByRole("button", { name: "Export files" }));

      // An empty filter would read as no filter, and select nothing.
      await vi.waitFor(() => expect(exported).toEqual([{ filter: "filter=LatestOnly%3A1%3Afalse", excludeIds: [] }]));
      tell({ id: "exp-1", status: "Done" });
    });

    it("says first when some exchanges are past the files storage keeps, and exports the rest on confirm", async () => {
      const exported: unknown[] = [];
      vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

      const { user } = renderApp("/exchanges", {
        handlers: [exchanges({ total: 3 }).handler, ...filterOptions, exportCheck(1, 3), ...exportInBackground(exported)],
      });

      await screen.findAllByRole("checkbox", { name: /^Select (?!all\b)/ });
      for (const box of rowCheckboxes()) await user.click(box);
      await user.click(screen.getByRole("button", { name: "Export files" }));

      const dialog = await screen.findByRole("dialog", { name: "Some files are gone" });
      expect(dialog).toHaveTextContent(
        "1 of the 3 exchanges selected is older than storage keeps files (30 days), so the zip won't have its files.",
      );
      expect(exported).toEqual([]);

      await user.click(within(dialog).getByRole("button", { name: "Export the other 2 exchanges" }));
      await vi.waitFor(() => expect(exported).toHaveLength(1));
      tell({ id: "exp-1", status: "Done" });
    });

    it("downloads nothing when none of the selection has files left, and says why", async () => {
      let exported = false;
      const { user } = renderApp("/exchanges", {
        handlers: [
          exchanges({ total: 3 }).handler,
          ...filterOptions,
          exportCheck(1, 1),
          http.post(apiPath("/xchanges/export"), () => {
            exported = true;
            return new HttpResponse(null, { status: 500 });
          }),
        ],
      });

      await screen.findAllByRole("checkbox", { name: /^Select (?!all\b)/ });
      await user.click(rowCheckboxes()[0]);
      await user.click(screen.getByRole("button", { name: "Export files" }));

      expect(await screen.findByRole("alert")).toHaveTextContent(
        "The selected exchange is older than storage keeps files (30 days), so there's nothing to export.",
      );
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(exported).toBe(false);
    });

    it("past the limit, says why it can't rather than failing on the server", async () => {
      const { user } = renderApp("/exchanges?status=failed", {
        handlers: [exchanges({ total: 60000 }).handler, ...filterOptions],
      });

      await user.click(await screen.findByRole("checkbox", { name: "Select all on this page" }));
      await user.click(screen.getByRole("button", { name: /^Select all .* matching this filter$/ }));

      const button = screen.getByRole("button", { name: "Export files" });
      expect(button).toBeDisabled();
      expect(button).toHaveAttribute("title", expect.stringContaining("at most 50,000 exchanges"));
    });
  });
});
