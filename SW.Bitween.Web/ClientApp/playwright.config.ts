import { defineConfig } from "@playwright/test";

// Points at the locally running backend (SW.Bitween.Web.Local), which serves
// the built SPA at the site root — there is no separate dev server to boot.
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  reporter: "list",
  // Tests run against a real database, so a failed run leaves data behind. This clears it and
  // repairs the admin's roles before anything else, so one failure can't cascade into the next run.
  globalSetup: "./e2e/global-setup.ts",
  use: {
    baseURL: "https://localhost:7155/",
    ignoreHTTPSErrors: true,
  },
});
