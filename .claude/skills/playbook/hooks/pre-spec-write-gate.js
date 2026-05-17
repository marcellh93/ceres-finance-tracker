#!/usr/bin/env node
// PreToolUse hook for Write|Edit|MultiEdit on docs/superpowers/specs/*.md.
//
// REPLACES .claude/hooks/require-verify-against-codebase-before-spec.js
// with a superset: also requires superpowers:brainstorming to have fired
// this session (Phase B in playbook/references/constitution.md).
//
// Behavior:
//   - If target is NOT under docs/superpowers/specs/ → allow.
//   - If target file already exists on disk → allow (rewrite of an existing spec).
//   - Else: check playbook state for both superpowers:brainstorming AND
//     verify-against-codebase. If either is missing, deny.
//   - Bypass: an explicit "verify-against-codebase" string in the transcript
//     counts (preserved from the old hook for behavior continuity).
//
// State source: .claude/state/playbook/<session_id>.json (written by
// state-record-skill-fire.js).

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "playbook");
const SPEC_PATH_PATTERN = /\/docs\/superpowers\/specs\/[^/]+\.md$/;

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
  try {
    return JSON.parse(fs.readFileSync(file, "utf8"));
  } catch {
    return null;
  }
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let payload;
  try { payload = JSON.parse(raw || "{}"); } catch { allow(); }

  const toolName = payload.tool_name || "";
  if (!/^(Write|Edit|MultiEdit)$/.test(toolName)) allow();

  const filePath = payload?.tool_input?.file_path || "";
  if (!SPEC_PATH_PATTERN.test(filePath)) allow();

  // Existing-spec rewrite exemption.
  try {
    if (fs.existsSync(filePath)) allow();
  } catch {
    // fall through; treat as new
  }

  const sessionId = payload.session_id;
  const state = readState(sessionId);
  const fired = (state && Array.isArray(state.fired)) ? state.fired : [];

  const brainstormingFired = fired.some((s) =>
    /superpowers:?brainstorming/i.test(s)
  );
  const verifyFired = fired.includes("verify-against-codebase");

  // Bypass-friendly carve-out for verify-against-codebase: scan transcript.
  let verifyInTranscript = false;
  const transcriptPath = payload?.transcript_path || "";
  if (!verifyFired && transcriptPath) {
    try {
      if (fs.existsSync(transcriptPath)) {
        const transcript = fs.readFileSync(transcriptPath, "utf8");
        if (/verify-against-codebase/i.test(transcript)) {
          verifyInTranscript = true;
        }
      }
    } catch {
      // transcript unreadable; err on the side of allowing rather than blocking
      allow();
    }
  }

  const verifyOk = verifyFired || verifyInTranscript;

  if (brainstormingFired && verifyOk) allow();

  const missing = [];
  if (!brainstormingFired) missing.push("`superpowers:brainstorming`");
  if (!verifyOk) missing.push("`verify-against-codebase`");

  const reason = [
    "Spec write blocked (Phase B in playbook/references/constitution.md).",
    "",
    `Required skill(s) missing from this session's state file: ${missing.join(" + ")}.`,
    "",
    "Why:",
    "  • superpowers:brainstorming establishes the design + user approval before the spec is written.",
    "  • verify-against-codebase catches the failure mode of inventing homegrown infrastructure for problems the framework / existing project conventions already solve (e.g. a custom MfaTicketService duplicating ASP.NET Identity's Identity.TwoFactorUserId cookie).",
    "",
    "Recover:",
    "  • If you have a design but skipped the formal brainstorm, invoke `superpowers:brainstorming` first.",
    "  • Then invoke `verify-against-codebase` against the proposed design.",
    "  • Bypass (verify only): mention the literal string `verify-against-codebase` in your next message — the hook scans the transcript for that string. (No bypass for brainstorming — it must be invoked via Skill.)",
    "",
    "Existing-spec rewrites are allowed without re-running either skill — the gate only fires for NEW spec authoring.",
  ].join("\n");

  deny(reason);
});
