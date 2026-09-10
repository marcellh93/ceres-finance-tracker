const { test } = require("node:test");
const assert = require("node:assert");
const { scan, isRoadmap, statusVerdict } = require("../roadmap-consistency-check.js");

// PostToolUse advisory that enforces the roadmap's checklist-marker & deferral rule.
// The three defect shapes it was built to catch (all real, from §12.5 / §12.8.1 / §12.8.3
// on 2026-09-10 — a heading flipped to Done while its Status line stayed ❌ Open).

test("isRoadmap matches roadmap-phase-*.md only", () => {
  assert.strictEqual(isRoadmap("docs/roadmap-phase-three.md"), true);
  assert.strictEqual(isRoadmap("/abs/docs/roadmap-phase-one.md"), true);
  assert.strictEqual(isRoadmap("docs/models.md"), false);
  assert.strictEqual(isRoadmap("docs/roadmap-notes.txt"), false);
  assert.strictEqual(isRoadmap(""), false);
});

// (1) heading ↔ status contradiction — the exact §12.8.3 shape
test("flags heading=Done but Status=Open", () => {
  const text = [
    "### Stage 12.8.3 — Promote the warning strip ✅ Done (2026-09-08)",
    "",
    "**Status: ❌ Open.** Surfaced 2026-08-29 while building the banner.",
  ].join("\n");
  const f = scan(text);
  assert.strictEqual(f.length, 1);
  assert.match(f[0], /heading says DONE but Status says open/);
});

test("passes when heading=Done and Status=Done", () => {
  const text = [
    "### Stage 12.8.3 — Promote the warning strip ✅ Done (2026-09-08)",
    "",
    "**Status: ✅ Done (2026-09-08, `abc`).** Surfaced 2026-08-29; built when the fifth caller crossed.",
  ].join("\n");
  assert.deepStrictEqual(scan(text), []);
});

// the §12.5 shape: Status verdict is Done, but its narrative contains the word "deferred"
test("does not false-flag when 'deferred' appears only in Status history, not the verdict", () => {
  const text = [
    "## Stage 12.5 — Deferred from Stage 12 ✅ Done (2026-09-08)",
    "",
    "**Status: ✅ Done (2026-09-08).** Originally deferred 2026-06-30; all now built this session.",
  ].join("\n");
  assert.deepStrictEqual(scan(text), []);
});

test("statusVerdict isolates the leading sentence", () => {
  assert.match(statusVerdict("✅ Done (2026-09-08). Originally deferred 2026-06-30."), /Done/);
  assert.doesNotMatch(statusVerdict("✅ Done (2026-09-08). Originally deferred."), /deferred/i);
});

// (2) [x] whose text says it is deferred/moved WITH NO destination named
test("flags a [x] item that says deferred/not-built with no receiving stage", () => {
  const f = scan("- [x] Register the cron — not built here, still open pending a host");
  assert.ok(f.some((x) => /\[x\] item says it is deferred\/not-built with no receiving/.test(x)));
});

test("does not flag a legitimate [x] that names a homed sub-part", () => {
  // Item shipped; only a minor sub-affordance is homed elsewhere — allowed.
  const f = scan("- [x] Admin list/triage SPA surface shipped. **Admin nav link deferred → Stage 15.8** (receiving [ ] there).");
  // 'deferred' here is about the sub-part; our regex targets whole-item phrases only.
  assert.deepStrictEqual(f, []);
});

// (3) [→] must have why AND where
test("flags a [→] deferral missing both why and where", () => {
  const f = scan("- [→] Opt-out toggle in Settings notification preferences.");
  assert.ok(f.some((x) => /no WHY/.test(x)));
  assert.ok(f.some((x) => /no WHERE/.test(x)));
});

test("passes a [→] deferral that names why and where", () => {
  const line =
    "- [→] Opt-out toggle — **DEFERRED. Why:** the prefs surface does not exist (user-authorised). **Where:** homed at Stage 17.";
  assert.deepStrictEqual(scan(line), []);
});

test("flags a [→] with a where but no why", () => {
  const f = scan("- [→] Something — homed at Stage 16 § Scheduled jobs.");
  assert.deepStrictEqual(f, [
    // only the WHY finding
    ...f.filter((x) => /no WHY/.test(x)),
  ]);
  assert.ok(f.some((x) => /no WHY/.test(x)));
  assert.ok(!f.some((x) => /no WHERE/.test(x)));
});

// Refinements from the first live run (2026-09-10):
test("does not flag a [x] whose sub-part is deferred to a NAMED destination", () => {
  // Real §Stage 6 line: enforcement shipped; only the UI toggle is homed at Stage 12.
  const line =
    "- [x] Per-session IP enforcement honoured (server-side wired 6a; UI toggle is deferred to Stage 12 — Sessions SPA page).";
  assert.deepStrictEqual(scan(line), []);
});

test("does not flag a heading whose TITLE contains 'Deferred' descriptively, with Status Done", () => {
  const text = [
    "## Stage 9.1.7 — Deferred code-simplification sweeps (test-project + React)",
    "",
    "**Status: ✅ Done (2026-06-15).** Split out of Stage 9.1.6.g.",
  ].join("\n");
  assert.deepStrictEqual(scan(text), []);
});

test("still flags a heading whose PARENTHETICAL suffix says (not scheduled) with Status Done", () => {
  const text = [
    "### Stage 12.8.1 — Email-change follow-ups (not scheduled)",
    "",
    "**Status: ✅ Done (2026-09-07).** Both items resolved.",
  ].join("\n");
  assert.strictEqual(scan(text).length, 1);
  assert.match(scan(text)[0], /heading says open\/deferred but Status says DONE/);
});

test("clean roadmap slice yields no findings", () => {
  const text = [
    "### Stage 12.11 — Dev server stale SPA shell ✅ Done (2026-09-07)",
    "",
    "**Status: ✅ Done (2026-09-07, Option B).** The test host runs as Testing.",
    "",
    "- [x] Option B shipped 2026-09-07.",
  ].join("\n");
  assert.deepStrictEqual(scan(text), []);
});
