#!/usr/bin/env node
// PreToolUse hook on Bash — Phase F HARD gate.
// Matches `git commit` (with -C, with -m, with heredoc) and requires the
// appropriate verify skill(s) to have fired AFTER the most recent tracked-code
// Write in this session.
//
// Path-based bypass (Option C, resolved 2026-05-17):
//   - The staged diff is inspected via `git diff --cached --name-only`.
//   - If NO files in the staged diff match `\.(cs|ts|tsx|csproj|sln)$` → allow.
//     (Pure doc/config/gitignore commits don't trigger the gate.)
//
// Tiered verify (added 2026-05-17 — same day as the split):
//   - Backend-only staged diff (any of *.cs / *.csproj, no *.ts / *.tsx) →
//     require `verify-backend` (or legacy `verify-against-codebase`).
//   - Frontend-only staged diff (any of *.ts / *.tsx, no *.cs / *.csproj) →
//     require `verify-frontend` (or legacy `verify-against-codebase`).
//   - Mixed staged diff (both sides), or contains *.sln →
//     require BOTH `verify-backend` AND `verify-frontend`
//     (legacy `verify-against-codebase` satisfies both as a router).
//
// State source: .claude/state/playbook/<session_id>.json — specifically
// `fired_with_timestamps` for the verify skills and
// `writes_since_last_skill` for the most-recent-code-write check.

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "playbook");
const TRACKED_EXT_RE = /\.(cs|ts|tsx|csproj|sln)$/i;
const BACKEND_EXT_RE = /\.(cs|csproj)$/i;
const FRONTEND_EXT_RE = /\.(ts|tsx)$/i;
const SLN_RE = /\.sln$/i;

// Which verify skills satisfy "backend" / "frontend" / "router"?
const BACKEND_SKILLS = new Set(["verify-backend", "verify-against-codebase"]);
const FRONTEND_SKILLS = new Set(["verify-frontend", "verify-against-codebase"]);

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

  // Tier the diff. .sln always escalates to mixed because solution edits can
  // ripple either side.
  const hasBackend = codeFiles.some((f) => BACKEND_EXT_RE.test(f));
  const hasFrontend = codeFiles.some((f) => FRONTEND_EXT_RE.test(f));
  const hasSln = codeFiles.some((f) => SLN_RE.test(f));
  const requireBackend = hasBackend || hasSln;
  const requireFrontend = hasFrontend || hasSln;

  // Code files are staged. Require the matching verify skill(s) to have fired
  // AFTER the most recent tracked-code Write in this session.
  const sessionId = payload.session_id;
  const state = readState(sessionId);
  if (!state) {
    // No state file → no verify recorded → block.
    deny(buildReason(codeFiles, requireBackend, requireFrontend, "no playbook state file for this session"));
  }

  const fired = Array.isArray(state.fired) ? state.fired : [];
  const backendOk = !requireBackend || fired.some((s) => BACKEND_SKILLS.has(s));
  const frontendOk = !requireFrontend || fired.some((s) => FRONTEND_SKILLS.has(s));

  if (!backendOk || !frontendOk) {
    const missing = [];
    if (!backendOk) missing.push("`verify-backend` (or legacy `verify-against-codebase`)");
    if (!frontendOk) missing.push("`verify-frontend` (or legacy `verify-against-codebase`)");
    deny(buildReason(codeFiles, requireBackend, requireFrontend, `not invoked this session: ${missing.join(" + ")}`));
  }

  // Required verify skill(s) fired SOMETIME. Did they fire AFTER the most
  // recent code Write? The state-record hook clears `writes_since_last_skill`
  // every time a Skill fires, so if `writes_since_last_skill` is non-empty
  // after a verify-fire, those writes are STALE relative to the verification.
  const stale = Array.isArray(state.writes_since_last_skill) ? state.writes_since_last_skill : [];
  const staleCode = stale.filter((w) => w && w.file && TRACKED_EXT_RE.test(w.file));

  if (staleCode.length > 0) {
    const names = staleCode.map((w) => w.file).join(", ");
    deny(buildReason(codeFiles, requireBackend, requireFrontend, `code Write(s) since last verify: ${names}`));
  }

  allow();
});

function buildReason(stagedCode, requireBackend, requireFrontend, detail) {
  const names = stagedCode.join(", ");
  const required = [];
  if (requireBackend && requireFrontend) {
    required.push("Mixed diff (both server and client code, OR `.sln` touched) — requires BOTH `verify-backend` AND `verify-frontend`. Invoking the legacy `verify-against-codebase` router satisfies both.");
  } else if (requireBackend) {
    required.push("Backend-only diff — requires `verify-backend` (or legacy `verify-against-codebase`).");
  } else if (requireFrontend) {
    required.push("Frontend-only diff — requires `verify-frontend` (or legacy `verify-against-codebase`).");
  }

  return [
    "Commit blocked (Phase F in playbook/references/constitution.md).",
    "",
    `Tracked-code files in staged diff: ${names}`,
    `Reason: ${detail}.`,
    "",
    ...required,
    "",
    "Phase F requires the matching verify skill(s) to have fired AFTER the most recent code Write in this session — to catch project-convention conflicts (wrong HTTP status codes, references to model fields that don't exist, hand-rolled UI patterns that have a documented primitive, shadcn API shapes that differ from base-ui, error shapes that don't match the project's error factory) before they ship.",
    "",
    "Path-based bypass (Option C): pure documentation / config / gitignore commits skip this gate. Only `*.cs / *.ts / *.tsx / *.csproj / *.sln` files trigger it.",
    "",
    "Recover:",
    "  • Invoke the appropriate verify skill against the latest diff (per the required-skill line above).",
    "  • Address any conflicts found.",
    "  • Then re-attempt the commit.",
  ].join("\n");
}
