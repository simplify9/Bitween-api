import { test, expect } from "@playwright/test";
import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { AdminApi, stamp } from "./api";
import { pickOption, signInAsAdmin } from "./helpers";
import { SEED } from "./seed-data";

/**
 * A response subscription built on its page and wired to a delivering subscription on that one's
 * page, then followed through a real exchange: the delivery's answer is handed on, and the
 * response subscription delivers it in turn. Both ends are HTTP endpoints this test runs.
 */

/** A local endpoint that answers every request with `reply` and records what it was sent. */
async function startEndpoint(reply: string) {
  const received: string[] = [];
  const server = createServer((req, res) => {
    let body = "";
    req.on("data", (chunk) => (body += chunk));
    req.on("end", () => {
      received.push(body);
      res.setHeader("Content-Type", "application/json");
      res.end(reply);
    });
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}/`;
  return { url, received, close: () => new Promise<void>((resolve) => server.close(() => resolve())) };
}

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("a response subscription made on its page and picked on a delivering subscription's Response step receives that delivery's answer and delivers it on", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const s = stamp();
  const answer = JSON.stringify({ label: `label-${s}` });
  const partnerSystem = await startEndpoint(answer);
  const labelStore = await startEndpoint("{}");
  let feederId: number | null = null;
  let responseId: number | null = null;

  try {
    // The response subscription, on its own page.
    const responseName = `Playwright Label Store ${s}`;
    await page.goto("response-subscriptions");
    await page.getByRole("button", { name: "New response subscription" }).click();
    await page.fill("#nr-name", responseName);
    await pickOption(page, "Information type", new RegExp(SEED.informationType));
    // Delivery is the step that is open to begin with.
    await pickOption(page, "handler adapter", "NativeHttpHandler");
    await page.locator("#prop-Url").fill(labelStore.url);
    await page.getByRole("button", { name: "Create response subscription" }).click();
    await expect(page).toHaveURL(/\/subscriptions\/\d+$/);
    responseId = Number(new URL(page.url()).pathname.split("/").pop());

    // A subscription whose delivery answers with a document.
    const feederName = `Playwright Label Request ${s}`;
    feederId = await api.post<number>("/subscriptions", {
      name: feederName,
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

    // Hand its answer to the response subscription, on its Response step.
    await page.goto(`subscriptions/${feederId}`);
    await page.getByRole("button", { name: /^Response/ }).click();
    await pickOption(page, "Hand the response to", responseName);
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);
    await page.reload();
    await expect(page.getByRole("combobox", { name: "Hand the response to" })).toHaveValue(responseName);
    expect((await api.get<{ responseSubscriptionId: number | null }>(`/subscriptions/${feederId}`)).responseSubscriptionId).toBe(
      responseId,
    );

    // The list of response subscriptions names what feeds this one.
    await page.goto("response-subscriptions");
    await expect(page.getByRole("row").filter({ hasText: responseName })).toContainText(feederName);

    // A document goes out, the partner system answers, and the answer is delivered on.
    await api.sendExchange(feederId, { shipment: `label-me-${s}` });
    await expect
      .poll(() => partnerSystem.received.length, { message: "the delivery never reached the partner system", timeout: 60_000 })
      .toBe(1);
    expect(partnerSystem.received[0]).toContain(`label-me-${s}`);
    await expect
      .poll(() => labelStore.received.length, { message: "the response subscription never delivered", timeout: 60_000 })
      .toBe(1);
    expect(JSON.parse(labelStore.received[0])).toEqual(JSON.parse(answer));
    // And it ran as an exchange of its own, which succeeded.
    await expect
      .poll(async () => (await api.exchangesOf(responseId!))[0]?.status ?? null, { timeout: 30_000 })
      .toBe(true);
  } finally {
    await partnerSystem.close();
    await labelStore.close();
    if (feederId !== null) await api.delete(`/subscriptions/${feederId}`);
    if (responseId !== null) await api.delete(`/subscriptions/${responseId}`);
  }
});
