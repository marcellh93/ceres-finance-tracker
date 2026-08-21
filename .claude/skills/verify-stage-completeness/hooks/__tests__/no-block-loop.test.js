const { test } = require("node:test");
const assert = require("node:assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const {
  sessionWroteCode,
  strikeCount,
  recordStrike,
  gapsFingerprint,
  normalizeGap,
  MAX_STRIKES,
} = require("../evidence-bundle-check.js");

// Regression suite for the 2026-08-07 unbounded-block loop: the hook read
// `git diff HEAD~1 HEAD` (a property of committed history, not of the turn) and
// exit-2'd on every Stop. Twelve identical blocks in one session, on turns that
// wrote no code. Both guards below must hold or that loop returns.

function tmpDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), "ceres-hook-test-"));
}

// ---------------------------------------------------------------------------
// Guard 1 — scope: a turn that wrote no tracked files is not gated at all.
// ---------------------------------------------------------------------------

test("sessionWroteCode: false when the session state file is absent", () => {
  assert.strictEqual(sessionWroteCode(tmpDir(), "no-such-session"), false);
});

test("sessionWroteCode: false when the state file is empty (0-byte tracker file)", () => {
  const dir = tmpDir();
  const stateDir = path.join(dir, ".claude", "state", "run-tests");
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(path.join(stateDir, "sess.json"), "");
  assert.strictEqual(sessionWroteCode(dir, "sess"), false);
});

test("sessionWroteCode: false when files array is present but empty", () => {
  const dir = tmpDir();
  const stateDir = path.join(dir, ".claude", "state", "run-tests");
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(path.join(stateDir, "sess.json"), JSON.stringify({ files: [] }));
  assert.strictEqual(sessionWroteCode(dir, "sess"), false);
});

test("sessionWroteCode: true when the session wrote a tracked file", () => {
  const dir = tmpDir();
  const stateDir = path.join(dir, ".claude", "state", "run-tests");
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(
    path.join(stateDir, "sess.json"),
    JSON.stringify({ files: ["ProjectCeres/Program.cs"] })
  );
  assert.strictEqual(sessionWroteCode(dir, "sess"), true);
});

// Hook ordering: run-tests.sh runs BEFORE this hook and truncates the live
// state file (`: > "$STATE_FILE"`). It leaves a .lastturn.json breadcrumb so
// downstream hooks can still see the turn's writes. Without this, the scope
// guard reads an empty file and silently disables the gate on real code turns.
test("sessionWroteCode: true from the .lastturn breadcrumb after run-tests.sh truncated the live file", () => {
  const dir = tmpDir();
  const stateDir = path.join(dir, ".claude", "state", "run-tests");
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(path.join(stateDir, "sess.json"), "");
  fs.writeFileSync(
    path.join(stateDir, "sess.lastturn.json"),
    JSON.stringify({ files: ["ProjectCeres/Program.cs"] })
  );
  assert.strictEqual(sessionWroteCode(dir, "sess"), true);
});

test("sessionWroteCode: false when both live file and breadcrumb are empty", () => {
  const dir = tmpDir();
  const stateDir = path.join(dir, ".claude", "state", "run-tests");
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(path.join(stateDir, "sess.json"), "");
  fs.writeFileSync(path.join(stateDir, "sess.lastturn.json"), JSON.stringify({ files: [] }));
  assert.strictEqual(sessionWroteCode(dir, "sess"), false);
});

test("sessionWroteCode: false on malformed JSON (fail-open, does not gate)", () => {
  const dir = tmpDir();
  const stateDir = path.join(dir, ".claude", "state", "run-tests");
  fs.mkdirSync(stateDir, { recursive: true });
  fs.writeFileSync(path.join(stateDir, "sess.json"), "{not json");
  assert.strictEqual(sessionWroteCode(dir, "sess"), false);
});

// ---------------------------------------------------------------------------
// Guard 2 — escapability: identical blocks must terminate.
// This is THE loop invariant. Same session + same stage + same gaps must not
// block unboundedly.
// ---------------------------------------------------------------------------

test("gapsFingerprint: stable for the same gap list, distinct for a different one", () => {
  const a = ["Missing: trace.zip", "Missing: console.json"];
  const b = ["Missing: trace.zip", "Missing: console.json"];
  const c = ["Missing: trace.zip"];
  assert.strictEqual(gapsFingerprint(a), gapsFingerprint(b));
  assert.notStrictEqual(gapsFingerprint(a), gapsFingerprint(c));
});

test("identical blocks stop blocking at MAX_STRIKES (the loop invariant)", () => {
  const dir = tmpDir();
  const gaps = ["Missing: trace.zip", "Missing: turn-shape.json"];
  const fp = gapsFingerprint(gaps);

  let blocked = 0;
  for (let turn = 0; turn < 12; turn++) {
    const strikes = strikeCount(dir, "sess", "12.9", fp);
    if (strikes >= MAX_STRIKES) break;
    recordStrike(dir, "sess", "12.9", fp);
    blocked++;
  }

  assert.strictEqual(
    blocked,
    MAX_STRIKES,
    `expected exactly ${MAX_STRIKES} blocks before escalation, got ${blocked}`
  );
});

