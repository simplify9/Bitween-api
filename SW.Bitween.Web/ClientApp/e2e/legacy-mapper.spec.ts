import { test, expect } from "@playwright/test";
import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { AdminApi, stamp } from "./api";
import { signInAsAdmin } from "./helpers";
import { SEED } from "./seed-data";

/**
 * The legacy JSON mapper (NativeJSONMapper) and its own editor. New subscriptions can't pick it any
 * more, but the ones already on it keep it and keep this editor, so it still has to open their
 * mapping, save it, and — what matters to them — run it. The subscription is made through the
 * API because the picker no longer offers the mapper; everything after that is the editor.
 */

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("the legacy JSON mapper's editor maps by auto-match, previews through the server, saves, reopens with the mapping, and the saved mapping shapes a real delivery", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const s = stamp();

  const received: string[] = [];
  const endpoint = createServer((req, res) => {
    let body = "";
    req.on("data", (chunk) => (body += chunk));
    req.on("end", () => {
      received.push(body);
      res.end("ok");
    });
  });
  await new Promise<void>((resolve) => endpoint.listen(0, "127.0.0.1", resolve));
  const url = `http://127.0.0.1:${(endpoint.address() as AddressInfo).port}/`;

  let id: number | null = null;
  try {
    id = await api.post<number>("/subscriptions", {
      name: `Playwright Legacy Mapping ${s}`,
      documentId: await api.idOf("documents", SEED.informationType),
      type: "ApiCall",
      partnerId: await api.idOf("partners", SEED.partner),
      mapperId: "NativeJSONMapper",
      // What a subscription on this mapper has as a minimum: a template, here an empty object.
      mapperProperties: [{ key: "ScribanTemplate", value: "{}" }],
      handlerId: "NativeHttpHandler",
      handlerProperties: [{ key: "Url", value: url }],
      receiverProperties: [],
      validatorProperties: [],
      documentFilter: [],
      inactive: false,
    });

    await page.goto(`subscriptions/${id}/mapper`);
    // The old editor, not the native mapper's: it is chosen by the mapper the subscription runs.
    await expect(page.getByText("Mapping Editor", { exact: true })).toBeVisible({ timeout: 15000 });

    await page.getByPlaceholder('{ "paste": "source JSON here" }').fill(
      JSON.stringify({ orderId: "A-1", customer: "Acme", internalNote: "drop me" }),
    );
    await page.getByPlaceholder(/output shape/).fill(JSON.stringify({ orderId: "", customer: "" }));
    await page.getByRole("button", { name: "Generate from JSON" }).click();
    await page.getByRole("button", { name: "Auto-match" }).click();
    await expect(page.getByText("2 mappings ·")).toBeVisible();

    await page.getByRole("button", { name: /Show Live Preview/ }).click();
    const preview = page.locator("pre").last();
    await expect(preview).toContainText('"orderId"', { timeout: 15000 });
    await expect(preview).toContainText("A-1");
    await expect(preview).toContainText("Acme");
    await expect(preview).not.toContainText("drop me");

    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByText("✓ Saved")).toBeVisible();

    // Reopened from scratch, the editor rebuilds the same mapping from what was saved.
    await page.reload();
    await expect(page.getByText("2 mappings ·")).toBeVisible({ timeout: 15000 });
    await expect(page.getByPlaceholder('{ "paste": "source JSON here" }')).toHaveValue(/A-1/);

    const raw = await api.get<{ mapperId: string; mapperProperties: { key: string; value: string }[] | Record<string, string> }>(
      `/subscriptions/${id}`,
    );
    expect(raw.mapperId).toBe("NativeJSONMapper");
    const props = Array.isArray(raw.mapperProperties)
      ? Object.fromEntries(raw.mapperProperties.map((p) => [p.key, p.value]))
      : raw.mapperProperties;
    expect(props.ScribanTemplate).toContain("orderId");

    // And it runs: a real exchange is delivered in the mapped shape.
    await api.sendExchange(id, { orderId: `B-${s}`, customer: "Zeta", internalNote: "secret" });
    await expect
      .poll(() => received.length, { message: "the mapped document never arrived", timeout: 60_000 })
      .toBe(1);
    expect(JSON.parse(received[0])).toEqual({ orderId: `B-${s}`, customer: "Zeta" });
  } finally {
    await new Promise<void>((resolve) => endpoint.close(() => resolve()));
    if (id !== null) await api.delete(`/subscriptions/${id}`);
  }
});
