import { test, expect } from "@playwright/test";
import { createRole, deleteRole, signInAsAdmin } from "./helpers";

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("create a custom role, then delete it", async ({ page }) => {
  const name = `PW Operator ${Date.now()}`;

  await createRole(page, {
    name,
    permissions: [
      { area: "Exchanges", action: "View" },
      { area: "Exchanges", action: "Operate" },
    ],
  });

  const row = page.getByRole("link", { name: new RegExp(name) });
  await expect(row).toBeVisible();
  await expect(row).toContainText("0 members");
  await expect(row).toContainText("2/");

  // Reopen it: the permissions must come back from the server exactly as ticked.
  await row.click();
  await expect(page.getByRole("checkbox", { name: "Exchanges: View", exact: true })).toBeChecked();
  await expect(page.getByRole("checkbox", { name: "Exchanges: Operate", exact: true })).toBeChecked();
  await expect(
    page.getByRole("checkbox", { name: "Partners: View", exact: true }),
  ).not.toBeChecked();

  await page.goto("team/roles");
  await deleteRole(page, name);
  await expect(page.getByText(name)).toHaveCount(0);
});

test("granting an action implies View, and clearing View clears the row", async ({ page }) => {
  await page.goto("team/roles/new");

  // An action you can't view is an action you can't reach, so View comes along.
  const view = page.getByRole("checkbox", { name: "Partners: View", exact: true });
  const edit = page.getByRole("checkbox", { name: "Partners: Edit", exact: true });
  const del = page.getByRole("checkbox", { name: "Partners: Delete", exact: true });

  // Only the count granted is asserted, not the catalog size — that changes whenever a
  // permission is added or dropped, and it isn't what this test is about.
  const granted = (n: number) => new RegExp(`\\b${n}/\\d+ permissions granted`);

  await edit.check();
  await expect(view).toBeChecked();
  await expect(page.getByText(granted(2))).toBeVisible();

  await del.check();
  await expect(page.getByText(granted(3))).toBeVisible();

  // Removing View takes the whole area with it.
  await view.uncheck();
  await expect(edit).not.toBeChecked();
  await expect(del).not.toBeChecked();
  await expect(page.getByText(granted(0))).toBeVisible();
});

test("the access preview shows what the role would see", async ({ page }) => {
  await page.goto("team/roles/new");

  await expect(page.getByText("No pages yet — grant a View permission.")).toBeVisible();

  await page.getByRole("checkbox", { name: "Partners: View", exact: true }).check();
  const preview = page.locator("section, div").filter({ hasText: "What members with this role see" }).last();
  await expect(preview.getByText("Partners")).toBeVisible();
  await expect(preview.getByText("Exchanges")).toHaveCount(0);

  await page.getByRole("checkbox", { name: "Exchanges: View", exact: true }).check();
  await expect(preview.getByText("Exchanges")).toBeVisible();
});

test("built-in roles are read-only", async ({ page }) => {
  await page.goto("team/roles");
  await page.getByRole("link", { name: /Administrator/ }).click();

  await expect(page.getByText("This role is built in")).toBeVisible();
  await expect(page.getByRole("checkbox", { name: "Partners: View", exact: true })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Delete role" })).toHaveCount(0);
  // Name and description aren't even rendered for a built-in.
  await expect(page.locator("#role-name")).toHaveCount(0);
});

test("duplicate a role", async ({ page }) => {
  const original = `PW Source ${Date.now()}`;
  await createRole(page, {
    name: original,
    permissions: [
      { area: "Partners", action: "View" },
      { area: "Partners", action: "Edit" },
    ],
  });

  await page.getByRole("link", { name: new RegExp(original) }).click();
  await page.getByRole("button", { name: "Duplicate" }).click();

  await expect(page.locator("#role-name")).toHaveValue(`Copy of ${original}`);
  await expect(page.getByRole("checkbox", { name: "Partners: Edit", exact: true })).toBeChecked();

  const copy = `PW Copy ${Date.now()}`;
  await page.fill("#role-name", copy);
  await page.getByRole("button", { name: "Create role" }).click();
  await page.waitForURL(/\/team\/roles$/);

  await expect(page.getByRole("link", { name: new RegExp(copy) })).toBeVisible();

  await deleteRole(page, copy);
  await deleteRole(page, original);
});
