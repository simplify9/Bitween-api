import { test, expect } from "@playwright/test";
import { AdminApi } from "./api";
import { signInAsAdmin } from "./helpers";

/**
 * An adapter version's source, read and compared on the Adapters page against a real package in
 * storage — and the read recorded in the audit trail.
 */

/**
 * Published by tools/e2e.sh: a Python handler with versions 1.0.0 and 2.0.0 (current), each
 * carrying its source, and — like every adapter not on .NET — in the catalog only.
 */
const SOURCE_HANDLER = "e2e.sourcehandler";

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

test("an adapter only in the catalog is listed, its source is shown, and two versions are compared", async ({
  page,
  request,
}) => {
  const api = await AdminApi.signIn(request);
  // Not skipped when missing: it is never written to adapters/<id>, so missing means the listing
  // has stopped finding adapters that are only in the catalog.
  expect(await api.hasAdapter("handlers", SOURCE_HANDLER)).toBe(true);

  await page.goto("adapters?tab=installed");
  const custom = page.getByRole("region", { name: /^Published/ });
  const row = custom.getByRole("button", { name: /Source handler \(e2e\)/ });
  await expect(row).toContainText("v2.0.0");
  await row.click();

  await page.getByRole("button", { name: "View source of v2.0.0" }).click();
  const source = page.getByRole("region", { name: "Source of Source handler (e2e)" });
  const files = source.getByRole("list", { name: "Source files" });
  await expect(files.getByRole("button")).toHaveText(["adapter/main.py", "adapter/retry.py", "requirements.txt"]);
  await expect(source.getByText("serverless build")).toBeVisible();

  await files.getByRole("button", { name: "adapter/main.py" }).click();
  const editor = source.getByTestId("source-editor");
  await expect(editor).toHaveAttribute("data-path", "adapter/main.py");
  await expect(editor).toContainText("retries = 3");

  await source.getByRole("combobox", { name: "Compare with" }).selectOption("1.0.0");
  await expect(files.getByRole("button")).toHaveText([
    "adapter/legacy.pyremoved",
    "adapter/main.pychanged",
    "adapter/retry.pyadded",
  ]);
  await files.getByRole("button", { name: /adapter\/main\.py/ }).click();
  await expect(editor).toHaveAttribute("data-diff", "true");
  // The diff shows the line added since 1.0.0.
  await expect(editor.locator(".cm-changedLine")).toContainText("retries = 3");

  // Every file read is in the trail, under who read it.
  const trail = await api.get<{ result: { changes: Record<string, { new?: unknown }> }[] }>(
    "/audit?entityName=AdapterSourceAccess&limit=200",
  );
  expect(
    trail.result.some(
      (r) => r.changes?.AdapterId?.new === SOURCE_HANDLER && r.changes?.Path?.new === "adapter/main.py",
    ),
  ).toBe(true);
});
