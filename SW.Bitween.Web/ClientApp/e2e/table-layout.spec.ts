import { test, expect, type Page } from "@playwright/test";
import { signInAsAdmin } from "./helpers";

/**
 * The layout contracts the tables have to keep, whatever is in them.
 *
 * These are all data-shape problems: a customer with 60-character subscription
 * names, a 4KB minified payload and 45 subscriptions on one information type.
 * None of that exists in a dev database, so every test here rewrites the API
 * response on the way past rather than seeding rows — nothing is written, and
 * the assertions don't drift with whatever the local data happens to be.
 */

const LONG_NAMES = [
  "Customer Aggregation Trace Out Manifest - Sodexi Cassini EDI",
  "Agent Tracing - Colissimo EDI Daily Reconciliation",
  // No spaces anywhere: there is nothing for the browser to break on when it
  // works out the column's intrinsic minimum, which is what `wrap-anywhere`
  // exists to handle. With `break-words` this one alone widened Aggregations by
  // nearly 900px.
  "Customer_Aggregation_Scan_Out_CUSTOMS_Chronopost_Returns",
];

/** Replaces every `name` the API returns with a production-length one. */
async function withLongNames(page: Page) {
  let n = 0;
  await page.route("**/api/**", async (route) => {
    const res = await route.fetch();
    if (!(res.headers()["content-type"] ?? "").includes("json")) return route.fulfill({ response: res });
    let body: unknown;
    try {
      body = await res.json();
    } catch {
      return route.fulfill({ response: res });
    }
    const walk = (v: unknown): void => {
      if (Array.isArray(v)) return v.forEach(walk);
      if (v && typeof v === "object")
        for (const k of Object.keys(v as Record<string, unknown>)) {
          const o = v as Record<string, unknown>;
          if (k === "name" && typeof o[k] === "string" && o[k]) o[k] = LONG_NAMES[n++ % LONG_NAMES.length];
          else walk(o[k]);
        }
    };
    walk(body);
    await route.fulfill({ response: res, json: body });
  });
}

/** Every table on the page that is wider than the box it scrolls inside. */
const overflowing = (page: Page) =>
  page.evaluate(() =>
    [...document.querySelectorAll("table")]
      .map((t) => {
        const box = t.closest("div[class*=overflow-x-auto]") ?? t.parentElement!;
        return { head: [...t.querySelectorAll("th")].map((h) => h.textContent!.trim()).join("/"),
                 over: t.scrollWidth - box.clientWidth };
      })
      .filter((r) => r.over > 1),
  );

/** Leaf elements whose text is cut off — an ellipsis the reader can't get past. */
const clipped = (page: Page) =>
  page.evaluate(() =>
    [...document.querySelectorAll("td")]
      .flatMap((td) => [td, ...td.querySelectorAll("*")])
      .filter((e) => e.children.length === 0 && e.scrollWidth > e.clientWidth + 1)
      .map((e) => e.textContent!.trim().slice(0, 40)),
  );

test.beforeEach(({ page }) => signInAsAdmin(page));

const LIST_PAGES = [
  "subscriptions", "aggregations", "scheduled-jobs", "bus-gateways", "api-gateways",
  "partners", "information-types", "work-groups", "retry-policies", "exchanges", "queue-health",
];

test("no list table is wider than the card it sits in", async ({ page }) => {
  // The densest pages carry twelve columns; the padding and the wrap floors are
  // tuned so they still land inside a 1440px window with nothing off the right.
  await page.setViewportSize({ width: 1440, height: 900 });
  for (const slug of LIST_PAGES) {
    await page.goto(slug);
    await page.waitForTimeout(1200);
    expect(await overflowing(page), `${slug} has a table wider than its card`).toEqual([]);
  }
});

test("long names wrap rather than collapsing into a row of ellipses", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await withLongNames(page);

  for (const slug of ["subscriptions", "aggregations", "scheduled-jobs"]) {
    await page.goto(slug);
    await page.waitForTimeout(1500);
    // Exceptions and joined key lists are allowed to clip — they carry a title.
    // A name never is.
    const cut = (await clipped(page)).filter((t) => LONG_NAMES.some((n) => n.startsWith(t.replace(/…$/, ""))));
    expect(cut, `${slug} clipped a name`).toEqual([]);
    expect(await overflowing(page), `${slug} widened past its card`).toEqual([]);
  }
});

test("a long payload doesn't stretch the exchanges table", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const payload = JSON.stringify({
    Shipment: {
      Uid: "STF.365673", Number: "3304169024", Account: "VFS-FR-UKEMB-PAR",
      Pieces: Array.from({ length: 12 }, (_, i) => ({ Barcode: `STF36567300${i}`, WeightKg: 2.4 + i })),
    },
  });
  await page.route("**/bitweendocs**", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ key: "k", data: payload }) }));

  await page.goto("exchanges");
  await page.waitForTimeout(1200);
  const before = await page.evaluate(() => document.querySelector("table")!.scrollWidth);

  // The expander, not the row: the row's cells carry links of their own.
  await page.locator("tbody tr").first().locator("td").last().click();
  await expect(page.getByRole("button", { name: "Download this document" })).toBeVisible();

  // Laid out over lines, and the one line of minified JSON never sets the width.
  await expect(page.getByText('"Shipment": {')).toBeVisible();
  expect(await overflowing(page)).toEqual([]);
  expect(await page.evaluate(() => document.querySelector("table")!.scrollWidth)).toBe(before);

  // Raw shows it exactly as it arrived, and still can't stretch anything.
  await page.getByRole("button", { name: "Raw" }).click();
  await expect(page.getByText('{"Shipment":{"Uid":"STF.365673"', { exact: false })).toBeVisible();
  expect(await overflowing(page)).toEqual([]);
  expect(await page.evaluate(() => document.querySelector("table")!.scrollWidth)).toBe(before);
});

test("a long panel list keeps its Type column inside the card", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.route("**/subscriptions?filter=DocumentId*", async (route) => {
    const res = await route.fetch();
    let body: any;
    try { body = await res.json(); } catch { return route.fulfill({ response: res }); }
    const one = body.result?.[0];
    if (one)
      body.result = Array.from({ length: 45 }, (_, i) => ({
        ...one, id: 900000 + i, name: `${LONG_NAMES[i % LONG_NAMES.length]} ${i}`,
      }));
    await route.fulfill({ response: res, json: body });
  });

  await page.goto("information-types");
  // The route above can only multiply a subscription that exists, so open a type something
  // uses — the first row is just whichever type was made last.
  const usedBy = page
    .locator('a[href*="/subscriptions/"]')
    .or(page.getByRole("button", { name: /^Show all \d+ subscriptions$/ }));
  await page.locator("tbody tr").filter({ has: usedBy }).first().locator("td").nth(1).click();
  await expect(page).toHaveURL(/\/information-types\/\d+$/);

  // Long names in a ~360px panel used to push Type off the right-hand edge. Only a real browser
  // lays the panel out; paging and filtering this list are in UsedByPanel.test.tsx.
  await expect(page.getByRole("columnheader", { name: "Type" }).first()).toBeVisible();
  expect(await overflowing(page)).toEqual([]);
});
