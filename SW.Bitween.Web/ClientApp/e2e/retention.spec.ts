import { test, expect, type Page } from "@playwright/test";
import { sessionToken, signInAsAdmin } from "./helpers";

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

/** The backend's own API, called with the signed-in admin's token. */
async function api<T>(page: Page, path: string): Promise<T> {
  const token = await sessionToken(page);
  return page.evaluate(
    async ({ p, t }) => {
      const res = await fetch(`/api${p}`, { headers: { Authorization: `Bearer ${t}` } });
      return res.json();
    },
    { p: path, t: token },
  );
}

/**
 * Whether the bucket deletes exchange files by age at all. The cloud providers' buckets carry a
 * temp30/ rule; local-disk storage — what tools/e2e.sh runs on — reports no rules, because
 * nothing on a local filesystem deletes files by age. The tests about that rule can only run
 * where it exists.
 */
async function hasTemp30Rule(page: Page) {
  const status = await api<{ storage: { rules: { prefix: string; enabled: boolean }[] } }>(page, "/retention");
  return status.storage.rules.some((r) => r.prefix === "temp30/" && r.enabled);
}

const NO_RULE = "this storage bucket has no temp30/ deletion rule (local-disk storage never has one)";

test("the storage section lists the bucket's rules and marks the one exchange files fall under", async ({
  page,
}) => {
  test.skip(!(await hasTemp30Rule(page)), NO_RULE);

  await page.goto("settings?section=Documents%20%26%20storage");
  const rules = page.getByRole("table");
  await expect(rules.getByText("temp30/", { exact: true })).toBeVisible();
  await expect(rules.getByText("Exchange files")).toBeVisible();
});

test("the storage section explains retention, previews a change and asks before saving it", async ({ page }) => {
  await page.goto("settings?section=Documents%20%26%20storage");

  await expect(page.getByText("What these settings do")).toBeVisible();

  const days = page.getByRole("spinbutton", { name: "Keep exchanges (days)" });
  await days.fill("45");
  await days.blur();
  await expect(page.getByText("What your unsaved changes would do")).toBeVisible();
  const removes = /Each run removes exchanges that started more than 45 days ago/;
  await expect(page.getByText(removes)).toBeVisible();

  await page.getByRole("button", { name: "Save changes" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByText("Save the retention changes?")).toBeVisible();
  await expect(dialog.getByText(removes)).toBeVisible();

  // Not saved: on this database the nightly job would really start deleting old exchanges.
  await dialog.getByRole("button", { name: "Cancel" }).click();
  await page.getByRole("button", { name: "Discard" }).click();
  await expect(page.getByText("What these settings do")).toBeVisible();
});

test("an exchange whose file the bucket deleted says so in its drawer", async ({ page }) => {
  test.skip(!(await hasTemp30Rule(page)), NO_RULE);

  // Anything older than the temp30/ rule has had its files deleted by the bucket.
  const before = new Date(Date.now() - 35 * 24 * 3600 * 1000).toISOString();
  const found = await api<{ result: { id: string }[] }>(
    page,
    `/xchanges?filter=${encodeURIComponent(`StartedOn:8:${before}`)}&size=1`,
  );
  test.skip(found.result.length === 0, "no exchange here is old enough for its files to have expired");

  await page.goto(`exchanges?ids=${found.result[0].id}`);
  const row = page.getByRole("row").nth(1);
  await row.getByRole("cell").last().click();

  await expect(page.getByText(/deleted by the storage retention policy/)).toBeVisible();
  await expect(page.getByText("Failed to load this document.")).toHaveCount(0);
});
