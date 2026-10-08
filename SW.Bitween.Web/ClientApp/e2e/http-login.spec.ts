import { createServer, type IncomingMessage, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { test, expect, type Page } from "@playwright/test";
import { pickOption, signInAsAdmin } from "./helpers";
import { SEED } from "./seed-data";

/**
 * The HTTP adapters' "Login" auth, against a partner API that logs in its own way: it wants
 * `user`/`secret` rather than the default body, and hands the token back under `data.token`
 * rather than `jwt`. The API is a real server in this process, so the backend has to get the
 * login right for both the receiver's pull and the handler's push to be let through.
 */

const TOKEN = "pw-login-token";

interface Hit {
  method: string;
  path: string;
  auth?: string;
  body: string;
}

let server: Server;
let base: string;
const hits: Hit[] = [];

const readBody = (req: IncomingMessage) =>
  new Promise<string>((resolve) => {
    let body = "";
    req.on("data", (chunk) => (body += chunk));
    req.on("end", () => resolve(body));
  });

test.beforeAll(async () => {
  server = createServer(async (req, res) => {
    const hit = { method: req.method!, path: req.url!, auth: req.headers.authorization, body: await readBody(req) };
    hits.push(hit);
    res.setHeader("Content-Type", "application/json");

    if (hit.path === "/login") {
      const creds = JSON.parse(hit.body || "{}");
      if (creds.user !== "pw-user" || creds.secret !== "pw-pass") {
        res.statusCode = 401;
        return res.end(JSON.stringify({ error: "bad credentials" }));
      }
      return res.end(JSON.stringify({ data: { token: TOKEN } }));
    }
    if (hit.auth !== `Bearer ${TOKEN}`) {
      res.statusCode = 401;
      return res.end(JSON.stringify({ error: "no token" }));
    }
    if (hit.path === "/feed") return res.end(JSON.stringify([{ orderId: "pw-1" }]));
    return res.end("{}");
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

test.afterAll(() => new Promise<void>((resolve) => server.close(() => resolve())));

test.beforeEach(async ({ page }) => signInAsAdmin(page));

/** Fills the login settings the receiver and handler share. Only one stage is open at a time. */
async function fillLogin(page: Page) {
  await page.locator("#prop-AuthType").fill("Login");
  await page.locator("#prop-LoginUrl").fill(`${base}/login`);
  await page.locator("#prop-LoginUsername").fill("pw-user");
  await page.locator("#prop-LoginPassword").fill("pw-pass");
  await page.locator("#prop-LoginBody").fill('{"user":"{{username}}","secret":"{{password}}"}');
  await page.locator("#prop-LoginTokenPath").fill("data.token");
}

test("HTTP receiver and handler log in with a custom body and token path", async ({ page }) => {
  const name = `Playwright HTTP login ${Date.now()}`;

  await page.goto("scheduled-jobs/new");
  await page.fill("#nj-name", name);
  await pickOption(page, "Information type", new RegExp(SEED.informationType));

  await pickOption(page, "receiver adapter", "NativeHttpReceiver");
  await page.locator("#prop-Url").fill(`${base}/feed`);
  await fillLogin(page);
  await page.getByRole("button", { name: "Close this step" }).click();

  await page.getByRole("button", { name: /^Delivery/ }).click();
  await pickOption(page, "handler adapter", "NativeHttpHandler");
  await page.locator("#prop-Url").fill(`${base}/sink`);
  await fillLogin(page);

  await page.getByRole("button", { name: "Create job" }).click();
  await expect(page).toHaveURL(/\/subscriptions\/\d+$/);

  await page.getByRole("button", { name: "Receive now" }).click();
  await page.getByRole("dialog", { name: "Receive now?" }).getByRole("button", { name: "Receive now" }).click();

  // The receiver pulls, then the handler delivers what it pulled. Both only get past the fake
  // API's token check if the login used the template and the token was read from data.token.
  await expect
    .poll(() => hits.some((h) => h.path === "/sink" && h.method === "POST"), { timeout: 30000 })
    .toBe(true);

  const logins = hits.filter((h) => h.path === "/login");
  expect(logins.length).toBeGreaterThanOrEqual(2);
  for (const login of logins) expect(JSON.parse(login.body)).toEqual({ user: "pw-user", secret: "pw-pass" });

  const feed = hits.find((h) => h.path === "/feed")!;
  expect(feed.auth).toBe(`Bearer ${TOKEN}`);
  const sink = hits.find((h) => h.path === "/sink")!;
  expect(sink.auth).toBe(`Bearer ${TOKEN}`);
  expect(JSON.parse(sink.body)).toMatchObject({ orderId: "pw-1" });

  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("button", { name: "Delete subscription" }).click();
  await expect(page).toHaveURL(/\/subscriptions$/);
});
