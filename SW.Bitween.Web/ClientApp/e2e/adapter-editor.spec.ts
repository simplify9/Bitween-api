import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";

/**
 * Writing a Python adapter in the browser against a real Bitween: started from the template,
 * edited, checked against the Bitween contract and tried by the server's own Python, published,
 * made current, and then listed with its source like any published adapter.
 */

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("a Python handler written in the editor is checked, tried, published and made current", async ({ page }) => {
  test.setTimeout(120_000);
  const name = `E2ePy${Date.now()}`;
  const adapterId = name.replace(/(?<=[a-z0-9])(?=[A-Z])/g, ".").toLowerCase();

  await page.goto("adapters");
  await page.getByRole("button", { name: "New adapter" }).click();
  const dialog = page.getByRole("dialog", { name: "New adapter" });
  await dialog.getByLabel("Name").fill(name);
  await dialog.getByLabel("Language").selectOption("python");
  await dialog.getByLabel("Kind").selectOption("handler");
  await dialog.getByRole("button", { name: "Start writing" }).click();

  await expect(page).toHaveURL(/\/adapters\/drafts\/\d+$/);
  await expect(page.getByRole("heading", { name: adapterId })).toBeVisible();
  const editor = page.getByTestId("draft-editor");
  await expect(editor).toHaveAttribute("data-path", "main.py");

  // An edit, typed into the editor (Monaco): the draft is unsaved until it is checked.
  await editor.locator(".monaco-editor .view-lines").click();
  // To the end of the file, as each platform's VS Code does it: Cmd+End means nothing on a Mac.
  await page.keyboard.press(process.platform === "darwin" ? "Meta+ArrowDown" : "Control+End");
  await page.keyboard.type("\n# Edited in the browser.\n");
  await expect(page.getByText("Unsaved", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Save and check" }).click();
  await expect(page.getByText("Builds and conforms.")).toBeVisible({ timeout: 60_000 });
  await expect(page.getByText("Unsaved", { exact: true })).toHaveCount(0);
  await page.getByLabel("ApiKey *").fill("e2e-key");

  await page.getByRole("tab", { name: "Try" }).click();
  await page.getByRole("button", { name: "Save and run" }).click();
  await expect(page.getByLabel("Try result")).toContainText('"Data"', { timeout: 60_000 });

  await page.getByRole("tab", { name: "Publish" }).click();
  await page.getByLabel("Release notes").fill("Written in the browser");
  await page.getByRole("button", { name: "Publish" }).click();
  const published = page.getByLabel("Published");
  await expect(published).toContainText("Published v1.0.0.", { timeout: 60_000 });
  await published.getByRole("button", { name: "Make v1.0.0 current" }).click();
  await expect(published).toContainText("v1.0.0 is now current");

  // Listed with the published adapters, current at 1.0.0, its source readable.
  await page.goto("adapters?tab=installed");
  const row = page.getByRole("region", { name: /^Published/ }).getByRole("button", { name: new RegExp(adapterId.replace(/\./g, "\\.")) });
  await expect(row).toContainText("v1.0.0");
  await row.click();
  await page.getByRole("button", { name: "View source of v1.0.0" }).click();
  const files = page.getByRole("list", { name: "Source files" });
  await files.getByRole("button", { name: "main.py" }).click();
  await expect(page.getByTestId("source-editor")).toContainText("Edited in the browser");
});
