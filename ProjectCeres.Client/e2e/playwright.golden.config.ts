import { defineConfig, devices } from '@playwright/test'

const APP_URL = process.env.E2E_APP_URL ?? 'https://localhost:7299'

export default defineConfig({
  testDir: './auth',
  testMatch: /.*\.spec\.ts$/,
  // Locally a flake is a failure until root-caused (docs/testing.md § Flaky tests).
  // On CI ONLY, allow exactly one retry (Stage 12.17): the ubuntu-latest runner is
  // CPU-starved relative to a dev machine, and the slowest browser (webkit) can slip a
  // single sub-timeout on an otherwise-correct test — a 60s webkit `locator.click`
  // timeout on the support-page reply test was root-caused to CI load, not a race
  // (the Send button mounts only after the thread load and takes no async disable gate).
  // Playwright reports a retried pass as `flaky`, NOT silent-green, so a degrading test
  // stays visible; a genuinely broken test still fails both attempts → red. Mirrors the
  // Vitest one-retry carve-out in docs/testing.md § Definition of Done.
  retries: process.env.CI ? 1 : 0,
  // Deterministic email-sink polling + a single shared loopback rate-limit partition.
  // Parallelism / sharding is Stage 16.16's job.
  workers: 1,
  fullyParallel: false,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['html', { outputFolder: '.artifacts/report', open: 'never' }]],
  outputDir: './.artifacts/test-results',
  use: {
    baseURL: APP_URL,
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],
  webServer: {
    // cwd defaults to the config-file dir (e2e/); anchor the script at the repo root.
    command: 'bash ../../tools/e2e/run-server.sh',
    url: `${APP_URL}/api/health`,
    timeout: 180_000,
    reuseExistingServer: false,
    ignoreHTTPSErrors: true,
    stdout: 'pipe',
    stderr: 'pipe',
  },
})
