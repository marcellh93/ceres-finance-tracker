#!/usr/bin/env node
// PostToolUse hook — records per-session writes to project code so the Stop
// hook (run-tests.sh) only fires `dotnet test` for sessions that actually
// touched code in THIS session.
//
// Why: multiple Claude Code sessions share one working tree. Without this,
// session A's uncommitted .cs/.ts edits make session B run `dotnet test`
// every turn even when B is only discussing design. That noise is the bug
// this hook fixes.
//
// State location: .claude/state/run-tests/<session_id>.json
// State shape: { "files": ["ProjectCeres/Program.cs", ...] }
//
// Tracked extensions match run-tests.sh's existing trigger list:
//   .cs .ts .tsx .csproj .sln

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "run-tests");

const TRACKED_EXT = new Set([".cs", ".ts", ".tsx", ".csproj", ".sln"]);

function ensureDir(p) {
  try { fs.mkdirSync(p, { recursive: true }); } catch {}
}

// Returns the project-relative path to record, or null to ignore this call.
// Pure — the whole gate decision, so __tests__/track-session-writes.test.js can
// pin it. run-tests.sh AND evidence-bundle-check.js both key off the result.
function classifyWrite(input, projectDir) {
  if (!input || typeof input !== "object") return null;

  const toolInput = input.tool_input || {};
  if (!input.session_id) return null;
  if (!["Edit", "Write", "MultiEdit", "NotebookEdit"].includes(input.tool_name)) return null;

  const filePath = toolInput.file_path || toolInput.notebook_path;
  if (!filePath) return null;

  if (!TRACKED_EXT.has(path.extname(filePath).toLowerCase())) return null;

  // Only count writes inside the project tree — out-of-tree edits (e.g. to
  // ~/.claude/...) don't affect the build.
  const abs = path.resolve(filePath);
  const projAbs = path.resolve(projectDir);
  if (!abs.startsWith(projAbs + path.sep)) return null;

  return path.relative(projAbs, abs);
}

if (typeof module !== "undefined" && module.exports) {
  module.exports = { classifyWrite };
}

let raw = "";
if (require.main === module) {
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try { input = JSON.parse(raw || "{}"); } catch { process.exit(0); }

  const sessionId = input.session_id;
  const rel = classifyWrite(input, PROJECT_DIR);
  if (rel === null) process.exit(0);

  ensureDir(STATE_DIR);
  const stateFile = path.join(STATE_DIR, `${sessionId}.json`);

  let state = { files: [] };
  try {
    state = JSON.parse(fs.readFileSync(stateFile, "utf8"));
    if (!Array.isArray(state.files)) state.files = [];
  } catch {}

  if (!state.files.includes(rel)) state.files.push(rel);

  try { fs.writeFileSync(stateFile, JSON.stringify(state)); } catch {}

  process.exit(0);
});
}
