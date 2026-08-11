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


function freshState() {
  return { fired: [], fired_with_timestamps: [], writes_since_last_skill: [], open_deferrals: [] };
}

// One state transition. Pure — returns a NEW state, never mutates the input.
// Pinned by __tests__/state-record-skill-fire.test.js because all four HARD
// playbook gates read what this writes: a missed write lets an unverified
// commit through, a missed reset blocks every commit forever.
function applyEvent(prevState, input, projectDir, now) {
  const out = {
    fired: [...(prevState.fired || [])],
    fired_with_timestamps: [...(prevState.fired_with_timestamps || [])],
    writes_since_last_skill: [...(prevState.writes_since_last_skill || [])],
    open_deferrals: [...(prevState.open_deferrals || [])],
  };
  const tName = (input && input.tool_name) || "";
  const tInput = (input && input.tool_input) || {};

  if (tName === "Skill") {
    const skillName = tInput.skill || tInput.name || (tInput.args && tInput.args.skill) || "";
    if (!skillName) return out;
    if (!out.fired.includes(skillName)) out.fired.push(skillName);
    out.fired_with_timestamps.push({
      skill: skillName,
      at: now,
      tool_use_index: out.fired_with_timestamps.length + out.writes_since_last_skill.length,
    });
    // Phase F asks "did the verify skill fire AFTER the most recent code Write?"
    // — so every skill fire resets the counter.
    out.writes_since_last_skill = [];
    return out;
  }

  if (["Edit", "Write", "MultiEdit", "NotebookEdit"].includes(tName)) {
    const fp = tInput.file_path || tInput.notebook_path;
    if (!fp) return out;
    if (!TRACKED_EXT.has(path.extname(fp).toLowerCase())) return out;
    const abs = path.resolve(fp);
    const projAbs = path.resolve(projectDir);
    if (!abs.startsWith(projAbs + path.sep)) return out;
    out.writes_since_last_skill.push({ file: path.relative(projAbs, abs), at: now });
    return out;
  }

  return out;
}

if (typeof module !== "undefined" && module.exports) {
  module.exports = { applyEvent, freshState, TRACKED_EXT };
}

let raw = "";
if (require.main === module) {
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

  const nextState = applyEvent(state, { tool_name: toolName, tool_input: toolInput }, PROJECT_DIR, now);
  if (JSON.stringify(nextState) !== JSON.stringify(state)) writeState(stateFile, nextState);

  process.exit(0);
});
}
