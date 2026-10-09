import { test, expect } from "@playwright/test";
import { createServer, type IncomingMessage } from "node:http";
import type { AddressInfo } from "node:net";
import { AdminApi, stamp } from "./api";
import { pickOption, signInAsAdmin } from "./helpers";
import { SEED } from "./seed-data";

/**
 * A notifier configured on its page, followed all the way to a notification arriving: the test
 * listens on a local port, the notifier delivers there with the built-in HTTP handler, and an
 * exchange that really fails is what sets it off.
 */

/** A local HTTP endpoint that records every request made to it. */
async function startInbox() {
  const received: { method: string; url: string; body: string }[] = [];
  const server = createServer((req: IncomingMessage, res) => {
    let body = "";
    req.on("data", (chunk) => (body += chunk));
    req.on("end", () => {
      received.push({ method: req.method ?? "", url: req.url ?? "", body });
      res.statusCode = 200;
      res.end("ok");
    });
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}/notify`;
  return { url, received, close: () => new Promise<void>((resolve) => server.close(() => resolve())) };
}

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("a notifier set on its page to post failures of one subscription to an HTTP endpoint delivers when an exchange to it fails, and lists the delivery", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const inbox = await startInbox();
  const s = stamp();

  // The seeded subscription whose every delivery fails: nothing listens where it sends. Used
  // rather than one of the test's own so the failure it adds stays retryable for the exchanges
  // spec — a failure whose subscription was deleted can't be retried.
  const subscriptionName = SEED.failingSubscription;
  const subscriptionId = await api.idOf("subscriptions", subscriptionName);

  try {
    const notifierName = `Playwright Notifier ${s}`;
    await page.goto("notifiers");
    await page.getByRole("button", { name: "New notifier" }).click();
    await page.fill("#nn-name", notifierName);
    await page.getByRole("button", { name: "Create notifier" }).click();
    await expect(page).toHaveURL(/\/notifiers\/\d+$/);
    const notifierId = Number(new URL(page.url()).pathname.split("/").pop());

    await page.getByRole("checkbox", { name: /^An exchange fails/ }).check();
    await pickOption(page, "Deliver via", "NativeHttpHandler");
    await page.fill("#nf-prop-Url", inbox.url);
    await page.getByRole("searchbox", { name: "Search subscriptions" }).fill(subscriptionName);
    await page.getByRole("checkbox", { name: new RegExp(subscriptionName) }).check();
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

    // Saved as set.
    await page.reload();
    await expect(page.getByRole("checkbox", { name: /^An exchange fails/ })).toBeChecked();
    await expect(page.getByRole("checkbox", { name: /^An exchange succeeds/ })).not.toBeChecked();
    await expect(page.locator("#nf-prop-Url")).toHaveValue(inbox.url);
    await page.getByRole("searchbox", { name: "Search subscriptions" }).fill(subscriptionName);
    await expect(page.getByRole("checkbox", { name: new RegExp(subscriptionName) })).toBeChecked();

    // An exchange to the watched subscription fails, and the notification arrives.
    await api.sendExchange(subscriptionId, { shipment: `notify-${s}` });
    await expect
      .poll(async () => (await api.exchangesOf(subscriptionId))[0]?.status ?? null, {
        message: "the exchange never finished",
        timeout: 60_000,
      })
      .toBe(false);
    const [{ id: exchangeId }] = await api.exchangesOf(subscriptionId);
    await expect
      .poll(() => inbox.received.length, { message: "no notification reached the endpoint", timeout: 60_000 })
      .toBeGreaterThan(0);
    expect(inbox.received[0].method).toBe("POST");

    // And the notifier's page records it against the exchange.
    await expect(async () => {
      await page.reload();
      const row = page.getByRole("row").filter({ hasText: exchangeId });
      await expect(row).toContainText("Sent", { timeout: 2000 });
    }).toPass({ timeout: 30_000 });

    await api.delete(`/notifiers/${notifierId}`);
  } finally {
    await inbox.close();
  }
});

test("a notifier switched off on its page sends nothing when a watched subscription fails", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const inbox = await startInbox();
  const s = stamp();

  // The seeded subscription whose every delivery fails: nothing listens where it sends. Used
  // rather than one of the test's own so the failure it adds stays retryable for the exchanges
  // spec — a failure whose subscription was deleted can't be retried.
  const subscriptionName = SEED.failingSubscription;
  const subscriptionId = await api.idOf("subscriptions", subscriptionName);

  try {
    await page.goto("notifiers");
    await page.getByRole("button", { name: "New notifier" }).click();
    await page.fill("#nn-name", `Playwright Quiet Notifier ${s}`);
    await page.getByRole("button", { name: "Create notifier" }).click();
    await expect(page).toHaveURL(/\/notifiers\/\d+$/);
    const notifierId = Number(new URL(page.url()).pathname.split("/").pop());

    await page.getByRole("checkbox", { name: /^An exchange fails/ }).check();
    await pickOption(page, "Deliver via", "NativeHttpHandler");
    await page.fill("#nf-prop-Url", inbox.url);
    await page.getByRole("searchbox", { name: "Search subscriptions" }).fill(subscriptionName);
    await page.getByRole("checkbox", { name: new RegExp(subscriptionName) }).check();
    // The badge beside the name switches it off without losing its setup.
    await page.getByRole("button", { name: "Active", exact: true }).click();
    await expect(page.getByRole("button", { name: "Off", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);
    await page.reload();
    await expect(page.getByRole("button", { name: "Off", exact: true })).toBeVisible();

    await api.sendExchange(subscriptionId, { shipment: `quiet-${s}` });
    await expect
      .poll(async () => (await api.exchangesOf(subscriptionId))[0]?.status ?? null, { timeout: 60_000 })
      .toBe(false);
    // Long enough for a notification that was going to be sent to have been.
    await page.waitForTimeout(5000);
    expect(inbox.received).toEqual([]);

    await api.delete(`/notifiers/${notifierId}`);
  } finally {
    await inbox.close();
  }
});
