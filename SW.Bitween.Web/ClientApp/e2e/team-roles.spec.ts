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
