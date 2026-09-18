#!/usr/bin/env node
// Surfaces Playwright flaky tests (§12.19 item 5). Reads the JSON report written
// by playwright.golden.config.ts and, for every test Playwright labelled `flaky`
// (passed only after the CI-only single retry, Stage 12.17), writes a table to the
// GitHub Actions job summary and a ::warning:: annotation per test.
//
// It never changes the run's pass/fail: a test that fails all attempts is `unexpected`
// (red) via Playwright's own exit code; this only makes a silent retry loud.
// Exit 0 always — a reporting step must not fail the job.
//
// Cross-run frequency aggregation is out of scope (tracked as the Stage 16 follow-up).
//
// Usage: node tools/e2e/report-flaky.mjs <path-to-results.json>

import { readFileSync, appendFileSync } from 'node:fs'

const jsonPath = process.argv[2]
if (!jsonPath) {
  console.error('report-flaky: no results.json path given; skipping.')
  process.exit(0)
}

let report
try {
  report = JSON.parse(readFileSync(jsonPath, 'utf8'))
} catch (err) {
  // No report (e.g. the server never came up) → nothing to surface, don't fail.
  console.error(`report-flaky: could not read ${jsonPath} (${err.message}); skipping.`)
  process.exit(0)
}

/** @type {{ title: string; file: string; project: string }[]} */
const flaky = []

/** Playwright suites nest recursively; specs hold the tests. */
function walk(suites) {
  for (const suite of suites ?? []) {
    for (const spec of suite.specs ?? []) {
      for (const test of spec.tests ?? []) {
        if (test.status === 'flaky') {
          flaky.push({
            title: spec.title,
            file: `${spec.file}:${spec.line}`,
            project: test.projectName || 'unknown',
          })
        }
      }
    }
    walk(suite.suites)
  }
}
walk(report.suites)

if (flaky.length === 0) {
  console.log('report-flaky: no flaky Playwright tests this run.')
  process.exit(0)
}

for (const f of flaky) {
  const line = `Playwright flake [${f.project}]: "${f.title}" (${f.file}) passed only after a retry. Root-cause per docs/testing.md § quarantine lifecycle.`
  console.log(`::warning title=Flaky Playwright test::${line}`)
}

const summaryPath = process.env.GITHUB_STEP_SUMMARY
if (summaryPath) {
  const rows = flaky
    .map((f) => `| \`${f.title}\` | ${f.project} | ${f.file} |`)
    .join('\n')
  const md =
    `\n### ⚠️ Flaky Playwright tests (passed after retry) — ${flaky.length}\n\n` +
    `These went green only after the CI retry. Each is owed a tracked \`[ ]\` on first sighting ` +
    `and a root-cause on recurrence (docs/testing.md § The quarantine lifecycle).\n\n` +
    `| Test | Browser | File |\n|---|---|---|\n${rows}\n`
  try {
    appendFileSync(summaryPath, md)
  } catch {
    // Summary is best-effort; the annotations above already surfaced the flake.
  }
}

process.exit(0)
