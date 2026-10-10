import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("promoted properties can be put in order, and the order is kept", async ({ page }) => {
  const name = `Playwright Order ${Date.now()}`;
  await page.goto("information-types");
  await page.getByRole("button", { name: "New information type" }).click();
  const dialog = page.getByRole("dialog", { name: "New information type" });
  await dialog.getByRole("textbox", { name: "Name" }).fill(name);
  await dialog.getByRole("button", { name: "Create information type" }).click();
  await expect(page).toHaveURL(/\/information-types\/\d+$/);

  // Postgres used to hand these back shortest name first, whatever order they were saved in.
  for (const [i, [key, path]] of [
    ["Customer", "$.customer"],
    ["OrderNumber", "$.order"],
    ["City", "$.city"],
  ].entries()) {
    await page.getByRole("button", { name: "Add promoted property" }).click();
    await page.getByRole("textbox", { name: `Name ${i + 1}`, exact: true }).fill(key);
    await page.getByRole("textbox", { name: `JSON path ${i + 1}` }).fill(path);
  }
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

  // OrderNumber up to first place, by keyboard, then City dragged above Customer.
  await page.getByRole("button", { name: /Move OrderNumber/ }).focus();
  await page.keyboard.press("ArrowUp");
  await page
    .getByRole("button", { name: /Move City/ })
    .dragTo(page.getByRole("button", { name: /Move Customer/ }));
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);

  await page.reload();
  const names = page.getByRole("textbox", { name: /^Name \d+$/ });
  await expect(names).toHaveCount(3);
  expect(await names.evaluateAll((boxes) => boxes.map((b) => (b as HTMLInputElement).value))).toEqual([
    "OrderNumber",
    "City",
    "Customer",
  ]);
  await expect(page.getByText("Main", { exact: true })).toBeVisible();

  // Cleanup.
  await page.getByRole("button", { name: "Delete" }).click();
  await page.getByRole("button", { name: "Delete information type" }).click();
  await expect(page).toHaveURL(/\/information-types$/);
});
