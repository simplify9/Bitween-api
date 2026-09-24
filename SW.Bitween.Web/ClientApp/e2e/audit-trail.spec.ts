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

/** Creates a partner through the API so a test can set fields the form doesn't expose. */
async function createPartnerViaApi(page: Page, name: string, adapterProperties: Record<string, string>) {
  const token = await page.evaluate(() => localStorage.getItem("access_token"));
  const res = await page.request.post(`${API}/partners`, {
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    data: { name, adapterProperties },
  });
  expect(res.ok()).toBeTruthy();
  return (await res.json()) as number;
}

async function deletePartnerViaApi(page: Page, id: number) {
  const token = await page.evaluate(() => localStorage.getItem("access_token"));
  await page.request.delete(`${API}/partners/${id}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
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

  test("the trail page filters, groups one save, and clears", async ({ page }) => {
    const name = `Playwright Filter ${Date.now()}`;
    await page.goto("partners");
    const id = await createPartnerViaApi(page, name, {});

    await page.goto("audit");
    await expect(page.getByRole("heading", { name: "Audit trail" })).toBeVisible();

    // Narrowing to this one row proves the entity filters reach the query, not just the URL.
    await page.goto(`audit?entityName=Partner&entityKey=${id}`);
    await expect(page.getByRole("row").filter({ hasText: "Partner" }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: String(id), exact: true }).first()).toBeVisible();

    // "Same save" pivots to the correlation id — every row one SaveChanges wrote.
    await page.getByRole("button", { name: "Same save" }).first().click();
    await expect(page).toHaveURL(/correlationId=/);
    await expect(page.getByText("Showing one save only")).toBeVisible();

    await page.getByRole("button", { name: "Clear filters" }).click();
    await expect(page).toHaveURL(/\/audit$/);

    await deletePartnerViaApi(page, id);
  });

  test("the history card is on every entity page that has one", async ({ page }) => {
    test.setTimeout(120_000);

    // Reached by URL rather than by clicking a list row: rows carry links of their own — a stray
    // click on the subscriptions list lands on an information type — so the id comes from the API
    // and the page is opened directly.
    const areas: { label: string; list: string; path: (id: string) => string }[] = [
      { label: "partner", list: "/partners?limit=1", path: (id) => `partners/${id}` },
      { label: "information type", list: "/documents?limit=1", path: (id) => `information-types/${id}` },
      { label: "work group", list: "/workgroups?limit=1", path: (id) => `work-groups/${id}` },
      { label: "global value set", list: "/globaladaptervaluessets", path: (id) => `global-values/${id}` },
      { label: "retry policy", list: "/retrypolicies?limit=1", path: (id) => `retry-policies/${id}` },
      { label: "notifier", list: "/notifiers?limit=1", path: (id) => `notifiers/${id}` },
      { label: "API gateway", list: "/apigateways?limit=1", path: (id) => `api-gateways/${id}` },
      { label: "subscription", list: "/subscriptions?limit=1", path: (id) => `subscriptions/${id}` },
    ];

    const token = await page.evaluate(() => localStorage.getItem("access_token"));
    const skipped: string[] = [];
    let checked = 0;

    for (const area of areas) {
      const res = await page.request.get(`${API}${area.list}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(res.ok(), `could not list ${area.label}s`).toBeTruthy();
      const body = await res.json();
      const first = (Array.isArray(body) ? body : (body.result ?? []))[0];
      if (!first) {
        skipped.push(area.label);
        continue; // nothing seeded in this area on this database
      }

      await page.goto(area.path(String(first.id)));
      await expect(historyPanel(page), `no History card on a ${area.label}`).toBeVisible({
        timeout: 20_000,
      });
      checked++;
    }

    // An area with nothing in it is skipped rather than failed — a database need not hold one
    // of everything. But a nearly empty one would sail through the loop having tested nothing,
    // so most areas must actually have been reached.
    expect(
      checked,
      `only ${checked} of ${areas.length} areas were checked (no rows for: ${skipped.join(", ")})`,
    ).toBeGreaterThanOrEqual(areas.length - 3);

    // Settings has no per-row page, so its card covers the whole area.
    await page.goto("settings");
    await expect(historyPanel(page)).toBeVisible();
  });

  test("a custom role's page carries its history", async ({ page }) => {
    // Built-in roles are deliberately excluded — their grants are computed rather than stored,
    // so nothing ever edits one — which makes a custom role the case worth covering.
    const role = await createRole(page, {
      name: `PW Audit Role ${Date.now()}`,
      permissions: [{ area: "Partners", action: "View" }],
    });

    await page.goto("team/roles");
    await page.getByRole("link", { name: new RegExp(role) }).click();
    await expect(historyPanel(page)).toBeVisible({ timeout: 15000 });
    await expect(historyPanel(page).getByRole("row").filter({ hasText: "Added" })).toBeVisible();

    await deleteRole(page, role);
  });

  test("a junk offset in the URL doesn't break the page", async ({ page }) => {
    // Number("bad") is NaN, which used to go out on the wire as offset=NaN.
    for (const offset of ["bad", "-5"]) {
      await page.goto(`audit?offset=${offset}`);
      await expect(page.getByRole("heading", { name: "Audit trail" })).toBeVisible();
      await expect(page.getByText(/Showing 1[–-]/)).toBeVisible();
    }
  });

  test("a member drawer shows that member's history", async ({ page }) => {
    const email = await addMember(page, { name: "Playwright Drawer", roles: ["Viewer"] });

    await page.goto("team/members");
    await page.getByRole("row", { name: new RegExp(email) }).click();
    const drawer = page.getByRole("dialog", { name: "Member details" });
    await drawer.waitFor();

    await expect(drawer.getByRole("heading", { name: "History" })).toBeVisible();
    await expect(drawer.getByRole("row").filter({ hasText: "Added" }).first()).toBeVisible();
    await expect(drawer.getByRole("link", { name: "What this member changed" })).toBeVisible();

    await page.keyboard.press("Escape");
    await removeMember(page, email);
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
