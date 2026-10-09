import { test, expect, type Page } from "@playwright/test";
import { createServer, type Server } from "node:http";
import { generateKeyPairSync, randomUUID, sign, type KeyObject } from "node:crypto";
import type { AddressInfo } from "node:net";
import { AdminApi, callGateway, stamp } from "./api";
import { signInAsAdmin } from "./helpers";
import { SEED } from "./seed-data";

/**
 * Who can call an API gateway, decided on the pages where it is configured: keys issued and
 * revoked on the partner's page, and how the gateway asks callers to prove who they are on the
 * gateway's own. Each change is checked the only way that matters — by calling the gateway the
 * way a partner would, from outside the browser.
 *
 * The gateways here attach their partner to a subscription that delivers nothing, so a call that
 * gets in creates an exchange (202) without leaving a failed delivery behind.
 */

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

/** A partner with a gateway it is attached to. */
async function partnerWithGateway(api: AdminApi) {
  const s = stamp();
  const partnerName = `Playwright Caller ${s}`;
  const partnerId = await api.createPartner(partnerName);
  const documentId = await api.idOf("documents", SEED.informationType);
  const gateway = await api.gatewayFor(partnerId, documentId, s);
  return { partnerId, partnerName, ...gateway };
}

async function cleanUp(api: AdminApi, g: { gatewayId: number; subscriptionId: number; partnerId: number }) {
  await api.delete(`/apigateways/${g.gatewayId}`);
  await api.delete(`/subscriptions/${g.subscriptionId}`);
  await api.delete(`/partners/${g.partnerId}`);
}

/** Issues a key on the partner's page and returns it, read off the one screen that shows it. */
async function issueKey(page: Page, partnerId: number, keyName: string): Promise<string> {
  await page.goto(`partners/${partnerId}`);
  await page.getByRole("button", { name: "New key" }).click();
  const dialog = page.getByRole("dialog", { name: "New API key" });
  await dialog.locator("#ak-name").fill(keyName);
  await dialog.getByRole("button", { name: "Generate key" }).click();

  const shown = page.getByRole("dialog", { name: "API key created" });
  await expect(shown.getByText(`Key "${keyName}"`)).toBeVisible();
  const key = (await shown.locator("code").textContent())?.trim() ?? "";
  expect(key, "the issued key").toMatch(/^[0-9a-f]{32}$/);
  await shown.getByRole("button", { name: "Done" }).click();
  await expect(shown).toHaveCount(0);
  return key;
}

/** The partner page's "API keys" panel. */
const keysTable = (page: Page) =>
  page.locator("section").filter({ has: page.getByRole("heading", { name: "API keys", exact: true }) });

test("a key issued on the partner page is shown once, opens the gateway, and is refused once revoked while the partner's other key keeps working", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  const g = await partnerWithGateway(api);

  // No key yet: the gateway turns the partner away.
  expect(await callGateway(request, g.urlName, { partnerkey: "not-a-key" })).toBe(401);

  const production = await issueKey(page, g.partnerId, "Production");
  const staging = await issueKey(page, g.partnerId, "Staging");

  // Afterwards the page lists the keys by name and prefix, and the secret itself is gone for good.
  await page.reload();
  const keys = keysTable(page);
  await expect(keys.getByRole("row").filter({ hasText: "Production" })).toContainText(`${production.slice(0, 4)}`);
  await expect(page.getByText(production)).toHaveCount(0);
  await expect(page.getByText(staging)).toHaveCount(0);

  expect(await callGateway(request, g.urlName, { partnerkey: production })).toBe(202);
  expect(await callGateway(request, g.urlName, { partnerkey: staging })).toBe(202);

  await keys.getByRole("row").filter({ hasText: "Production" }).getByRole("button", { name: "Revoke" }).click();
  await page
    .getByRole("dialog", { name: "Revoke this API key?" })
    .getByRole("button", { name: "Revoke key" })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(keys.getByRole("row").filter({ hasText: "Production" })).toHaveCount(0);
  await expect(keys.getByRole("row").filter({ hasText: "Staging" })).toBeVisible();

  expect(await callGateway(request, g.urlName, { partnerkey: production })).toBe(401);
  // Revoking rewrites the partner's credentials, so the key left behind has to survive that.
  expect(await callGateway(request, g.urlName, { partnerkey: staging })).toBe(202);

  await cleanUp(api, g);
});

