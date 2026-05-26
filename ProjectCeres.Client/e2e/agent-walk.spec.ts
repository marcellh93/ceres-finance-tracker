import { test, expect } from '@playwright/test';

/**
 * Agent-callable smoke suite — parameterized via env vars.
 *
 * The PRIMARY entrypoint for agents is `pnpm agent-walk` (which runs
 * agent-walk.ts directly via tsx). This spec is the runner-driven counterpart
 * for cases where the agent wants Playwright's test reporter + HTML report.
 *
 * Env:
 *   ROUTES — comma-separated routes (default: auth smoke matrix)
 *
 * baseURL comes from APP_URL via playwright.config.ts.
 */

const DEFAULT_ROUTES = ['/login', '/register', '/email-verify', '/account/unlock', '/password-reset'];
const BASE_PATH = '/app';

const routes = (process.env.ROUTES?.split(',').map((r) => r.trim()).filter(Boolean)) || DEFAULT_ROUTES;

for (const route of routes) {
  test(`route ${route} renders without console errors or 5xx`, async ({ page }) => {
    const consoleErrors: string[] = [];
    const fiveXx: Array<{ url: string; status: number }> = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') consoleErrors.push(msg.text());
    });
    page.on('response', (resp) => {
      if (resp.status() >= 500 && resp.status() < 600) {
        fiveXx.push({ url: resp.url(), status: resp.status() });
      }
    });
    await page.goto(`${BASE_PATH}${route}`, { waitUntil: 'networkidle' });
    expect(consoleErrors, `console errors at ${route}`).toEqual([]);
    expect(fiveXx, `5xx responses at ${route}`).toEqual([]);
  });
}
