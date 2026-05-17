#!/usr/bin/env node
// PostToolUse hook (Edit/Write) — counts edits per file per session.
// Advisory only: at 5 edits to the same file, injects "are you converging or circling?" reminder.
// Distinct from loop-fingerprint.js because the same file can be edited 5 times with 5 different fingerprints (Fixation across attempts).

const fs = require("fs");
const path = require("path");

const ADVISORY_AT = 5;
const HARD_NUDGE_AT = 8;

const STATE_DIR = path.join(
  process.env.CLAUDE_PROJECT_DIR || process.cwd(),
  ".claude",
  "state",
  "deep-fix-mode"
);

function ensureDir(p) {
  try {
    fs.mkdirSync(p, { recursive: true });
  } catch {}
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  const sessionId = input.session_id;
  const toolName = input.tool_name;
  const toolInput = input.tool_input || {};
  const filePath = toolInput.file_path;

  if (!sessionId || !filePath || (toolName !== "Edit" && toolName !== "Write")) {
    process.exit(0);
  }

  ensureDir(STATE_DIR);
  const stateFile = path.join(STATE_DIR, `${sessionId}-edits.json`);

  let state = { counts: {} };
  try {
    state = JSON.parse(fs.readFileSync(stateFile, "utf8"));
    if (!state.counts) state.counts = {};
  } catch {}

  state.counts[filePath] = (state.counts[filePath] || 0) + 1;
  const n = state.counts[filePath];

  try {
    fs.writeFileSync(stateFile, JSON.stringify(state));
  } catch {}

  if (n < ADVISORY_AT) process.exit(0);

  const hard = n >= HARD_NUDGE_AT;
  const msg = hard
    ? [
        `🔁 ${n} edits to ${filePath} this session — deep-fix-mode trigger.`,
        "",
        "This many edits to one file is a strong Fixation signal (Zhou et al. 2026, CB6: 'anchor efforts on initial assumptions even with contradictory evidence'). The bug is almost certainly NOT in this file. The bug is in the layer that calls this file, the layer that configures it, or the layer that consumes its output.",
        "",
        "MANDATORY: Invoke `deep-fix-mode` skill before the next edit. Step 4 (layer naming) is the load-bearing step — name the file or module you have NOT touched yet.",
      ].join("\n")
    : [
        `📝 ${n} edits to ${filePath} this session.`,
        "",
        "Self-check: are you converging or circling? If the same file has been edited 5+ times and the original symptom is still present, the root cause is likely outside this file. Consider invoking `deep-fix-mode` to do the layer-naming exercise.",
        "",
        "Not blocking — advisory only.",
      ].join("\n");

  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: { hookEventName: "PostToolUse", additionalContext: msg },
    })
  );
  process.exit(0);
});
