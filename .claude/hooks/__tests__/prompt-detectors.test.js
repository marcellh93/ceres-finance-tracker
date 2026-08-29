const { test } = require("node:test");
const assert = require("node:assert");
const path = require("path");

const S = (p) => require(path.join(__dirname, "..", "..", "skills", p));
const { DECISION_PHRASES } = S("decision-mode/hooks/decision-detect.js");
const { PUSHBACK_PHRASES } = S("no-unjustified-deferrals/hooks/pushback-detect.js");
const { STAGE_START_PHRASES } = S("playbook/hooks/stage-start-detect.js");
const { FRONTEND_PHRASES } = S("playbook/hooks/frontend-touch-detect.js");

// Four UserPromptSubmit detectors. They inject advisory context, never deny —
// so the cost of a false positive is noise on every unrelated turn, and the
// cost of a false negative is a skill that silently stops firing. Both erode
// trust in the gate. None had coverage.

const fires = (list) => (text) => list.some((re) => re.test(text));
const decision = fires(DECISION_PHRASES);
const pushback = fires(PUSHBACK_PHRASES);
const stageStart = fires(STAGE_START_PHRASES);
const frontend = fires(FRONTEND_PHRASES);

// ── decision-detect ──────────────────────────────────────────────────────

for (const t of [
  "Should we ship this now?",
  "Is it worth the time?",
  "Make the call.",
  "What do you recommend?",
  "Does this fit the sprint?",
  "Is that in scope?",
  "What's the impact of this change?",
  "How much effort is that?",
  "Is this a refactor or a feature?",
  "Which approach should we take?",
  "Explain it in plain english.",
  "That's too much jargon.",
]) {
  test(`decision fires: "${t}"`, () => assert.strictEqual(decision(t), true));
}

test("decision does NOT fire on a plain status question", () => {
  assert.strictEqual(decision("Did the tests pass?"), false);
});

test("decision does NOT fire on a bare implementation request", () => {
  assert.strictEqual(decision("Add a null check to the parser."), false);
});

// `should we` and `what do you think` are as common in ordinary task talk as in
// a real tech-lead decision, and each false fire injects ~1.1KB. Tightened
// 2026-08-29 to require a decision-shaped object rather than any following verb.
test("decision does NOT fire on procedural 'should we'", () => {
  for (const t of [
    "Should we run the tests first?",
    "Should we start with the regex fix?",
    "Should I open the file?",
  ]) {
    assert.strictEqual(decision(t), false, `expected NO fire for: ${t}`);
  }
});

test("decision still fires on 'should we' + a real decision object", () => {
  for (const t of [
    "Should we defer this to Stage 13?",
    "Should we ship this now or wait?",
    "Should we use RLS or app-level filtering?",
  ]) {
    assert.strictEqual(decision(t), true, `expected a fire for: ${t}`);
  }
});

// ── pushback-detect ──────────────────────────────────────────────────────

for (const t of [
  "You deferred this again.",
  "This keeps getting punted.",
  "You forgot about the IP toggle.",
  "We agreed to fix that.",
  "This is still broken.",
  "You promised.",
  "Bring that back please.",
]) {
  test(`pushback fires: "${t}"`, () => assert.strictEqual(pushback(t), true));
}

test("pushback does NOT fire on neutral feedback", () => {
  assert.strictEqual(pushback("That looks good, thanks."), false);
});

test("pushback does NOT fire on a first-time bug report", () => {
  assert.strictEqual(pushback("The revoke button returns a 500."), false);
});

test("pushback requires the repeat marker, not just the verb", () => {
  // "you deferred X" alone is a statement of fact; the phrase needs
  // again/same/earlier to read as a callout.
  assert.strictEqual(pushback("You deferred the IP toggle to Stage 12.5."), false);
});

// ── stage-start-detect ───────────────────────────────────────────────────

for (const t of [
  "Let's start stage 13",
  "Let's move on to stage 12",
  "Proceed with stage 14",
  "Kick off stage 15",
  "What's next?",
]) {
  test(`stage-start fires: "${t}"`, () => assert.strictEqual(stageStart(t), true));
}

test("stage-start does NOT fire on a bug report", () => {
  assert.strictEqual(stageStart("The sessions page throws on load."), false);
});

test("stage-start does NOT fire on a question about a stage", () => {
  assert.strictEqual(stageStart("Is stage 12 done yet?"), false);
});

// These two were pinned as KNOWN BROAD on 2026-08-11 — documented, not endorsed.
// Tightened 2026-08-29: both fired on ordinary mid-task conversation and each
// injected ~1.2KB of advisory context, so the common case paid for the rare one.
//
// `let's build` now needs a roadmap-shaped object (a stage/feature reference or
// a UI noun), not any noun. `what's next` now needs to be the whole request, not
// a trailing aside after work already in flight.
test("stage-start does NOT fire on 'let's build' + an abstract noun", () => {
  for (const t of [
    "Let's build confidence in the hooks first.",
    "Let's build a shared understanding of the flow.",
  ]) {
    assert.strictEqual(stageStart(t), false, `expected NO fire for: ${t}`);
  }
});

