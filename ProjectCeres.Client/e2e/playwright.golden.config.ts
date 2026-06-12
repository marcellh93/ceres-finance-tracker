import { defineConfig, devices } from '@playwright/test'

const APP_URL = process.env.E2E_APP_URL ?? 'https://localhost:7299'

export default defineConfig({
  testDir: './auth',
  testMatch: /.*\.spec\.ts$/,
  // A flake is a failure until root-caused (docs/testing.md § Flaky tests).
  retries: 0,
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
