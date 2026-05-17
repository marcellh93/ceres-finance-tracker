#!/usr/bin/env node
// PRE-PR-REVIEW DETECTOR — DISABLED IN CERES.
//
// Ceres is local-only (feedback_no_remote_no_push_suggestions). This hook is
// NOT wired in .claude/settings.json. It lives on disk as a placeholder so
// Phase G in playbook/references/constitution.md is documented end-to-end
// and re-enabling it later is one settings.json edit, not a re-build.
//
// To enable: add the following to UserPromptSubmit in .claude/settings.json
//   { "matcher": "", "hooks": [{ "type": "command",
//     "command": "node .claude/skills/playbook/hooks/pre-pr-review-detect.js" }] }
//
// Intended behavior (when enabled):
//   - UserPromptSubmit trigger on phrases: "open a PR", "request review",
//     "ready for review", "ready to merge", "create a pull request".
//   - Emits additionalContext reminding the agent to invoke
//     superpowers:requesting-code-review before proceeding.
//   - Advisory only, never blocks.

// Currently a no-op: read stdin to avoid SIGPIPE, then exit 0 silently.
let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  // No-op. See header comment.
  process.exit(0);
});
