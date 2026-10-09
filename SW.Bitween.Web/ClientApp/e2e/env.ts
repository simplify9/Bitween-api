/**
 * Where the suite points and who it signs in as, in one place.
 *
 * Read from the environment because the answer differs by database. A fresh install refuses to
 * start until it is given an administrator password of its own (Bitween__InitialAdminPassword),
 * so tools/e2e.sh chooses one and passes it in; a team dev database migrated before that rule
 * still has the old published one, which stays the default so running `yarn test:e2e` against it
 * keeps working without any setup.
 *
 * Deliberately free of Playwright imports: playwright.config.ts reads it too.
 */

export const BASE_URL = (process.env.E2E_BASE_URL ?? "https://localhost:7155/").replace(/\/?$/, "/");

/** The backend's API root, for the specs and the seed that call it directly. */
export const API = `${BASE_URL}api`;

export const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? "admin@Bitween.systems";
export const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? "Mtm@dmin!2";

/**
 * The database the instance under test runs on, as seen from this machine. The data source specs
 * connect a PostgreSQL data source to it: it is the one database certain to be there. tools/e2e.sh
 * sets the port; the rest is what its container is started with.
 */
export const E2E_DATABASE = {
  host: process.env.E2E_PG_HOST ?? "localhost",
  port: process.env.E2E_PG_PORT ?? "55432",
  database: process.env.E2E_PG_DATABASE ?? "bitween",
  user: process.env.E2E_PG_USER ?? "postgres",
  password: process.env.E2E_PG_PASSWORD ?? "postgres",
};
