const { test } = require("node:test");
const assert = require("node:assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const cp = require("child_process");

const {
  sessionWrittenFiles,
  getChangedFiles,
  getStageId,
  getTurnStartSha,
} = require("../evidence-bundle-check.js");

// Regression suite for the 2026-08-21 misattribution: the hook derived its
// changed-file list from `git diff HEAD~1 HEAD` — a property of committed
// history, not of the turn. On a turn that committed nothing it audited the
// PREVIOUS commit and, via getStageId's history walk, billed the work to an
// unrelated stage (CSRF work reported as stage 12.1). The gate must read what
// THIS turn changed.

function repo() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "ceres-turn-diff-"));
  const run = (cmd) => cp.execSync(cmd, { cwd: dir, stdio: ["ignore", "pipe", "ignore"] }).toString().trim();
  run("git init -q");
  run('git config user.email "t@e2e.local"');
  run('git config user.name "Test"');
  run("git config commit.gpgsign false");
  return { dir, run };
}

function commit(r, file, body, message) {
  fs.mkdirSync(path.dirname(path.join(r.dir, file)), { recursive: true });
  fs.writeFileSync(path.join(r.dir, file), body);
  r.run(`git add ${JSON.stringify(file)}`);
  r.run(`git commit -q -m ${JSON.stringify(message)}`);
  return r.run("git rev-parse HEAD");
}

function withProjectDir(dir, fn) {
  const prev = process.env.CLAUDE_PROJECT_DIR;
  process.env.CLAUDE_PROJECT_DIR = dir;
  const prevCwd = process.cwd();
  process.chdir(dir);
  try { return fn(); } finally {
    process.chdir(prevCwd);
    if (prev === undefined) delete process.env.CLAUDE_PROJECT_DIR;
    else process.env.CLAUDE_PROJECT_DIR = prev;
  }
}

// ---------------------------------------------------------------------------
// getChangedFiles — the turn, not the last commit.
// ---------------------------------------------------------------------------

test("getChangedFiles: sees uncommitted working-tree edits", () => {
  const r = repo();
  commit(r, "base.txt", "base\n", "chore: base");
  fs.mkdirSync(path.join(r.dir, "ProjectCeres"), { recursive: true });
  fs.writeFileSync(path.join(r.dir, "ProjectCeres", "Program.cs"), "x\n");

  const files = withProjectDir(r.dir, () => getChangedFiles("no-session", null));
  assert.ok(
    files.includes("ProjectCeres/Program.cs"),
    `expected the uncommitted file, got ${JSON.stringify(files)}`,
  );
});

test("getChangedFiles: does NOT return the previous commit's files on a no-change turn", () => {
  const r = repo();
  commit(r, "base.txt", "base\n", "chore: base");
  commit(r, "ProjectCeres/Other.cs", "other\n", "feat(12.1): someone else's work");

  // Clean tree, nothing committed this turn.
  const files = withProjectDir(r.dir, () => getChangedFiles("no-session", null));
  assert.deepStrictEqual(
    files, [],
    `a turn that changed nothing must report nothing, got ${JSON.stringify(files)}`,
  );
});

test("getChangedFiles: includes commits made during the turn", () => {
  const r = repo();
  const start = commit(r, "base.txt", "base\n", "chore: base");
  commit(r, "ProjectCeres/A.cs", "a\n", "feat(13.2): first");
  commit(r, "ProjectCeres/B.cs", "b\n", "feat(13.2): second");

  const files = withProjectDir(r.dir, () => getChangedFiles("no-session", start));
  assert.ok(files.includes("ProjectCeres/A.cs"), "first turn commit missing");
  assert.ok(files.includes("ProjectCeres/B.cs"), "second turn commit missing");
});

test("getChangedFiles: untracked files count as turn changes", () => {
  const r = repo();
  commit(r, "base.txt", "base\n", "chore: base");
  fs.mkdirSync(path.join(r.dir, "ProjectCeres.Tests"), { recursive: true });
  fs.writeFileSync(path.join(r.dir, "ProjectCeres.Tests", "NewSpec.cs"), "new\n");

  const files = withProjectDir(r.dir, () => getChangedFiles("no-session", null));
  assert.ok(
    files.includes("ProjectCeres.Tests/NewSpec.cs"),
    `untracked file missing, got ${JSON.stringify(files)}`,
  );
});

// ---------------------------------------------------------------------------
// getStageId — attribution must be honest about its source.
// ---------------------------------------------------------------------------

test("getStageId: reads the stage from a commit made during the turn", () => {
  const r = repo();
  const start = commit(r, "base.txt", "base\n", "chore: base");
  commit(r, "x.cs", "x\n", "fix(14.3): real work this turn");

  const got = withProjectDir(r.dir, () => getStageId(start));
  assert.strictEqual(got.stageId, "14.3");
  assert.strictEqual(got.source, "turn-commit");
});

test("getStageId: marks a history-inferred stage as such (the misattribution guard)", () => {
  const r = repo();
  commit(r, "base.txt", "base\n", "chore: base");
  commit(r, "y.cs", "y\n", "feat(12.1): unrelated earlier stage");

  // Nothing committed this turn → turnStartSha is HEAD, so no turn commits.
  const head = r.run("git rev-parse HEAD");
  const got = withProjectDir(r.dir, () => getStageId(head));
  assert.strictEqual(got.stageId, "12.1", "it still finds the id");
  assert.strictEqual(
    got.source, "history",
    "an id from earlier history must be labelled 'history' so runMain does not gate on it",
  );
});

test("getStageId: reports none when no commit carries a stage scope", () => {
  const r = repo();
  commit(r, "base.txt", "base\n", "chore: base");
  const got = withProjectDir(r.dir, () => getStageId(null));
  assert.strictEqual(got.stageId, null);
  assert.strictEqual(got.source, "none");
});

// ---------------------------------------------------------------------------
// getTurnStartSha
// ---------------------------------------------------------------------------

test("getTurnStartSha: returns null without a turn marker", () => {
  const r = repo();
  commit(r, "base.txt", "base\n", "chore: base");
  assert.strictEqual(withProjectDir(r.dir, () => getTurnStartSha(0, null)), null);
});

test("getTurnStartSha: picks the last commit older than the turn marker", () => {
  const r = repo();
  const base = commit(r, "base.txt", "base\n", "chore: base");
  // Marker in the future → every commit is older → the newest one is the start.
  const sha = withProjectDir(r.dir, () => getTurnStartSha(Date.now() + 60_000, null));
  assert.strictEqual(sha, base);
});

// ---------------------------------------------------------------------------
// sessionWrittenFiles — the floor that survives a stash.
// ---------------------------------------------------------------------------

test("sessionWrittenFiles: reads both the live file and the lastturn breadcrumb", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "ceres-written-"));
  const stateDir = path.join(dir, ".claude", "state", "run-tests");
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(path.join(stateDir, "s.json"), JSON.stringify({ files: ["a.cs"] }));
  fs.writeFileSync(path.join(stateDir, "s.lastturn.json"), JSON.stringify({ files: ["b.ts"] }));

  const got = sessionWrittenFiles(dir, "s").sort();
  assert.deepStrictEqual(got, ["a.cs", "b.ts"]);
});

test("sessionWrittenFiles: empty for an unknown session", () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "ceres-written-"));
  assert.deepStrictEqual(sessionWrittenFiles(dir, "unknown"), []);
  assert.deepStrictEqual(sessionWrittenFiles(dir, "nope"), []);
});
