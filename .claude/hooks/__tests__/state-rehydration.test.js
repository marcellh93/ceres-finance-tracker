const { test } = require("node:test");
const assert = require("node:assert");
const path = require("path");

const H = (f) => require(path.join(__dirname, "..", "..", "skills", "state-rehydration", "hooks", f));
const { buildSnapshot } = H("snapshot-on-turn-end.js");
const { summarize } = H("detect-compaction.js");
const { tailLastAssistantThought } = H("snapshot-on-pre-compact.js");

// These three only matter AFTER a context compaction, so a break degrades
// silently until exactly the moment the state is needed and cannot be
// reconstructed. detect-compaction's summarize() runs at SessionStart right
// after the compaction — a throw there is the worst possible time to fail.

const snap = (over = {}) => ({
  session_id: "s",
  snapshot_timestamp: "2026-08-12T00:00:00.000Z",
  trigger: "PostToolUse-turn-end",
  skills_fired: [],
  last_code_writes: [],
  open_deferrals: [],
  current_artifacts: {},
  last_assistant_thought: "",
  snapshot_version: 1,
  ...over,
});

// ── summarize: must never throw ──────────────────────────────────────────
// It reads a JSON file written by another process; every field is untrusted.

test("summarize handles a fully populated snapshot", () => {
  const out = summarize(
    snap({
      skills_fired: ["brainstorming", "sync-docs"],
      last_code_writes: [{ file: "a.cs" }, { file: "b.cs" }],
      open_deferrals: [{ stage: "12.5" }],
      current_artifacts: {
        open_spec: { path: "docs/superpowers/specs/x.md" },
        open_plan: { path: "docs/superpowers/plans/y.md" },
        open_roadmap: { path: "docs/roadmap-phase-three.md" },
      },
      last_assistant_thought: "mid-procedure on the U2 fix",
    })
  );
  assert.ok(out.includes("brainstorming"));
  assert.ok(out.includes("b.cs"), "names the MOST RECENT write");
  assert.ok(out.includes("12.5"));
  assert.ok(out.includes("docs/roadmap-phase-three.md"));
});

test("summarize handles an empty snapshot", () => {
  const out = summarize(snap());
  assert.ok(out.includes("skills_fired: 0 entries"));
  assert.ok(out.includes("last_code_writes: 0 entries"));
});

// Regression: last_code_writes[last].file was dereferenced without a guard, so
// a null entry threw a TypeError at SessionStart — right after a compaction,
// when the snapshot is the only surviving state. Found 2026-08-12.
test("summarize does NOT throw on a null entry in last_code_writes", () => {
  assert.doesNotThrow(() => summarize(snap({ last_code_writes: [null] })));
});

test("summarize does NOT throw on an entry missing .file", () => {
  assert.doesNotThrow(() => summarize(snap({ last_code_writes: [{ at: "ts" }] })));
});

test("summarize does NOT throw on a null entry in open_deferrals", () => {
  assert.doesNotThrow(() => summarize(snap({ open_deferrals: [null] })));
});

test("summarize tolerates non-array fields", () => {
  assert.doesNotThrow(() =>
    summarize(snap({ skills_fired: "nope", last_code_writes: null, open_deferrals: 42 }))
  );
});

test("summarize tolerates a missing current_artifacts", () => {
  assert.doesNotThrow(() => summarize(snap({ current_artifacts: undefined })));
});

test("summarize truncates a long assistant thought", () => {
  const out = summarize(snap({ last_assistant_thought: "x".repeat(500) }));
  const line = out.split("\n").find((l) => l.includes("last_assistant_thought"));
  assert.ok(line.length < 260, `expected truncation, got ${line.length} chars`);
});

test("summarize collapses newlines in the thought", () => {
  const out = summarize(snap({ last_assistant_thought: "line one\nline two" }));
  assert.ok(!out.split("last_assistant_thought")[1].startsWith(': "line one\n'));
});

// ── buildSnapshot: the shape detect-compaction depends on ────────────────

test("buildSnapshot returns every field summarize reads", () => {
  const s = buildSnapshot("no-such-session", "test-trigger");
  for (const k of [
    "session_id", "snapshot_timestamp", "trigger", "skills_fired",
    "last_code_writes", "open_deferrals", "current_artifacts", "snapshot_version",
  ]) {
    assert.ok(k in s, `missing field: ${k}`);
  }
});

test("buildSnapshot records the trigger it was given", () => {
  assert.strictEqual(buildSnapshot("no-such-session", "PreCompact").trigger, "PreCompact");
});

test("buildSnapshot degrades to empty arrays for an unknown session", () => {
  const s = buildSnapshot("definitely-not-a-real-session", "t");
  assert.deepStrictEqual(s.skills_fired, []);
  assert.deepStrictEqual(s.last_code_writes, []);
  assert.deepStrictEqual(s.open_deferrals, []);
});

test("a snapshot built for an unknown session still summarizes without throwing", () => {
  assert.doesNotThrow(() => summarize(buildSnapshot("definitely-not-a-real-session", "t")));
});

test("buildSnapshot stamps an ISO timestamp", () => {
  assert.match(buildSnapshot("x", "t").snapshot_timestamp, /^\d{4}-\d{2}-\d{2}T[\d:.]+Z$/);
});

// ── tailLastAssistantThought ─────────────────────────────────────────────

test("returns empty string for a missing transcript", () => {
  assert.strictEqual(tailLastAssistantThought("/no/such/transcript.jsonl"), "");
});

test("returns empty string for an empty path", () => {
  assert.strictEqual(tailLastAssistantThought(""), "");
  assert.strictEqual(tailLastAssistantThought(undefined), "");
});
