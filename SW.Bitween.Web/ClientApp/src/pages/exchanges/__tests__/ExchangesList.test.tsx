import { cleanup, screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
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

/** The pickers above the list. Nothing in them matters here. */
const filterOptions = ["/partners", "/subscriptions", "/documents"].map((p) =>
  http.get(apiPath(p), () => HttpResponse.json({ result: [], totalCount: 0 })),
);

const rowCheckboxes = () => screen.getAllByRole("checkbox", { name: /^Select (?!all\b)/ });

describe("the exchanges list", () => {
  /**
   * A selection has to be able to mean "everything this filter matches", or a 200-exchange
   * recovery is 8 pages of ticking boxes. Stops at the confirm — what it says is the point, and
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
      // What it asked about is the filter itself, minus the row unticked — not 24 ids off one page.
      expect(previews).toEqual([
        { filter: "filter=StatusFilter%3A1%3A3", excludeIds: [exchange(0).id], reason: "Bulk retry", reset: false },
      ]);

      // Nothing is retried from here: the retry endpoint has no handler, so asking it would fail.
      await user.click(within(dialog).getByRole("button", { name: "Cancel" }));
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    it.each([
      {
        answer: "past the cap, it refuses",
        plan: { selected: 60, willRetry: 0, limit: 50, overLimit: true },
        says: /more than the 50 a single retry will carry out/,
      },
      {
        answer: "with nothing retryable, it says so",
        plan: { selected: 60, willRetry: 0, limit: 500, overLimit: false },
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
  });

  /**
   * A chain is one piece of work however many attempts it took, so the list needs to be able to
   * show the newest attempt of each — otherwise a chain retried nine times fills nine rows, none
   * of which is the current state of anything.
   *
   * Which rows that leaves is the server's: Exchange_search_can_return_only_the_newest_attempt_of_each_chain
   * in RetryChainTests. What's left here is the toggle, and what it asks for.
   */
  it("can show only the newest attempt of each chain", async () => {
    const latest = (q: URLSearchParams) => q.getAll("filter").includes("LatestOnly:1:true");
    const list = exchanges({ total: (q) => (latest(q) ? 30 : 40) });
    const { user, router } = renderApp("/exchanges?status=failed", { handlers: [list.handler, ...filterOptions] });

    expect(await screen.findByText(/Showing 1–25 of 40/)).toBeVisible();
    const pill = screen.getByRole("button", { name: "Latest attempt only" });
    expect(pill).toHaveAttribute("aria-pressed", "false");
    expect(latest(list.asked.at(-1)!)).toBe(false);

    await user.click(pill);
    expect(router.state.location.search).toMatch(/latest=1/);
    expect(pill).toHaveAttribute("aria-pressed", "true");
    expect(await screen.findByText(/Showing 1–25 of 30/)).toBeVisible();
    // Narrowing whatever the status picked, not replacing it.
    expect(list.asked.at(-1)!.getAll("filter")).toEqual(["StatusFilter:1:3", "LatestOnly:1:true"]);

    // And it survives a reload, since it lives in the URL like every other filter.
    const { pathname, search } = router.state.location;
    cleanup();
    list.asked.length = 0;
    renderApp(pathname + search, { handlers: [list.handler, ...filterOptions] });
    expect(await screen.findByRole("button", { name: "Latest attempt only" })).toHaveAttribute("aria-pressed", "true");
    expect(await screen.findByText(/Showing 1–25 of 30/)).toBeVisible();
    expect(latest(list.asked[0])).toBe(true);
  });

  it("opens promoted properties in a panel, not just a tooltip", async () => {
    // Ten properties, one value too long for a chip, and a null — the value shape that used to
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

    // Every property, in full — including the one too long to have fitted a chip.
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

    expect(await screen.findByText(/Showing 1–25 of 40/)).toBeVisible();
    await user.click(screen.getAllByRole("button", { name: "Next" })[0]);

    expect(await screen.findByText(/Showing 26–40 of 40/)).toBeVisible();
    expect(list.asked.at(-1)?.get("page")).toBe("1");
    expect(screen.getAllByText("True").length).toBeGreaterThan(0);
    expect(screen.queryByText("Unexpected Application Error")).not.toBeInTheDocument();
  });
});
