#!/usr/bin/env node
// PostToolUse hook — opportunistic per-turn snapshotter. Backup path for
// state-rehydration. The primary path (snapshot-on-pre-compact.js) is wired
// on PreCompact and fires explicitly before compaction.
//
// Snapshot location: .claude/state/state-rehydration/<session_id>/snapshot.json

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();

function safeReadJson(file) {
  try { return JSON.parse(fs.readFileSync(file, "utf8")); } catch { return null; }
}

function ensureDir(p) { try { fs.mkdirSync(p, { recursive: true }); } catch {} }

// Find the most recently modified file under a directory matching a pattern.
function mostRecentMatching(dir, regex) {
  try {
    const files = fs.readdirSync(dir);
    let best = null;
    for (const f of files) {
      if (!regex.test(f)) continue;
      const full = path.join(dir, f);
      const stat = fs.statSync(full);
      if (!stat.isFile()) continue;
      if (!best || stat.mtimeMs > best.mtimeMs) {
        best = { path: path.relative(PROJECT_DIR, full), last_modified: stat.mtime.toISOString(), mtimeMs: stat.mtimeMs };
      }
    }
    if (best) { delete best.mtimeMs; }
    return best;
  } catch {
    return null;
  }
}

function buildSnapshot(sessionId, trigger) {
  const playbookState = safeReadJson(path.join(PROJECT_DIR, ".claude", "state", "playbook", `${sessionId}.json`));
  const runTestsState = safeReadJson(path.join(PROJECT_DIR, ".claude", "state", "run-tests", `${sessionId}.json`));

  const skillsFired = (playbookState && Array.isArray(playbookState.fired)) ? playbookState.fired : [];
  let lastCodeWrites = [];
  if (playbookState && Array.isArray(playbookState.writes_since_last_skill)) {
    lastCodeWrites = playbookState.writes_since_last_skill.slice(-10);
  } else if (runTestsState && Array.isArray(runTestsState.files)) {
    lastCodeWrites = runTestsState.files.slice(-10).map((f) => ({ file: f, at: null }));
  }
  const openDeferrals = (playbookState && Array.isArray(playbookState.open_deferrals)) ? playbookState.open_deferrals : [];

  const docsDir = path.join(PROJECT_DIR, "docs");
  const currentArtifacts = {
    open_spec: mostRecentMatching(path.join(docsDir, "superpowers", "specs"), /\.md$/),
    open_plan: mostRecentMatching(path.join(docsDir, "superpowers", "plans"), /\.md$/),
    open_roadmap: mostRecentMatching(docsDir, /^roadmap-phase-[a-z0-9-]+\.md$/i),
  };

  return {
    session_id: sessionId,
    snapshot_timestamp: new Date().toISOString(),
    trigger,
    skills_fired: skillsFired,
    last_code_writes: lastCodeWrites,
    open_deferrals: openDeferrals,
    current_artifacts: currentArtifacts,
    last_assistant_thought: "",
    snapshot_version: 1,
  };
}

function writeSnapshot(sessionId, snapshot) {
  const dir = path.join(PROJECT_DIR, ".claude", "state", "state-rehydration", sessionId);
  ensureDir(dir);
  const file = path.join(dir, "snapshot.json");
  try { fs.writeFileSync(file, JSON.stringify(snapshot, null, 2)); } catch {}
}

let raw = "";
if (require.main === module) {
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try { input = JSON.parse(raw || "{}"); } catch { process.exit(0); }
  const sessionId = input.session_id;
  if (!sessionId) process.exit(0);
  const snapshot = buildSnapshot(sessionId, "PostToolUse-turn-end");
  writeSnapshot(sessionId, snapshot);
  process.exit(0);
});
}

module.exports = { buildSnapshot, writeSnapshot };
