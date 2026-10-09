import { test, expect } from "@playwright/test";
import { AdminApi, stamp } from "./api";
import { pickOption, signInAsAdmin } from "./helpers";
import { SEED } from "./seed-data";

/**
 * The read-only views: the Adapters page, the flow map, and the filters on the subscription
 * list. Nothing here changes anything through the page; what each view shows is checked against
 * rows the test made, so a view that shows the wrong thing — not just nothing — fails.
 */

/** Published by tools/e2e.sh: a custom handler with versions 1.0.0 and 2.0.0 (current). */
const CUSTOM_HANDLER = "e2e.samplehandler";

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("the Adapters page opens on Installed, lists built-in adapters under Built-in and published ones under Custom with their versions, and narrows by kind and search", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  const hasCustom = await api.hasAdapter("handlers", CUSTOM_HANDLER);

  await page.goto("adapters");
  await expect(page.getByRole("tab", { name: "Installed" })).toHaveAttribute("aria-selected", "true");
  const builtIn = page.getByRole("region", { name: /^Built-in/ });
  const custom = page.getByRole("region", { name: /^Custom/ });

  // The two HTTP adapters every subscription in this suite is built from, by their display names.
  await expect(builtIn.getByRole("button", { name: /HTTP request/ })).toBeVisible();
  await expect(builtIn.getByRole("button", { name: /HTTP poll/ })).toBeVisible();
  // Built-in adapters have no versions of their own, so nothing about versions is said of them.
  await expect(builtIn.getByText(/^\d+ versions?$/)).toHaveCount(0);

  if (hasCustom) {
    const row = custom.getByRole("button", { name: /Echo handler \(e2e\)/ });
    await expect(row).toContainText("v2.0.0");
    await expect(row).toContainText("2 versions");
    await expect(row).toContainText(CUSTOM_HANDLER);
    await row.click();
    await expect(custom.getByRole("row").filter({ hasText: "v2.0.0" })).toContainText("Current");
    await expect(custom.getByRole("row").filter({ hasText: "v1.0.0" })).toContainText("Release 1.0.0.");
    await expect(custom.getByRole("row").filter({ hasText: "ContentType" })).toContainText("text/plain");
    // Nothing in the built-in section is the custom one.
    await expect(builtIn.getByText(CUSTOM_HANDLER)).toHaveCount(0);
  }

  // Narrowed by kind: receivers only, so the HTTP request handler goes and the HTTP poll stays.
  const kind = page.getByRole("radiogroup", { name: "Kind" });
  await kind.getByRole("radio", { name: "Receivers" }).click();
  await expect(builtIn.getByRole("button", { name: /HTTP poll/ })).toBeVisible();
  await expect(builtIn.getByRole("button", { name: /HTTP request/ })).toHaveCount(0);
  if (hasCustom) await expect(custom.getByText("No custom adapter matches.")).toBeVisible();

  // And by search, across both sections.
  await kind.getByRole("radio", { name: "All" }).click();
  await page.getByRole("searchbox", { name: "Search adapters" }).fill("smtp");
  await expect(builtIn.getByRole("button", { name: /Email \(SMTP\)/ })).toBeVisible();
  await expect(builtIn.getByRole("button", { name: /HTTP/ })).toHaveCount(0);
  await expect(custom.getByText("No custom adapter matches.")).toBeVisible();
});

test("the Adapters page's Marketplace tab shows its placeholder, and the tab is kept in the URL across a reload", async ({
  page,
}) => {
  await page.goto("adapters");
  await page.getByRole("tab", { name: "Marketplace" }).click();
  await expect(page).toHaveURL(/[?&]tab=marketplace/);
  await expect(page.getByRole("tab", { name: "Marketplace" })).toHaveAttribute("aria-selected", "true");
  await expect(page.getByText("The marketplace is on its way")).toBeVisible();
  await expect(page.getByRole("region", { name: /^Built-in/ })).toHaveCount(0);

  await page.reload();
  await expect(page.getByText("The marketplace is on its way")).toBeVisible();

  await page.getByRole("tab", { name: "Installed" }).click();
  await expect(page).not.toHaveURL(/tab=/);
  await expect(page.getByRole("region", { name: /^Built-in/ })).toBeVisible();
});