test("a custom key header set on the gateway page: the key works in it, in partnerkey, as a Bearer token and as Basic auth, and not in any other header", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  const g = await partnerWithGateway(api);
  const key = await issueKey(page, g.partnerId, "Main");

  await page.goto(`api-gateways/${g.gatewayId}`);
  await page.fill("#ag-key-header", "X-Acme-Key");
  // The call preview follows the draft and says it isn't live yet.
  await expect(page.getByText("— with your unsaved changes")).toBeVisible();
  await expect(page.getByLabel("Ways to send the key")).toContainText("X-Acme-Key: <key>");
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

  await page.reload();
  await expect(page.locator("#ag-key-header")).toHaveValue("X-Acme-Key");

  const basic = (name: string) => `Basic ${Buffer.from(`${name}:${key}`).toString("base64")}`;
  expect(await callGateway(request, g.urlName, { "X-Acme-Key": key })).toBe(202);
  expect(await callGateway(request, g.urlName, { partnerkey: key })).toBe(202);
  expect(await callGateway(request, g.urlName, { Authorization: `Bearer ${key}` })).toBe(202);
  expect(await callGateway(request, g.urlName, { Authorization: basic("Main") })).toBe(202);
  // Basic carries the key's name as the username, and it has to be this key's.
  expect(await callGateway(request, g.urlName, { Authorization: basic("Someone else") })).toBe(401);
  expect(await callGateway(request, g.urlName, { "X-Other-Key": key })).toBe(401);
  expect(await callGateway(request, g.urlName, {})).toBe(401);

  await cleanUp(api, g);
});

/**
 * A login server of our own: the discovery document and signing keys a JWT gateway reads, served
 * from this test process. Bitween accepts plain http for a loopback login server, for exactly this.
 */
