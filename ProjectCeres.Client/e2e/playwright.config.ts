import { defineConfig } from '@playwright/test';

/**
 * Playwright config for Project Ceres E2E.
 *
 * Spec files live alongside this config. The agent's entrypoint is
 * `agent-walk.ts` (run via `pnpm agent-walk`), not the Playwright runner — but
 * the runner is still useful for parameterized smoke specs invoked via
 * `pnpm e2e`.
 *
 * baseURL is read from APP_URL so the same config works against any host:port
 * the dev environment chose (tools/agent-env/up.sh prints `APP_URL=...`).
 */
export default defineConfig({
  testDir: '.',
  testMatch: /.*\.spec\.ts$/,
  fullyParallel: false,
  workers: 1, // avoid parallel-against-single-app collisions
  reporter: [['list'], ['json', { outputFile: 'test-results/results.json' }]],
  use: {
    baseURL: process.env.APP_URL,
    trace: 'on',
    screenshot: 'on',
    video: 'retain-on-failure',
    ignoreHTTPSErrors: true, // .NET dev server uses a self-signed cert
  },
});
