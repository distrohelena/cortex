import { defineConfig } from "vitest/config";

process.env.OPENCLAW_LIVE_TEST = "1";

export default defineConfig({
  test: {
    testTimeout: 180_000,
    include: ["**/*.e2e.test.ts"],
    exclude: ["**/node_modules/**"],
  },
});
