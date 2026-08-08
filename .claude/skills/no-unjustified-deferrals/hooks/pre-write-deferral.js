#!/usr/bin/env node
// PreToolUse hook — detects deferral language in writes to docs/ and HARD-BLOCKS
// the write when the well-formed-deferral guard fails (Phase D in
// playbook/references/constitution.md).
//
// Behavior:
//   - Advisory additionalContext is ALWAYS emitted when deferral phrases match
//     in a docs/ Write (preserves the prior reminder).
//   - When the three-field guard (Stage X ref + `- [ ]` checkbox + tooling/
//     already-scheduled reason marker) does NOT satisfy, also emit
//     permissionDecision: "deny" to hard-block.
//   - When the guard satisfies, silent exit (well-formed deferrals pass).
//
// Why hard now: incident 2.3#5 (verbatim user quote: "Fixing an issue is never
// out of scope") in the 2026-05-11 walked session is the failure mode advisory
// could not catch. Five of the 11 missed-fires in the cohesion-review audit
// would have been caught by this upgrade.

const path = require("path");
const { stripDiscussionFrames } = require(
  path.join(process.env.CLAUDE_PROJECT_DIR || process.cwd(), ".claude/hooks/lib/discussion-frame-strip.js")
);

const PHRASES = [
  /doesn'?t change (the )?structural/i,
  /doesn'?t block (Phase|Stage)/i,
  /Phase \w+ polish/i,
  /follow[- ]?up:/i,
  /(deferred|punted|kicked) to (?!stage)/i, // allow "Deferred to Stage X"
  /we can address (this|that|it) later/i,
  /we can batch (this|that|these)/i,
  /out of scope for (this|the current) (stage|sprint)/i,
  /(?<!high )low priority/i,
  /(?<!isn'?t )not blocking/i,
  /cosmetic only/i,
  /UX[- ]only/i,
  /nice[- ]to[- ]have/i,
  /can wait until/i,
  /will be addressed when/i,
  /we should revisit/i,
  /let'?s circle back/i,
  /track(ed|ing) (in|on) the spec/i,
];

const STAGE_REF = /Stage \d+(\.\d+)?\b/;
const CHECKBOX = /^\s*-\s+\[ \]\s+/m;
const REASON_MARKER = /\b(tooling|already scheduled|scheduled in)\b/i;

// A deferral is well-formed when it names a receiving stage, opens a real
// checkbox, AND cites one of the two valid reasons. Those three together are
// the escape hatch — the gate stays quiet so correct behaviour isn't punished.
function isWellFormedDeferral(text) {
  if (!text) return false;
  return STAGE_REF.test(text) && CHECKBOX.test(text) && REASON_MARKER.test(text);
}

// Exported for __tests__/pre-write-deferral.test.js.
if (typeof module !== "undefined" && module.exports) {
  module.exports = { PHRASES, STAGE_REF, CHECKBOX, REASON_MARKER, isWellFormedDeferral };
}

let raw = "";
if (require.main === module) {
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  const toolName = input.tool_name || "";
  if (!/^(Edit|Write|MultiEdit)$/.test(toolName)) process.exit(0);

  const ti = input.tool_input || {};
  const filePath = ti.file_path || "";
  if (!filePath) process.exit(0);

  const projectRoot = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  const rel = path.relative(projectRoot, filePath);
  if (rel.startsWith("..") || path.isAbsolute(rel)) process.exit(0);
  if (!rel.startsWith("docs/") && !rel.startsWith("docs" + path.sep)) process.exit(0);

  // Collect candidate text from various tool shapes
  const chunks = [];
  if (typeof ti.content === "string") chunks.push(ti.content);
  if (typeof ti.new_string === "string") chunks.push(ti.new_string);
  if (Array.isArray(ti.edits)) {
    for (const e of ti.edits) {
      if (e && typeof e.new_string === "string") chunks.push(e.new_string);
    }
  }
  const text = chunks.join("\n");
  if (!text) process.exit(0);

  // Strip discussion frames (stage headings, fenced code, blockquotes,
  // tool-use payloads) before regex-testing. Same shared helper every
  // phrase-scanning hook uses, so the matcher discipline stays uniform.
  const textForMatching = stripDiscussionFrames(text);

  const matched = PHRASES.filter((re) => re.test(textForMatching)).map((re) => re.toString());
  if (matched.length === 0) process.exit(0);

  // Allowed-pattern guard: if the text also contains a stage ref, a checkbox,
  // AND a reason marker (tooling / already scheduled), it's a properly formed
  // deferral and we don't pester.
  const hasStage = STAGE_REF.test(text);
  const hasCheckbox = CHECKBOX.test(text);
  const hasReason = REASON_MARKER.test(text);
  if (isWellFormedDeferral(text)) process.exit(0);

  const missingFields = [];
  if (!hasStage) missingFields.push("no Stage X reference");
  if (!hasCheckbox) missingFields.push("no `- [ ]` checkbox");
  if (!hasReason) missingFields.push("no Reason 1 (tooling) / Reason 2 (already scheduled) marker");

  const additionalContext = [
    "🛑 Deferral language detected in this write.",
    "",
    `File: ${rel}`,
    `Matched phrases: ${matched.join(", ")}`,
    `Missing required deferral fields: ${missingFields.join("; ")}`,
    "",
    "The `no-unjustified-deferrals` skill must run before this write completes. The two valid reasons:",
    "  1. Tooling gap — a specific tool/dep/infra/feature you need is unavailable, with evidence",
    "  2. Already-scheduled — the active batch stage has a `[ ]` line covering this as part of its scope",
    "",
    "If neither holds, the bug goes into the active batch stage's checklist — open the batch stage if one isn't open. That's the rule the user agreed to (queue-and-flush, not fix-eventually).",
    "",
    "If one holds, the deferral entry needs THREE fields per the template in `.claude/skills/no-unjustified-deferrals/references/deferral-entry-template.md`:",
    "  • Cited reason (tooling gap OR already-scheduled, with specifics)",
    "  • Receiving-stage `[ ]` checkbox added in THIS commit",
    "  • Mechanical tripwire (failing test / architecture assertion / FIXME marker / CI check)",
    "",
    "Invoke the skill, run the procedure, and either rewrite the deferral to pass the template OR replace it with the fix-now plan before completing this write.",
  ].join("\n");

  // HARD-BLOCK: the three-field guard failed AND deferral phrases matched in
  // a docs/ Write. Per Phase D in playbook/references/constitution.md.
  const reason = [
    "Deferral write blocked (Phase D in playbook/references/constitution.md).",
    "",
    `File: ${rel}`,
    `Matched deferral phrases: ${matched.join(", ")}`,
    `Missing required deferral fields: ${missingFields.join("; ")}`,
    "",
    "A deferral entry must include THREE fields per .claude/skills/no-unjustified-deferrals/references/deferral-entry-template.md:",
    "  • Stage X reference (e.g. 'deferred to Stage 9.2')",
    "  • Receiving-stage `- [ ]` checkbox in the same edit",
    "  • Cited reason marker (Reason 1 'tooling gap' OR Reason 2 'already scheduled')",
    "",
    "Two valid reasons, verbatim from SKILL.md:",
    "  1. Tooling gap — a specific tool/dep/infra/feature is unavailable, with evidence.",
    "  2. Already-scheduled — the active batch stage has a `[ ]` line covering this as part of its scope.",
    "",
    "If neither holds, the bug goes into the active batch stage's checklist — open the batch stage if none is open. That's the rule the user agreed to (queue-and-flush, not fix-eventually).",
    "",
    "Recover:",
    "  • Invoke the `no-unjustified-deferrals` skill, run the six-step procedure.",
    "  • Either rewrite the deferral to include all three fields, OR replace it with the fix-now plan.",
    "  • Then re-attempt the Write.",
  ].join("\n");

  process.stdout.write(
    JSON.stringify({
      permissionDecision: "deny",
      permissionDecisionReason: reason,
      hookSpecificOutput: { hookEventName: "PreToolUse", additionalContext },
    })
  );
  process.exit(0);
});
}
