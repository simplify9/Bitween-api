import { defineConfig } from "vitest/config";

// Standalone test config so the build config (vite.config.ts) stays untouched.
// `globals: true` matches the legacy Bitween-UI setup the mapping suites were
// copied from verbatim, so those files run unmodified — which is the proof the
// mapping logic wasn't touched during the port.
export default defineConfig({
  test: {
    globals: true,
    include: ["src/**/__tests__/**/*.test.ts"],
  },
});
