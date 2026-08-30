import { test, expect, type Page } from "@playwright/test";
import {
  FIRST_PASSWORD,
  addMember,
  createRole,
  deleteRole,
  removeMember,
  signIn,
  openMember,
  signInAsAdmin,
  signOut,
  startsWith,
} from "./helpers";

/**
 * What a role grants has to hold in three places at once: the pages offered in the sidebar, the
 * page reached by typing its URL, and the API behind the button. A permission system that only
 * hides UI isn't one, so every check here ends at the server.
 */

const sidebarLinks = (page: Page) =>
  page.getByRole("navigation").getByRole("link").filter({ hasNotText: /^$/ });

/** Calls the API directly with the signed-in member's own token — no UI in the way. */
async function apiStatus(
  page: Page,
  method: "GET" | "POST",
  path: string,
  body?: unknown,
): Promise<number> {
  const token = await page.evaluate(() => localStorage.getItem("access_token"));
  const res = await page.request.fetch(`https://localhost:7155/api${path}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    ...(body === undefined ? {} : { data: body }),
  });
  return res.status();
}

test("a custom role grants exactly what was ticked, in the nav, by URL, and at the API", async ({
  page,
}) => {
  const roleName = `PW Exchange Watcher ${Date.now()}`;
  await signInAsAdmin(page);
  await createRole(page, {
    name: roleName,
    permissions: [{ area: "Exchanges", action: "View" }],
  });
  const email = await addMember(page, { name: "Watcher Only", roles: [roleName] });

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);

  // 1. The sidebar offers the one page they can see, and nothing else.
  await expect(sidebarLinks(page).filter({ hasText: "Exchanges" })).toBeVisible();
  for (const hidden of ["Partners", "Subscriptions", "Work groups", "Team", "Settings"])
    await expect(sidebarLinks(page).filter({ hasText: hidden })).toHaveCount(0);

  // 2. Typing the URL of a page they lack doesn't get them in.
  await page.goto("partners");
  await expect(page.getByText("You don't have access to this page")).toBeVisible();

  await page.goto("team/members");
  await expect(page.getByText("You don't have access to this page")).toBeVisible();

  // 3. The page they do have loads, and offers no write actions.
  await page.goto("exchanges");
  await expect(page.getByText("You don't have access to this page")).toHaveCount(0);
  await expect(page.getByRole("button", { name: /^Retry/ })).toHaveCount(0);

  // 4. And the API refuses the same things, so a crafted request gains nothing.
  expect(await apiStatus(page, "POST", "/partners", { name: "sneaky" })).toBe(401);
  expect(await apiStatus(page, "GET", "/accounts?limit=5")).toBe(401);
  expect(await apiStatus(page, "POST", "/roles", { name: "x", description: "", permissions: [] })).toBe(401);

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
  await deleteRole(page, roleName);
});

test("Viewer can read but not write", async ({ page }) => {
  await signInAsAdmin(page);
  const email = await addMember(page, { name: "Read Only", roles: ["Viewer"] });

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);

  // Reading the configuration pages is fine.
  await page.goto("partners");
  await expect(page.getByText("You don't have access to this page")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "New partner" })).toHaveCount(0);

  await page.goto("retry-policies");
  await expect(page.getByText("You don't have access to this page")).toHaveCount(0);
  await expect(page.getByRole("button", { name: /^New retry policy/ })).toHaveCount(0);

  // Administration is out of reach entirely.
  await expect(sidebarLinks(page).filter({ hasText: "Team" })).toHaveCount(0);
  await page.goto("settings");
  await expect(page.getByText("You don't have access to this page")).toBeVisible();

  // Writes are refused at the source, not just hidden.
  expect(await apiStatus(page, "POST", "/partners", { name: "nope" })).toBe(401);
  expect(await apiStatus(page, "POST", "/retrypolicies", { name: "nope" })).toBe(401);
  // Work groups had no guard of any kind until the handlers were given one.
  expect(await apiStatus(page, "POST", "/workgroups", { name: "nope", busMessageName: "nope" })).toBe(401);

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
});

test("Member can configure subscriptions but not manage the team", async ({ page }) => {
  await signInAsAdmin(page);
  const email = await addMember(page, { name: "Regular Member", roles: ["Member"] });

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);

  await page.goto("partners");
  await expect(page.getByRole("button", { name: "New partner" })).toBeVisible();

  // The whole Administration group is absent for a Member.
  await expect(sidebarLinks(page).filter({ hasText: "Team" })).toHaveCount(0);
  await expect(sidebarLinks(page).filter({ hasText: "Settings" })).toHaveCount(0);

  expect(await apiStatus(page, "GET", "/accounts?limit=5")).toBe(401);
  expect(await apiStatus(page, "POST", "/roles", { name: "x", description: "", permissions: [] })).toBe(401);

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
});

test("editing a role changes what its members can do, without them signing in again", async ({
  page,
}) => {
  const roleName = `PW Growing ${Date.now()}`;
  await signInAsAdmin(page);
  await createRole(page, { name: roleName, permissions: [{ area: "Exchanges", action: "View" }] });
  const email = await addMember(page, { name: "Gains Access", roles: [roleName] });

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);
  await expect(sidebarLinks(page).filter({ hasText: "Partners" })).toHaveCount(0);

  // Grant Partners while they're signed in. Permissions are resolved per request from the
  // database rather than baked into the token, so this must take effect without a new login.
  const admin = await page.context().browser()!.newContext({ ignoreHTTPSErrors: true });
  const adminPage = await admin.newPage();
  await signInAsAdmin(adminPage);
  await adminPage.goto("team/roles");
  await adminPage.getByRole("link", { name: new RegExp(roleName) }).click();
  await adminPage.getByRole("checkbox", { name: "Partners: View", exact: true }).check();
  await adminPage.getByRole("button", { name: "Save changes" }).click();
  await adminPage.waitForURL(/\/team\/roles$/);

  await page.reload();
  await expect(sidebarLinks(page).filter({ hasText: "Partners" })).toBeVisible();
  await page.goto("partners");
  await expect(page.getByText("You don't have access to this page")).toHaveCount(0);

  await admin.close();
  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
  await deleteRole(page, roleName);
});

test("a member with no roles at all sees nothing and can do nothing", async ({ page }) => {
  await signInAsAdmin(page);
  // A member cannot be created without a role — the server refuses an empty one — so the
  // roleless state is reached the only way it can be: by taking their one role away after.
  const email = await addMember(page, { name: "No Roles", roles: ["Viewer"] });
  await openMember(page, email);
  const drawer = page.getByRole("dialog", { name: "Member details" });
  await drawer.getByRole("checkbox", { name: startsWith("Viewer") }).uncheck();
  await drawer.getByRole("button", { name: "Save roles" }).click();
  await expect(drawer.getByRole("button", { name: "Save roles" })).toHaveCount(0);

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);

  await expect(sidebarLinks(page)).toHaveCount(0);
  for (const path of ["exchanges", "partners", "team/members", "settings"]) {
    await page.goto(path);
    await expect(page.getByText("You don't have access to this page")).toBeVisible();
  }
  expect(await apiStatus(page, "POST", "/partners", { name: "nope" })).toBe(401);

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
});
