#!/usr/bin/env tsx
/**
 * agent-walk — the entrypoint the AI coding agent invokes when verifying a stage.
 *
 * Drives a running app at $APP_URL through a list of routes and captures
 * runtime evidence (Playwright trace, screenshots, console messages, network
 * responses) under .claude/state/evidence/stage-${STAGE_ID}/.
 *
 * Env:
 *   APP_URL                 required — printed by tools/agent-env/up.sh
 *   STAGE_ID                required — e.g. "9.3" or "10"
 *   ROUTES                  optional — comma-separated routes (default: auth smoke matrix)
 *   MANUAL_HANDOFF_LIST     optional — path to a markdown / json file containing a
 *                                       manual-test handoff list. If set, the script
 *                                       greps it for route mentions and asserts every
 *                                       referenced route was actually walked.
 *
 * Exit codes:
 *   0 — clean walk, no console errors, no 5xx, manual-test handoff (if any) covered
 *   1 — precondition failure (missing env, app not responding)
 *   2 — console errors or 5xx responses observed
 *   3 — manual-test handoff references routes not in the walked set
 */

import { chromium, type ConsoleMessage } from '@playwright/test';
import { spawnSync } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync, existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

type ConsoleEntry = { route: string; type: string; text: string };
type NetworkEntry = { route: string; requested_url: string; status: number; method: string; ms: number };

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const REPO_ROOT = resolve(__dirname, '..', '..');
const DEFAULT_ROUTES = ['/login', '/register', '/email-verify', '/account/unlock', '/password-reset'];
const BASE_PATH = '/app'; // React Router basename — see ProjectCeres.Client/src/app/main.tsx
const HEALTH_TIMEOUT_SECONDS = 2;

function die(code: number, message: string): never {
  process.stderr.write(`agent-walk: ${message}\n`);
  process.exit(code);
}