async function startLoginServer() {
  const { privateKey, publicKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
  const kid = randomUUID();
  let issuer = "";
  const server: Server = createServer((req, res) => {
    res.setHeader("Content-Type", "application/json");
    if (req.url === "/.well-known/openid-configuration")
      res.end(JSON.stringify({ issuer, jwks_uri: `${issuer}/jwks` }));
    else if (req.url === "/jwks")
      res.end(JSON.stringify({ keys: [{ ...publicKey.export({ format: "jwk" }), kid, use: "sig", alg: "RS256" }] }));
    else {
      res.statusCode = 404;
      res.end("{}");
    }
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  issuer = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;

  const token = (claims: Record<string, unknown>, key: KeyObject = privateKey) => {
    const now = Math.floor(Date.now() / 1000);
    const part = (o: unknown) => Buffer.from(JSON.stringify(o)).toString("base64url");
    const body = `${part({ alg: "RS256", typ: "JWT", kid })}.${part({ iss: issuer, iat: now, nbf: now, exp: now + 600, ...claims })}`;
    return `${body}.${sign("sha256", Buffer.from(body), key).toString("base64url")}`;
  };

  return { issuer, token, close: () => new Promise<void>((resolve) => server.close(() => resolve())) };
}

test("a gateway switched to login-server tokens on its page refuses API keys, accepts a token naming the partner's login identity, and refuses a token for another audience, another partner or a forged signature", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  const g = await partnerWithGateway(api);
  const key = await issueKey(page, g.partnerId, "Main");
  const login = await startLoginServer();
  const identity = `acme-${stamp()}`;

  try {
    // The partner's side: which identity its tokens carry.
    await page.goto(`partners/${g.partnerId}`);
    await page.fill("#pf-login-identity", identity);
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

    // The gateway's side: take tokens from that login server instead of keys.
    await page.goto(`api-gateways/${g.gatewayId}`);
    await page.locator("#ag-auth-method").selectOption("Jwt");
    await page.fill("#ag-jwt-issuer", login.issuer);
    await page.fill("#ag-jwt-audience", "bitween-e2e");
    await page.getByRole("button", { name: "Save changes" }).click();
    const confirm = page.getByRole("dialog", { name: "Change how partners authenticate?" });
    await expect(confirm).toContainText("Partners calling with API keys will get 401s.");
    await confirm.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);

    // Saved as typed, and the attached partner's identity shows in the table.
    await page.reload();
    await expect(page.locator("#ag-auth-method")).toHaveValue("Jwt");
    await expect(page.locator("#ag-jwt-issuer")).toHaveValue(login.issuer);
    await expect(page.getByRole("row").filter({ hasText: g.partnerName })).toContainText(identity);

    const bearer = (t: string) => ({ Authorization: `Bearer ${t}` });
    expect(await callGateway(request, g.urlName, { partnerkey: key })).toBe(401);
    expect(await callGateway(request, g.urlName, bearer(key))).toBe(401);
    expect(await callGateway(request, g.urlName, bearer(login.token({ aud: "bitween-e2e", sub: identity })))).toBe(202);
    expect(await callGateway(request, g.urlName, bearer(login.token({ aud: "someone-else", sub: identity })))).toBe(401);
    expect(await callGateway(request, g.urlName, bearer(login.token({ aud: "bitween-e2e", sub: "nobody-we-know" })))).toBe(401);
    const forger = generateKeyPairSync("rsa", { modulusLength: 2048 }).privateKey;
    expect(
      await callGateway(request, g.urlName, bearer(login.token({ aud: "bitween-e2e", sub: identity }, forger))),
    ).toBe(401);

    // And back to keys: the key works again, the token no longer does.
    await page.locator("#ag-auth-method").selectOption("PartnerKey");
    await page.getByRole("button", { name: "Save changes" }).click();
    await page
      .getByRole("dialog", { name: "Change how partners authenticate?" })
      .getByRole("button", { name: "Save changes" })
      .click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect(await callGateway(request, g.urlName, { partnerkey: key })).toBe(202);
    expect(await callGateway(request, g.urlName, bearer(login.token({ aud: "bitween-e2e", sub: identity })))).toBe(401);
  } finally {
    await login.close();
  }

  await cleanUp(api, g);
});

test("renaming a gateway and changing its URL name on its page: the name survives a reload, the old URL answers 404 and the new one takes calls", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  const g = await partnerWithGateway(api);
  const key = await issueKey(page, g.partnerId, "Main");
  const newName = `Playwright Renamed Gateway ${stamp()}`;
  const newUrl = `${g.urlName}-v2`;

  await page.goto(`api-gateways/${g.gatewayId}`);
  await page.getByRole("textbox", { name: "Name", exact: true }).fill(newName);
  // Typed with capitals and a space: the field turns it into a URL name as you type.
  await page.locator("#ag-url").fill(`${g.urlName} V2`);
  await expect(page.locator("#ag-url")).toHaveValue(newUrl);
  await expect(page.getByText(`/api/gateway/${newUrl}/sync`).first()).toBeVisible();

  await page.getByRole("button", { name: "Save changes" }).click();
  const confirm = page.getByRole("dialog", { name: "Change this gateway's URL?" });
  await expect(confirm).toContainText(`/api/gateway/${g.urlName}`);
  await expect(confirm).toContainText(`/api/gateway/${newUrl}`);
  await confirm.getByRole("button", { name: "Change URL" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);

  await page.reload();
  await expect(page.getByRole("textbox", { name: "Name", exact: true })).toHaveValue(newName);
  await expect(page.locator("#ag-url")).toHaveValue(newUrl);

  expect(await callGateway(request, g.urlName, { partnerkey: key })).toBe(404);
  expect(await callGateway(request, newUrl, { partnerkey: key })).toBe(202);

  // The list knows it by its new name.
  await page.goto("api-gateways");
  await expect(page.getByText(newName).first()).toBeVisible();

  await cleanUp(api, g);
});

test("a URL name ending in sync is refused on the gateway page before anything is saved", async ({ page, request }) => {
  const api = await AdminApi.signIn(request);
  const g = await partnerWithGateway(api);

  await page.goto(`api-gateways/${g.gatewayId}`);
  await page.locator("#ag-url").fill(`${g.urlName}/sync`);
  await expect(page.getByText(`It can't end in "sync"`).first()).toBeVisible();
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);

  const saved = await api.get<{ urlName: string }>(`/apigateways/${g.gatewayId}`);
  expect(saved.urlName).toBe(g.urlName);

  await cleanUp(api, g);
});

test("deactivating a gateway on its page makes partner calls answer 503 until it is activated again, and keeps its key header", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  const g = await partnerWithGateway(api);
  const key = await issueKey(page, g.partnerId, "Main");

  await page.goto(`api-gateways/${g.gatewayId}`);
  await page.fill("#ag-key-header", "X-Acme-Key");
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

  await page.getByRole("button", { name: "Deactivate" }).click();
  await page.getByRole("dialog", { name: /^Deactivate / }).getByRole("button", { name: "Deactivate" }).click();
  await expect(page.getByText("Deactivated", { exact: true })).toBeVisible();
  expect(await callGateway(request, g.urlName, { "X-Acme-Key": key })).toBe(503);

  await page.getByRole("button", { name: "Activate" }).click();
  await page.getByRole("dialog", { name: /^Activate / }).getByRole("button", { name: "Activate" }).click();
  await expect(page.getByText("Deactivated", { exact: true })).toHaveCount(0);
  expect(await callGateway(request, g.urlName, { "X-Acme-Key": key })).toBe(202);

  // Switching it off and on sends only the name, URL and state: the header has to survive that.
  await page.reload();
  await expect(page.locator("#ag-key-header")).toHaveValue("X-Acme-Key");

  await cleanUp(api, g);
});
