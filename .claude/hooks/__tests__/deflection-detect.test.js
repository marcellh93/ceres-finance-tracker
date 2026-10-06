const { test } = require("node:test");
const assert = require("node:assert");
const { findMarkerProblems } = require("../stop-deflection-detect.js");

// Marker-convention hook (rewritten 2026-10-06 after the prose-scanning version
// false-positived on the assistant's own meta-discussion of the rule). The hook
// blocks ONLY a malformed <defer-defect> marker. Plain prose — including prose that
// quotes the forbidden "pre-existing / not mine / I'll flag it not fold it in"
// phrase while DISCUSSING the rule — contains no marker and must pass.

// --- must PASS (no problems) ---
const PASS = [
  // The exact meta-discussion that broke the old hook.
  'Yes — we have talked about this repeatedly, and the repo already had the rule. I said the forbidden thing anyway: "not mine to fix in this commit… pre-existing… I\'ll flag it, not fold it in." That is the exact soft-deflection you have corrected before.',
  // Normal work prose with no marker.
  "Fixed the NUL byte in the same commit. Tests 11/11 green.",
  // A VALID tooling-gap deferral.
  '<defer-defect reason="tooling-gap">The webkit timing flake needs Playwright trace-viewer v2, which is not yet on the CI image — evidence: run 12345 log line 88.</defer-defect>',
  // A VALID already-scheduled deferral.
  '<defer-defect reason="already-scheduled">The ES-render test is covered by the Stage 13.b `[ ]` line in roadmap-phase-three.md with a tripwire.</defer-defect>',
  // Discussing the marker syntax itself (no real malformed marker — it's in a code fence / prose).
  'The hook requires reason="tooling-gap" or reason="already-scheduled". Those are the only valid values.',
];

// --- must BLOCK (a malformed marker) ---
const BLOCK = [
  // Missing reason.
  ['<defer-defect>This NUL byte is pre-existing so I am leaving it.</defer-defect>', "missing-reason"],
  // Invalid reason — "pre-existing" is not a valid reason.
  ['<defer-defect reason="pre-existing">The raw NUL predates my work.</defer-defect>', "invalid-reason"],
  // Invalid reason — "out-of-scope".
  ['<defer-defect reason="out-of-scope">Not mine to fix in this commit.</defer-defect>', "invalid-reason"],
  // Valid reason but empty/too-thin body (no substantiation).
  ['<defer-defect reason="tooling-gap">n/a</defer-defect>', "empty-body"],
];

for (const [i, txt] of PASS.entries()) {
  test(`passes clean/valid text #${i + 1}`, () => {
    assert.deepStrictEqual(
      findMarkerProblems(txt), [],
      `expected no problems for: ${txt.slice(0, 80)}`,
    );
  });
}

for (const [i, [txt, kind]] of BLOCK.entries()) {
  test(`blocks malformed <defer-defect> (${kind}) #${i + 1}`, () => {
    const problems = findMarkerProblems(txt);
    assert.ok(problems.length > 0, `expected a problem for: ${txt.slice(0, 80)}`);
    assert.strictEqual(problems[0].kind, kind, `expected kind ${kind}, got ${problems[0].kind}`);
  });
}

// The regression that motivated the rewrite: meta-discussion quoting the forbidden
// phrase must NOT block (the old prose-scanner false-positived on exactly this).
test("meta-discussion quoting the forbidden phrase does not block", () => {
  const meta =
    'The forbidden move is: a defect framed as "pre-existing / not mine" plus "I\'ll flag it, not fold it in". This hook no longer scans prose, so explaining it is safe.';
  assert.deepStrictEqual(findMarkerProblems(meta), []);
});
