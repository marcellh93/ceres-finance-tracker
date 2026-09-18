import { appendFileSync } from 'node:fs'
import type { Reporter, TestModule } from 'vitest/node'

/**
 * Surfaces Vitest retried-then-passed tests (§12.19 item 5). A test whose
 * result is `passed` but carries attached errors was green only after a retry
 * (Vitest docs: "a test with passed state can still have errors attached — this
 * can happen if retry was triggered at least once"). That is the flake signal
 * the CI-only `retry` (vite.config.ts) would otherwise green silently.
 *
 * Writes a table to the GitHub Actions job summary and a ::warning:: per flake
 * so the retry is loud on the run it happened. It never changes pass/fail —
 * a test that fails all attempts is still red via the normal reporters.
 *
 * Cross-run frequency aggregation is deliberately out of scope here (tracked as
 * the Stage 16 follow-up); this is the per-run half.
 */
export default class FlakyReporter implements Reporter {
  onTestRunEnd(testModules: ReadonlyArray<TestModule>): void {
    const flaky: { name: string; module: string; errors: number }[] = []

    for (const testModule of testModules) {
      for (const testCase of testModule.children.allTests()) {
        const result = testCase.result()
        if (result.state === 'passed' && result.errors && result.errors.length > 0) {
          flaky.push({
            name: testCase.fullName,
            module: testCase.module.moduleId,
            errors: result.errors.length,
          })
        }
      }
    }

    if (flaky.length === 0) return

    // Inline annotations — visible on the run without opening the summary.
    for (const f of flaky) {
      const line = `Vitest flake: "${f.name}" passed only after a retry (${f.errors} attempt error(s)). Root-cause per docs/testing.md § quarantine lifecycle.`
      console.log(`::warning title=Flaky Vitest test::${line}`)
    }

    const summaryPath = process.env.GITHUB_STEP_SUMMARY
    if (!summaryPath) return

    const rows = flaky
      .map((f) => `| \`${f.name}\` | ${f.module.replace(process.cwd(), '.')} | ${f.errors} |`)
      .join('\n')
    const md =
      `\n### ⚠️ Flaky Vitest tests (passed after retry) — ${flaky.length}\n\n` +
      `These went green only after a CI retry. Each is owed a tracked \`[ ]\` on first sighting ` +
      `and a root-cause on recurrence (docs/testing.md § The quarantine lifecycle).\n\n` +
      `| Test | File | Attempt errors |\n|---|---|---|\n${rows}\n`

    try {
      appendFileSync(summaryPath, md)
    } catch {
      // Summary is best-effort; the annotations above already surfaced the flake.
    }
  }
}
