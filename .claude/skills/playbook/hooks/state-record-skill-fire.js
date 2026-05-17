#!/usr/bin/env node
// PostToolUse hook — records Skill tool invocations and code Writes to
// .claude/state/playbook/<session_id>.json so every HARD gate (Phases B, D,
// E, F) can answer "did skill X fire this session?" without scanning the
// transcript.
//
// State file shape:
//   {
//     "fired": ["superpowers:brainstorming", "verify-against-codebase", ...],
//     "fired_with_timestamps": [
//       { "skill": "...", "at": "ISO-8601", "tool_use_index": N }
//     ],
//     "writes_since_last_skill": [
//       { "file": "ProjectCeres/Common/Authentication/EmailChangeService.cs", "at": "ISO-8601" }
//     ],
//     "open_deferrals": []
//   }
//
// Triggers on TWO tool names (the hook receives both because settings.json
// wires it with a broader matcher):
//   - Skill         → record a fired skill
//   - Edit/Write/MultiEdit on tracked code → record into writes_since_last_skill
//
// Tracked code extensions (for Phase F's "did verify-against-codebase fire
// AFTER the most recent code Write?" check):
//   .cs .ts .tsx .csproj .sln

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "playbook");
const TRACKED_EXT = new Set([".cs", ".ts", ".tsx", ".csproj", ".sln"]);

function ensureDir(p) {
  try { fs.mkdirSync(p, { recursive: true }); } catch {}
}

function readState(file) {
  let state = { fired: [], fired_with_timestamps: [], writes_since_last_skill: [], open_deferrals: [] };
  try {
    const parsed = JSON.parse(fs.readFileSync(file, "utf8"));
    if (parsed && typeof parsed === "object") {
      if (Array.isArray(parsed.fired)) state.fired = parsed.fired;
      if (Array.isArray(parsed.fired_with_timestamps)) state.fired_with_timestamps = parsed.fired_with_timestamps;
      if (Array.isArray(parsed.writes_since_last_skill)) state.writes_since_last_skill = parsed.writes_since_last_skill;
      if (Array.isArray(parsed.open_deferrals)) state.open_deferrals = parsed.open_deferrals;
    }
  } catch {
    // missing or malformed → initialize fresh
  }
  return state;
}

function writeState(file, state) {
  try { fs.writeFileSync(file, JSON.stringify(state, null, 2)); } catch {}
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try { input = JSON.parse(raw || "{}"); } catch { process.exit(0); }

  const sessionId = input.session_id;
  if (!sessionId) process.exit(0);

  const toolName = input.tool_name || "";
  const toolInput = input.tool_input || {};
  const now = new Date().toISOString();

  ensureDir(STATE_DIR);
  const stateFile = path.join(STATE_DIR, `${sessionId}.json`);
  const state = readState(stateFile);

  if (toolName === "Skill") {
    // Record the skill fire.
    const skillName =
      toolInput.skill ||
      toolInput.name ||
      toolInput.args?.skill ||
      "";
    if (!skillName) process.exit(0);

    if (!state.fired.includes(skillName)) state.fired.push(skillName);
    state.fired_with_timestamps.push({
      skill: skillName,
      at: now,
      tool_use_index: state.fired_with_timestamps.length + state.writes_since_last_skill.length,
    });
    // A skill fired — clear the write list. Phase F asks "did verify-against-codebase
    // fire AFTER the most recent code Write?" so we reset the counter at every fire.
    state.writes_since_last_skill = [];

    writeState(stateFile, state);
    process.exit(0);
  }

  if (["Edit", "Write", "MultiEdit", "NotebookEdit"].includes(toolName)) {
    const filePath = toolInput.file_path || toolInput.notebook_path;
    if (!filePath) process.exit(0);

    const ext = path.extname(filePath).toLowerCase();
    if (!TRACKED_EXT.has(ext)) process.exit(0);

    // Only count writes inside the project tree.
    const abs = path.resolve(filePath);
    const projAbs = path.resolve(PROJECT_DIR);
    if (!abs.startsWith(projAbs + path.sep)) process.exit(0);

    const rel = path.relative(projAbs, abs);
    state.writes_since_last_skill.push({ file: rel, at: now });
    writeState(stateFile, state);
    process.exit(0);
  }

  process.exit(0);
});
