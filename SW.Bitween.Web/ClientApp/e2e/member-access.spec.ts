import { test, expect, type Page } from "@playwright/test";
import { AdminApi } from "./api";
import {
  ADMIN_EMAIL,
  API,
  FIRST_PASSWORD,
  ROTATED_PASSWORD,
  addMember,
  createRole,
  deleteRole,
  openMember,
  removeMember,
  sessionToken,
  signIn,
  signInAsAdmin,
  signOut,
  startsWith,
} from "./helpers";

/**
 * What an administrator does to a member after adding them — change their roles, disable them,
 * unlock them — and the guards around it: the last administrator can't be demoted, and a member
 * who manages members can't hand out more than they hold. Every change is followed to its effect:
 * the member signing in (or not), and the server's own record of their roles.
 */

const drawer = (page: Page) => page.getByRole("dialog", { name: "Member details" });

/** Submits the login form and leaves the page wherever that lands. */
async function trySignIn(page: Page, email: string, password: string) {
  await page.goto("login");
  await page.fill("#login-email", email);
  await page.fill("#login-password", password);
  await page.getByRole("button", { name: "Sign in" }).click();
}

const sidebarLink = (page: Page, text: string) =>
  page.getByRole("navigation").getByRole("link").filter({ hasText: text });

async function roleNamesOf(api: AdminApi, email: string) {
  return ((await api.account(email)).roles ?? []).map((r) => r.name).sort();
}

