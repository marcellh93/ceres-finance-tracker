const { test } = require("node:test");
const assert = require("node:assert");
const { hasFailureSignature, isLogReading } = require("../read-failure-first.js");

// PostToolUse advisory: when a command's output shows a build/test/CI failure,
// nudge "read the actual failure output first" before hypothesizing a cause.
// Codifies the 2026-09-10 lesson (three blind pushes on the pnpm-on-PATH bug).

// ── failure signatures fire ──────────────────────────────────────────────
for (const out of [
  "Build FAILED.",
  "Build failed. Use dotnet build to see the errors.",
  "Foo.cs(12,5): error CS1002: ; expected",
  "ProjectCeres.csproj(41,5): error MSB3073: exited with code 127",
  "error NETSDK1004: Assets file not found",
  "Failed!  - Failed:     4, Passed:  1377, Skipped:     0, Total:  1381",
  " Tests  8 failed | 1105 passed (1113)",
  " ELIFECYCLE  Test failed. See above for more details.",
  "##[error]Process completed with exit code 1.",
  "Process completed with exit code 127.",
]) {
  test(`failure signature fires: ${out.slice(0, 40)}`, () =>
    assert.strictEqual(hasFailureSignature(out), true));
}

// ── clean / passing output does NOT fire ──────────────────────────────────
for (const out of [
  "Build succeeded.",
  "Passed!  - Failed:     0, Passed:  1381, Skipped:     0, Total:  1381",
  " Tests  1113 passed (1113)",
  "roadmaps consistent",
  "no leaks found",
  "The deployment failed gracefully and recovered", // prose 'failed', not a build/test signature
  "",
  undefined,
]) {
  test(`no false positive: ${String(out).slice(0, 40)}`, () =>
    assert.strictEqual(hasFailureSignature(out), false));
}

// ── log-reading commands are never nagged ─────────────────────────────────
for (const cmd of [
  "gh run view 12345 --log-failed",
  "gh run view 12345 --job 999 --log",
  "gh run watch 12345 --interval 25",
  "cat /tmp/dt.log",
  "grep -i error build.log",
  "tail -50 .claude/state/run-tests/last.log",
]) {
  test(`log-reading command exempt: ${cmd.slice(0, 40)}`, () =>
    assert.strictEqual(isLogReading(cmd), true));
}

// ── a command that CAUSES the failure is not exempt ───────────────────────
for (const cmd of [
  "dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj",
  "pnpm --dir ProjectCeres.Client test",
  "git push origin main",
  "gh run list --limit 5", // listing is not reading THE failing log
]) {
  test(`not log-reading (still nag-eligible): ${cmd.slice(0, 40)}`, () =>
    assert.strictEqual(isLogReading(cmd), false));
}
