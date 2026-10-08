import { defineConfig } from "@playwright/test";
import { BASE_URL } from "./e2e/env";

// Points at a running backend, which serves the built SPA at the site root — there is no
// separate dev server to boot. tools/e2e.sh starts a throwaway one and runs this against it;
// on its own, this runs against whatever answers at E2E_BASE_URL (https://localhost:7155/).
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  reporter: "list",
  use: {
    baseURL: BASE_URL,
    ignoreHTTPSErrors: true,
    // Kept for failures only, so a red run says what the page looked like without every green
    // one paying for a trace.
    trace: "retain-on-failure",
  },
  projects: [
    // Tests run against a real database, so a failed run leaves data behind, and a new database
    // has none of the rows the specs build on. This clears the one and creates the other before
    // anything else — see e2e/seed.setup.ts.
    { name: "seed", testMatch: /seed\.setup\.ts/ },
    { name: "chromium", dependencies: ["seed"] },
  ],
});
