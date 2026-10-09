import { test, expect, type Page } from "@playwright/test";
import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { inflateRawSync } from "node:zlib";
import { AdminApi, stamp } from "./api";
import { signInAsAdmin } from "./helpers";
import { SEED } from "./seed-data";

/**
 * What the Exchanges page does to exchanges — retry them in bulk and one at a time, with and
 * without re-resolving the subscription's settings, and export their files — each followed to
 * its effect: deliveries arriving at an endpoint the test runs, the API's record of the chain,
 * and the bytes of the download.
 *
 * Every failure a test here creates is retried into a success before it ends. The exchanges spec
 * retries whichever failures are newest, and a failure left behind by a subscription deleted here
 * would be one it can't retry.
 */

/** A local endpoint that records the bodies sent to it. */
async function startEndpoint() {
  const received: string[] = [];
  const server = createServer((req, res) => {
    let body = "";
    req.on("data", (chunk) => (body += chunk));
    req.on("end", () => {
      received.push(body);
      res.end("ok");
    });
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}/`;
  return { url, received, close: () => new Promise<void>((resolve) => server.close(() => resolve())) };
}

/** A live subscription delivering over HTTP to `url`. */
async function subscriptionTo(api: AdminApi, name: string, url: string) {
  return api.post<number>("/subscriptions", {
    name,
    documentId: await api.idOf("documents", SEED.informationType),
    type: "ApiCall",
    partnerId: await api.idOf("partners", SEED.partner),
    handlerId: "NativeHttpHandler",
    handlerProperties: [{ key: "Url", value: url }],
    receiverProperties: [],
    validatorProperties: [],
    mapperProperties: [],
    documentFilter: [],
    retryPolicyId: null,
    inactive: false,
  });
}

/** The subscription's exchanges by outcome, counting only the newest attempt of each chain. */
async function outcomes(api: AdminApi, subscriptionId: number) {
  const filter = ["LatestOnly:1:true", `SubscriptionId:1:${subscriptionId}`]
    .map((f) => `filter=${encodeURIComponent(f)}`)
    .join("&");
  const { result } = await api.get<{ result: { id: string; status: boolean | null }[] }>(
    `/xchanges?${filter}&page=0&size=200`,
  );
  return {
    failed: result.filter((x) => x.status === false).map((x) => x.id),
    succeeded: result.filter((x) => x.status === true).map((x) => x.id),
    running: result.filter((x) => x.status === null).length,
  };
}

/** Points the subscription's delivery somewhere else, on its page. */
async function changeDeliveryUrl(page: Page, subscriptionId: number, url: string) {
  await page.goto(`subscriptions/${subscriptionId}?stage=delivery`);
  await page.locator("#prop-Url").fill(url);
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);
}

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("selecting all matching failures across pages and retrying them with re-resolved settings runs every one again against the subscription's new URL", async ({
  page,
  request,
}) => {
  test.setTimeout(180_000);
  const api = await AdminApi.signIn(request);
  const endpoint = await startEndpoint();
  const id = await subscriptionTo(api, `Playwright Bulk Retry ${stamp()}`, SEED.failingUrl);
  // One more than a page holds, so "all matching" means more than what is on screen.
  const count = 26;

  try {
    for (let i = 0; i < count; i++) await api.sendExchange(id, { shipment: `bulk-${i}` });
    await expect
      .poll(async () => (await outcomes(api, id)).failed.length, {
        message: `the ${count} exchanges never all failed`,
        timeout: 90_000,
      })
      .toBe(count);

    // The fix: deliver somewhere that answers. Only a retry that re-resolves picks it up.
    await changeDeliveryUrl(page, id, endpoint.url);

    await page.goto(`exchanges?subscriptionId=${id}&status=failed&latest=1`);
    await page.getByLabel("Refresh interval").selectOption("0");
    await expect(page.getByRole("checkbox", { name: /^Select [0-9a-f]{32}$/ })).toHaveCount(25);
    await page.getByRole("checkbox", { name: "Select all on this page" }).check();
    await expect(page.getByText("Only the 25 rows on this page.")).toBeVisible();
    await page.getByRole("button", { name: `Select all ${count} matching this filter` }).click();
    await expect(page.getByText("— everything this filter matches")).toBeVisible();

    await page.getByRole("button", { name: "Retry selected…" }).click();
    const dialog = page.getByRole("dialog", { name: `Retry ${count} exchanges?` });
    await expect(dialog).toContainText(`${count} exchanges will run again`);
    await dialog.getByRole("checkbox", { name: /^Re-resolve adapter properties/ }).check();
    await dialog.getByRole("button", { name: "Retry" }).click();
    await expect(page.getByText(`${count} retries started.`)).toBeVisible({ timeout: 30_000 });

    // Every one of them, not just the page that was showing, delivered to the new URL.
    await expect
      .poll(() => endpoint.received.length, { message: "not every retry was delivered", timeout: 90_000 })
      .toBe(count);
    await expect
      .poll(async () => {
        const o = await outcomes(api, id);
        return { failed: o.failed.length, succeeded: o.succeeded.length };
      }, { timeout: 60_000 })
      .toEqual({ failed: 0, succeeded: count });
  } finally {
    await endpoint.close();
  }
  await api.delete(`/subscriptions/${id}`);
});

test("retrying a failure without re-resolving reuses the settings it first ran with and fails again; retrying with them re-resolved delivers to the new URL", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const endpoint = await startEndpoint();
  const id = await subscriptionTo(api, `Playwright Reset Retry ${stamp()}`, SEED.failingUrl);

  /** Opens one exchange's drawer and retries it, re-resolving or not. */
  const retry = async (exchangeId: string, reResolve: boolean) => {
    await page.goto(`exchanges?ids=${exchangeId}`);
    await page.getByLabel("Refresh interval").selectOption("0");
    await page.locator(`tr:has(input[aria-label="Select ${exchangeId}"])`).locator("td").last().click();
    await page.getByRole("button", { name: "Retry…" }).click();
    const dialog = page.getByRole("dialog", { name: "Retry this exchange?" });
    const box = dialog.getByRole("checkbox", { name: /^Re-resolve adapter properties/ });
    await expect(box).not.toBeChecked();
    if (reResolve) await box.check();
    await dialog.getByRole("button", { name: "Retry" }).click();
    await expect(page.getByText(/Retry started/)).toBeVisible({ timeout: 15000 });
  };

  try {
    await api.sendExchange(id, { shipment: "reset-me" });
    await expect.poll(async () => (await outcomes(api, id)).failed.length, { timeout: 60_000 }).toBe(1);
    const [original] = (await outcomes(api, id)).failed;

    await changeDeliveryUrl(page, id, endpoint.url);

    // As it first ran: the old, unreachable URL travels with the exchange.
    await retry(original, false);
    await expect
      .poll(async () => (await outcomes(api, id)).failed, { message: "the plain retry never finished", timeout: 60_000 })
      .not.toEqual([original]);
    const [again] = (await outcomes(api, id)).failed;
    expect(again).not.toBe(original);
    expect(endpoint.received).toEqual([]);

    // Re-resolved: the subscription's settings as they are now.
    await retry(again, true);
    await expect
      .poll(async () => {
        const o = await outcomes(api, id);
        return { failed: o.failed.length, succeeded: o.succeeded.length };
      }, { message: "the re-resolved retry never succeeded", timeout: 60_000 })
      .toEqual({ failed: 0, succeeded: 1 });
    expect(endpoint.received).toHaveLength(1);
    expect(endpoint.received[0]).toContain("reset-me");
  } finally {
    await endpoint.close();
  }
  await api.delete(`/subscriptions/${id}`);
});

test("Export files on two selected exchanges downloads one zip holding a folder for each, with the document each one carried", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const endpoint = await startEndpoint();
  const s = stamp();
  const id = await subscriptionTo(api, `Playwright Export ${s}`, endpoint.url);

  try {
    await api.sendExchange(id, { shipment: `export-a-${s}` });
    await api.sendExchange(id, { shipment: `export-b-${s}` });
    await expect.poll(async () => (await outcomes(api, id)).succeeded.length, { timeout: 60_000 }).toBe(2);
    const ids = (await outcomes(api, id)).succeeded;

    await page.goto(`exchanges?ids=${ids.join(",")}`);
    await page.getByLabel("Refresh interval").selectOption("0");
    for (const x of ids) await page.getByRole("checkbox", { name: `Select ${x}`, exact: true }).check();
    await expect(page.getByText("2 selected")).toBeVisible();

    const download = page.waitForEvent("download");
    await page.getByRole("button", { name: "Export files" }).click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(/\.zip$/);
    const zip = await readStream(await file.createReadStream());

    // A folder per exchange, named by its id, holding the document that exchange carried.
    const entries = zipEntries(zip);
    for (const x of ids) {
      const mine = entries.filter((e) => e.name.startsWith(`${x}/`));
      expect(mine.length, `no files for ${x} in ${entries.map((e) => e.name).join(", ")}`).toBeGreaterThan(0);
      expect(mine.map((e) => e.content).join("\n")).toMatch(new RegExp(`export-[ab]-${s}`));
    }
  } finally {
    await endpoint.close();
  }
  await api.delete(`/subscriptions/${id}`);
});

/** The entries of a zip, read through its central directory; stored or deflated. */
function zipEntries(zip: Buffer): { name: string; content: string }[] {
  const entries: { name: string; content: string }[] = [];
  const end = zip.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  let at = zip.readUInt32LE(end + 16);
  for (let i = 0; i < zip.readUInt16LE(end + 10); i++) {
    const method = zip.readUInt16LE(at + 10);
    const size = zip.readUInt32LE(at + 20);
    const nameLength = zip.readUInt16LE(at + 28);
    const extraLength = zip.readUInt16LE(at + 30);
    const commentLength = zip.readUInt16LE(at + 32);
    const local = zip.readUInt32LE(at + 42);
    const name = zip.subarray(at + 46, at + 46 + nameLength).toString("utf8");
    const dataAt = local + 30 + zip.readUInt16LE(local + 26) + zip.readUInt16LE(local + 28);
    const data = zip.subarray(dataAt, dataAt + size);
    entries.push({ name, content: (method === 8 ? inflateRawSync(data) : data).toString("utf8") });
    at += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

async function readStream(stream: NodeJS.ReadableStream): Promise<Buffer> {
  const chunks: Buffer[] = [];
  for await (const chunk of stream) chunks.push(Buffer.from(chunk as Buffer));
  return Buffer.concat(chunks);
}
