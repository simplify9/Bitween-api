import { test, expect, type Page } from "@playwright/test";
import {
  FIRST_PASSWORD,
  addMember,
  createRole,
  deleteRole,
  removeMember,
  signIn,
  signInAsAdmin,
  signOut,
} from "./helpers";

/**
 * The audit trail, end to end.
 *
 * Two things make these tests different from the rest of the suite. Audit rows are never
 * deleted — that is the point of the table — so nothing here may assume the trail is empty or
 * that its own row is first; every assertion is scoped to the entity it created. And the
 * trail's whole value rests on one negative claim — that credentials never reach it — which is
 * asserted against the stored row, not against what the screen happens to render.
 */

const API = "https://localhost:7155/api";

/** Reads the trail through the API with the signed-in session's own token. */
async function audit(page: Page, query: string) {
  const token = await page.evaluate(() => localStorage.getItem("access_token"));
  const res = await page.request.get(`${API}/audit?${query}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(res.ok()).toBeTruthy();
  return (await res.json()) as {
    totalCount: number;
    result: {
      entityName: string;
      entityKey: string;
      state: string;
      correlationId: string;
      changes: Record<string, { old: unknown; new: unknown }>;
    }[];
  };
}

/** The History panel on an entity page, and the rows inside it. */
const historyPanel = (page: Page) =>
  page.locator("section").filter({ has: page.getByRole("heading", { name: "History", level: 2 }) });

test.describe("audit trail", () => {
  test.beforeEach(async ({ page }) => {
    await signInAsAdmin(page);
  });

  test("creating, renaming and deleting a partner are each recorded", async ({ page }) => {
    const name = `Playwright Audit ${Date.now()}`;
    const renamed = `${name} Renamed`;

    await page.goto("partners");
    await page.getByRole("button", { name: "New partner" }).click();
    const dialog = page.getByRole("dialog", { name: "New partner" });
    await dialog.getByRole("textbox", { name: "Name" }).fill(name);
    await dialog.getByRole("button", { name: "Create partner" }).click();

    await expect(page).toHaveURL(/\/partners\/\d+$/);
    const id = page.url().split("/").pop()!;

    // — the create shows on the partner's own page —
    const panel = historyPanel(page);
    await expect(panel).toBeVisible();
    await expect(panel.getByRole("row").filter({ hasText: "Added" })).toBeVisible();

    // — renaming records both sides of the change —
    await page.getByRole("textbox", { name: "Name", exact: true }).fill(renamed);
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

    // Deliberately no reload: the card sits on the same page as the form that just wrote to it,
    // so it has to catch up on its own. It used to need a manual refresh.
    await expect(panel.getByRole("row").filter({ hasText: "Modified" })).toBeVisible();

    // The before/after values are reachable without a mouse — they used to be in a title,
    // which keyboard and touch users can't get at.
    await panel.getByRole("button", { name: /Show what changed/ }).first().click();
    await expect(page.getByText(`"${name}"`, { exact: false }).first()).toBeVisible();
    await expect(page.getByText(`"${renamed}"`, { exact: false }).first()).toBeVisible();
    await page.keyboard.press("Escape");

    const afterRename = await audit(page, `entityName=Partner&entityKey=${id}`);
    const modified = afterRename.result.find((r) => r.state === "Modified");
    expect(modified?.changes.Name).toEqual({ old: name, new: renamed });

    // — the deletion is recorded, which the trails this replaced never did —
    await page.getByRole("button", { name: "Delete partner" }).first().click();
    await page.getByRole("button", { name: "Delete partner" }).last().click();
    await expect(page).toHaveURL(/\/partners$/);

    const afterDelete = await audit(page, `entityName=Partner&entityKey=${id}`);
    const deleted = afterDelete.result.find((r) => r.state === "Deleted");
    expect(deleted, "a delete must leave a record").toBeTruthy();
    // A delete records what the row held, so the trail can still answer what was lost.
    expect(deleted!.changes.Name.old).toBe(renamed);
    expect(deleted!.changes.Name.new).toBeNull();
  });

  test("without audit.view there is no nav item, no page, and no card", async ({ page }) => {
    // Everything the role needs to reach the pages the card sits on — but not the trail.
    const role = await createRole(page, {
      name: `PW No Audit ${Date.now()}`,
      permissions: [
        { area: "Partners", action: "View" },
        { area: "Settings", action: "View" },
      ],
    });
    const email = await addMember(page, { name: "Playwright NoAudit", roles: [role] });
    await signOut(page);
    await signIn(page, email, FIRST_PASSWORD);

    await expect(page.getByRole("navigation").getByRole("link", { name: "Audit trail" })).toHaveCount(0);

    // Typing the URL must not work either — hiding a link is not a permission system.
    await page.goto("audit");
    await expect(page.getByRole("heading", { name: "Audit trail" })).toHaveCount(0);

    // And the API behind it refuses, which is the check that actually matters.
    const token = await page.evaluate(() => localStorage.getItem("access_token"));
    const res = await page.request.get(`${API}/audit?limit=1`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.status()).toBe(401);

    // The card is hidden on a page this role can otherwise see in full.
    await page.goto("settings");
    await expect(page.getByRole("heading", { name: "Settings", level: 1 })).toBeVisible();
    await expect(historyPanel(page)).toHaveCount(0);

    await signOut(page);
    await signInAsAdmin(page);
    await removeMember(page, email);
    await deleteRole(page, role);
  });
});
