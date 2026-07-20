import { defineConfig } from "@playwright/test";

// Points at the locally running backend (SW.Bitween.Web.Local), which serves
// the built SPA under /bitween — there is no separate dev server to boot.
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  reporter: "list",
  use: {
    // Trailing slash matters: goto() calls use relative (no leading "/") paths
    // so they resolve under /bitween instead of replacing it (WHATWG URL rules).
    baseURL: "https://localhost:7155/bitween/",
    ignoreHTTPSErrors: true,
  },
});
