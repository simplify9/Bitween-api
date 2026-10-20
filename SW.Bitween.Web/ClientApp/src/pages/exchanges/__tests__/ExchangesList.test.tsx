import { cleanup, screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

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
 * asked. `total` may depend on the query, the way a filter changes the count.
 */
function exchanges({
  total,
  properties = null,
}: {
  total: number | ((q: URLSearchParams) => number);
  properties?: Record<string, string | null> | null;
}) {
  const asked: URLSearchParams[] = [];
  const handler = http.get(apiPath("/xchanges"), ({ request }) => {
    const q = new URL(request.url).searchParams;
    asked.push(q);
    const count = typeof total === "number" ? total : total(q);
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
      expect(screen.getByText(/everything this filter matches/)).toBeVisible();
      expect(screen.getByText("60")).toBeVisible();

      // Unticking a row in this mode records an exclusion rather than dropping out of it.
      await user.click(rowCheckboxes()[0]);
      expect(screen.getByText(/1 unticked/)).toBeVisible();
      expect(screen.getByText("59")).toBeVisible();
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
     * Past what one retry carries out, the confirm doesn't send the selection: it sends the attempts
     * the preview listed, a round at a time. Here the limit is 2, so 5 attempts are 3 rounds.
     */
    describe("past one retry, in rounds", () => {
      const targets = [11, 12, 13, 14, 15].map((n) => exchange(n).id);
      const preview = http.post(apiPath("/xchanges/bulkretrypreview"), () =>
        HttpResponse.json({
          selected: 59,
          willRetry: 5,
          limit: 2,
          maxSelection: 5000,
          overLimit: false,
          targets,
          substituted: [],
          skipped: [{ id: exchange(16).id, reason: "It succeeded, so there is nothing to retry." }],
          properties: {},
        }),
      );
      /**
       * The retry endpoint, keeping every round it was sent; `fails` answers that round with a 500,
       * and `holds` keeps that round waiting until `release` is called.
       */
      function rounds({ fails, holds }: { fails?: number; holds?: number } = {}) {
        const sent: { ids: string[] }[] = [];
        let release = () => {};
        const held = new Promise<void>((resolve) => (release = resolve));
        const handler = http.post(apiPath("/xchanges/bulkretry"), async ({ request }) => {
          const body = (await request.json()) as { ids: string[] };
          sent.push(body);
          if (sent.length === holds) await held;
          if (sent.length === fails) return HttpResponse.json({ message: "Broker unreachable" }, { status: 500 });
          const n = body.ids.length;
          return HttpResponse.json({ selected: n, willRetry: n, limit: 2, maxSelection: 5000, overLimit: false, targets: body.ids });
        });
        return { handler, sent, release: () => release() };
      }

      it("sends the planned attempts a round at a time, then says what ran", async () => {
        const retry = rounds({ holds: 2 });
        const { user } = renderApp("/exchanges?status=failed", {
          handlers: [exchanges({ total: 60 }).handler, ...filterOptions, preview, retry.handler],
        });

        await selectAllMatching(user);
        await user.click(screen.getByRole("button", { name: "Retry selected…" }));
        const dialog = await screen.findByRole("dialog");
        expect(await within(dialog).findByText(/they go in 3 rounds of up to 2/)).toBeVisible();

        await user.click(within(dialog).getByRole("button", { name: "Retry" }));
        // While the second round is out, the first one's attempts are counted.
        expect(await within(dialog).findByText("Retried 2 of 5…")).toBeVisible();
        expect(within(dialog).getByRole("progressbar", { name: "Retried so far" })).toHaveAttribute("aria-valuenow", "2");
        retry.release();

        expect(await screen.findByText("5 retries started, 1 skipped.")).toBeVisible();
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
        expect(retry.sent.map((r) => r.ids)).toEqual([targets.slice(0, 2), targets.slice(2, 4), targets.slice(4)]);
      });

      it("closed mid-way, stops after the round under way and leaves a confirm opened since alone", async () => {
        const retry = rounds({ holds: 1 });
        const { user } = renderApp("/exchanges?status=failed", {
          handlers: [exchanges({ total: 60 }).handler, ...filterOptions, preview, retry.handler],
        });

        await selectAllMatching(user);
        await user.click(screen.getByRole("button", { name: "Retry selected…" }));
        const dialog = await screen.findByRole("dialog");
        await within(dialog).findByText(/they go in 3 rounds/);
        await user.click(within(dialog).getByRole("button", { name: "Retry" }));
        await user.click(within(dialog).getByRole("button", { name: "Cancel" }));

        await user.click(screen.getByRole("button", { name: "Retry selected…" }));
        await screen.findByRole("dialog");
        retry.release();

        expect(
          await screen.findByText("2 retries started, 1 skipped. Stopped after 2 of 5; the rest were not retried."),
        ).toBeVisible();
        expect(retry.sent).toHaveLength(1);
        expect(screen.getByRole("dialog")).toBeVisible();
        expect(screen.getByRole("button", { name: "Retry selected…" })).toBeVisible();
      });

      it("stops at a round that fails, says how far it got, and doesn't offer to go again", async () => {
        const retry = rounds({ fails: 2 });
        const { user } = renderApp("/exchanges?status=failed", {
          handlers: [exchanges({ total: 60 }).handler, ...filterOptions, preview, retry.handler],
        });

        await selectAllMatching(user);
        await user.click(screen.getByRole("button", { name: "Retry selected…" }));
        const dialog = await screen.findByRole("dialog");
        await within(dialog).findByText(/they go in 3 rounds/);
        await user.click(within(dialog).getByRole("button", { name: "Retry" }));

        expect(await within(dialog).findByRole("alert")).toHaveTextContent(
          /^2 of 5 were retried, then a round failed .*The rest were not retried\.$/,
        );
        expect(retry.sent).toHaveLength(2);
        expect(within(dialog).queryByRole("button", { name: "Retry" })).not.toBeInTheDocument();
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

    afterEach(() => {
      vi.restoreAllMocks();
      Reflect.deleteProperty(URL, "createObjectURL");
      Reflect.deleteProperty(URL, "revokeObjectURL");
    });

    it("lets a viewer download the ticked rows' files, without offering a retry", async () => {
      const exported: unknown[] = [];
      URL.createObjectURL = vi.fn(() => "blob:export");
      URL.revokeObjectURL = vi.fn();
      const saved: string[] = [];
      vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
        saved.push(this.download);
      });

      const { user } = renderApp("/exchanges", {
        as: { permissions: ["exchanges.view"] },
        handlers: [
          exchanges({ total: 3 }).handler,
          ...filterOptions,
          exportCheck(),
          http.post(apiPath("/xchanges/export"), async ({ request }) => {
            exported.push(await request.json());
            // Bytes, not a Blob: jsdom's Blob can't be streamed into a response.
            return new HttpResponse(new Uint8Array([0x50, 0x4b]), {
              headers: {
                "Content-Type": "application/zip",
                "Content-Disposition": "attachment; filename=exchanges-2026-10-01-101500.zip",
              },
            });
          }),
        ],
      });

      await screen.findAllByRole("checkbox", { name: /^Select (?!all\b)/ });
      await user.click(rowCheckboxes()[0]);
      await user.click(rowCheckboxes()[2]);

      expect(screen.queryByRole("button", { name: "Retry selected…" })).not.toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: "Export files" }));

      await vi.waitFor(() => expect(saved).toEqual(["exchanges-2026-10-01-101500.zip"]));
      expect(exported).toEqual([{ ids: [exchange(0).id, exchange(2).id] }]);
    });

    it("over an unfiltered list, select all matching still sends a filter that matches everything", async () => {
      const exported: unknown[] = [];
      URL.createObjectURL = vi.fn(() => "blob:export");
      URL.revokeObjectURL = vi.fn();
      vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

      const { user } = renderApp("/exchanges", {
        handlers: [
          exchanges({ total: 30 }).handler,
          ...filterOptions,
          exportCheck(0, 30),
          http.post(apiPath("/xchanges/export"), async ({ request }) => {
            exported.push(await request.json());
            return new HttpResponse(new Uint8Array([0x50, 0x4b]), { headers: { "Content-Type": "application/zip" } });
          }),
        ],
      });

      await user.click(await screen.findByRole("checkbox", { name: "Select all on this page" }));
      await user.click(screen.getByRole("button", { name: "Select all 30 matching this filter" }));
      await user.click(screen.getByRole("button", { name: "Export files" }));

      // An empty filter would read as no filter, and select nothing.
      await vi.waitFor(() => expect(exported).toEqual([{ filter: "filter=LatestOnly%3A1%3Afalse", excludeIds: [] }]));
    });

    it("says first when some exchanges are past the files storage keeps, and exports the rest on confirm", async () => {
      const exported: unknown[] = [];
      URL.createObjectURL = vi.fn(() => "blob:export");
      URL.revokeObjectURL = vi.fn();
      vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

      const { user } = renderApp("/exchanges", {
        handlers: [
          exchanges({ total: 3 }).handler,
          ...filterOptions,
          exportCheck(1, 3),
          http.post(apiPath("/xchanges/export"), async ({ request }) => {
            exported.push(await request.json());
            return new HttpResponse(new Uint8Array([0x50, 0x4b]), { headers: { "Content-Type": "application/zip" } });
          }),
        ],
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
        handlers: [exchanges({ total: 600 }).handler, ...filterOptions],
      });

      await user.click(await screen.findByRole("checkbox", { name: "Select all on this page" }));
      await user.click(screen.getByRole("button", { name: "Select all 600 matching this filter" }));

      const button = screen.getByRole("button", { name: "Export files" });
      expect(button).toBeDisabled();
      expect(button).toHaveAttribute("title", expect.stringContaining("at most 500 exchanges"));
    });
  });
});
