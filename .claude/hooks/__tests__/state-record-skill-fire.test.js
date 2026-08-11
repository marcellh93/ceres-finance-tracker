const { test } = require("node:test");
const assert = require("node:assert");
const path = require("path");
const {
  applyEvent,
  freshState,
  TRACKED_EXT,
} = require(path.join(
  __dirname, "..", "..", "skills", "playbook", "hooks", "state-record-skill-fire.js"
));

// This hook writes the state ALL FOUR HARD playbook gates read. pre-commit-gate
// asks "did a verify skill fire AFTER the most recent code Write?" and answers
// it by checking whether writes_since_last_skill is empty. So:
//
//   missed write  -> the list stays empty -> the gate thinks code was verified
//                    when it wasn't, and lets an unverified commit through.
//   missed reset  -> the list never empties -> the gate blocks every commit
//                    forever, and gets bypassed out of habit.
//
// applyEvent(state, {tool_name, tool_input}, projectDir, now) -> new state

const PROJ = "/proj";
const AT = "2026-08-11T00:00:00.000Z";
const ev = (tool_name, tool_input) => ({ tool_name, tool_input });
const apply = (state, e) => applyEvent(state, e, PROJ, AT);

// ── recording a code write ───────────────────────────────────────────────

test("records a tracked code write", () => {
  const s = apply(freshState(), ev("Edit", { file_path: `${PROJ}/ProjectCeres/Program.cs` }));
  assert.deepStrictEqual(s.writes_since_last_skill.map((w) => w.file), ["ProjectCeres/Program.cs"]);
});

test("accumulates multiple writes", () => {
  let s = freshState();
  s = apply(s, ev("Write", { file_path: `${PROJ}/a.cs` }));
  s = apply(s, ev("Edit", { file_path: `${PROJ}/b.ts` }));
  assert.strictEqual(s.writes_since_last_skill.length, 2);
});

test("every tracked extension is recorded", () => {
  for (const ext of [...TRACKED_EXT]) {
    const s = apply(freshState(), ev("Edit", { file_path: `${PROJ}/f${ext}` }));
    assert.strictEqual(s.writes_since_last_skill.length, 1, `${ext} should be tracked`);
  }
});

test("NotebookEdit uses notebook_path", () => {
  const s = apply(freshState(), ev("NotebookEdit", { notebook_path: `${PROJ}/n.ts` }));
  assert.strictEqual(s.writes_since_last_skill.length, 1);
});

// ── writes that must NOT be recorded ─────────────────────────────────────
// A false record makes the gate block a commit with nothing to verify.

test("ignores an untracked extension", () => {
  const s = apply(freshState(), ev("Edit", { file_path: `${PROJ}/docs/x.md` }));
  assert.deepStrictEqual(s.writes_since_last_skill, []);
});

test("ignores a write outside the project tree", () => {
  const s = apply(freshState(), ev("Edit", { file_path: "/elsewhere/Program.cs" }));
  assert.deepStrictEqual(s.writes_since_last_skill, []);
});

test("ignores a sibling dir sharing the project name prefix", () => {
  const s = apply(freshState(), ev("Edit", { file_path: "/proj-backup/Program.cs" }));
  assert.deepStrictEqual(s.writes_since_last_skill, []);
});

test("ignores a non-write tool", () => {
  const s = apply(freshState(), ev("Read", { file_path: `${PROJ}/a.cs` }));
  assert.deepStrictEqual(s.writes_since_last_skill, []);
});

test("ignores a write with no path", () => {
  assert.deepStrictEqual(apply(freshState(), ev("Edit", {})).writes_since_last_skill, []);
});

// ── recording a skill fire ───────────────────────────────────────────────

test("records a skill fire by name", () => {
  const s = apply(freshState(), ev("Skill", { skill: "verify-against-codebase" }));
  assert.ok(s.fired.includes("verify-against-codebase"));
});

test("reads the skill name from any of the accepted shapes", () => {
  for (const ti of [{ skill: "x" }, { name: "x" }, { args: { skill: "x" } }]) {
    assert.ok(apply(freshState(), ev("Skill", ti)).fired.includes("x"), JSON.stringify(ti));
  }
});

test("fired list de-duplicates but the timestamp log does not", () => {
  let s = freshState();
  s = apply(s, ev("Skill", { skill: "sync-docs" }));
  s = apply(s, ev("Skill", { skill: "sync-docs" }));
  assert.deepStrictEqual(s.fired, ["sync-docs"]);
  assert.strictEqual(s.fired_with_timestamps.length, 2, "each fire is logged");
});

test("a skill fire with no resolvable name is ignored", () => {
  const s = apply(freshState(), ev("Skill", {}));
  assert.deepStrictEqual(s.fired, []);
});

// ── the reset: the load-bearing transition ───────────────────────────────

test("a skill fire clears writes_since_last_skill", () => {
  let s = freshState();
  s = apply(s, ev("Edit", { file_path: `${PROJ}/a.cs` }));
  assert.strictEqual(s.writes_since_last_skill.length, 1);
  s = apply(s, ev("Skill", { skill: "verify-against-codebase" }));
  assert.deepStrictEqual(s.writes_since_last_skill, [], "Phase F reads this to mean 'verified'");
});

test("a write AFTER a skill fire makes the code stale again", () => {
  let s = freshState();
  s = apply(s, ev("Edit", { file_path: `${PROJ}/a.cs` }));
  s = apply(s, ev("Skill", { skill: "verify-against-codebase" }));
  s = apply(s, ev("Edit", { file_path: `${PROJ}/b.cs` }));
  assert.deepStrictEqual(
    s.writes_since_last_skill.map((w) => w.file),
    ["b.cs"],
    "the post-verify write must re-arm the gate"
  );
});

test("a skill fire does NOT clear the fired history", () => {
  let s = freshState();
  s = apply(s, ev("Skill", { skill: "brainstorming" }));
  s = apply(s, ev("Skill", { skill: "sync-docs" }));
  assert.deepStrictEqual(s.fired, ["brainstorming", "sync-docs"]);
});

// ── state integrity ──────────────────────────────────────────────────────

test("applyEvent does not mutate the input state", () => {
  const before = freshState();
  const snapshot = JSON.stringify(before);
  apply(before, ev("Edit", { file_path: `${PROJ}/a.cs` }));
  assert.strictEqual(JSON.stringify(before), snapshot, "must return a new state, not mutate");
});

test("open_deferrals is preserved across events", () => {
  const s0 = { ...freshState(), open_deferrals: [{ id: "d1" }] };
  const s1 = apply(s0, ev("Edit", { file_path: `${PROJ}/a.cs` }));
  const s2 = apply(s1, ev("Skill", { skill: "x" }));
  assert.deepStrictEqual(s2.open_deferrals, [{ id: "d1" }]);
});
