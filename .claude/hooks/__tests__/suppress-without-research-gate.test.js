const { test } = require("node:test");
const assert = require("node:assert");
const {
  introducesSuppression,
  hasResearchEvidence,
  addedText,
} = require("../suppress-without-research-gate.js");

// PreToolUse HARD gate: an edit that INTRODUCES a warning/analyzer/test suppression
// is denied unless it also carries evidence the cause was researched. Codifies the
// 2026-09-12 lesson (steering to <NoWarn>CS9107> instead of researching the fix).

// ── suppression constructs are detected ───────────────────────────────────
for (const [text, label] of [
  ["<NoWarn>$(NoWarn);CS9107</NoWarn>", "NoWarn"],
  ["#pragma warning disable CS9107", "pragma"],
  ["dotnet_diagnostic.CA1050.severity = none", "editorconfig none"],
  ["[SuppressMessage(\"Usage\", \"CA1050\")]", "SuppressMessage"],
  ["[Fact(Skip = \"flaky\")]", "Fact Skip"],
  ["it.skip('renders', () => {})", "it.skip"],
  ["// eslint-disable-next-line no-console", "eslint-disable"],
  ["// @ts-ignore", "ts-ignore"],
  ["test: { dangerouslyIgnoreUnhandledErrors: true }", "dangerouslyIgnore"],
]) {
  test(`detects suppression: ${label}`, () =>
    assert.ok(introducesSuppression(text)));
}

// ── non-suppression edits are NOT flagged ─────────────────────────────────
for (const text of [
  "public class Foo { }",
  "const x = 1;",
  "// this comment mentions warning but disables nothing",
  "retry: { count: 2, condition: /timeout/i }", // a retry alone is not a suppress construct
]) {
  test(`ignores non-suppression: ${text.slice(0, 30)}`, () =>
    assert.strictEqual(introducesSuppression(text), null));
}

// ── research evidence lets a suppression pass ─────────────────────────────
for (const text of [
  "#pragma warning disable CS9107 // root cause: this is a verified false positive, see docs/x.md",
  "// @ts-ignore — the upstream type is wrong (researched, filed upstream #123)",
  "[Fact(Skip=\"quarantine\")] // owed fix tracked at roadmap - [ ] flake §12.19",
  "<NoWarn>CS9107</NoWarn> <!-- intentional because verified: same instance, see the study -->",
  "// trade-off: fixing needs a 56-file refactor; quarantined with a - [ ] line",
]) {
  test(`research evidence passes: ${text.slice(0, 35)}`, () =>
    assert.strictEqual(hasResearchEvidence(text), true));
}

// ── bare suppression WITHOUT evidence does not pass ───────────────────────
for (const text of [
  "<NoWarn>CS9107</NoWarn>",
  "#pragma warning disable CS9107",
  "[Fact(Skip=\"flaky\")]",
]) {
  test(`bare suppression lacks evidence: ${text.slice(0, 30)}`, () =>
    assert.strictEqual(hasResearchEvidence(text), false));
}

// ── addedText extracts the right field per tool ───────────────────────────
test("addedText: Write uses content", () => {
  const { added } = addedText({ tool_name: "Write", tool_input: { content: "<NoWarn>CS9107</NoWarn>" } });
  assert.ok(introducesSuppression(added));
});
test("addedText: Edit uses new_string and exposes old_string as removed", () => {
  const { added, removed } = addedText({
    tool_name: "Edit",
    tool_input: { new_string: "#pragma warning disable CS1", old_string: "clean" },
  });
  assert.ok(introducesSuppression(added));
  assert.strictEqual(introducesSuppression(removed), null);
});
test("addedText: MultiEdit joins all new_strings", () => {
  const { added } = addedText({
    tool_name: "MultiEdit",
    tool_input: { edits: [{ new_string: "ok" }, { new_string: "// eslint-disable" }] },
  });
  assert.ok(introducesSuppression(added));
});

// ── editing a file that ALREADY had the suppression is not a new introduction ──
test("suppression present in removed text = not newly introduced", () => {
  // Simulates the gate's own logic: if old_string already had it, don't flag.
  const removed = "#pragma warning disable CS9107\nold body";
  const added = "#pragma warning disable CS9107\nnew body";
  assert.ok(introducesSuppression(added));
  assert.ok(introducesSuppression(removed)); // gate exits 0 when removed also matches
});
