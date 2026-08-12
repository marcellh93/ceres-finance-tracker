const { test } = require("node:test");
const assert = require("node:assert");
const { isDocWorthy, extractPath } = require("../flag-doc-change.js");

// PostToolUse advisory: nudges toward /sync-docs when a schema- or
// behaviour-bearing file changes.
//
// It read process.argv[2] while being wired with no arguments — Claude Code
// passes the payload on STDIN — so `file` was always "" and the hook had never
// fired once since being wired. Fixed 2026-08-12.

test("extractPath reads file_path from a PostToolUse payload", () => {
  assert.strictEqual(
    extractPath({ tool_input: { file_path: "/p/ProjectCeres/Models/Account.cs" } }),
    "/p/ProjectCeres/Models/Account.cs"
  );
});

test("extractPath reads notebook_path", () => {
  assert.strictEqual(extractPath({ tool_input: { notebook_path: "/p/x.ipynb" } }), "/p/x.ipynb");
});

test("extractPath returns empty string for a malformed payload", () => {
  assert.strictEqual(extractPath({}), "");
  assert.strictEqual(extractPath(undefined), "");
  assert.strictEqual(extractPath({ tool_input: {} }), "");
});

// ── doc-worthy paths ─────────────────────────────────────────────────────

for (const p of [
  "ProjectCeres/Models/Account.cs",
  "ProjectCeres/Controllers/Api/SessionsApiController.cs",
  "ProjectCeres/Data/AppDbContext.cs",
  "ProjectCeres/Migrations/20260514_AddRls.cs",
]) {
  test(`doc-worthy: ${p}`, () => assert.strictEqual(isDocWorthy(p), true));
}

test("doc-worthy matches an absolute path too", () => {
  assert.strictEqual(isDocWorthy("/Users/x/repo/ProjectCeres/Models/Account.cs"), true);
});

// ── not doc-worthy ───────────────────────────────────────────────────────

for (const p of [
  "ProjectCeres.Client/src/App.tsx",
  "ProjectCeres/Services/HeaderDetectionService.cs",
  "docs/roadmap-phase-three.md",
  ".claude/hooks/run-tests.sh",
]) {
  test(`not doc-worthy: ${p}`, () => assert.strictEqual(isDocWorthy(p), false));
}

test("an empty path is not doc-worthy", () => {
  assert.strictEqual(isDocWorthy(""), false);
  assert.strictEqual(isDocWorthy(undefined), false);
});
