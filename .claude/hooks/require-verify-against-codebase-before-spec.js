#!/usr/bin/env node
// PreToolUse hook for the Write tool.
//
// Purpose: enforce that the verify-against-codebase skill is invoked before any
// new spec is written under docs/superpowers/specs/. This catches the failure
// mode of writing a spec that proposes homegrown infrastructure for problems
// the framework / existing codebase already solves.
//
// Behaviour:
//   - If the Write target is NOT under docs/superpowers/specs/ → allow.
//   - If the target IS under that path AND the transcript shows that
//     verify-against-codebase was invoked (Skill tool with args matching it,
//     OR an explicit "verify-against-codebase" string mention from the agent)
//     in the current session → allow.
//   - Otherwise → deny with a reason instructing the agent to invoke the skill.
//
// Bypass: writing a spec file that already exists (i.e. an Edit-equivalent
// rewrite of an existing spec) is allowed without re-running the skill.
// The verify gate is for *new* spec authoring.

const fs = require("fs");
const path = require("path");

let raw = "";
process.stdin.on("data", (chunk) => { raw += chunk; });
process.stdin.on("end", () => {
  let payload;
  try {
    payload = JSON.parse(raw);
  } catch {
    // Malformed input → don't break the agent; fall through.
    process.stdout.write(JSON.stringify({ permissionDecision: "allow" }));
    process.exit(0);
  }

  const filePath = payload?.tool_input?.file_path || "";
  const transcriptPath = payload?.transcript_path || "";

  // Only gate writes under docs/superpowers/specs/*.md
  const SPEC_PATH_PATTERN = /\/docs\/superpowers\/specs\/[^/]+\.md$/;
  if (!SPEC_PATH_PATTERN.test(filePath)) {
    process.stdout.write(JSON.stringify({ permissionDecision: "allow" }));
    process.exit(0);
  }

  // Allow rewrites of an already-existing spec (the verify-gate is for new specs).
  try {
    if (fs.existsSync(filePath)) {
      process.stdout.write(JSON.stringify({ permissionDecision: "allow" }));
      process.exit(0);
    }
  } catch {
    // fall through; treat as new
  }

  // Scan the transcript for a prior verify-against-codebase invocation.
  let invoked = false;
  if (transcriptPath && fs.existsSync(transcriptPath)) {
    try {
      const transcript = fs.readFileSync(transcriptPath, "utf8");
      // The Skill tool emits a tool_use with name="Skill" and an "args" string;
      // the canonical invocation also contains the literal skill name in the
      // surrounding tool result. Match either signal.
      if (
        /"name"\s*:\s*"Skill"[\s\S]{0,400}verify-against-codebase/i.test(transcript) ||
        /verify-against-codebase/i.test(transcript)
      ) {
        invoked = true;
      }
    } catch {
      // transcript unreadable; err on the side of allowing rather than blocking
      process.stdout.write(JSON.stringify({ permissionDecision: "allow" }));
      process.exit(0);
    }
  }

  if (invoked) {
    process.stdout.write(JSON.stringify({ permissionDecision: "allow" }));
    process.exit(0);
  }

  // Block. Tell the agent why and how to recover.
  const reason =
    "Spec write blocked: docs/superpowers/specs/ files require running the " +
    "`verify-against-codebase` skill first. That skill catches the failure " +
    "mode of inventing homegrown infrastructure for problems the framework / " +
    "existing project conventions already solve (e.g. a custom MfaTicketService " +
    "duplicating ASP.NET Identity's built-in Identity.TwoFactorUserId cookie). " +
    "Run `verify-against-codebase` against the proposed design, address any " +
    "findings, then re-attempt the Write. " +
    "Bypass: if you genuinely have run an equivalent verification pass and just " +
    "didn't use the named skill, mention `verify-against-codebase` explicitly " +
    "in your next message — the hook scans the transcript for that string.";

  process.stdout.write(JSON.stringify({
    permissionDecision: "deny",
    permissionDecisionReason: reason,
  }));
  process.exit(0);
});
