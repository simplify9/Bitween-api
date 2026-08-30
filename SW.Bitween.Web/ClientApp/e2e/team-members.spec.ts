import { test, expect } from "@playwright/test";
import {
  ADMIN_EMAIL,
  startsWith,
  FIRST_PASSWORD,
  ROTATED_PASSWORD,
  addMember,
  openMember,
  removeMember,
  signIn,
  signInAsAdmin,
  signOut,
} from "./helpers";

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("add a member, they can sign in, then remove them", async ({ page }) => {
  const email = await addMember(page, { name: "New Joiner", roles: ["Viewer"] });

  const row = page.getByRole("row", { name: new RegExp(email) });
  await expect(row).toBeVisible();
  await expect(row).toContainText("Viewer");
  await expect(row).toContainText("Active");

  // The account is real from the first moment — no accept step in between.
  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);
  await expect(page.getByRole("button", { name: "Account menu" })).toContainText("New Joiner");

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
  await expect(page.getByText(email)).toHaveCount(0);
});

test("change which roles a member holds", async ({ page }) => {
  const email = await addMember(page, { name: "Role Swap", roles: ["Viewer"] });

  await openMember(page, email);
  const drawer = page.getByRole("dialog", { name: "Member details" });
  await drawer.getByRole("checkbox", { name: startsWith("Viewer") }).uncheck();
  await drawer.getByRole("checkbox", { name: startsWith("Member") }).check();
  await drawer.getByRole("button", { name: "Save roles" }).click();
  await expect(drawer.getByRole("button", { name: "Save roles" })).toHaveCount(0);

  // Reload rather than trust the optimistic UI — proves the write reached the database.
  await page.reload();
  const row = page.getByRole("row", { name: new RegExp(email) });
  await expect(row).toContainText("Member");
  await expect(row).not.toContainText("Viewer");

  await removeMember(page, email);
});

test("an administrator resets a member's password", async ({ page }) => {
  const email = await addMember(page, { name: "Forgot Pass", roles: ["Viewer"] });

  await openMember(page, email);
  const drawer = page.getByRole("dialog", { name: "Member details" });
  await drawer.getByLabel("New password").fill(ROTATED_PASSWORD);
  await drawer.getByRole("button", { name: "Set password" }).click();
  // The form is replaced by a confirmation carrying the new password to copy: Bitween sends
  // no email, so this is the only place it is ever shown.
  await expect(drawer.getByText(/Password set for/)).toBeVisible();

  await signOut(page);
  await signIn(page, email, ROTATED_PASSWORD);
  await expect(page.getByRole("button", { name: "Account menu" })).toContainText("Forgot Pass");

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
});

test("the old password stops working after a reset", async ({ page }) => {
  const email = await addMember(page, { name: "Stale Pass", roles: ["Viewer"] });

  await openMember(page, email);
  const drawer = page.getByRole("dialog", { name: "Member details" });
  await drawer.getByLabel("New password").fill(ROTATED_PASSWORD);
  await drawer.getByRole("button", { name: "Set password" }).click();
  // The form is replaced by a confirmation carrying the new password to copy: Bitween sends
  // no email, so this is the only place it is ever shown.
  await expect(drawer.getByText(/Password set for/)).toBeVisible();

  await signOut(page);
  await page.fill("#login-email", email);
  await page.fill("#login-password", FIRST_PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page).toHaveURL(/\/login$/);

  await signInAsAdmin(page);
  await removeMember(page, email);
});

test("disable a member, then re-enable them", async ({ page }) => {
  const email = await addMember(page, { name: "On Leave", roles: ["Viewer"] });

  await openMember(page, email);
  const drawer = page.getByRole("dialog", { name: "Member details" });
  await drawer.getByRole("button", { name: "Disable account" }).click();
  await expect(drawer.getByRole("button", { name: "Re-enable account" })).toBeVisible();

  await page.reload();
  await expect(page.getByRole("row", { name: new RegExp(email) })).toContainText("Disabled");

  // A disabled account keeps its roles and history but must not be able to sign in.
  await signOut(page);
  await page.fill("#login-email", email);
  await page.fill("#login-password", FIRST_PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page).toHaveURL(/\/login$/);

  await signInAsAdmin(page);
  await openMember(page, email);
  await drawer.getByRole("button", { name: "Re-enable account" }).click();
  await expect(drawer.getByRole("button", { name: "Disable account" })).toBeVisible();

  await removeMember(page, email);
});

test("the last administrator can't be removed or disabled", async ({ page }) => {
  // This test only means anything while the seeded admin is the *only* administrator, and it's
  // the one test that could strip its own role if the guard didn't fire. So assert the
  // precondition rather than assume it, and put the role back if the save somehow goes through.
  await page.goto("team/members");
  const admins = page.getByRole("row", { name: /Administrator/ });
  await expect(
    admins,
    "another account holds Administrator — the guard under test can't fire",
  ).toHaveCount(1);

  await openMember(page, ADMIN_EMAIL);
  const drawer = page.getByRole("dialog", { name: "Member details" });
  const role = drawer.getByRole("checkbox", { name: startsWith("Administrator") });

  // Nothing destructive is even offered on your own account.
  await expect(drawer.getByRole("button", { name: "Remove from team" })).toHaveCount(0);
  await expect(drawer.getByRole("button", { name: "Disable account" })).toHaveCount(0);

  // Dropping the role is offered, but the server refuses it.
  await role.uncheck();
  await drawer.getByRole("button", { name: "Save roles" }).click();

  try {
    await expect(drawer.getByText(/only member with the Administrator role/i)).toBeVisible();
    await page.reload();
    await expect(page.getByRole("row", { name: new RegExp(ADMIN_EMAIL) })).toContainText(
      "Administrator",
    );
  } finally {
    // Belt and braces: if the guard let it through, put the role back before failing, so the
    // rest of the suite doesn't run against an instance nobody can administer. Read the list
    // fresh — the unchecked box in the drawer is a rejected draft, not what the server holds.
    await page.goto("team/members");
    const adminRow = page.getByRole("row", { name: new RegExp(ADMIN_EMAIL) });
    if (!((await adminRow.textContent()) ?? "").includes("Administrator")) {
      await adminRow.click();
      await drawer.getByRole("checkbox", { name: startsWith("Administrator") }).check();
      await drawer.getByRole("button", { name: "Save roles" }).click();
      await expect(drawer.getByRole("button", { name: "Save roles" })).toHaveCount(0);
    }
  }
});

test("filter and search the member list", async ({ page }) => {
  const email = await addMember(page, { name: "Findable Person", roles: ["Viewer"] });

  await page.getByLabel("Search members").fill("Findable");
  await expect(page.getByRole("row", { name: new RegExp(email) })).toBeVisible();
  await expect(page.getByRole("row", { name: new RegExp(ADMIN_EMAIL) })).toHaveCount(0);

  await page.getByLabel("Search members").fill("");
  await page.getByRole("button", { name: "Disabled", exact: true }).click();
  await expect(page.getByRole("row", { name: new RegExp(email) })).toHaveCount(0);

  await page.getByRole("button", { name: "Active", exact: true }).click();
  await expect(page.getByRole("row", { name: new RegExp(email) })).toBeVisible();

  await removeMember(page, email);
});