test("the flow map draws an API gateway as a card that opens the gateway's page", async ({ page, request }) => {
  const api = await AdminApi.signIn(request);
  const s = stamp();
  const partnerId = await api.createPartner(`Playwright Map Partner ${s}`);
  const g = await api.gatewayFor(partnerId, await api.idOf("documents", SEED.informationType), s);
  const gatewayName = `Playwright Gateway ${s}`;

  await page.goto("flow");
  await expect(page.getByRole("heading", { name: "Flow map" })).toBeVisible();
  await expect(page.getByText(/\d+ gateways? · \d+ messages?/)).toBeVisible();
  const card = page.getByRole("link", { name: new RegExp(gatewayName) });
  await expect(card).toBeVisible();
  await card.click();
  await expect(page).toHaveURL(new RegExp(`/api-gateways/${g.gatewayId}$`));
  await expect(page.getByRole("textbox", { name: "Name", exact: true })).toHaveValue(gatewayName);

  await api.delete(`/apigateways/${g.gatewayId}`);
  await api.delete(`/subscriptions/${g.subscriptionId}`);
  await api.delete(`/partners/${partnerId}`);
});

test("the subscription list's search, type, partner and status filters each narrow it to the matching rows, and clearing the partner filter brings them back", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  const s = stamp();
  const documentId = await api.idOf("documents", SEED.informationType);
  const otherPartner = `Playwright Filter Partner ${s}`;
  const otherPartnerId = await api.createPartner(otherPartner);
  const base = {
    documentId,
    handlerId: "NativeHttpHandler",
    handlerProperties: [{ key: "Url", value: "https://example.com/filtered" }],
    receiverProperties: [],
    validatorProperties: [],
    mapperProperties: [],
    documentFilter: [],
  };
  // Two words in every name: a multi-word search once matched nothing at all.
  const acme = `Playwright Filter Acme ${s}`;
  const other = `Playwright Filter Other ${s}`;
  const job = `Playwright Filter Job ${s}`;
  const ids = [
    await api.post<number>("/subscriptions", {
      ...base,
      name: acme,
      type: "ApiCall",
      partnerId: await api.idOf("partners", SEED.partner),
      inactive: false,
    }),
    await api.post<number>("/subscriptions", { ...base, name: other, type: "ApiCall", partnerId: otherPartnerId, inactive: true }),
    await api.post<number>("/subscriptions", {
      ...base,
      name: job,
      type: "Receiving",
      receiverId: "NativeHttpReceiver",
      receiverProperties: [{ key: "Url", value: "https://example.com/source" }],
      schedules: [{ recurrence: "Daily", days: 0, hours: 3, minutes: 0, backwards: false }],
      inactive: true,
    }),
  ];

  const rows = page.getByRole("row").filter({ hasText: `Playwright Filter ` }).filter({ hasText: s });
  const shown = async (...names: string[]) => {
    await expect(rows).toHaveCount(names.length);
    for (const n of names) await expect(rows.filter({ hasText: n })).toHaveCount(1);
  };

  await page.goto("subscriptions");
  await page.getByRole("searchbox", { name: "Search subscriptions" }).fill(`Filter Acme ${s}`);
  await shown(acme);

  await page.getByRole("searchbox", { name: "Search subscriptions" }).fill(s);
  await shown(acme, other, job);

  await page.getByRole("button", { name: "Scheduled job", exact: true }).click();
  await expect(page).toHaveURL(/type=Receiving/);
  await shown(job);
  await page.getByRole("button", { name: "Scheduled job", exact: true }).click();

  await pickOption(page, "Filter by partner", otherPartner);
  await expect(page).toHaveURL(new RegExp(`partnerId=${otherPartnerId}`));
  await shown(other);

  // Focus moves off the picker first: it opens on taking focus, and it still has it.
  await page.getByRole("heading", { name: "Subscriptions", level: 1 }).click();
  await pickOption(page, "Filter by partner", "Any partner");
  await shown(acme, other, job);
  await page.getByRole("combobox", { name: "Filter by status" }).selectOption("true");
  await shown(other, job);
  await page.getByRole("combobox", { name: "Filter by status" }).selectOption("false");
  await shown(acme);

  // The filters live in the URL, so a reload — or a link sent to someone — shows the same rows.
  await page.reload();
  await shown(acme);

  for (const id of ids) await api.delete(`/subscriptions/${id}`);
  await api.delete(`/partners/${otherPartnerId}`);
});