test("a custom role given to an existing member in the drawer lets them create a partner, but not delete it or open pages outside the role", async ({
  page,
  request,
}) => {
  const roleName = `PW Partner Clerk ${Date.now()}`;
  await signInAsAdmin(page);
  await createRole(page, {
    name: roleName,
    permissions: [
      { area: "Partners", action: "View" },
      { area: "Partners", action: "Create" },
    ],
  });
  // They start with Viewer, which can read partners but not create them.
  const email = await addMember(page, { name: "Becomes Clerk", roles: ["Viewer"] });

  await openMember(page, email);
  await drawer(page).getByRole("checkbox", { name: startsWith("Viewer") }).uncheck();
  await drawer(page).getByRole("checkbox", { name: startsWith(roleName) }).check();
  await drawer(page).getByRole("button", { name: "Save roles" }).click();
  await expect(drawer(page).getByRole("button", { name: "Save roles" })).toHaveCount(0);

  // The list says so, and so does the server.
  await page.keyboard.press("Escape");
  const row = page.getByRole("row", { name: new RegExp(email) });
  await expect(row).toContainText(roleName);
  await expect(row).not.toContainText("Viewer");
  const api = await AdminApi.signIn(request);
  expect(await roleNamesOf(api, email)).toEqual([roleName]);

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);

  // Partners: they can open the list and create one.
  await expect(sidebarLink(page, "Partners")).toBeVisible();
  // Viewer's pages are gone with the role.
  for (const hidden of ["Exchanges", "Subscriptions", "Team"])
    await expect(sidebarLink(page, hidden)).toHaveCount(0);
  await page.goto("subscriptions");
  await expect(page.getByText("You don't have access to this page")).toBeVisible();

  await page.goto("partners");
  await page.getByRole("button", { name: "New partner" }).first().click();
  const partnerName = `Playwright Clerk Partner ${Date.now()}`;
  const dialog = page.getByRole("dialog", { name: "New partner" });
  await dialog.locator("#pf-name").fill(partnerName);
  await dialog.getByRole("button", { name: "Create partner" }).click();
  await expect(page).toHaveURL(/\/partners\/\d+$/);
  await expect(page.getByText(partnerName).first()).toBeVisible();

  // But not delete it: no button, and the API refuses the request the button would have sent.
  const partnerId = await api.idOf("partners", partnerName);
  await page.goto(`partners/${partnerId}`);
  await expect(page.getByRole("heading", { level: 1 })).toContainText(partnerName);
  await expect(page.getByRole("button", { name: "Delete partner" })).toHaveCount(0);
  const token = await sessionToken(page);
  const del = await page.request.delete(`${API}/partners/${partnerId}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(del.status()).toBe(403);

  await signOut(page);
  await signInAsAdmin(page);
  await api.delete(`/partners/${partnerId}`);
  await removeMember(page, email);
  await deleteRole(page, roleName);
});

test("a role with Create but not Edit can create an information type and a work group from their New dialogs and open what it made", async ({
  page,
  request,
}) => {
  const roleName = `PW Creator ${Date.now()}`;
  await signInAsAdmin(page);
  await createRole(page, {
    name: roleName,
    permissions: [
      { area: "Information types", action: "View" },
      { area: "Information types", action: "Create" },
      { area: "Work groups", action: "View" },
      { area: "Work groups", action: "Create" },
    ],
  });
  const email = await addMember(page, { name: "Create Only", roles: [roleName] });
  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);

  const typeName = `Playwright Created Type ${Date.now()}`;
  await page.goto("information-types");
  await page.getByRole("button", { name: "New information type" }).first().click();
  const typeDialog = page.getByRole("dialog", { name: "New information type" });
  await typeDialog.getByRole("textbox", { name: "Name" }).fill(typeName);
  await typeDialog.getByRole("button", { name: "Create information type" }).click();
  await expect(page).toHaveURL(/\/information-types\/\d+$/);
  await expect(page.getByRole("heading", { name: typeName })).toBeVisible();

  const groupName = `Playwright Created Group ${Date.now()}`;
  await page.goto("work-groups");
  await page.getByRole("button", { name: "New work group" }).first().click();
  const groupDialog = page.getByRole("dialog", { name: "New work group" });
  await groupDialog.getByRole("textbox", { name: "Name", exact: true }).fill(groupName);
  await groupDialog.getByRole("button", { name: "Create work group" }).click();
  await expect(page).toHaveURL(/\/work-groups\/\d+$/, { timeout: 15000 });
  await expect(page.getByRole("heading", { name: groupName })).toBeVisible();

  const api = await AdminApi.signIn(request);
  const typeId = await api.idOf("documents", typeName);
  const groupId = await api.idOf("workgroups", groupName);

  await signOut(page);
  await signInAsAdmin(page);
  await api.delete(`/documents/${typeId}`);
  await api.post(`/workgroups/${groupId}/delete`);
  await removeMember(page, email);
  await deleteRole(page, roleName);
});

test("changing a member's roles in the drawer replaces them: the new role's pages open, the old one's close", async ({
  page,
  request,
}) => {
  await signInAsAdmin(page);
  const email = await addMember(page, { name: "Role Swap", roles: ["Viewer"] });

  // Viewer reads configuration but has no Team pages; Member runs integrations but no Team
  // either — so the difference to look for is write access, e.g. "New partner".
  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);
  await page.goto("partners");
  await expect(page.getByRole("heading", { name: "Partners" })).toBeVisible();
  await expect(page.getByRole("button", { name: "New partner" })).toHaveCount(0);

  await signOut(page);
  await signInAsAdmin(page);
  await openMember(page, email);
  await drawer(page).getByRole("checkbox", { name: startsWith("Viewer") }).uncheck();
  await drawer(page).getByRole("checkbox", { name: startsWith("Member") }).check();
  await drawer(page).getByRole("button", { name: "Save roles" }).click();
  await expect(drawer(page).getByRole("button", { name: "Save roles" })).toHaveCount(0);

  // Reopened from scratch, the drawer shows what the server now holds.
  await page.reload();
  await expect(drawer(page).getByRole("checkbox", { name: startsWith("Member") })).toBeChecked();
  await expect(drawer(page).getByRole("checkbox", { name: startsWith("Viewer") })).not.toBeChecked();
  const api = await AdminApi.signIn(request);
  expect(await roleNamesOf(api, email)).toEqual(["Member"]);

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);
  await page.goto("partners");
  await expect(page.getByRole("button", { name: "New partner" })).toBeVisible();

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
});

test("a disabled member can't sign in, loses the session they had open, and can again once re-enabled", async ({
  page,
  browser,
}) => {
  await signInAsAdmin(page);
  const email = await addMember(page, { name: "Gets Disabled", roles: ["Viewer"] });

  // The member is signed in elsewhere when it happens.
  const theirs = await browser.newContext({ ignoreHTTPSErrors: true });
  const theirPage = await theirs.newPage();
  await signIn(theirPage, email, FIRST_PASSWORD);

  await openMember(page, email);
  await drawer(page).getByRole("button", { name: "Disable account" }).click();
  await expect(drawer(page).getByRole("button", { name: "Re-enable account" })).toBeVisible();
  await expect(drawer(page).getByText("Disabled", { exact: true })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("row", { name: new RegExp(email) })).toContainText("Disabled");

  // Their open session ends: a reload has to renew the Jwt, and the refresh token is gone.
  await theirPage.reload();
  await theirPage.waitForURL((url) => url.pathname.endsWith("/login"), { timeout: 15000 });
  await theirs.close();

  // And signing in again is refused, saying why.
  await signOut(page);
  await trySignIn(page, email, FIRST_PASSWORD);
  await expect(page.getByRole("alert")).toContainText("Your account has been disabled");
  await expect(page).toHaveURL(/\/login$/);

  await signInAsAdmin(page);
  await openMember(page, email);
  await drawer(page).getByRole("button", { name: "Re-enable account" }).click();
  await expect(drawer(page).getByRole("button", { name: "Disable account" })).toBeVisible();

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);
  await expect(page.getByRole("button", { name: "Account menu" })).toContainText("Gets Disabled");

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
});

test("a member locked by wrong passwords from many addresses shows as Locked, is refused even with the right password, and signs in once unlocked", async ({
  page,
  request,
}) => {
  await signInAsAdmin(page);
  const email = await addMember(page, { name: "Guess Target", roles: ["Viewer"] });

  // The account-wide lock comes after 20 wrong passwords (Login.MaxFailedLoginAttempts). From a
  // single address the per-address throttle stops counting after 5, so this is guessing spread
  // over twenty addresses — what the account-wide lock is there for. The app trusts the nearest
  // proxy's X-Forwarded-For, which is how each attempt arrives from its own address.
  for (let i = 0; i < 20; i++) {
    const res = await request.post(`${API}/accounts/login`, {
      headers: { "X-Forwarded-For": `198.51.100.${i + 1}` },
      data: { Username: email, Password: `wrong-${i}` },
    });
    expect(res.ok()).toBe(false);
  }

  await page.goto("team/members");
  const status = page.getByRole("row", { name: new RegExp(email) }).getByRole("cell").nth(2);
  await expect(status).toHaveText("Locked");

  await signOut(page);
  await trySignIn(page, email, FIRST_PASSWORD);
  await expect(page.getByRole("alert")).toContainText("temporarily locked");
  await expect(page).toHaveURL(/\/login$/);

  await signInAsAdmin(page);
  await openMember(page, email);
  await drawer(page).getByRole("button", { name: "Unlock account" }).click();
  await expect(drawer(page).getByRole("button", { name: "Unlock account" })).toHaveCount(0);
  await expect(drawer(page).getByText("Active", { exact: true })).toBeVisible();

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);
  await expect(page.getByRole("button", { name: "Account menu" })).toContainText("Guess Target");

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
});

test("the only administrator can't take Administrator off themselves: the drawer says why and the role stays", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  // The guard only bites when nobody else holds the role. The seed removes the suite's own
  // leftovers, so anyone else here is a real administrator and the premise doesn't hold.
  const accounts = await api.get<{ result: { email: string; disabled: boolean; roles: { id: number }[] | null }[] }>(
    "/accounts?limit=500",
  );
  const otherAdmins = accounts.result.filter(
    (a) => a.email.toLowerCase() !== ADMIN_EMAIL.toLowerCase() && !a.disabled && (a.roles ?? []).some((r) => r.id === 1),
  );
  expect(otherAdmins.map((a) => a.email), "another enabled administrator exists on this database").toEqual([]);

  await signInAsAdmin(page);
  await openMember(page, ADMIN_EMAIL);
  await expect(drawer(page).getByText("You", { exact: true })).toBeVisible();
  await drawer(page).getByRole("checkbox", { name: startsWith("Administrator") }).uncheck();
  await drawer(page).getByRole("checkbox", { name: startsWith("Viewer") }).check();
  await drawer(page).getByRole("button", { name: "Save roles" }).click();

  await expect(drawer(page).getByRole("alert")).toContainText(
    "This is the only member with the Administrator role. Give it to someone else first.",
  );
  expect(await roleNamesOf(api, ADMIN_EMAIL)).toEqual(["Administrator"]);

  // Nothing was half-applied either: after a reload the drawer still shows Administrator alone.
  await page.reload();
  await expect(drawer(page).getByRole("checkbox", { name: startsWith("Administrator") })).toBeChecked();
  await expect(drawer(page).getByRole("checkbox", { name: startsWith("Viewer") })).not.toBeChecked();
});

test("a member who can edit members but isn't an administrator can't grant Administrator, to themselves or a peer", async ({
  page,
  request,
}) => {
  const roleName = `PW Member Manager ${Date.now()}`;
  await signInAsAdmin(page);
  await createRole(page, {
    name: roleName,
    permissions: [
      { area: "Members", action: "View" },
      { area: "Members", action: "Edit" },
      // The drawer lists the roles to pick from, which reading them needs.
      { area: "Roles", action: "View" },
    ],
  });
  const manager = await addMember(page, { name: "Member Manager", roles: [roleName] });
  // A peer holding no more than the manager does, so the manager is allowed to edit them at all.
  const peer = await addMember(page, { name: "Managed Peer", roles: [roleName] });

  await signOut(page);
  await signIn(page, manager, FIRST_PASSWORD);

  for (const target of [peer, manager]) {
    await openMember(page, target);
    await drawer(page).getByRole("checkbox", { name: startsWith("Administrator") }).check();
    await drawer(page).getByRole("button", { name: "Save roles" }).click();
    await expect(drawer(page).getByRole("alert")).toContainText("You can only grant permissions you hold yourself");
    await page.keyboard.press("Escape");
    await drawer(page).waitFor({ state: "detached" });
  }

  const api = await AdminApi.signIn(request);
  expect(await roleNamesOf(api, peer)).toEqual([roleName]);
  expect(await roleNamesOf(api, manager)).toEqual([roleName]);

  // What they hold themselves they can still hand out: the guard is about the excess, not the act.
  await openMember(page, peer);
  await drawer(page).getByRole("checkbox", { name: startsWith(roleName) }).uncheck();
  await drawer(page).getByRole("button", { name: "Save roles" }).click();
  await expect(drawer(page).getByRole("button", { name: "Save roles" })).toHaveCount(0);
  expect(await roleNamesOf(api, peer)).toEqual([]);

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, peer);
  await removeMember(page, manager);
  await deleteRole(page, roleName);
});

test("changing your own password on the profile page: the old one stops working, the new one signs in", async ({
  page,
}) => {
  await signInAsAdmin(page);
  const email = await addMember(page, { name: "Own Password", roles: ["Viewer"] });

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);
  await page.goto("profile");
  await page.fill("#pf-current", FIRST_PASSWORD);
  await page.fill("#pf-new", ROTATED_PASSWORD);
  await page.fill("#pf-confirm", ROTATED_PASSWORD);
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByText("Saved", { exact: true })).toBeVisible();

  await signOut(page);
  await trySignIn(page, email, FIRST_PASSWORD);
  await expect(page.getByRole("alert")).toContainText("Invalid username or password");
  await expect(page).toHaveURL(/\/login$/);

  await signIn(page, email, ROTATED_PASSWORD);
  await expect(page.getByRole("button", { name: "Account menu" })).toContainText("Own Password");

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
});

test("the profile page refuses a password change when the current password is wrong, and the old one keeps working", async ({
  page,
}) => {
  await signInAsAdmin(page);
  const email = await addMember(page, { name: "Wrong Current", roles: ["Viewer"] });

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);
  await page.goto("profile");
  await page.fill("#pf-current", "not-my-password-1A!");
  await page.fill("#pf-new", ROTATED_PASSWORD);
  await page.fill("#pf-confirm", ROTATED_PASSWORD);
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByRole("alert")).toBeVisible();
  await expect(page.getByText("Saved", { exact: true })).toHaveCount(0);

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);
  await expect(page.getByRole("button", { name: "Account menu" })).toContainText("Wrong Current");

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
});