test("a CHANGED gap list resets the strike counter (new problem still blocks)", () => {
  const dir = tmpDir();
  const first = gapsFingerprint(["Missing: trace.zip"]);
  for (let i = 0; i < MAX_STRIKES; i++) recordStrike(dir, "sess", "12.9", first);
  assert.ok(strikeCount(dir, "sess", "12.9", first) >= MAX_STRIKES);

  const second = gapsFingerprint(["Missing: rls-audit.psql"]);
  assert.strictEqual(strikeCount(dir, "sess", "12.9", second), 0);
});

test("strikes are scoped per stage — a different stage blocks independently", () => {
  const dir = tmpDir();
  const fp = gapsFingerprint(["Missing: trace.zip"]);
  for (let i = 0; i < MAX_STRIKES; i++) recordStrike(dir, "sess", "12.9", fp);
  assert.strictEqual(strikeCount(dir, "sess", "13.1", fp), 0);
});

test("strikes are scoped per session — a fresh session blocks independently", () => {
  const dir = tmpDir();
  const fp = gapsFingerprint(["Missing: trace.zip"]);
  for (let i = 0; i < MAX_STRIKES; i++) recordStrike(dir, "sess-a", "12.9", fp);
  assert.strictEqual(strikeCount(dir, "sess-b", "12.9", fp), 0);
});

// ---------------------------------------------------------------------------
// Guard 3 — volatile gap text must not defeat Guard 2.
//
// The 2026-08-21 recurrence: the guards above only ever fed gapsFingerprint
// bare strings ("Missing: trace.zip"), but the hook emits stale gaps carrying
// the turn-start timestamp, which changes every turn. Every turn therefore
// minted a new fingerprint, strikes stayed at 1, MAX_STRIKES was unreachable,
// and the Stop blocked unboundedly. Fingerprint gap IDENTITY, not rendered text.
// ---------------------------------------------------------------------------

const staleGaps = (turnStart) => [
  `Stale: build-matrix.json (mtime 2026-08-09T22:11:34.433Z, turn-start ${turnStart})`,
  `Stale: turn-shape.json (mtime 2026-08-09T22:20:01.475Z, turn-start ${turnStart})`,
];

test("gapsFingerprint: identical for the same stale slots across different turn-start values", () => {
  assert.strictEqual(
    gapsFingerprint(staleGaps("2026-08-21T00:16:06.336Z")),
    gapsFingerprint(staleGaps("2026-09-14T18:42:11.001Z")),
    "a timestamp embedded in the gap text must not change the fingerprint"
  );
});

test("gapsFingerprint: mtime differences on the same slots do not change the fingerprint", () => {
  const a = ["Stale: build-matrix.json (mtime 2026-01-01T00:00:00.000Z, turn-start T)"];
  const b = ["Stale: build-matrix.json (mtime 2026-07-07T07:07:07.007Z, turn-start T)"];
  assert.strictEqual(gapsFingerprint(a), gapsFingerprint(b));
});

test("gapsFingerprint: still distinguishes different slots and kinds", () => {
  const stale = gapsFingerprint(["Stale: build-matrix.json (mtime X, turn-start T)"]);
  const other = gapsFingerprint(["Stale: turn-shape.json (mtime X, turn-start T)"]);
  const missing = gapsFingerprint(["Missing: build-matrix.json (required because always)"]);
  assert.notStrictEqual(stale, other, "different slots must not collide");
  assert.notStrictEqual(stale, missing, "Stale and Missing on one slot are different problems");
});

test("gapsFingerprint: insensitive to gap ordering", () => {
  const gaps = staleGaps("2026-08-21T00:16:06.336Z");
  assert.strictEqual(gapsFingerprint(gaps), gapsFingerprint([...gaps].reverse()));
});

test("normalizeGap: leaves a non-slot gap (turn-shape validation message) untouched", () => {
  const gap = "turn-shape.json: missing/invalid 'fix_mentions' array";
  assert.strictEqual(normalizeGap(gap), gap);
});

test("stale blocks terminate at MAX_STRIKES even when turn-start changes every turn", () => {
  const dir = tmpDir();
  let blocked = 0;
  for (let turn = 0; turn < 12; turn++) {
    // A new turn-start on every iteration — exactly what the live hook produces.
    const fp = gapsFingerprint(staleGaps(`2026-08-21T00:${String(turn).padStart(2, "0")}:00.000Z`));
    if (strikeCount(dir, "sess", "12.1", fp) >= MAX_STRIKES) break;
    recordStrike(dir, "sess", "12.1", fp);
    blocked++;
  }
  assert.strictEqual(
    blocked,
    MAX_STRIKES,
    `stale gaps must escalate after ${MAX_STRIKES} blocks, got ${blocked} (unbounded loop)`
  );
});
