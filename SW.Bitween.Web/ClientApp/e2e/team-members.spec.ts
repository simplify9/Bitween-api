import { test, expect } from "@playwright/test";
import {
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
