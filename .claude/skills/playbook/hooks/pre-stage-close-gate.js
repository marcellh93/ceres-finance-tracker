#!/usr/bin/env node
// PreToolUse hook for Edit|Write|MultiEdit on docs/roadmap-phase-*.md.
//
// Phase E HARD gate (playbook/references/constitution.md).
//
// Detection: the new_string (or content) ADDS "✅ Done" to a stage header
//   OR flips a stage header from `- [ ]` to `- [x]`.
//
// Stage-section parse (Option A per resolved decision 2026-05-17):
//   - Start: the matching `## Stage N — ...` header (or `### Stage N` etc.) inside
//     the new_string surrounding the change.
//   - End: the next `## ` heading at level-2 depth.
//
// Block conditions (deny if ANY hold):
//   (a) Any unchecked `- [ ]` items remain in the closing stage's body AND they
//       are NOT paired with a `- [ ]` line under a different stage's header in
//       the same new_string (move-to-receiving-stage allowance).
//   (b) sync-docs was NOT invoked this session (state-file check).
//   (c) changelog-sync was NOT invoked this session (state-file check).
//
// All other Edit/Write/MultiEdit calls (non-roadmap, or roadmap but not closing
// a stage) pass silently.

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "playbook");
const ROADMAP_PATH_PATTERN = /\/docs\/roadmap-phase-[a-z0-9-]+\.md$/i;

function allow() {
  process.stdout.write(JSON.stringify({ permissionDecision: "allow" }));
  process.exit(0);
}

function deny(reason) {
  process.stdout.write(JSON.stringify({ permissionDecision: "deny", permissionDecisionReason: reason }));
  process.exit(0);
}

function readState(sessionId) {
  if (!sessionId) return null;
  const file = path.join(STATE_DIR, `${sessionId}.json`);
  try { return JSON.parse(fs.readFileSync(file, "utf8")); } catch { return null; }
}

