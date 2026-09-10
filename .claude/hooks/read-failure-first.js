#!/usr/bin/env node
// PostToolUse hook on `Bash` — advisory, never blocks.
//
// Fires when a command's output carries a build/test/CI FAILURE signature, to
// enforce one rule: READ THE ACTUAL FAILURE OUTPUT FIRST, before hypothesizing a
// cause or editing a file to "fix" it.
//
// Why this exists (2026-09-10): during the Stage 12.13 CI bring-up the same
// pnpm-on-PATH failure was "fixed" three times on plausible theories and pushed
// blind — each wrong. The moment the actual step log was read, the cause
// (action-setup misparsing the packageManager sha512 hash) was obvious and the
// fix landed first try. The habit being corrected is theorizing-before-reading.
//
// The nudge fires once per session (dedup) so it flags the pattern without
// nagging on every failing command. It does NOT fire on the log-reading commands
// themselves (gh run view --log*, reading a saved log) — those ARE the right
// action, and flagging them would invert the message.
//
// Full reference: docs/runbooks/ci-actions-troubleshooting.md

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "read-failure-first");

// Failure signatures in command output. Kept specific to build/test/CI so a
// command that merely prints the word "failed" in prose doesn't trip it.
const FAILURE_SIGNATURES = [
  /\bBuild FAILED\b/i,
  /Build failed\. Use dotnet build to see the errors/i,
  /\berror\s+(CS|MSB|NETSDK)\d+/, // dotnet compile/build errors
  /\bFailed!\s+-\s+Failed:\s+[1-9]/, // dotnet test summary with >0 failures
  /\bTests\b.*\b[1-9]\d*\s+failed\b/i, // vitest "Tests  N failed"
  /\bELIFECYCLE\b.*Test failed/i, // pnpm test failure
  /##\[error\]/, // GitHub Actions error annotation in a pasted log
  /\bProcess completed with exit code [1-9]/, // Actions step failure
];

// Commands that ARE reading a failure log — never nag on these; they are the
// correct response to a failure.
const LOG_READING = [
  /gh\s+run\s+view\b[^\n]*--log/, // gh run view ... --log / --log-failed
  /gh\s+run\s+watch\b/,
  /\b(cat|less|tail|head|grep|rg|sed)\b[^\n]*\.log\b/, // reading a saved log file
  /\/last\.log\b/, // the project's own run-tests log
];

// Does the command output carry a build/test/CI failure signature?
function hasFailureSignature(output) {
  if (!output || typeof output !== "string") return false;
  return FAILURE_SIGNATURES.some((re) => re.test(output));
}

// Is the command itself reading a failure log? (Then it is the RIGHT move — no nag.)
function isLogReading(command) {
  if (!command || typeof command !== "string") return false;
  return LOG_READING.some((re) => re.test(command));
}

function readState(sessionId) {
  const file = path.join(STATE_DIR, `${sessionId}.json`);
  try {
    const j = JSON.parse(fs.readFileSync(file, "utf8"));
    if (typeof j.fired !== "boolean") j.fired = false;
    return j;
  } catch {
    return { fired: false };
  }
}

function writeState(sessionId, state) {
  try {
    fs.mkdirSync(STATE_DIR, { recursive: true });
    fs.writeFileSync(path.join(STATE_DIR, `${sessionId}.json`), JSON.stringify(state));
  } catch {
    /* fail open — never block on a write error */
  }
}

module.exports = { hasFailureSignature, isLogReading };

// Only run the stdin loop when invoked directly (Claude Code), not when required
// by the test file.
if (require.main !== module) return;

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  if (input.tool_name !== "Bash") process.exit(0);

  const command = input.tool_input?.command || "";

  // The command was itself reading a failure log — that IS the right move.
  if (isLogReading(command)) process.exit(0);

  // tool_response can be a string OR an object with stdout/stderr/output.
  const tr = input.tool_response;
  let output = "";
  if (typeof tr === "string") {
    output = tr;
  } else if (tr && typeof tr === "object") {
    output =
      (typeof tr.stdout === "string" ? tr.stdout : "") +
      (typeof tr.stderr === "string" ? "\n" + tr.stderr : "") +
      (typeof tr.output === "string" ? "\n" + tr.output : "");
  }
  if (!output) process.exit(0);

  if (!hasFailureSignature(output)) process.exit(0);

  const sessionId = input.session_id || "no-session";
  const state = readState(sessionId);
  if (state.fired) process.exit(0); // once per session — flag the pattern, don't nag

  state.fired = true;
  writeState(sessionId, state);

  const additionalContext = [
    "⚠️ read-failure-first: that command's output carries a build/test/CI failure.",
    "",
    "Before hypothesizing a cause or editing a file to fix it, READ THE ACTUAL",
    "FAILURE OUTPUT — the specific failing step's log, the exact error line. Do not",
    'reach for a plausible mechanism ("probably the ordering / the cache / the',
    'culture") before you have read what actually broke.',
    "",
    "For CI: `gh run view <run-id> --log-failed`, or `--job <id> --log` when the",
    "error is swallowed (a script's >/dev/null, a subprocess, an implicit build).",
    "Reproduce the failing command locally before pushing a fix.",
    "",
    "This fires once per session. Full guide: docs/runbooks/ci-actions-troubleshooting.md",
  ].join("\n");

  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: { hookEventName: "PostToolUse", additionalContext },
    })
  );
  process.exit(0);
});
