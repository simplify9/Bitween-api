import { test, expect } from "@playwright/test";
import { createHmac, randomUUID } from "node:crypto";
import { AdminApi, stamp } from "./api";
import { signInAsAdmin } from "./helpers";
import { BASE_URL } from "./env";
import { SEED } from "./seed-data";

/**
 * A gateway that takes signed calls, the way webhook senders call: no key, no token, a signature
 * over the body made with a secret held on the partner. Set up on the gateway's page, then checked
 * by calling the gateway from outside the browser.
 */

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

const SECRET_PROPERTY = "WebhookSecret";

function timestamped(
  secret: string,
  body: string,
  at = Math.floor(Date.now() / 1000),
) {
  const mac = createHmac("sha256", secret)
    .update(`${at}.${body}`)
    .digest("hex");
  return `t=${at},v1=${mac}`;
}

test("a gateway switched to signed calls takes a call signed with the partner's secret and refuses the rest", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  const s = stamp();
  const partnerName = `Playwright Signer ${s}`;
  const partnerId = await api.createPartner(partnerName);
  const secret = randomUUID();
  // The partner's secret, marked secret so the API never returns it in clear.
  await api.call("POST", `/partners/${partnerId}`, {
    name: partnerName,
    adapterProperties: { [SECRET_PROPERTY]: secret },
    secretProperties: [SECRET_PROPERTY],
    loginIdentity: null,
  });
  const documentId = await api.idOf("documents", SEED.informationType);
  const g = await api.gatewayFor(partnerId, documentId, s);

  try {
    await page.goto(`api-gateways/${g.gatewayId}`);
    await page.locator("#ag-auth-method").selectOption("Signature");
    await page.fill("#ag-sig-header", "X-Hook-Signature");
    await page.locator("#ag-sig-scheme").selectOption("TimestampedHmacSha256");
    await page.fill("#ag-sig-secret", SECRET_PROPERTY);
    await expect(
      page.getByText(/X-Hook-Signature: t=<unix seconds>,v1=/),
    ).toBeVisible();
    await page.getByRole("button", { name: "Save changes" }).click();
    const confirm = page.getByRole("dialog", {
      name: "Change how partners authenticate?",
    });
    await expect(confirm).toContainText(
      "Only calls signed with an attached partner's secret are accepted.",
    );
    await confirm.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);

    // Saved as typed.
    await page.reload();
    await expect(page.locator("#ag-auth-method")).toHaveValue("Signature");
    await expect(page.locator("#ag-sig-header")).toHaveValue(
      "X-Hook-Signature",
    );
    await expect(page.locator("#ag-sig-secret")).toHaveValue(SECRET_PROPERTY);

    const body = JSON.stringify({ id: 42, event: `e2e-${s}` });
    const call = async (headers: Record<string, string>, data = body) =>
      (
        await request.post(`${BASE_URL}api/gateway/${g.urlName}/async`, {
          headers: { "Content-Type": "application/json", ...headers },
          data,
        })
      ).status();

    expect(await call({ "X-Hook-Signature": timestamped(secret, body) })).toBe(
      202,
    );
    expect(await call({})).toBe(401);
    expect(
      await call({ "X-Hook-Signature": timestamped("guessed", body) }),
    ).toBe(401);
    expect(
      await call(
        { "X-Hook-Signature": timestamped(secret, body) },
        body.replace("42", "43"),
      ),
    ).toBe(401);
    expect(
      await call({
        "X-Hook-Signature": timestamped(
          secret,
          body,
          Math.floor(Date.now() / 1000) - 3600,
        ),
      }),
    ).toBe(401);

    // The plain format: HMAC of the body, no time.
    await page.locator("#ag-sig-scheme").selectOption("HmacSha256");
    await expect(page.locator("#ag-sig-tolerance")).toHaveCount(0);
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(
      page.getByRole("button", { name: "Save changes" }),
    ).toHaveCount(0);
    const plain = createHmac("sha256", secret).update(body).digest("hex");
    expect(await call({ "X-Hook-Signature": `sha256=${plain}` })).toBe(202);
    expect(await call({ "X-Hook-Signature": timestamped(secret, body) })).toBe(
      401,
    );
  } finally {
    await api.delete(`/apigateways/${g.gatewayId}`);
    await api.delete(`/subscriptions/${g.subscriptionId}`);
    await api.delete(`/partners/${partnerId}`);
  }
});
