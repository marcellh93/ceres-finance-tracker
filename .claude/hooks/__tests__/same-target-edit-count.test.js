const { test } = require("node:test");
const assert = require("node:assert");
const path = require("path");
const {
  classifyEditRun,
  DOC_EXTENSIONS,
  ADVISORY_AT_DOC,
  ADVISORY_AT_CODE,
} = require(path.join(
  __dirname, "..", "..", "skills", "deep-fix-mode", "hooks", "same-target-edit-count.js"
));

// Fires deep-fix-mode when one file is edited repeatedly. The load-bearing
// idea is the repetition RATIO, not the raw count: 10 edits across 10 distinct
// fingerprints is a convergent sweep (silent); 10 edits across 2 is circling on
// the same block (fire). Getting this wrong either nags through every planned
// refactor or never catches real Fixation.
//
// classifyEditRun(total, distinct, ext) -> "silent" | "advisory" | "hard"

// ── below threshold: always silent ───────────────────────────────────────

test("silent below the code threshold even when fully repetitive", () => {
  assert.strictEqual(classifyEditRun(4, 1, ".cs"), "silent");
});

test("silent below the doc threshold", () => {
  assert.strictEqual(classifyEditRun(9, 1, ".md"), "silent");
});

test("a doc file gets a higher threshold than a source file", () => {
  assert.ok(ADVISORY_AT_DOC > ADVISORY_AT_CODE);
  // 6 edits: over the code bar, under the doc bar.
  assert.strictEqual(classifyEditRun(6, 1, ".cs"), "advisory");
  assert.strictEqual(classifyEditRun(6, 1, ".md"), "silent");
});

// ── the ratio rule: sweeps stay silent ───────────────────────────────────

test("a convergent sweep stays silent (ratio 1.0, every edit distinct)", () => {
  assert.strictEqual(classifyEditRun(10, 10, ".cs"), "silent");
});

test("ratio exactly at the 1.5 boundary stays silent (rule is > 1.5)", () => {
  assert.strictEqual(classifyEditRun(6, 4, ".cs"), "silent");
});

test("ratio just past 1.5 fires", () => {
  // 7 edits / 4 distinct = 1.75 > 1.5, and 7 < ADVISORY_AT_CODE + 3, so advisory.
  assert.strictEqual(classifyEditRun(7, 4, ".cs"), "advisory");
});

test("ratio past 1.5 AND past threshold+3 escalates to hard", () => {
  // 8 / 4 = 2.0 with total >= ADVISORY_AT_CODE + 3 (8) — the hard band.
  assert.strictEqual(classifyEditRun(8, 4, ".cs"), "hard");
});

// ── real fixation fires ──────────────────────────────────────────────────

test("advisory when repetitive and over the code threshold", () => {
  assert.strictEqual(classifyEditRun(5, 1, ".cs"), "advisory");
});

test("hard nudge at threshold + 3", () => {
  assert.strictEqual(classifyEditRun(ADVISORY_AT_CODE + 3, 2, ".cs"), "hard");
});

test("hard nudge for docs at the doc threshold + 3", () => {
  assert.strictEqual(classifyEditRun(ADVISORY_AT_DOC + 3, 2, ".md"), "hard");
});

test("many edits on very few fingerprints is the clearest fixation signal", () => {
  assert.strictEqual(classifyEditRun(20, 2, ".cs"), "hard");
});

// ── extension classification ─────────────────────────────────────────────

test("known doc extensions are treated as docs", () => {
  for (const ext of [".md", ".json", ".yml", ".yaml", ".toml", ".txt"]) {
    assert.ok(DOC_EXTENSIONS.has(ext), `${ext} should be a doc extension`);
    assert.strictEqual(classifyEditRun(6, 1, ext), "silent", `${ext} uses the doc threshold`);
  }
});

test("source extensions use the lower threshold", () => {
  for (const ext of [".cs", ".ts", ".tsx", ".js", ".py"]) {
    assert.strictEqual(classifyEditRun(6, 1, ext), "advisory", `${ext} uses the code threshold`);
  }
});

// ── degenerate input ─────────────────────────────────────────────────────

test("zero distinct fingerprints does not divide by zero", () => {
  assert.doesNotThrow(() => classifyEditRun(5, 0, ".cs"));
  assert.strictEqual(classifyEditRun(5, 0, ".cs"), "silent");
});

test("a single edit is always silent", () => {
  assert.strictEqual(classifyEditRun(1, 1, ".cs"), "silent");
});
