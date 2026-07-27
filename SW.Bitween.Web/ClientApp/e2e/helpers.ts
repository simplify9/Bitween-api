import type { Page } from "@playwright/test";

/** Checkbox labels carry their description in the accessible name, so anchor at the start. */
export const startsWith = (text: string) =>
  new RegExp("^" + text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"));

export const ADMIN_EMAIL = "admin@Bitween.systems";
export const ADMIN_PASSWORD = "Mtm@dmin!2";

/** Passwords the members these tests create are given. Both clear the 8-character minimum. */
export const FIRST_PASSWORD = "Pl4ywright!1";
export const ROTATED_PASSWORD = "R0tated!Pass2";

export async function signIn(page: Page, email: string, password: string) {
  await page.goto("login");
  await page.fill("#login-email", email);
  await page.fill("#login-password", password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL((url) => !url.pathname.endsWith("/login"), { timeout: 15000 });
}

export const signInAsAdmin = (page: Page) => signIn(page, ADMIN_EMAIL, ADMIN_PASSWORD);

export async function signOut(page: Page) {
  // An open drawer lays a backdrop over the sidebar, which would swallow the click.
  const drawer = page.getByRole("dialog", { name: "Member details" });
  if (await drawer.count()) {
    await page.keyboard.press("Escape");
    await drawer.waitFor({ state: "detached" });
  }
  await page.getByRole("button", { name: "Account menu" }).click();
  await page.getByRole("button", { name: "Sign out" }).click();
  await page.waitForURL((url) => url.pathname.endsWith("/login"), { timeout: 15000 });
}

/**
 * Adds a member through the UI and returns their email. Unique per run so the suite can be
 * re-run against the same database without colliding on the unique email index.
 */
export async function addMember(
  page: Page,
  { name, roles, password = FIRST_PASSWORD }: { name: string; roles: string[]; password?: string },
) {
  const email = `pw-${name.toLowerCase().replace(/\W+/g, "-")}-${Date.now()}@example.test`;

  await page.goto("team/members");
  await page.getByRole("button", { name: "Add member" }).click();
  await page.fill("#member-name", name);
  await page.fill("#member-email", email);
  await page.fill("#member-password", password);
  for (const role of roles) await page.getByRole("checkbox", { name: startsWith(role) }).check();
  await page.getByRole("button", { name: "Add member" }).last().click();

  await page.waitForSelector(`text=${email}`, { timeout: 15000 });
  return email;
}

/** Creates a role holding exactly the given permissions. Returns its name. */
export async function createRole(
  page: Page,
  { name, permissions }: { name: string; permissions: { area: string; action: string }[] },
) {
  await page.goto("team/roles/new");
  await page.fill("#role-name", name);
  await page.fill("#role-desc", "Created by the Playwright suite.");
  for (const { area, action } of permissions)
    await page.getByRole("checkbox", { name: `${area}: ${action}`, exact: true }).check();
  await page.getByRole("button", { name: "Create role" }).click();
  await page.waitForURL(/\/team\/roles$/, { timeout: 15000 });
  return name;
}

/** Opens a member's drawer from the members list. */
export async function openMember(page: Page, email: string) {
  await page.goto("team/members");
  await page.getByRole("row", { name: new RegExp(email) }).click();
  await page.getByRole("dialog", { name: "Member details" }).waitFor();
}

export async function removeMember(page: Page, email: string) {
  await openMember(page, email);
  await page.getByRole("button", { name: "Remove from team" }).click();
  await page.getByRole("button", { name: "Remove member" }).click();
  await page.getByRole("dialog", { name: "Member details" }).waitFor({ state: "detached" });
}

export async function deleteRole(page: Page, name: string) {
  await page.goto("team/roles");
  await page.getByRole("link", { name: new RegExp(name) }).click();
  await page.getByRole("button", { name: "Delete role" }).click();
  await page.getByRole("button", { name: "Delete role" }).last().click();
  await page.waitForURL(/\/team\/roles$/, { timeout: 15000 });
}
