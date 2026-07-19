import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// https://vite.dev/config/
export default defineConfig({
  // Served by SW.Bitween.Web, which mounts everything under /bitween.
  base: "/bitween/",
  plugins: [react(), tailwindcss()],
  build: {
    // Build straight into the backend's webroot so `dotnet run` serves it.
    // emptyOutDir stays off because wwwroot has pre-existing legacy content;
    // the SPA owns index.html, assets/ and brand/ in there (gitignored).
    outDir: "../wwwroot",
    emptyOutDir: false,
  },
});
