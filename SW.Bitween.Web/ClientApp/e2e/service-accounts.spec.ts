import { test, expect } from "@playwright/test";
import { API, signInAsAdmin } from "./helpers";

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("a service account's token is shown once, signs in, and stops when revoked", async ({ page, request }) => {
  const name = `playwright-svc-${Date.now()}`;

  await page.goto("team/service-accounts");
  await page.getByRole("button", { name: "New service account" }).first().click();
  await page.getByLabel("Name").fill(name);
  await page.getByRole("checkbox", { name: /^Viewer/ }).check();
  await page.getByRole("button", { name: "Create" }).click();

  const drawer = page.getByRole("dialog", { name: "Service account details" });
  await expect(drawer.getByRole("heading", { name })).toBeVisible();

  await drawer.getByLabel("New token").fill("e2e job");
  await drawer.getByRole("button", { name: "Issue token" }).click();
  const token = (await drawer.locator("code").first().textContent())!.trim();
  expect(token).toMatch(/^bwt_[A-Za-z0-9]{40}$/);

  // The token signs in, as the account it belongs to, with what its role allows.
  const session = await request.post(`${API}/accounts/servicetoken`, { data: { token } });
  expect(session.ok()).toBeTruthy();
  const { jwt } = await session.json();
  const headers = { Authorization: `Bearer ${jwt}` };
  expect((await request.get(`${API}/documents?page=0&size=1`, { headers })).ok()).toBeTruthy();
  expect(
    (
      await request.post(`${API}/documents`, { headers, data: { name: `nope ${name}`, documentFormat: "Json" } })
    ).status(),
  ).toBe(403);

  // Never shown again once dismissed.
  await drawer.getByRole("button", { name: "Done" }).click();
  await expect(drawer.getByText(token)).toHaveCount(0);
  await page.reload();
  await expect(page.getByText(token)).toHaveCount(0);

  await drawer.getByRole("button", { name: "Revoke" }).click();
  await page.getByRole("button", { name: "Revoke token" }).click();
  await expect(drawer.getByText("Revoked", { exact: true })).toBeVisible();
  expect((await request.post(`${API}/accounts/servicetoken`, { data: { token } })).ok()).toBeFalsy();

  await drawer.getByRole("button", { name: "Remove service account" }).click();
  await page.getByRole("button", { name: "Remove service account" }).last().click();
  await expect(page.getByRole("row", { name: new RegExp(name) })).toHaveCount(0);
});