// Detects a stage-close signal in the new_string. Returns true if so.
function isStageClose(newString) {
  if (!newString) return false;
  // Pattern A: "✅ Done" added near a stage header.
  if (/✅\s*Done/.test(newString)) return true;
  // Pattern B: a stage header line that flips to [x].
  //   "## Stage 7 — ... [x]" — the [x] vs [ ] is what matters
  if (/^#{2,3}\s+Stage\s+\d+(\.\d+)?[\s\S]*?\[x\]/im.test(newString)) return true;
  return false;
}

// Given the post-edit content of a roadmap doc (best-effort: the new_string
// only), find the closing stage's section header and count unchecked items
// until the next `## ` heading.
function countUncheckedInClosingStage(newString) {
  if (!newString) return { unchecked: 0, headerLine: "" };
  const lines = newString.split(/\r?\n/);
  // Find the stage-header line that contains the close signal.
  let headerIdx = -1;
  let headerLine = "";
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i];
    // A stage header line at level-2 or level-3 that contains ✅ Done or [x]
    if (/^#{2,3}\s+Stage\s+\d+/i.test(l) && (/✅\s*Done/.test(l) || /\[x\]/i.test(l))) {
      headerIdx = i;
      headerLine = l.trim();
      break;
    }
  }
  if (headerIdx === -1) {
    // No closing-stage header in the diff — fall back to scanning whole
    // new_string for unchecked items adjacent to the close signal.
    const close = newString.match(/✅\s*Done/);
    if (!close) return { unchecked: 0, headerLine: "" };
    // Find the nearest ## or ### header above the close signal.
    const before = newString.slice(0, close.index);
    const m = before.match(/(^#{2,3}\s+Stage\s+\d+[^\n]*)\n(?![\s\S]*^#{2,3}\s+Stage)/m);
    if (m) {
      headerLine = m[1].trim();
      headerIdx = lines.findIndex((l) => l.trim() === headerLine);
    }
    if (headerIdx === -1) return { unchecked: 0, headerLine: "" };
  }
  // Walk forward until the next `## ` heading at level-2 (Option A boundary).
  let endIdx = lines.length;
  for (let i = headerIdx + 1; i < lines.length; i++) {
    if (/^##\s+/.test(lines[i])) { endIdx = i; break; }
  }
  const body = lines.slice(headerIdx + 1, endIdx).join("\n");
  const unchecked = (body.match(/^\s*-\s+\[ \]\s+/gm) || []).length;
  return { unchecked, headerLine };
}

// Given the full new_string, count unchecked items under stage headers OTHER
// than the one being closed. Used for the move-to-receiving-stage allowance —
// if N items remain unchecked in the closing stage AND there are >= N matching
// `- [ ]` lines under another stage's header, the allowance applies.
function countUncheckedInOtherStages(newString, closingHeaderLine) {
  if (!newString) return 0;
  const lines = newString.split(/\r?\n/);
  let total = 0;
  let inClosing = false;
  let inOtherStage = false;
  for (const l of lines) {
    if (/^##\s+/.test(l)) {
      const isStageHeader = /^#{2,3}\s+Stage\s+\d+/i.test(l);
      inClosing = isStageHeader && l.trim() === closingHeaderLine;
      inOtherStage = isStageHeader && !inClosing;
      continue;
    }
    if (inOtherStage && /^\s*-\s+\[ \]\s+/.test(l)) total++;
  }
  return total;
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let payload;
  try { payload = JSON.parse(raw || "{}"); } catch { allow(); }

  const toolName = payload.tool_name || "";
  if (!/^(Edit|Write|MultiEdit)$/.test(toolName)) allow();

  const ti = payload.tool_input || {};
  const filePath = ti.file_path || "";
  if (!ROADMAP_PATH_PATTERN.test(filePath)) allow();

  // Collect the candidate text — the new_string / content / each MultiEdit slot.
  const chunks = [];
  if (typeof ti.content === "string") chunks.push(ti.content);
  if (typeof ti.new_string === "string") chunks.push(ti.new_string);
  if (Array.isArray(ti.edits)) {
    for (const e of ti.edits) {
      if (e && typeof e.new_string === "string") chunks.push(e.new_string);
    }
  }
  const text = chunks.join("\n");
  if (!text) allow();

  if (!isStageClose(text)) allow();

  // This is a stage-close edit. Run the gates.
  const sessionId = payload.session_id;
  const state = readState(sessionId);
  const fired = (state && Array.isArray(state.fired)) ? state.fired : [];

  const syncDocsFired = fired.includes("sync-docs");
  const changelogSyncFired = fired.includes("changelog-sync");

  const { unchecked, headerLine } = countUncheckedInClosingStage(text);
  const otherStageUnchecked = countUncheckedInOtherStages(text, headerLine);

  // Move-to-receiving-stage allowance: if every unchecked item in the closing
  // stage is paired with at least one unchecked item under another stage's
  // header (i.e. the same diff moves the work to a receiving stage), the
  // unchecked-count gate passes.
  const uncheckedNetOfMoved = unchecked - Math.min(unchecked, otherStageUnchecked);

  const problems = [];
  if (uncheckedNetOfMoved > 0) {
    problems.push(`${uncheckedNetOfMoved} unchecked \`- [ ]\` item(s) remain in the closing stage with no matching receiving-stage entry`);
  }
  if (!syncDocsFired) problems.push("`sync-docs` was NOT invoked this session");
  if (!changelogSyncFired) problems.push("`changelog-sync` was NOT invoked this session");

  if (problems.length === 0) allow();

  const reason = [
    "Stage-close blocked (Phase E in playbook/references/constitution.md).",
    "",
    `File: ${filePath}`,
    `Closing stage header: ${headerLine || "(not detected in diff)"}`,
    "",
    "Failing checks:",
    ...problems.map((p) => `  • ${p}`),
    "",
    "Memory rules engaged:",
    "  • feedback_finished_stages_have_no_unchecked_items — before flipping a stage to ✅ Done, every `[ ]` is either ticked OR moved to the receiving stage's checklist (with a back-reference) in the same commit.",
    "  • feedback_deferral_requires_receiving_stage_checkbox — annotating 'Deferred to Stage X' in the source stage is necessary but NOT sufficient; the receiving stage MUST also have a matching `[ ]` line.",
    "  • feedback_sync_docs_before_spa_commits — run sync-docs against the diff before every Batch 2 commit.",
    "",
    "Recover:",
    "  • Tick every remaining `- [ ]` in the closing stage's section, OR move them to the receiving stage's checklist with a back-reference, in the same edit.",
    "  • If sync-docs is missing: invoke the `sync-docs` skill against the diff before re-attempting.",
    "  • If changelog-sync is missing: invoke the `changelog-sync` skill to log under [Unreleased] before re-attempting.",
  ].join("\n");

  deny(reason);
});
