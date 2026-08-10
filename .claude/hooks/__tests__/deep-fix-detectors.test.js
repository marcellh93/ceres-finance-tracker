const { test } = require("node:test");
const assert = require("node:assert");
const path = require("path");

const LF = require(path.join(
  __dirname, "..", "..", "skills", "deep-fix-mode", "hooks", "loop-fingerprint.js"
));

// deep-fix-mode auto-fires when these detectors see repetition. A fingerprint
// that is too LOOSE collapses distinct calls together and nags mid-progress; too
// TIGHT and genuine circling never trips the skill — the failure the skill was
// built to catch. Neither had coverage.

const { normalizeInput, fingerprint, resultPreview, WARN_AT, BLOCK_AT } = LF;

// ── identical calls collide ──────────────────────────────────────────────

test("identical Bash commands share a fingerprint", () => {
  const a = fingerprint("Bash", { command: "dotnet test" }, "failed");
  const b = fingerprint("Bash", { command: "dotnet test" }, "failed");
  assert.strictEqual(a, b);
});

test("Bash normalization trims surrounding whitespace", () => {
  assert.strictEqual(
    normalizeInput("Bash", { command: "  ls -la  " }),
    normalizeInput("Bash", { command: "ls -la" })
  );
});

test("identical Reads of the same range collide", () => {
  const i = { file_path: "/a.cs", offset: 1, limit: 50 };
  assert.strictEqual(fingerprint("Read", i, "x"), fingerprint("Read", { ...i }, "x"));
});

// ── distinct calls must NOT collide ──────────────────────────────────────

test("different Bash commands differ", () => {
  assert.notStrictEqual(
    fingerprint("Bash", { command: "dotnet test" }, "x"),
    fingerprint("Bash", { command: "dotnet build" }, "x")
  );
});

test("same command with a DIFFERENT result differs (progress is not circling)", () => {
  assert.notStrictEqual(
    fingerprint("Bash", { command: "dotnet test" }, "3 failed"),
    fingerprint("Bash", { command: "dotnet test" }, "0 failed")
  );
});

test("Reads of different offsets differ", () => {
  assert.notStrictEqual(
    fingerprint("Read", { file_path: "/a.cs", offset: 1 }, "x"),
    fingerprint("Read", { file_path: "/a.cs", offset: 500 }, "x")
  );
});

test("Edits with different new_string differ", () => {
  assert.notStrictEqual(
    fingerprint("Edit", { file_path: "/a.cs", new_string: "one" }, "ok"),
    fingerprint("Edit", { file_path: "/a.cs", new_string: "two" }, "ok")
  );
});

test("Edits to different files differ", () => {
  assert.notStrictEqual(
    fingerprint("Edit", { file_path: "/a.cs", new_string: "x" }, "ok"),
    fingerprint("Edit", { file_path: "/b.cs", new_string: "x" }, "ok")
  );
});

test("Grep on different patterns differs", () => {
  assert.notStrictEqual(
    fingerprint("Grep", { pattern: "foo" }, "x"),
    fingerprint("Grep", { pattern: "bar" }, "x")
  );
});

// ── Edit normalization hashes content rather than storing it ─────────────

test("Edit normalization does not leak raw file content", () => {
  const secret = "PASSWORD=hunter2";
  const norm = normalizeInput("Edit", { file_path: "/a.cs", new_string: secret });
  assert.ok(!norm.includes(secret), "raw new_string must not appear in the fingerprint payload");
});

test("identical Edit content still collides via its hash", () => {
  const i = { file_path: "/a.cs", new_string: "same", old_string: "prev" };
  assert.strictEqual(normalizeInput("Edit", i), normalizeInput("Edit", { ...i }));
});

// ── resultPreview: whitespace-insensitive, bounded ───────────────────────

test("resultPreview collapses whitespace", () => {
  assert.strictEqual(resultPreview("a   b\n\nc"), "a b c");
});

test("resultPreview is bounded at 200 chars", () => {
  assert.strictEqual(resultPreview("x".repeat(500)).length, 200);
});

test("resultPreview handles null and objects", () => {
  assert.strictEqual(resultPreview(null), "");
  assert.strictEqual(resultPreview({ ok: true }), '{"ok":true}');
});

// ── malformed input must not throw ───────────────────────────────────────

test("normalizeInput tolerates a missing tool_input", () => {
  assert.doesNotThrow(() => normalizeInput("Bash", undefined));
  assert.doesNotThrow(() => normalizeInput("Edit", null));
});

test("an unknown tool falls back to whole-input serialization", () => {
  assert.strictEqual(normalizeInput("Whatever", { a: 1 }), JSON.stringify({ a: 1 }));
});

// ── thresholds ───────────────────────────────────────────────────────────

test("WARN_AT fires before BLOCK_AT", () => {
  assert.ok(WARN_AT < BLOCK_AT, `WARN_AT (${WARN_AT}) must be below BLOCK_AT (${BLOCK_AT})`);
  assert.ok(WARN_AT >= 2, "warning on the first repeat would nag");
});
