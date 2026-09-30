import { expect, request, test, type APIRequestContext } from "@playwright/test";
import { ADMIN_EMAIL, ADMIN_PASSWORD, pickOption, signInAsAdmin } from "./helpers";

const API = "https://localhost:7155/api";
const SHOTS = process.env.PW_SHOTS;

/**
 * A channel is made once in Settings and picked wherever something notifies. This walks the one
 * path that crosses pages: made in Settings, picked by a subscription, saved, still there after a
 * reload — and picked again as a retry policy's alert.
 */
test.describe("notification channels", () => {
  let api: APIRequestContext;
  let auth: Record<string, string>;
  let documentId: number;
  let subscriptionId: number;
  let policyId: number;
  const stamp = Date.now();
  const channelName = `PW channel ${stamp}`;

  test.beforeAll(async () => {
    api = await request.newContext({ ignoreHTTPSErrors: true });
    const login = await api.post(`${API}/accounts/login`, { data: { Username: ADMIN_EMAIL, Password: ADMIN_PASSWORD } });
    auth = { Authorization: `Bearer ${(await login.json()).jwt}` };
    documentId = await (await api.post(`${API}/documents`, { headers: auth, data: { name: `PW channel doc ${stamp}` } })).json();
    subscriptionId = await (
      await api.post(`${API}/subscriptions`, {
        headers: auth,
        data: { name: `PW channel sub ${stamp}`, documentId, type: "BusGateway" },
      })
    ).json();
    policyId = await (await api.post(`${API}/retrypolicies`, { headers: auth, data: { name: `PW channel policy ${stamp}`, groups: [] } })).json();
  });

  test.afterAll(async () => {
    // The channel is in use until both are gone, and a channel in use cannot be deleted.
    await api.delete(`${API}/subscriptions/${subscriptionId}`, { headers: auth });
    await api.delete(`${API}/retrypolicies/${policyId}`, { headers: auth });
    await api.delete(`${API}/documents/${documentId}`, { headers: auth });
    const lookup: Record<string, string> = await (await api.get(`${API}/notificationchannels?lookup=true`, { headers: auth })).json();
    for (const [id, name] of Object.entries(lookup))
      if (name === channelName) await api.delete(`${API}/notificationchannels/${id}`, { headers: auth });
    await api.dispose();
  });

  test("made in Settings, then picked by a subscription and a retry policy", async ({ page }) => {
    await signInAsAdmin(page);

    await page.goto(`settings?section=${encodeURIComponent("Notification channels")}`);
    await page.getByRole("button", { name: "New channel" }).click();
    const dialog = page.getByRole("dialog", { name: "New notification channel" });
    await dialog.getByLabel("Name").fill(channelName);
    await pickOption(page, "handler adapter", "HttpHandler");
    await dialog.getByLabel("Url *").fill("https://example.test/hook");
    if (SHOTS) await page.screenshot({ path: `${SHOTS}/1-channel-dialog.png` });
    await dialog.getByRole("button", { name: "Create channel" }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole("cell", { name: channelName, exact: true })).toBeVisible();
    if (SHOTS) await page.screenshot({ path: `${SHOTS}/2-settings-section.png` });

    await page.goto(`subscriptions/${subscriptionId}`);
    const panel = page.locator("section", { has: page.getByRole("heading", { name: "Notifications" }) });
    await panel.getByRole("button", { name: "Add" }).click();
    await pickOption(page, "Notification channel", channelName);
    await expect(panel.getByRole("checkbox", { name: "Fails" })).toBeChecked();
    await panel.getByRole("checkbox", { name: "Succeeds" }).check();
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByText("Unsaved changes", { exact: true })).toBeHidden();
    if (SHOTS) await page.screenshot({ path: `${SHOTS}/3-subscription.png`, fullPage: true });

    await page.reload();
    await expect(panel.getByRole("combobox", { name: "Notification channel" })).toHaveValue(channelName);
    await expect(panel.getByRole("checkbox", { name: "Succeeds" })).toBeChecked();

    await page.goto(`retry-policies/${policyId}`);
    await pickOption(page, "Notification channel", channelName);
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByText("Unsaved changes", { exact: true })).toBeHidden();
    if (SHOTS) await page.screenshot({ path: `${SHOTS}/4-retry-policy.png`, fullPage: true });

    // Both places now count as using it, which is what stops it being deleted from under them.
    await page.goto(`settings?section=${encodeURIComponent("Notification channels")}`);
    await expect(page.getByRole("row", { name: new RegExp(channelName) }).getByText("2 places")).toBeVisible();
    await expect(page.getByRole("button", { name: `Delete ${channelName}` })).toBeDisabled();
  });
});
