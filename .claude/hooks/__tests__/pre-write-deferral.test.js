const { test } = require("node:test");
const assert = require("node:assert");
const path = require("path");
const {
  PHRASES,
  STAGE_REF,
  CHECKBOX,
  REASON_MARKER,
  isWellFormedDeferral,
} = require(path.join(
  __dirname,
  "..",
  "..",
  "skills",
  "no-unjustified-deferrals",
  "hooks",
  "pre-write-deferral.js"
));

// This gate fires on deferral language in docs/ writes. Its 18 phrases carry
// negative lookarounds — /(deferred|punted) to (?!stage)/ must allow the
// legitimate "Deferred to Stage X", /(?<!high )low priority/ must not fire on
// "high priority". A broken lookaround either nags on every correct deferral
// (training the user to ignore it) or silently stops catching the bad ones.

const matches = (text) => PHRASES.some((re) => re.test(text));

// ── phrases that MUST fire ────────────────────────────────────────────────

const SHOULD_FIRE = [
  "This doesn't change the structural design.",
  "It doesn't block Phase 3.",
  "Leaving this as Phase 3 polish.",
  "follow-up: wire the remaining call sites",
  "Deferred to a later batch.",
  "punted to whenever we revisit this",
  "We can address this later.",
  "We can batch these with the next stage.",
  "Out of scope for this stage.",
  "low priority",
  "not blocking",
  "cosmetic only",
  "UX-only change",
  "nice-to-have",
  "can wait until Batch 6",
  "will be addressed when the SPA lands",
  "we should revisit the naming",
  "let's circle back on this",
  "tracked in the spec",
];

for (const text of SHOULD_FIRE) {
  test(`fires on: "${text.slice(0, 45)}"`, () => {
    assert.strictEqual(matches(text), true, `expected a phrase match for: ${text}`);
  });
}

// ── negative lookarounds: the load-bearing exclusions ─────────────────────

test('does NOT fire on the legitimate "Deferred to Stage 12"', () => {
  assert.strictEqual(matches("Deferred to Stage 12 with a receiving checkbox."), false);
});

test('does NOT fire on "deferred to stage" case-insensitively', () => {
  assert.strictEqual(matches("deferred to stage 13"), false);
});

test('does NOT fire on "high priority"', () => {
  assert.strictEqual(matches("This is high priority work."), false);
});

test('does NOT fire on "isn\'t blocking"', () => {
  assert.strictEqual(matches("The warning isn't blocking the build."), false);
});

test("does NOT fire on ordinary prose", () => {
  assert.strictEqual(matches("Shipped the reauthentication dialog and its tests."), false);
});

// ── the three-field escape hatch ──────────────────────────────────────────
// A properly formed deferral (stage ref + checkbox + reason marker) must pass
// silently, or the gate punishes correct behaviour.

test("well-formed deferral: stage ref + checkbox + tooling reason passes", () => {
  const text = [
    "Deferred to Stage 12.5 — tooling gap: no roles/admin-identity system exists.",
    "",
    "- [ ] Admin ticket-list UI (needs role claims)",
  ].join("\n");
  assert.strictEqual(isWellFormedDeferral(text), true);
});

test("well-formed deferral: 'already scheduled' reason also passes", () => {
  const text = "Deferred to Stage 13 — already scheduled there.\n\n- [ ] GDPR erasure trigger";
  assert.strictEqual(isWellFormedDeferral(text), true);
});

test("missing the checkbox is NOT well-formed", () => {
  assert.strictEqual(isWellFormedDeferral("Deferred to Stage 12 — tooling gap."), false);
});

test("missing the stage ref is NOT well-formed", () => {
  assert.strictEqual(isWellFormedDeferral("Tooling gap.\n\n- [ ] do the thing"), false);
});

test("missing the reason marker is NOT well-formed", () => {
  assert.strictEqual(isWellFormedDeferral("Deferred to Stage 12.\n\n- [ ] do the thing"), false);
});

test("empty text is NOT well-formed", () => {
  assert.strictEqual(isWellFormedDeferral(""), false);
});

// ── the individual field regexes ──────────────────────────────────────────

test("STAGE_REF matches both `Stage 12` and `Stage 12.9`", () => {
  assert.ok(STAGE_REF.test("Stage 12"));
  assert.ok(STAGE_REF.test("Stage 12.9"));
  assert.ok(!STAGE_REF.test("stage twelve"));
});

test("CHECKBOX matches an unchecked box, not a ticked one", () => {
  assert.ok(CHECKBOX.test("- [ ] pending item"));
  assert.ok(!CHECKBOX.test("- [x] done item"));
});

test("CHECKBOX matches when the box is not the first line", () => {
  assert.ok(CHECKBOX.test("Some prose first.\n\n- [ ] the item"));
});

test("REASON_MARKER matches the two valid reasons only", () => {
  assert.ok(REASON_MARKER.test("tooling gap"));
  assert.ok(REASON_MARKER.test("already scheduled"));
  assert.ok(REASON_MARKER.test("scheduled in Stage 13"));
  assert.ok(!REASON_MARKER.test("it is small and cosmetic"));
});
