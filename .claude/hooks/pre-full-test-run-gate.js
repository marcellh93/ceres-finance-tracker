#!/usr/bin/env node
// PreToolUse gate — one unfiltered `dotnet test` per turn, run in the background.
//
// Why this exists (2026-08-24): the full suite takes 7-11 minutes and an agent tool
// call caps at 10. A run that is simply still working times out and reads as a hang.
// On 2026-08-24 that timeout was misread as failure six times in a row. Every re-run
// was wasted, and each seeded ~1,000 more orphan rows into the shared test database —
// worsening the very bloat being investigated. Hours of wall-clock, and the user's
// verdict was "you are spending too much tokens on this".
//
// The gate is deliberately narrow. It does NOT block:
//   * filtered runs (--filter), which are the right tool while iterating
//   * `dotnet build`, `dotnet ef`, or anything else
//   * the first unfiltered run of a turn, if backgrounded
// It blocks only the second unfiltered run in one turn, and any foreground one.
//
// Bypass: CERES_SKIP_FULL_TEST_GATE=1

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "full-test-runs");

function readInput() {
  try {
    return JSON.parse(fs.readFileSync(0, "utf8"));
  } catch {
    return null;
  }
}

function allow() {
  process.exit(0);
}

function deny(message) {
  // Exit 2 with stderr = deny, per the PreToolUse contract.
  process.stderr.write(message);
  process.exit(2);
}

function turnKey(sessionId) {
  // The turn marker run-tests.sh maintains is the closest thing to a turn id.
  // Fall back to the session so the gate still counts within a session.
  const marker = path.join(PROJECT_DIR, ".claude", "state", "run-tests", "last.log");
  try {
    return `${sessionId}:${Math.floor(fs.statSync(marker).mtimeMs / 1000)}`;
  } catch {
    return `${sessionId}:no-marker`;
  }
}

function main() {
  if (process.env.CERES_SKIP_FULL_TEST_GATE === "1") allow();

  const input = readInput();
  if (!input || input.tool_name !== "Bash") allow();

  const cmd = String(input.tool_input?.command ?? "");
  if (!/\bdotnet\s+test\b/.test(cmd)) allow();

  // Filtered runs are always fine — they finish in seconds and are the
  // recommended way to iterate.
  if (/--filter\b/.test(cmd)) allow();

  const backgrounded = /\bnohup\b/.test(cmd) || /&\s*$/.test(cmd.trim()) ||
                       input.tool_input?.run_in_background === true;

  if (!backgrounded) {
    deny(
      "pre-full-test-run-gate: an unfiltered `dotnet test` takes 7-11 minutes on this\n" +
      "project and a tool call caps at 10, so a foreground run will often time out while\n" +
      "still working — which reads as a hang and invites a pointless re-run.\n\n" +
      "Do one of these instead:\n" +
      "  * Filter it:      dotnet test --filter \"FullyQualifiedName~TheThingYouChanged\"\n" +
      "  * Background it:  nohup dotnet test > /tmp/dt.log 2>&1 &   then poll /tmp/dt.log\n\n" +
      "Before diagnosing a timeout as a failure, check the log for a `Passed!` line — the\n" +
      "run usually completed. See memory: feedback_run_full_suite_once_per_turn.\n"
    );
  }

  // Backgrounded and unfiltered: allow the first per turn, refuse the rest.
  const sessionId = input.session_id || "unknown";
  const key = turnKey(sessionId);
  let seen = {};
  const stateFile = path.join(STATE_DIR, "turns.json");
  try {
    seen = JSON.parse(fs.readFileSync(stateFile, "utf8"));
  } catch { /* first run */ }

  if (seen[key]) {
    deny(
      "pre-full-test-run-gate: the full suite has already run once this turn.\n\n" +
      "Re-running it rarely produces new information, and on this project each run seeds\n" +
      "~1,000 orphan rows into the shared test database (26 seeded categories per test\n" +
      "user, no FK to AspNetUsers so cleanup misses them). Six re-runs on 2026-08-24 took\n" +
      "the table from 95k to 230k rows and made the suite measurably slower.\n\n" +
      "If the earlier run timed out, read its log rather than repeating it — it usually\n" +
      "finished. If you genuinely need another full run, set CERES_SKIP_FULL_TEST_GATE=1\n" +
      "and say in your reply why the second run was necessary.\n"
    );
  }

  seen[key] = new Date().toISOString();
  // Keep the file small: only the most recent handful of turns matter.
  const entries = Object.entries(seen).slice(-20);
  try {
    fs.mkdirSync(STATE_DIR, { recursive: true });
    fs.writeFileSync(stateFile, JSON.stringify(Object.fromEntries(entries), null, 2));
  } catch { /* fail open — never block on a write error */ }

  allow();
}

main();
