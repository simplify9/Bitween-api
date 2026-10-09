import { test, expect, type Locator, type Page } from "@playwright/test";
import { AdminApi, stamp } from "./api";
import { E2E_DATABASE } from "./env";
import { signInAsAdmin } from "./helpers";

/**
 * A PostgreSQL data source, made and used entirely through its pages, against a database that is
 * certainly there: the one Bitween itself runs on. Every step goes through the real adapter —
 * the connection test, the catalog the schema browser reads, and the statements it prepares.
 */

/** Published by tools/e2e.sh; Bitween's own provider for PostgreSQL. */
const PROVIDER = "bitween.db.postgresql";

test.beforeEach(async ({ page, request }) => {
  const api = await AdminApi.signIn(request);
  const providers = await api.get<{ adapterId: string }[]>("/datasources/providers");
  test.skip(
    !providers.some((p) => p.adapterId === PROVIDER),
    `${PROVIDER} is not published here — tools/e2e.sh publishes it; another instance needs it published`,
  );
  await signInAsAdmin(page);
});

/** Creates a PostgreSQL data source through New data source, and lands on its page. */
async function createDataSource(page: Page, name: string) {
  await page.goto("data-sources");
  await page.getByRole("button", { name: "New data source" }).click();
  await page.fill("#ds-name", name);
  await page.locator("#ds-provider").selectOption(PROVIDER);
  await page.getByRole("button", { name: "Create", exact: true }).click();
  await expect(page).toHaveURL(/\/data-sources\/\d+$/);
  await expect(page.getByRole("heading", { name, level: 1 })).toBeVisible();
  return Number(new URL(page.url()).pathname.split("/").pop());
}

async function fillConnection(page: Page, password: string) {
  await page.fill("#ds-prop-Host", E2E_DATABASE.host);
  await page.fill("#ds-prop-Port", E2E_DATABASE.port);
  await page.fill("#ds-prop-Database", E2E_DATABASE.database);
  await page.fill("#ds-prop-UserName", E2E_DATABASE.user);
  await page.fill("#ds-prop-Password", password);
}

async function save(page: Page) {
  await page.getByRole("button", { name: "Save changes" }).click();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);
}

/** The open "Add statement" form: the innermost box holding both its SQL and its button. */
const statementForm = (page: Page, panel: Locator) =>
  panel
    .locator("div")
    .filter({ has: page.locator("textarea") })
    .filter({ has: page.getByRole("button", { name: "Create statement" }) })
    .last();

async function testConnection(page: Page) {
  await page.getByRole("button", { name: "Test connection" }).click();
  const verdict = page.getByRole("heading", { name: /^The connection (works|failed)$/ });
  await expect(verdict).toBeVisible({ timeout: 60_000 });
  return verdict;
}

test("a PostgreSQL data source pointed at the e2e database: a wrong password fails the connection test, the right one passes, and the password is never shown again", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const id = await createDataSource(page, `Playwright Warehouse ${stamp()}`);

  await fillConnection(page, "not-the-password");
  await save(page);
  await expect(await testConnection(page)).toHaveText("The connection failed");

  await page.fill("#ds-prop-Password", E2E_DATABASE.password);
  await save(page);
  await expect(await testConnection(page)).toHaveText("The connection works");

  // Stored, and masked from then on — on the page and from the API.
  await page.reload();
  await expect(page.locator("#ds-prop-Password")).toHaveValue("");
  await expect(page.locator("#ds-prop-Password")).toHaveAttribute("placeholder", "••••••••");
  const stored = await api.get<{ properties: Record<string, string> }>(`/datasources/${id}`);
  expect(stored.properties.Password).not.toBe(E2E_DATABASE.password);
  expect(stored.properties.Host).toBe(E2E_DATABASE.host);

  await api.delete(`/datasources/${id}`);
});

test("the schema browser lists the database's tables and opens one to its columns, and a statement written from it is prepared against the live schema", async ({
  page,
  request,
}) => {
  test.setTimeout(120_000);
  const api = await AdminApi.signIn(request);
  const id = await createDataSource(page, `Playwright Catalog ${stamp()}`);
  await fillConnection(page, E2E_DATABASE.password);
  await save(page);
  await expect(await testConnection(page)).toHaveText("The connection works");

  // The test runs on a throwaway connection; the browser asks the adapter the node holds open,
  // which the supervisor starts within its next pass after the save.
  await expect
    .poll(async () => (await api.get<{ lastKnownState: string | null }>(`/datasources/${id}`)).lastKnownState ?? "", {
      message: "the node never started the data source's adapter",
      timeout: 60_000,
    })
    .toMatch(/connected|ready|idle|running/i);
  await page.reload();

  // Bitween's own tables are what this database holds; infolink.partner is one of them.
  const browser = page.locator("section").filter({
    has: page.getByRole("heading", { name: "What is in the database", exact: true }),
  });
  await expect(browser.getByText(/^PostgreSQL /)).toBeVisible({ timeout: 30_000 });
  await browser.getByRole("textbox", { name: "Search the catalog by name" }).fill("partner");
  const table = browser.getByRole("listitem").filter({ has: page.getByText("partner", { exact: true }) }).first();
  await expect(table).toBeVisible({ timeout: 30_000 });
  await table.getByRole("button", { expanded: false }).click();
  await expect(table.getByText("login_identity", { exact: true })).toBeVisible({ timeout: 30_000 });

  // A statement drafted from the table, saved — which prepares it against the database.
  await table.getByRole("button", { name: "Use in a statement" }).click();
  const statements = page.locator("section").filter({
    has: page.getByRole("heading", { name: "SQL statements", exact: true }),
  });
  const form = statementForm(page, statements);
  const sql = form.locator("textarea");
  await expect(sql).toHaveValue(/partner/);
  const statementName = `partnersByName${stamp()}`;
  await form.getByRole("textbox").first().fill(statementName);
  await sql.fill(`select id, name from infolink.partner where name = @name`);
  await form.getByRole("button", { name: "Create statement" }).click();
  await expect(statements.getByText(statementName)).toBeVisible();
  await expect(statements.getByText("Saved, but not checked")).toHaveCount(0);

  // SQL naming a column that isn't there is refused when it is saved, saying what is wrong.
  await statements.getByRole("button", { name: "Add statement" }).click();
  const bad = statementForm(page, statements);
  await bad.getByRole("textbox").first().fill(`badColumn${stamp()}`);
  await bad.locator("textarea").fill(`select no_such_column from infolink.partner`);
  await bad.getByRole("button", { name: "Create statement" }).click();
  await expect(bad.getByRole("alert")).toContainText(/no_such_column/i);

  // The connection test now prepares the saved statement too, and it passes.
  await expect(await testConnection(page)).toHaveText("The connection works");
  await expect(page.getByText(`statement:${statementName}`)).toBeVisible();

  const filter = encodeURIComponent(`DataSourceId:1:${id}`);
  const { result: rows } = await api.get<{ result: { id: number; name: string; sql: string }[] }>(
    `/datasourcestatements?filter=${filter}&offset=0&limit=100`,
  );
  expect(rows.map((r) => r.name)).toEqual([statementName]);
  expect(rows[0].sql).toBe(`select id, name from infolink.partner where name = @name`);

  for (const row of rows) await api.delete(`/datasourcestatements/${row.id}`);
  await api.delete(`/datasources/${id}`);
});
