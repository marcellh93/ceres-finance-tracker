const { test } = require("node:test");
const assert = require("node:assert");
const path = require("path");

// The four PreToolUse gates can DENY a tool call. A false negative silently
// removes a safety net (the protected-path gate exists because of a near-miss
// bulk delete); a false positive blocks legitimate work. Neither had coverage
// until now. These pin the decision predicates — the part that decides whether
// a call is gated at all.

const R = (p) => require(path.join(__dirname, "..", p));
const RS = (p) => require(path.join(__dirname, "..", "..", "skills", p));

const { extractCandidatePaths } = R("pre-protected-path-gate.js");
const { isGitCommit } = RS("playbook/hooks/pre-commit-gate.js");
const { isStageClose, countUncheckedInClosingStage } = RS("playbook/hooks/pre-stage-close-gate.js");
const { extractProposedContent } = RS("playbook/hooks/pre-spec-write-gate.js");

// ── pre-protected-path-gate: extractCandidatePaths ────────────────────────
// Whatever this misses is never checked against the .protected markers.

test("protected-path: extracts an rm target", () => {
  const got = extractCandidatePaths("Bash", { command: "rm -rf .claude/skills/dev-teacher" });
  assert.ok(got.includes(".claude/skills/dev-teacher"), `got ${JSON.stringify(got)}`);
});

test("protected-path: extracts multiple rm targets, skipping flags", () => {
  const got = extractCandidatePaths("Bash", { command: "rm -f a.txt b.txt" });
  assert.ok(got.includes("a.txt") && got.includes("b.txt"), `got ${JSON.stringify(got)}`);
  assert.ok(!got.some((p) => p.startsWith("-")), "flags must not be treated as paths");
});

test("protected-path: extracts a git rm target", () => {
  const got = extractCandidatePaths("Bash", { command: "git rm -r docs/guide" });
  assert.ok(got.includes("docs/guide"), `got ${JSON.stringify(got)}`);
});

test("protected-path: extracts an mv source", () => {
  const got = extractCandidatePaths("Bash", { command: "mv old.md new.md" });
  assert.ok(got.includes("old.md"), `got ${JSON.stringify(got)}`);
});

test("protected-path: strips surrounding quotes from paths", () => {
  const got = extractCandidatePaths("Bash", { command: `rm "docs/a b.md"` });
  assert.ok(!got.some((p) => p.includes('"')), `quotes not stripped: ${JSON.stringify(got)}`);
});

test("protected-path: flags destructive dotnet ef database drop", () => {
  const got = extractCandidatePaths("Bash", { command: "dotnet ef database drop --force" });
  assert.ok(got.includes("__db_destructive__"), `got ${JSON.stringify(got)}`);
});

test("protected-path: flags dotnet ef migrations remove --force", () => {
  const got = extractCandidatePaths("Bash", { command: "dotnet ef migrations remove --force" });
  assert.ok(got.includes("__db_destructive__"), `got ${JSON.stringify(got)}`);
});

test("protected-path: extracts Write/Edit file_path", () => {
  assert.deepStrictEqual(extractCandidatePaths("Write", { file_path: "/tmp/x.md" }), ["/tmp/x.md"]);
  assert.deepStrictEqual(extractCandidatePaths("Edit", { file_path: "/tmp/y.md" }), ["/tmp/y.md"]);
});

test("protected-path: a non-destructive Bash command yields no candidates", () => {
  assert.deepStrictEqual(extractCandidatePaths("Bash", { command: "ls -la && git status" }), []);
});

test("protected-path: tolerates missing tool_input without throwing", () => {
  assert.deepStrictEqual(extractCandidatePaths("Bash", {}), []);
  assert.deepStrictEqual(extractCandidatePaths("Bash", undefined), []);
});

// ── pre-commit-gate: isGitCommit ──────────────────────────────────────────

test("commit-gate: recognises a plain git commit", () => {
  assert.strictEqual(isGitCommit("git commit -m 'x'"), true);
});

test("commit-gate: recognises git commit with -C", () => {
  assert.strictEqual(isGitCommit("git -C /repo commit -m 'x'"), true);
});

test("commit-gate: recognises a leading-whitespace git commit", () => {
  assert.strictEqual(isGitCommit("   git commit --amend"), true);
});

test("commit-gate: does NOT fire on git status / add / log", () => {
  assert.strictEqual(isGitCommit("git status"), false);
  assert.strictEqual(isGitCommit("git add -A"), false);
  assert.strictEqual(isGitCommit("git log -1"), false);
});

test("commit-gate: does NOT fire on a word merely containing 'commit'", () => {
  assert.strictEqual(isGitCommit("echo 'commit this later'"), false);
});

test("commit-gate: handles empty/undefined input", () => {
  assert.strictEqual(isGitCommit(""), false);
  assert.strictEqual(isGitCommit(undefined), false);
});

// ── pre-stage-close-gate: isStageClose / countUncheckedInClosingStage ─────

test("stage-close: fires on a ✅ Done marker", () => {
  assert.strictEqual(isStageClose("## Stage 12 — Sessions ✅ Done"), true);
});

test("stage-close: fires on a stage header flipped to [x]", () => {
  assert.strictEqual(isStageClose("## Stage 7 — Reports [x]"), true);
});

test("stage-close: does NOT fire on ordinary prose", () => {
  assert.strictEqual(isStageClose("Working on Stage 12 today."), false);
});

test("stage-close: does NOT fire on an unchecked stage header", () => {
  assert.strictEqual(isStageClose("## Stage 7 — Reports [ ]"), false);
});

test("stage-close: handles empty input", () => {
  assert.strictEqual(isStageClose(""), false);
  assert.strictEqual(isStageClose(undefined), false);
});

test("stage-close: counts unchecked items under the closing stage", () => {
  const doc = [
    "## Stage 12 — Sessions ✅ Done",
    "- [x] built the controller",
    "- [ ] browser-verify the dialog",
    "- [ ] capture the trace",
    "## Stage 13 — Next",
    "- [ ] not this one",
  ].join("\n");
  assert.strictEqual(countUncheckedInClosingStage(doc).unchecked, 2);
});

test("stage-close: a fully ticked closing stage counts zero", () => {
  const doc = ["## Stage 12 — Sessions ✅ Done", "- [x] a", "- [x] b"].join("\n");
  assert.strictEqual(countUncheckedInClosingStage(doc).unchecked, 0);
});

// ── pre-spec-write-gate: extractProposedContent ───────────────────────────

test("spec-write: reads Write content", () => {
  assert.strictEqual(extractProposedContent({ tool_name: "Write", tool_input: { content: "hi" } }), "hi");
});

test("spec-write: reads Edit new_string", () => {
  assert.strictEqual(extractProposedContent({ tool_name: "Edit", tool_input: { new_string: "hi" } }), "hi");
});

test("spec-write: concatenates MultiEdit new_strings", () => {
  const got = extractProposedContent({
    tool_name: "MultiEdit",
    tool_input: { edits: [{ new_string: "a" }, { new_string: "b" }] },
  });
  assert.strictEqual(got, "a\nb");
});

test("spec-write: returns empty string for an unrelated tool", () => {
  assert.strictEqual(extractProposedContent({ tool_name: "Bash", tool_input: { command: "ls" } }), "");
});

test("spec-write: tolerates a malformed payload without throwing", () => {
  assert.strictEqual(extractProposedContent({}), "");
  assert.strictEqual(extractProposedContent(undefined), "");
});
