import { test, expect, type Page } from "@playwright/test";
import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { AdminApi, stamp } from "./api";
import { pickOption, signInAsAdmin } from "./helpers";
import { SEED } from "./seed-data";

/**
 * Running a schedule now instead of waiting for it: Receive now on a scheduled job, and Roll up
 * now on an aggregation. Each is followed to what it should cause — a document fetched from a
 * source and delivered, a roll-up of the source's exchanges delivered — at endpoints this test
 * runs, and to the run it records on the subscription's page.
 */

/** A local endpoint that answers every request with `reply` and records what it was sent. */
async function startEndpoint(reply = "ok") {
  const received: { method: string; body: string }[] = [];
  const server = createServer((req, res) => {
    let body = "";
    req.on("data", (chunk) => (body += chunk));
    req.on("end", () => {
      received.push({ method: req.method ?? "", body });
      res.setHeader("Content-Type", "application/json");
      res.end(reply);
    });
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}/`;
  return { url, received, close: () => new Promise<void>((resolve) => server.close(() => resolve())) };
}

/** A schedule twelve hours from now, so it never fires while the test is running. */
const farSchedule = () => ({
  recurrence: "Daily",
  days: 0,
  hours: (new Date().getUTCHours() + 12) % 24,
  minutes: 0,
  backwards: false,
});

/** Presses a row's run-now button on a list page and confirms. */
async function runNow(page: Page, list: string, name: string, button: string, dialog: string) {
  await page.goto(list);
  const row = page.getByRole("row").filter({ hasText: name });
  await row.getByRole("button", { name: button }).click();
  await page.getByRole("dialog", { name: dialog }).getByRole("button", { name: button }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
}

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("Receive now on the scheduled jobs list fetches the job's source once and delivers what it found, and the job's page records the run", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const s = stamp();
  const document = JSON.stringify({ shipment: `fetched-${s}` });
  const source = await startEndpoint(document);
  const sink = await startEndpoint();
  const name = `Playwright Fetch Job ${s}`;
  let id: number | null = null;

  try {
    id = await api.post<number>("/subscriptions", {
      name,
      documentId: await api.idOf("documents", SEED.informationType),
      type: "Receiving",
      receiverId: "NativeHttpReceiver",
      receiverProperties: [{ key: "Url", value: source.url }],
      handlerId: "NativeHttpHandler",
      handlerProperties: [{ key: "Url", value: sink.url }],
      validatorProperties: [],
      mapperProperties: [],
      documentFilter: [],
      schedules: [farSchedule()],
      inactive: false,
    });

    await runNow(page, "scheduled-jobs", name, "Receive now", "Receive now?");

    await expect.poll(() => source.received.length, { message: "the source was never fetched", timeout: 60_000 }).toBe(1);
    await expect.poll(() => sink.received.length, { message: "what was fetched never arrived", timeout: 60_000 }).toBe(1);
    expect(JSON.parse(sink.received[0].body)).toEqual(JSON.parse(document));

    await expect(async () => {
      await page.goto(`subscriptions/${id}`);
      await expect(page.getByText("Received 1 item")).toBeVisible({ timeout: 2000 });
    }).toPass({ timeout: 30_000 });
  } finally {
    await source.close();
    await sink.close();
    if (id !== null) await api.delete(`/subscriptions/${id}`);
  }
});

test("an aggregation made on its page and rolled up now delivers one roll-up naming each successful exchange of its source, and a second roll-up with nothing new delivers nothing", async ({
  page,
  request,
}) => {
  test.setTimeout(150_000);
  const api = await AdminApi.signIn(request);
  const s = stamp();
  const partnerSystem = await startEndpoint();
  const manifestStore = await startEndpoint();
  const sourceName = `Playwright Agg Source ${s}`;
  const name = `Playwright Daily Manifest ${s}`;
  let sourceId: number | null = null;
  let aggregationId: number | null = null;

  try {
    sourceId = await api.post<number>("/subscriptions", {
      name: sourceName,
      documentId: await api.idOf("documents", SEED.informationType),
      type: "ApiCall",
      partnerId: await api.idOf("partners", SEED.partner),
      handlerId: "NativeHttpHandler",
      handlerProperties: [{ key: "Url", value: partnerSystem.url }],
      receiverProperties: [],
      validatorProperties: [],
      mapperProperties: [],
      documentFilter: [],
      inactive: false,
    });
    await api.sendExchange(sourceId, { shipment: `one-${s}` });
    await api.sendExchange(sourceId, { shipment: `two-${s}` });
    let collected: string[] = [];
    await expect
      .poll(async () => {
        collected = (await api.exchangesOf(sourceId!)).filter((x) => x.status === true).map((x) => x.id);
        return collected.length;
      }, { timeout: 60_000 })
      .toBe(2);

    await page.goto("aggregations/new");
    await page.fill("#na-name", name);
    await pickOption(page, "Subscription to roll up", sourceName);
    await page.getByRole("button", { name: /^Schedule/ }).click();
    await page.getByRole("button", { name: "Add schedule" }).click();
    const schedule = farSchedule();
    await page.locator("#sc-rec").selectOption("Daily");
    await page.fill("#sc-h", String(schedule.hours));
    await page.fill("#sc-m", "0");
    await page.getByRole("dialog", { name: "Add schedule" }).getByRole("button", { name: "Add schedule" }).click();
    await page.getByRole("button", { name: /^Delivery/ }).click();
    await pickOption(page, "handler adapter", "NativeHttpHandler");
    await page.locator("#prop-Url").fill(manifestStore.url);
    await page.getByRole("checkbox", { name: /^Enable immediately/ }).check();
    await page.getByRole("button", { name: "Create aggregation" }).click();
    await expect(page).toHaveURL(/\/subscriptions\/\d+$/);
    aggregationId = Number(new URL(page.url()).pathname.split("/").pop());

    await runNow(page, "aggregations", name, "Roll up now", "Roll up now?");
    await expect
      .poll(() => manifestStore.received.length, { message: "the roll-up was never delivered", timeout: 60_000 })
      .toBe(1);
    for (const id of collected) expect(manifestStore.received[0].body).toContain(id);

    await expect(async () => {
      await page.goto(`subscriptions/${aggregationId}`);
      await expect(page.locator("td").getByText("Rolled up", { exact: true })).toBeVisible({ timeout: 2000 });
    }).toPass({ timeout: 30_000 });

    // Everything outstanding was collected, so the next run has nothing to do.
    await runNow(page, "aggregations", name, "Roll up now", "Roll up now?");
    await expect(async () => {
      await page.goto(`subscriptions/${aggregationId}`);
      await expect(page.locator("td").getByText("Nothing to roll up", { exact: true })).toBeVisible({ timeout: 2000 });
    }).toPass({ timeout: 30_000 });
    expect(manifestStore.received).toHaveLength(1);
  } finally {
    await partnerSystem.close();
    await manifestStore.close();
    if (aggregationId !== null) await api.delete(`/subscriptions/${aggregationId}`);
    if (sourceId !== null) await api.delete(`/subscriptions/${sourceId}`);
  }
});