test("stage-start still fires on 'let's build' + a roadmap object", () => {
  for (const t of [
    "Let's build stage 13",
    "Let's build the sessions page",
    "Let's build feature X from the roadmap",
  ]) {
    assert.strictEqual(stageStart(t), true, `expected a fire for: ${t}`);
  }
});

test("stage-start does NOT fire on 'what's next' as a mid-task aside", () => {
  for (const t of [
    "Nice, what's next?",
    "That worked — what's next then?",
  ]) {
    assert.strictEqual(stageStart(t), false, `expected NO fire for: ${t}`);
  }
});

test("stage-start still fires on 'what's next' as the whole request", () => {
  for (const t of ["What's next?", "what's next"]) {
    assert.strictEqual(stageStart(t), true, `expected a fire for: ${t}`);
  }
});

// ── frontend-touch-detect ────────────────────────────────────────────────

for (const t of [
  "Update ProjectCeres.Client/src/App.tsx",
  "Build the sessions page",
  "Polish the empty state",
  "Review the UI",
  "Make it bolder",
  "Add a design token",
  "This is frontend work",
]) {
  test(`frontend fires: "${t}"`, () => assert.strictEqual(frontend(t), true));
}

// Regression: the design-system phrase matched plurals only, so "Add a design
// token" / "a new design primitive" — both natural phrasings — silently skipped
// the orchestrator. Fixed 2026-08-11 by making the plural optional.
//
// The singular coverage is preserved, but the bare nouns now need a design-domain
// qualifier (2026-08-29) — see "does NOT fire on auth-domain 'token'" below.
test("frontend fires on SINGULAR design-system nouns", () => {
  for (const t of [
    "Add a design token",
    "a new design primitive",
    "one design recipe",
    "the design system",
  ]) {
    assert.strictEqual(frontend(t), true, `expected a fire for: ${t}`);
  }
});

// The design-system vocabulary also appears without the "design" prefix when the
// sentence already establishes the domain some other way. These must still fire.
test("frontend fires on design-system nouns qualified by context", () => {
  for (const t of [
    "Add a token to the design system",
    "Extract this into a primitive in ProjectCeres.Client",
    "Document the recipe in docs/design-system.md",
  ]) {
    assert.strictEqual(frontend(t), true, `expected a fire for: ${t}`);
  }
});

test("frontend does NOT fire on pure backend work", () => {
  assert.strictEqual(frontend("Add an index to the Transactions table."), false);
});

test("frontend does NOT fire on a migration request", () => {
  assert.strictEqual(frontend("Write an EF migration for SupportTicket."), false);
});

// The design-system pattern carried bare `tokens?` / `primitives?` / `recipes?`
// alternatives, so every non-UI sense of those words routed a backend or docs
// turn through the orchestrator (~18KB of SKILL.md). Phase 3 is an auth phase —
// "token" is a session/reset/JWT noun far more often than a design noun.
// Tightened 2026-08-29 to require a design-domain qualifier.
test("frontend does NOT fire on auth-domain 'token'", () => {
  for (const t of [
    "The reset token expires after 24 hours.",
    "Store the refresh token hashed, never in plaintext.",
    "The JWT token claims freeze at sign-in.",
  ]) {
    assert.strictEqual(frontend(t), false, `expected NO fire for: ${t}`);
  }
});

test("frontend does NOT fire on context-window 'tokens'", () => {
  assert.strictEqual(
    frontend("We're wasting a lot of tokens on normal tasks."),
    false
  );
});

test("frontend does NOT fire on non-UI 'primitives' or 'recipes'", () => {
  for (const t of [
    "Use the framework's concurrency primitives instead of a lock.",
    "The runbook has recipes for each failure mode.",
  ]) {
    assert.strictEqual(frontend(t), false, `expected NO fire for: ${t}`);
  }
});

// Known-broad: the bare filename pattern matches any .ts/.tsx token, so
// backend-only conversations that happen to name a test file trip it.
test("KNOWN BROAD: a bare .ts filename fires even in a hooks discussion", () => {
  assert.strictEqual(frontend("The tier-classify.ts helper is untested."), true);
});

// ── cross-detector independence ──────────────────────────────────────────
// Each hook injects its own context block; overlapping fires stack advisory
// noise onto one prompt.

test("a plain greeting fires nothing", () => {
  const t = "Hi, thanks for the update.";
  assert.deepStrictEqual(
    { decision: decision(t), pushback: pushback(t), stageStart: stageStart(t), frontend: frontend(t) },
    { decision: false, pushback: false, stageStart: false, frontend: false }
  );
});

test("a backend bug report fires nothing", () => {
  const t = "The reauth endpoint returns 401 for valid passwords.";
  assert.deepStrictEqual(
    { decision: decision(t), pushback: pushback(t), stageStart: stageStart(t), frontend: frontend(t) },
    { decision: false, pushback: false, stageStart: false, frontend: false }
  );
});
