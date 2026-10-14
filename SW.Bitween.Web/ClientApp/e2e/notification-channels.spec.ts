import { expect, request, test, type APIRequestContext } from "@playwright/test";
import { ADMIN_EMAIL, ADMIN_PASSWORD, pickOption, signInAsAdmin } from "./helpers";

const API = "https://localhost:7155/api";
const SHOTS = process.env.PW_SHOTS;

/**
 * A channel is made once - in Settings, or from any picker - and picked wherever something
 * notifies. This walks the paths that cross pages: one made from Settings and picked by a retry
 * policy, and one made from a subscription's picker, which comes back to the unsaved subscription
 * with the new channel already in the row it was made from.
 */
test.describe("notification channels", () => {
  let api: APIRequestContext;
  let auth: Record<string, string>;
  let documentId: number;
  let subscriptionId: number;
  let policyId: number;
  const stamp = Date.now();
  const slackName = `PW Slack ${stamp}`;
  const teamsName = `PW Teams ${stamp}`;

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
    // A channel is in use until both are gone, and a channel in use cannot be deleted.
    await api.delete(`${API}/subscriptions/${subscriptionId}`, { headers: auth });
    await api.delete(`${API}/retrypolicies/${policyId}`, { headers: auth });
    await api.delete(`${API}/documents/${documentId}`, { headers: auth });
    const lookup: Record<string, string> = await (await api.get(`${API}/notificationchannels?lookup=true`, { headers: auth })).json();
    for (const [id, name] of Object.entries(lookup))
      if (name === slackName || name === teamsName) await api.delete(`${API}/notificationchannels/${id}`, { headers: auth });
    await api.dispose();
  });

  test("made in Settings or from a picker, then picked by a subscription and a retry policy", async ({ page }) => {
    await signInAsAdmin(page);

    // From Settings: a page of its own, Email / Teams / Slack first.
    await page.goto(`settings?section=${encodeURIComponent("Notification channels")}`);
    await page.getByRole("button", { name: "New channel" }).click();
    await expect(page).toHaveURL(/\/notification-channels\/new$/);
    await expect(page.getByRole("radiogroup", { name: "How it sends" })).toBeVisible();
    await page.getByLabel("Channel name").fill(slackName);
    await page.getByRole("radio", { name: /Slack/ }).click();
    await page.getByLabel("Incoming webhook URL *").fill("https://hooks.slack.test/services/T0/B0/x");
    if (SHOTS) await page.screenshot({ path: `${SHOTS}/1-new-channel.png`, fullPage: true });
    await page.getByRole("button", { name: "Create channel" }).click();
    await expect(page).toHaveURL(/\/notification-channels\/\d+$/);
    await expect(page.getByText("Nothing sent yet.")).toBeVisible();
    // Saved, so hidden: anyone with a webhook address can post to the channel.
    await expect(page.getByText("••••••••")).toBeVisible();

    // From a subscription's picker: off to the new channel page and back, edits intact.
    await page.goto(`subscriptions/${subscriptionId}`);
    const panel = page.locator("section", { has: page.getByRole("heading", { name: "Notifications" }) });
    await panel.getByRole("button", { name: "Add" }).click();
    await panel.getByRole("checkbox", { name: "Succeeds" }).check();
    await panel.getByRole("button", { name: "New channel" }).click();
    await expect(page).toHaveURL(/\/notification-channels\/new\?return=/);
    // The URL changes a moment before the page does; the subscription has a Name field of its own.
    await expect(page.getByRole("radiogroup", { name: "How it sends" })).toBeVisible();
    await page.getByLabel("Channel name").fill(teamsName);
    await page.getByRole("radio", { name: /Microsoft Teams/ }).click();
    await page.getByLabel("Workflows webhook URL *").fill("https://example.test/workflows/hook");
    await page.getByRole("button", { name: "Create channel" }).click();

    await expect(page).toHaveURL(new RegExp(`/subscriptions/${subscriptionId}`));
    await expect(panel.getByRole("combobox", { name: "Notification channel" })).toHaveValue(teamsName);
    // The tick made before leaving is still there: the draft came back with the channel.
    await expect(panel.getByRole("checkbox", { name: "Succeeds" })).toBeChecked();
    if (SHOTS) await page.screenshot({ path: `${SHOTS}/2-subscription.png`, fullPage: true });
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByText("Unsaved changes", { exact: true })).toBeHidden();

    await page.reload();
    await expect(panel.getByRole("combobox", { name: "Notification channel" })).toHaveValue(teamsName);

    await page.goto(`retry-policies/${policyId}`);
    await pickOption(page, "Notification channel", slackName);
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByText("Unsaved changes", { exact: true })).toBeHidden();

    // Each now counts as used, which is what stops it being deleted from under its users.
    await page.goto(`settings?section=${encodeURIComponent("Notification channels")}`);
    for (const name of [slackName, teamsName])
      await expect(page.getByRole("button", { name: `Delete ${name}` })).toBeDisabled();
    if (SHOTS) await page.screenshot({ path: `${SHOTS}/3-settings.png`, fullPage: true });
  });
});
