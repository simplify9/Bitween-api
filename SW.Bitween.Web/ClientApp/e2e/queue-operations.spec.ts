import { test, expect, type APIRequestContext, type Page } from "@playwright/test";
import { AdminApi, stamp } from "./api";
import { E2E_RABBITMQ_MANAGEMENT as MQ } from "./env";
import { signInAsAdmin } from "./helpers";

/**
 * The two Queue health actions that change the broker: sending dead letters back, and deleting
 * queues nothing reads. Their inputs — a message in a bad queue, a queue no consumer declares —
 * only arise in production from a failure or a leftover, so the test puts them there through
 * RabbitMQ's management API, and reads the broker's answer back the same way.
 */

const mq = (request: APIRequestContext) => {
  const headers = {
    Authorization: `Basic ${Buffer.from(`${MQ.user}:${MQ.password}`).toString("base64")}`,
    "Content-Type": "application/json",
  };
  const queue = (name: string) => `${MQ.url}/api/queues/%2F/${encodeURIComponent(name)}`;
  return {
    reachable: async () => (await request.get(`${MQ.url}/api/overview`, { headers }).catch(() => null))?.ok() ?? false,
    declare: async (name: string) =>
      expect((await request.put(queue(name), { headers, data: { durable: true } })).ok()).toBe(true),
    publish: async (name: string, payload: string) => {
      const res = await request.post(`${MQ.url}/api/exchanges/%2F/amq.default/publish`, {
        headers,
        data: { properties: {}, routing_key: name, payload, payload_encoding: "string" },
      });
      expect((await res.json()).routed, `nothing routed to ${name}`).toBe(true);
    },
    /** Messages in the queue, or null once it is gone. */
    messages: async (name: string): Promise<number | null> => {
      const res = await request.get(queue(name), { headers });
      if (res.status() === 404) return null;
      // From the broker's statistics, which lag by a few seconds: callers poll.
      const q = (await res.json()) as { messages?: number; messages_ready?: number };
      return q.messages ?? q.messages_ready ?? 0;
    },
    remove: (name: string) => request.delete(queue(name), { headers }),
  };
};

test.beforeEach(async ({ page, request }) => {
  test.skip(!(await mq(request).reachable()), `RabbitMQ's management API is not reachable at ${MQ.url}`);
  await signInAsAdmin(page);
});

/** This instance's queue prefix, as its own consumers are named — "v3.development.bitween", say. */
async function queuePrefix(api: AdminApi) {
  const consumers = await api.get<{ queueName: string }[]>("/ops/consumers");
  const sample = consumers.map((c) => c.queueName).find((q) => q.includes(".bitween."));
  expect(sample, "no consumer queue to read the prefix from").toBeTruthy();
  return sample!.slice(0, sample!.indexOf(".bitween.") + ".bitween".length);
}

/** Reloads Queue health until `check` passes: its figures come from broker statistics that lag. */
async function untilShown(page: Page, check: () => Promise<void>) {
  await expect(async () => {
    await page.goto("queue-health");
    await expect(page.getByRole("heading", { name: "Queue health" })).toBeVisible();
    await check();
  }).toPass({ timeout: 60_000, intervals: [2000] });
}

test("a dead letter in a consumer's bad queue is listed on Queue health with its body, and Send back empties the bad queue", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const broker = mq(request);
  // A consumer whose handling of this message is harmless: an unpause for a subscription that
  // doesn't exist is acknowledged and does nothing, so the message sent back is consumed for good.
  const consumers = await api.get<{ queueName: string }[]>("/ops/consumers");
  const main = consumers.map((c) => c.queueName).find((q) => q.endsWith(".subscriptionunpausedevent"));
  expect(main, "no subscriptionunpausedevent consumer").toBeTruthy();
  const bad = `${main}.bad`;
  const marker = `pw-dead-${stamp()}`;

  await broker.publish(bad, JSON.stringify({ Id: 0, Marker: marker }));

  // The count comes from broker statistics, which lag: it is only required to be there. The
  // message itself is checked below, where the page reads the queue rather than its statistics.
  const item = page.getByRole("listitem").filter({ has: page.getByText(bad, { exact: true }) });
  await untilShown(page, async () => {
    await expect(item.getByText(/^\d+ dead$/)).toBeVisible({ timeout: 1000 });
  });

  await item.getByRole("button", { name: "Show messages" }).click();
  const shown = page.getByRole("dialog", { name: /^Dead letters — / });
  await expect(shown.getByText(marker)).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(shown).toHaveCount(0);

  await item.getByRole("button", { name: "Send back" }).click();
  await page.getByRole("dialog", { name: /^Send (this message|\d+ messages) back\?$/ }).getByRole("button", { name: "Send back" }).click();
  await expect(item.getByRole("status")).toHaveText(/^(1 message|\d+ messages) sent back\.$/);
  await expect.poll(() => broker.messages(bad), { message: "the bad queue still holds messages", timeout: 30_000 }).toBe(0);
  // And they were taken off the main queue by its consumer, not left sitting there.
  await expect.poll(() => broker.messages(main!), { timeout: 30_000 }).toBe(0);
});

test("a queue under this instance's prefix that nothing consumes is listed as unattended with what it holds, and deleting it there removes it and its bad queue from the broker", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const broker = mq(request);
  const lane = `${await queuePrefix(api)}.xchangeservice.playwright-orphan-${stamp()}`;

  try {
    await broker.declare(lane);
    await broker.declare(`${lane}.bad`);
    await broker.publish(lane, "{}");
    await broker.publish(lane, "{}");
    await broker.publish(`${lane}.bad`, "{}");

    const row = page.getByRole("row").filter({ has: page.getByText(lane, { exact: true }) });
    await untilShown(page, async () => {
      await expect(page.getByRole("heading", { name: "Nobody is reading these" })).toBeVisible({ timeout: 1000 });
      // Queued, retrying, dead.
      await expect(row.getByRole("cell").nth(2)).toHaveText("2", { timeout: 1000 });
      await expect(row.getByRole("cell").nth(4)).toHaveText("1", { timeout: 1000 });
    });
    await expect(row).toContainText("+1");

    await row.getByRole("checkbox", { name: `Select ${lane}` }).check();
    await expect(page.getByText("— holding 3 messages")).toBeVisible();
    await page.getByRole("button", { name: "Delete selected…" }).click();
    const confirm = page.getByRole("dialog", { name: "Delete these queues?" });
    await expect(confirm).toContainText("They still hold 3 messages");
    await confirm.getByRole("button", { name: "Delete queues" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);

    await expect.poll(() => broker.messages(lane), { message: "the queue is still on the broker", timeout: 30_000 }).toBeNull();
    expect(await broker.messages(`${lane}.bad`)).toBeNull();
  } finally {
    await broker.remove(lane);
    await broker.remove(`${lane}.bad`);
  }
});
