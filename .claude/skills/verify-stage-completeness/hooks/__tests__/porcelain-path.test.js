const { test } = require("node:test");
const assert = require("node:assert");
const { porcelainPath } = require("../evidence-bundle-check.js");

// Regression suite for the 2026-08-27 .claude/-path misparse.
//
// getChangedFiles parses `git status --porcelain=v1` lines, but safeExec calls
// .trim() on git's output, which strips the leading status space of the first line
// (" M path" -> "M path"). The old fixed slice(3) then ate a real path character,
// turning ".claude/foo.js" into "claude/foo.js". The dotless path dodged the
// DOCS_CONFIG_RE `\.claude\/` docs filter, so an edit to a .claude/ file looked like
// a code change and forced the code-turn evidence slots on a docs-only turn.
//
// porcelainPath must recover the exact path from every porcelain status shape,
// whether or not trim() has removed the leading space.

test("porcelainPath: untracked file (?? prefix)", () => {
  assert.strictEqual(porcelainPath("?? .claude/foo.js"), ".claude/foo.js");
});

test("porcelainPath: staged-modified (M in col 1, space in col 2)", () => {
  assert.strictEqual(porcelainPath("M  .claude/foo.js"), ".claude/foo.js");
});

test("porcelainPath: unstaged-modified, untrimmed (leading space)", () => {
  assert.strictEqual(porcelainPath(" M .claude/foo.js"), ".claude/foo.js");
});

test("porcelainPath: unstaged-modified after trim() strips the lead space", () => {
  // This is the exact shape that broke: safeExec trimmed " M .claude/..." to
  // "M .claude/...", and slice(3) then removed the dot. porcelainPath must not.
  assert.strictEqual(porcelainPath("M .claude/foo.js"), ".claude/foo.js");
});

test("porcelainPath: leading dot preserved (the specific regression)", () => {
  // Belt-and-braces: the failure mode was a lost leading dot on a .claude/ path.
  const out = porcelainPath("M .claude/skills/x/hook.js");
  assert.ok(out.startsWith(".claude/"), `expected a .claude/ path, got ${out}`);
});

test("porcelainPath: added file", () => {
  assert.strictEqual(porcelainPath("A  ProjectCeres/Services/X.cs"), "ProjectCeres/Services/X.cs");
});

test("porcelainPath: both-modified (MM)", () => {
  assert.strictEqual(porcelainPath("MM ProjectCeres/Services/X.cs"), "ProjectCeres/Services/X.cs");
});

test("porcelainPath: rename takes the destination", () => {
  assert.strictEqual(porcelainPath("R  old/path.cs -> new/path.cs"), "new/path.cs");
});

test("porcelainPath: a plain docs path is unchanged", () => {
  assert.strictEqual(
    porcelainPath("A  docs/superpowers/specs/2026-08-27-x-design.md"),
    "docs/superpowers/specs/2026-08-27-x-design.md",
  );
});
