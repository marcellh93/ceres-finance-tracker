const { test } = require("node:test");
const assert = require("node:assert");
const { classifyWrite } = require("../track-session-writes.js");

// track-session-writes.js is the input to BOTH .NET gates: run-tests.sh tiers on
// its output, and evidence-bundle-check.js's scope guard reads the same state.
// A false negative here silently disables both — the turn looks write-free, so
// no tests run and no evidence is required. A false positive makes every turn
// pay the full suite. Neither had coverage.
//
// classifyWrite returns the project-relative path to record, or null to ignore.

const PROJ = "/Users/x/project-ceres";

const call = (over = {}) =>
  classifyWrite(
    {
      session_id: "sess",
      tool_name: "Edit",
      tool_input: { file_path: `${PROJ}/ProjectCeres/Program.cs` },
      ...over,
    },
    PROJ
  );

// ── tracked extensions ────────────────────────────────────────────────────

for (const ext of ["cs", "ts", "tsx", "csproj", "sln"]) {
  test(`records a .${ext} write`, () => {
    assert.strictEqual(
      call({ tool_input: { file_path: `${PROJ}/ProjectCeres/File.${ext}` } }),
      `ProjectCeres/File.${ext}`
    );
  });
}

test("extension match is case-insensitive", () => {
  assert.strictEqual(
    call({ tool_input: { file_path: `${PROJ}/ProjectCeres/File.CS` } }),
    "ProjectCeres/File.CS"
  );
});

// ── ignored extensions ────────────────────────────────────────────────────
// .md/.json/.js are deliberately untracked: docs turns must stay tier 0, and
// hook .js edits are covered by scripts/test-hooks.sh instead.

for (const ext of ["md", "json", "js", "css", "yml"]) {
  test(`ignores a .${ext} write`, () => {
    assert.strictEqual(call({ tool_input: { file_path: `${PROJ}/docs/file.${ext}` } }), null);
  });
}

test("ignores a file with no extension", () => {
  assert.strictEqual(call({ tool_input: { file_path: `${PROJ}/Dockerfile` } }), null);
});

// ── tool filter ───────────────────────────────────────────────────────────

for (const tool of ["Edit", "Write", "MultiEdit"]) {
  test(`records a ${tool} call`, () => {
    assert.strictEqual(call({ tool_name: tool }), "ProjectCeres/Program.cs");
  });
}

test("NotebookEdit uses notebook_path", () => {
  assert.strictEqual(
    call({ tool_name: "NotebookEdit", tool_input: { notebook_path: `${PROJ}/analysis.ts` } }),
    "analysis.ts"
  );
});

for (const tool of ["Read", "Bash", "Grep", "Glob", "Skill"]) {
  test(`ignores a ${tool} call`, () => {
    assert.strictEqual(call({ tool_name: tool }), null);
  });
}

// ── project-tree boundary ─────────────────────────────────────────────────
// Out-of-tree edits (~/.claude/, /tmp) don't affect the build and must not
// make the turn look like it touched project code.

test("ignores a write outside the project tree", () => {
  assert.strictEqual(call({ tool_input: { file_path: "/Users/x/other-repo/Program.cs" } }), null);
});

test("ignores a write to the home .claude dir", () => {
  assert.strictEqual(call({ tool_input: { file_path: "/Users/x/.claude/settings.cs" } }), null);
});

test("ignores a sibling dir sharing the project's name prefix", () => {
  assert.strictEqual(
    call({ tool_input: { file_path: "/Users/x/project-ceres-backup/ProjectCeres/Program.cs" } }),
    null
  );
});

test("records a deeply nested in-tree path", () => {
  assert.strictEqual(
    call({ tool_input: { file_path: `${PROJ}/ProjectCeres.Tests/Unit/Reports/NetWorthTests.cs` } }),
    "ProjectCeres.Tests/Unit/Reports/NetWorthTests.cs"
  );
});

test("normalises a path containing ..", () => {
  assert.strictEqual(
    call({ tool_input: { file_path: `${PROJ}/ProjectCeres/../ProjectCeres/Program.cs` } }),
    "ProjectCeres/Program.cs"
  );
});

// ── malformed payloads ────────────────────────────────────────────────────

test("ignores a payload with no session_id", () => {
  assert.strictEqual(call({ session_id: undefined }), null);
});

test("ignores a payload with no file path", () => {
  assert.strictEqual(call({ tool_input: {} }), null);
});

test("ignores a payload with no tool_input", () => {
  assert.strictEqual(call({ tool_input: undefined }), null);
});

test("ignores an empty payload without throwing", () => {
  assert.strictEqual(classifyWrite({}, PROJ), null);
  assert.strictEqual(classifyWrite(undefined, PROJ), null);
});
