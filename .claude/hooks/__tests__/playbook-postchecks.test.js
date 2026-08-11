const { test } = require("node:test");
const assert = require("node:assert");
const path = require("path");

const S = (p) => require(path.join(__dirname, "..", "..", "skills", "playbook", "hooks", p));
const { FRONTEND_SIGNALS, isSpecPath, readNewContent } = S("frontend-spec-postcheck.js");
const { isBrainstormSkill } = S("brainstorm-needs-decision-mode.js");

// Two PostToolUse advisories. frontend-spec-postcheck nudges when a spec under
// docs/superpowers/specs/ carries frontend signals but frontend-orchestrator
// never fired; brainstorm-needs-decision-mode nudges when brainstorming fires
// without decision-mode. Both are advisory, so a false negative is a rule that
// silently stops being enforced.

const signal = (t) => FRONTEND_SIGNALS.some((re) => re.test(t));

// ── frontend signal detection ────────────────────────────────────────────

for (const t of [
  "Update ProjectCeres.Client/src/App.tsx",
  "the Sessions.tsx page",
  "a shadcn dialog",
  "use tailwind utilities",
  "the empty state copy",
  "a useEffect hook",
  "see docs/design-system.md",
  "@/components/ui/button",
]) {
  test(`spec signal fires: "${t}"`, () => assert.strictEqual(signal(t), true));
}

// Regression: the alias alternative sat inside /\b(...)\b/, and a leading \b
// before `@` requires a word char immediately BEFORE the @ — so "@/components"
// never matched while nonsense like "x@/components" did. Path aliases are a
// primary frontend signal per CLAUDE.md, so this quietly weakened the detector.
// Fixed 2026-08-11 by giving the aliases their own alternative.
test("path aliases fire in REAL usage, not just when glued to a word", () => {
  for (const t of ["@/components/ui/button", "import from @/lib/utils", "see @/hooks/use-toast"]) {
    assert.strictEqual(signal(t), true, `expected a signal for: ${t}`);
  }
});

test("no signal on a pure backend spec", () => {
  assert.strictEqual(
    signal("Add a SupportTicket entity with an RLS policy and an EF migration."),
    false
  );
});

test("no signal on a docs-only spec", () => {
  assert.strictEqual(signal("Update the roadmap checklist and the changelog."), false);
});

// Sibling regression: frontend-touch-detect matched design-system nouns in the
// plural only, so "a design token" silently skipped the orchestrator (fixed
// 2026-08-11). This list carries the same phrase — pin its behaviour here too.
test("design-system nouns: plural fires", () => {
  assert.strictEqual(signal("update the design tokens"), true);
});

test("KNOWN: singular 'design token' does NOT fire this list", () => {
  // Documented, not endorsed. Unlike its sibling this list has no bare
  // /tokens?/ alternative, so the singular misses. Left as-is because a spec
  // naming a single token in isolation is rare; pinned so a future change is
  // deliberate rather than accidental.
  assert.strictEqual(signal("update the design token"), false);
});

// ── isSpecPath ───────────────────────────────────────────────────────────

test("matches a spec markdown file", () => {
  assert.strictEqual(isSpecPath("docs/superpowers/specs/2026-08-11-x.md"), true);
});

test("does NOT match a plan file", () => {
  assert.strictEqual(isSpecPath("docs/superpowers/plans/2026-08-11-x.md"), false);
});

test("does NOT match a non-markdown file in the spec dir", () => {
  assert.strictEqual(isSpecPath("docs/superpowers/specs/notes.txt"), false);
});

test("does NOT match an unrelated doc", () => {
  assert.strictEqual(isSpecPath("docs/roadmap-phase-three.md"), false);
});

test("handles an empty path", () => {
  assert.strictEqual(isSpecPath(""), false);
  assert.strictEqual(isSpecPath(undefined), false);
});

// ── readNewContent: every tool shape reaches the matcher ─────────────────

test("reads Write content", () => {
  assert.strictEqual(readNewContent("Write", { content: "x" }, "/f.md"), "x");
});

test("reads Edit new_string", () => {
  assert.strictEqual(readNewContent("Edit", { new_string: "x" }, "/f.md"), "x");
});

test("concatenates MultiEdit new_strings", () => {
  assert.strictEqual(
    readNewContent("MultiEdit", { edits: [{ new_string: "a" }, { new_string: "b" }] }, "/f.md"),
    "a\nb"
  );
});

test("returns empty string when a nonexistent file is the only source", () => {
  assert.strictEqual(readNewContent("Unknown", {}, "/no/such/file.md"), "");
});

// ── brainstorm-needs-decision-mode ───────────────────────────────────────

test("recognises the brainstorming skill by its full name", () => {
  assert.strictEqual(isBrainstormSkill({ skill: "superpowers:brainstorming" }), true);
});

test("reads the skill name from any accepted shape", () => {
  assert.strictEqual(isBrainstormSkill({ name: "superpowers:brainstorming" }), true);
  assert.strictEqual(isBrainstormSkill({ args: { skill: "superpowers:brainstorming" } }), true);
});

test("does NOT fire for a different skill", () => {
  assert.strictEqual(isBrainstormSkill({ skill: "sync-docs" }), false);
});

test("does NOT fire for the unprefixed name", () => {
  // The constitution names the skill with its plugin prefix; an unprefixed
  // match would fire on a differently-scoped skill of the same short name.
  assert.strictEqual(isBrainstormSkill({ skill: "brainstorming" }), false);
});

test("handles an empty tool_input", () => {
  assert.strictEqual(isBrainstormSkill({}), false);
  assert.strictEqual(isBrainstormSkill(undefined), false);
});
