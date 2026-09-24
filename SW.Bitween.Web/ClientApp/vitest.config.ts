import { defineConfig } from "vitest/config";

// Standalone test config so the build config (vite.config.ts) stays untouched.
// `globals: true` matches the legacy Bitween-UI setup the mapping suites were
// copied from verbatim, so those files run unmodified — which is the proof the
// mapping logic wasn't touched during the port.
export default defineConfig({
  test: {
    globals: true,
    projects: [
      {
        extends: true,
        test: { name: "unit", include: ["src/**/__tests__/**/*.test.ts"] },
      },
      {
        // Whole pages rendered in jsdom against a mock network: see src/__tests__/support.
        extends: true,
        test: {
          name: "component",
          include: ["src/**/__tests__/**/*.test.tsx"],
          environment: "jsdom",
          setupFiles: ["./src/__tests__/support/setup.ts"],
        },
      },
    ],
  },
});
