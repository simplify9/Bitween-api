import { rm } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { defineConfig, type Plugin } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

/**
 * Empties wwwroot/assets before a build writes the new ones.
 *
 * Every build names its chunks after their content, so nothing ever overwrites
 * anything and the folder only grows — 146 files and 365MB of superseded bundles
 * by the time this was added, all of them still servable.
 *
 * Only assets/: the rest of wwwroot is legacy content the SPA does not own, which
 * is why `emptyOutDir` has to stay off.
 */
function cleanAssets(outDir: string): Plugin {
  return {
    name: "clean-assets",
    apply: "build",
    buildStart: () => rm(outDir, { recursive: true, force: true }),
  };
}

const assetsDir = fileURLToPath(new URL("../wwwroot/assets", import.meta.url));

// https://vite.dev/config/
export default defineConfig({
  // Served by SW.Bitween.Web at the site root. router.tsx derives the router's
  // basename from this, so this is the only place the base path is declared.
  base: "/",
  plugins: [react(), tailwindcss(), cleanAssets(assetsDir)],
  build: {
    // Build straight into the backend's webroot so `dotnet run` serves it.
    // emptyOutDir stays off because wwwroot has pre-existing legacy content;
    // the SPA owns index.html, assets/ and brand/ in there (gitignored).
    // assets/ is cleaned by cleanAssets above, since nothing else clears it.
    outDir: "../wwwroot",
    emptyOutDir: false,
  },
});
