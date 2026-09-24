import { test, expect } from "@playwright/test";
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
 * Reads are permission-guarded too, which is easy to get wrong in the other direction: a page can
 * legitimately need data from an area the viewer has no business browsing. These cover both sides —
 * what a narrow role can't read, and the pages it can still open in full.
 */

test("a page still loads when the area behind its Used by count is refused", async ({ page }) => {
  const roleName = `PW No Subscriptions ${Date.now()}`;
  await signInAsAdmin(page);
  await createRole(page, {
    name: roleName,
    permissions: [{ area: "Information types", action: "View" }],
  });
  const email = await addMember(page, { name: "No Subscriptions", roles: [roleName] });

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);

  // The information types list counts how many subscriptions use each type, which needs the
  // subscriptions list this role can't read. The count is what's expendable, not the page.
  await page.goto("information-types");
  await expect(page.getByText("You don't have access to this page")).toHaveCount(0);
  await expect(page.getByRole("table")).toBeVisible();
  await expect(page.getByText(/failed|error/i)).toHaveCount(0);

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
  await deleteRole(page, roleName);
});
