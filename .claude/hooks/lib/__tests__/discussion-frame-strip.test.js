const { test } = require("node:test");
const assert = require("node:assert");
const {
  stripDiscussionFrames,
  isMetaContext,
  META_MARKERS,
} = require("../discussion-frame-strip.js");

// Shared normalizer. pre-write-deferral.js and turn-shape-generator.js both run
// text through this BEFORE phrase-matching, so a bug here changes several gates
// at once: strip too much and a real deferral slips past the gate; strip too
// little and the gate fires on text the assistant is quoting rather than
// asserting (the 2026-05-22 false-positive audit that motivated the helper).

// ── fenced code ──────────────────────────────────────────────────────────

test("removes a fenced code block", () => {
  assert.strictEqual(stripDiscussionFrames("a\n```\nlow priority\n```\nb").includes("low priority"), false);
});

test("keeps prose outside the fence", () => {
  const out = stripDiscussionFrames("keep me\n```\ndrop me\n```\nkeep me too");
  assert.ok(out.includes("keep me") && out.includes("keep me too"));
  assert.ok(!out.includes("drop me"));
});

test("removes multiple fenced blocks", () => {
  const out = stripDiscussionFrames("```\nx\n```\nmid\n```\ny\n```");
  assert.ok(!out.includes("x") && !out.includes("y"));
  assert.ok(out.includes("mid"));
});

test("an unterminated fence is left alone (no runaway strip)", () => {
  assert.ok(stripDiscussionFrames("real text\n```\ndangling").includes("real text"));
});

// ── blockquotes ──────────────────────────────────────────────────────────

test("removes a blockquoted line", () => {
  assert.strictEqual(stripDiscussionFrames("a\n> low priority\nb").includes("low priority"), false);
});

test("removes only the quoted lines", () => {
  const out = stripDiscussionFrames("keep me\n> quoted\nkeep me too");
  assert.ok(out.includes("keep me") && out.includes("keep me too") && !out.includes("quoted"));
});

test("does NOT remove a mid-line > character", () => {
  assert.ok(stripDiscussionFrames("if a > b then low priority").includes("low priority"));
});

// ── stage headings ───────────────────────────────────────────────────────

test("removes a stage heading", () => {
  assert.strictEqual(stripDiscussionFrames("## Stage 12 — Sessions\nreal").includes("Stage 12"), false);
});

test("removes a decimal stage heading", () => {
  assert.strictEqual(stripDiscussionFrames("## Stage 9.5 — Hardening\nreal").includes("Stage 9.5"), false);
});

test("keeps a non-stage heading", () => {
  assert.ok(stripDiscussionFrames("## Verification checklist\nreal").includes("Verification checklist"));
});

// ── tool payloads ────────────────────────────────────────────────────────

test("removes an echoed tool_use payload", () => {
  const out = stripDiscussionFrames('before {"tool_use_id":"abc","x":1} after');
  assert.ok(!out.includes("tool_use_id"));
  assert.ok(out.includes("before") && out.includes("after"));
});

test("removes a transcript_path payload", () => {
  assert.ok(!stripDiscussionFrames('{"transcript_path":"/x.jsonl"}').includes("transcript_path"));
});

test("leaves ordinary JSON-looking prose alone", () => {
  const t = '{"files":["a.cs"]}';
  assert.strictEqual(stripDiscussionFrames(t), t);
});

// ── idempotence / state ──────────────────────────────────────────────────
// All four patterns are module-level with /g. String.replace resets lastIndex
// (unlike .test()), but pin it — a refactor to .test() would silently break
// every second call.

test("repeated calls on the same input give the same result", () => {
  const t = "a\n```\ndrop\n```\n> quoted\nb";
  assert.strictEqual(stripDiscussionFrames(t), stripDiscussionFrames(t));
});

test("stripping is idempotent", () => {
  const once = stripDiscussionFrames("a\n```\ndrop\n```\nb");
  assert.strictEqual(stripDiscussionFrames(once), once);
});

test("handles empty and undefined input", () => {
  assert.strictEqual(stripDiscussionFrames(""), "");
  assert.strictEqual(stripDiscussionFrames(undefined), "");
});

test("text with no frames passes through unchanged", () => {
  const t = "This is ordinary prose about low priority work.";
  assert.strictEqual(stripDiscussionFrames(t), t);
});

// ── isMetaContext ────────────────────────────────────────────────────────
// The short-circuit: when the assistant is DISCUSSING a hook, the caller
// should exit before phrase-matching at all.

for (const t of [
  "the hook fired on my last turn",
  "that's a false positive",
  "see the audit trail",
  "check log.jsonl",
  "hook recursion is the risk",
  "Recovery options:",
  "Matched phrases: /low priority/i",
]) {
  test(`isMetaContext true: "${t}"`, () => assert.strictEqual(isMetaContext(t), true));
}

test("isMetaContext false on ordinary work prose", () => {
  assert.strictEqual(isMetaContext("Deferred to Stage 12.5 — tooling gap."), false);
});

test("isMetaContext false on a plain bug report", () => {
  assert.strictEqual(isMetaContext("The revoke button returns a 500."), false);
});

test("isMetaContext handles empty input", () => {
  assert.strictEqual(isMetaContext(""), false);
  assert.strictEqual(isMetaContext(undefined), false);
});

test("META_MARKERS is a non-empty array of regexes", () => {
  assert.ok(Array.isArray(META_MARKERS) && META_MARKERS.length > 0);
  for (const re of META_MARKERS) assert.ok(re instanceof RegExp);
});

// ── the combination the callers actually use ─────────────────────────────

test("a deferral phrase inside a fence does not survive stripping", () => {
  const text = "Here's the rejected wording:\n```\nthis is low priority\n```\nThe real plan is Stage 12.5.";
  assert.strictEqual(/low priority/i.test(stripDiscussionFrames(text)), false);
});

test("a deferral phrase in real prose DOES survive stripping", () => {
  const text = "Skipping the toggle, it's low priority.";
  assert.strictEqual(/low priority/i.test(stripDiscussionFrames(text)), true);
});
