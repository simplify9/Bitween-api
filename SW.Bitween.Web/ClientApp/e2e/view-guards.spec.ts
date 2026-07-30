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

async function apiStatus(page: import("@playwright/test").Page, path: string): Promise<number> {
  const token = await page.evaluate(() => localStorage.getItem("access_token"));
  const res = await page.request.fetch(`https://localhost:7155/api${path}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  return res.status();
}

test("a role with one view permission can't read any other area's list", async ({ page }) => {
  const roleName = `PW Docs Reader ${Date.now()}`;
  await signInAsAdmin(page);
  await createRole(page, {
    name: roleName,
    permissions: [{ area: "Information types", action: "View" }],
  });
  const email = await addMember(page, { name: "Docs Reader", roles: [roleName] });

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);

  // The one area they hold is readable.
  expect(await apiStatus(page, "/documents")).toBe(200);

  // Every other list is refused, not merely hidden in the nav.
  for (const path of [
    "/partners",
    "/xchanges",
    "/subscriptions",
    "/notifiers",
    "/apigateways",
    "/busgateways",
    "/retrypolicies",
    "/globaladaptervaluessets",
    "/workgroups",
    "/delayedretries",
    "/ops/summary",
  ])
    expect(await apiStatus(page, path), `${path} should be refused`).toBe(401);

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
  await deleteRole(page, roleName);
});

test("lookup mode stays readable, because pickers across the app depend on it", async ({ page }) => {
  const roleName = `PW Lookup Only ${Date.now()}`;
  await signInAsAdmin(page);
  await createRole(page, {
    name: roleName,
    permissions: [{ area: "Information types", action: "View" }],
  });
  const email = await addMember(page, { name: "Lookup User", roles: [roleName] });

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);

  // id/name pairs only — what a picker needs, and not the data the guard protects.
  for (const path of [
    "/partners?lookup=true",
    "/subscriptions?lookup=true",
    "/retrypolicies?lookup=true",
    "/accounts?lookup=true",
  ])
    expect([200, 206], `${path} should be allowed in lookup mode`).toContain(
      await apiStatus(page, path),
    );

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
  await deleteRole(page, roleName);
});

test("a page still loads when the area behind its Used by count is refused", async ({ page }) => {
  const roleName = `PW No Integrations ${Date.now()}`;
  await signInAsAdmin(page);
  await createRole(page, {
    name: roleName,
    permissions: [{ area: "Information types", action: "View" }],
  });
  const email = await addMember(page, { name: "No Integrations", roles: [roleName] });

  await signOut(page);
  await signIn(page, email, FIRST_PASSWORD);

  // The information types list counts how many integrations use each type, which needs the
  // integrations list this role can't read. The count is what's expendable, not the page.
  await page.goto("information-types");
  await expect(page.getByText("You don't have access to this page")).toHaveCount(0);
  await expect(page.getByRole("table")).toBeVisible();
  await expect(page.getByText(/failed|error/i)).toHaveCount(0);

  await signOut(page);
  await signInAsAdmin(page);
  await removeMember(page, email);
  await deleteRole(page, roleName);
});
