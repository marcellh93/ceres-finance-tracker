#!/usr/bin/env node
// PreToolUse hook on Bash — Phase F HARD gate.
// Matches `git commit` (with -C, with -m, with heredoc) and requires
// verify-against-codebase to have fired AFTER the most recent tracked-code
// Write in this session.
//
// Path-based bypass (Option C, resolved 2026-05-17):
//   - The staged diff is inspected via `git diff --cached --name-only`.
//   - If NO files in the staged diff match `\.(cs|ts|tsx|csproj|sln)$` → allow.
//     (Pure doc/config/gitignore commits don't trigger the gate.)
//
// State source: .claude/state/playbook/<session_id>.json — specifically
// `fired_with_timestamps` for verify-against-codebase and
// `writes_since_last_skill` for the most-recent-code-write check.

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "playbook");
const TRACKED_EXT_RE = /\.(cs|ts|tsx|csproj|sln)$/i;

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

// Identify whether a Bash command is `git commit`. Accepts:
//   git commit ...
//   git -C <dir> commit ...
//   git commit -m "..."
//   git commit -m "$(cat <<'EOF' ... EOF)"
function isGitCommit(cmd) {
  if (!cmd) return false;
  // Strip leading whitespace and any env-var prefixes.
  const trimmed = cmd.replace(/^\s+/, "");
  return /^git(\s+-C\s+\S+)?\s+commit\b/.test(trimmed);
}

// Returns the array of staged file paths for the project repo. May return [].
function getStagedFiles() {
  try {
    const out = execFileSync(
      "git",
      ["-C", PROJECT_DIR, "diff", "--cached", "--name-only"],
      { encoding: "utf8" }
    );
    return out.split(/\r?\n/).filter(Boolean);
  } catch {
    return [];
  }
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let payload;
  try { payload = JSON.parse(raw || "{}"); } catch { allow(); }

  const toolName = payload.tool_name || "";
  if (toolName !== "Bash") allow();

  const cmd = payload?.tool_input?.command || "";
  if (!isGitCommit(cmd)) allow();

  // Inspect staged diff. Doc-only commits bypass.
  const staged = getStagedFiles();
  const codeFiles = staged.filter((f) => TRACKED_EXT_RE.test(f));
  if (codeFiles.length === 0) allow();

  // Code files are staged. Require verify-against-codebase to have fired AFTER
  // the most recent tracked-code Write in this session.
  const sessionId = payload.session_id;
  const state = readState(sessionId);
  if (!state) {
    // No state file → no verify recorded → block.
    deny(buildReason(codeFiles, "no playbook state file for this session"));
  }

  const fired = Array.isArray(state.fired) ? state.fired : [];
  const verifyFired = fired.includes("verify-against-codebase");

  if (!verifyFired) {
    deny(buildReason(codeFiles, "`verify-against-codebase` has not been invoked this session"));
  }

  // verify-against-codebase fired SOMETIME. Did it fire AFTER the most recent
  // code Write? The state-record hook clears `writes_since_last_skill` every
  // time a Skill fires, so if `writes_since_last_skill` is non-empty after a
  // verify-fire, those writes are STALE relative to the verification.
  const stale = Array.isArray(state.writes_since_last_skill) ? state.writes_since_last_skill : [];
  const staleCode = stale.filter((w) => w && w.file && TRACKED_EXT_RE.test(w.file));

  if (staleCode.length > 0) {
    const names = staleCode.map((w) => w.file).join(", ");
    deny(buildReason(codeFiles, `code Write(s) since last verify: ${names}`));
  }

  allow();
});

function buildReason(stagedCode, detail) {
  const names = stagedCode.join(", ");
  return [
    "Commit blocked (Phase F in playbook/references/constitution.md).",
    "",
    `Tracked-code files in staged diff: ${names}`,
    `Reason: ${detail}.`,
    "",
    "Phase F requires `verify-against-codebase` to have fired AFTER the most recent code Write in this session — to catch project-convention conflicts (wrong HTTP status codes, references to model fields that don't exist, hand-rolled UI patterns that have a documented primitive, shadcn API shapes that differ from base-ui, error shapes that don't match the project's error factory) before they ship.",
    "",
    "Path-based bypass (Option C): pure documentation / config / gitignore commits skip this gate. Only `*.cs / *.ts / *.tsx / *.csproj / *.sln` files trigger it.",
    "",
    "Recover:",
    "  • Invoke the `verify-against-codebase` skill against the latest diff.",
    "  • Address any conflicts found.",
    "  • Then re-attempt the commit.",
  ].join("\n");
}