function slugify(route: string): string {
  return route.replace(/^\//, '').replace(/\//g, '_').replace(/[^a-z0-9_.-]/gi, '-') || 'root';
}

function ensureAppRunning(appUrl: string): void {
  // curl-based liveness probe. Fails in <2s so a dead app doesn't burn a
  // 30-90s Playwright navigation timeout. Accept 2xx, 3xx, AND 404 — a 404
  // proves the server is alive even if there's no root route.
  const result = spawnSync(
    'curl',
    ['-sS', '-o', '/dev/null', '-w', '%{http_code}', appUrl + '/', '--max-time', String(HEALTH_TIMEOUT_SECONDS)],
    { encoding: 'utf8' },
  );
  if (result.error || result.status !== 0) {
    die(1, `App at ${appUrl} not responding (curl exit ${result.status}). Run tools/agent-env/up.sh first.`);
  }
  const code = parseInt((result.stdout || '').trim(), 10);
  if (!Number.isFinite(code)) {
    die(1, `App at ${appUrl} returned non-numeric HTTP code "${result.stdout}". Run tools/agent-env/up.sh first.`);
  }
  const ok = (code >= 200 && code < 400) || code === 404;
  if (!ok) {
    die(1, `App at ${appUrl} returned HTTP ${code}. Run tools/agent-env/up.sh first.`);
  }
}

function manualHandoffCrossCheck(handoffPath: string, routesWalked: string[]): void {
  if (!existsSync(handoffPath)) {
    die(3, `MANUAL_HANDOFF_LIST="${handoffPath}" does not exist.`);
  }
  const text = readFileSync(handoffPath, 'utf8');
  // Route mentions look like `/login`, `/accounts/:id/edit`, etc.
  // Match a leading `/` followed by one or more URL-path-safe chars; stop at
  // whitespace, `)`, `]`, `,`, `"`, `'` or end-of-line.
  const matches = text.match(/\/[a-zA-Z0-9_\-/.:]+/g) || [];
  const referenced = new Set<string>();
  for (const m of matches) {
    // Strip trailing punctuation Markdown picks up (e.g. `/login.` or `/login,`).
    const cleaned = m.replace(/[.,;:!?]+$/, '');
    // Skip obviously non-route tokens (paths to files, URLs, leading-slash absolute paths).
    if (cleaned.includes('://')) continue;
    if (/\.(md|json|ts|tsx|js|cs|sh|png|jpg|svg)$/.test(cleaned)) continue;
    referenced.add(cleaned);
  }
  const walkedSet = new Set(routesWalked);
  const missing = [...referenced].filter((r) => !walkedSet.has(r));
  if (missing.length > 0) {
    die(
      3,
      `Manual-test handoff references routes not walked: [${missing.join(', ')}]. ` +
        `Either walk these routes (set ROUTES env var) or remove them from the handoff list.`,
    );
  }
}

async function main(): Promise<void> {
  const appUrl = process.env.APP_URL;
  const stageId = process.env.STAGE_ID;
  if (!appUrl) die(1, 'APP_URL env var is required (printed by tools/agent-env/up.sh).');
  if (!stageId) die(1, 'STAGE_ID env var is required (e.g. STAGE_ID=9.3).');

  ensureAppRunning(appUrl);

  const routes = (process.env.ROUTES?.split(',').map((r) => r.trim()).filter(Boolean)) || DEFAULT_ROUTES;

  const evidenceDir = resolve(REPO_ROOT, '.claude', 'state', 'evidence', `stage-${stageId}`);
  const screenshotsDir = resolve(evidenceDir, 'screenshots');
  mkdirSync(screenshotsDir, { recursive: true });

  const consoleLog: ConsoleEntry[] = [];
  const networkLog: NetworkEntry[] = [];
  const startedAt = new Date().toISOString();

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    baseURL: appUrl,
    ignoreHTTPSErrors: true, // .NET dev server uses a self-signed cert
  });
  await context.tracing.start({ screenshots: true, snapshots: true, sources: true });

  const requestStarts = new Map<string, number>();
  for (const route of routes) {
    const page = await context.newPage();
    const onConsole = (msg: ConsoleMessage) => {
      consoleLog.push({ route, type: msg.type(), text: msg.text() });
    };
    page.on('console', onConsole);
    page.on('request', (req) => requestStarts.set(req.url(), Date.now()));
    page.on('response', async (resp) => {
      const start = requestStarts.get(resp.url()) ?? Date.now();
      networkLog.push({
        route,
        requested_url: resp.url(),
        status: resp.status(),
        method: resp.request().method(),
        ms: Date.now() - start,
      });
    });

    const target = `${BASE_PATH}${route}`;
    try {
      await page.goto(target, { waitUntil: 'networkidle', timeout: 30_000 });
    } catch (err) {
      consoleLog.push({
        route,
        type: 'error',
        text: `agent-walk: navigation to ${target} failed: ${(err as Error).message}`,
      });
    }
    await page.screenshot({ path: resolve(screenshotsDir, `${slugify(route)}.png`), fullPage: true });
    await page.close();
  }

  const tracePath = resolve(evidenceDir, 'trace.zip');
  await context.tracing.stop({ path: tracePath });
  await browser.close();

  const consoleErrors = consoleLog.filter((e) => e.type === 'error');
  const network5xx = networkLog.filter((e) => e.status >= 500 && e.status < 600);

  const summary = {
    stage_id: stageId,
    app_url: appUrl,
    routes_walked: routes,
    started_at: startedAt,
    finished_at: new Date().toISOString(),
    console_errors_count: consoleErrors.length,
    network_5xx_count: network5xx.length,
  };

  writeFileSync(resolve(evidenceDir, 'console.json'), JSON.stringify(consoleLog, null, 2));
  writeFileSync(resolve(evidenceDir, 'network.json'), JSON.stringify(networkLog, null, 2));
  writeFileSync(resolve(evidenceDir, 'walk-summary.json'), JSON.stringify(summary, null, 2));

  const handoff = process.env.MANUAL_HANDOFF_LIST;
  if (handoff) {
    manualHandoffCrossCheck(handoff, routes);
  }

  if (consoleErrors.length > 0 || network5xx.length > 0) {
    const detail = [
      consoleErrors.length > 0 ? `${consoleErrors.length} console errors` : null,
      network5xx.length > 0 ? `${network5xx.length} 5xx responses` : null,
    ]
      .filter(Boolean)
      .join(', ');
    process.stderr.write(`agent-walk: walk completed with issues — ${detail}.\n`);
    process.stderr.write(`agent-walk: evidence at ${evidenceDir}\n`);
    for (const e of consoleErrors.slice(0, 10)) {
      process.stderr.write(`  console.${e.type} @ ${e.route}: ${e.text}\n`);
    }
    for (const n of network5xx.slice(0, 10)) {
      process.stderr.write(`  network ${n.status} @ ${n.route}: ${n.method} ${n.requested_url}\n`);
    }
    process.exit(2);
  }

  process.stdout.write(`agent-walk: ${routes.length} route(s) walked, evidence at ${evidenceDir}\n`);
}

main().catch((err) => {
  die(1, `unexpected failure: ${(err as Error).stack || err}`);
});
