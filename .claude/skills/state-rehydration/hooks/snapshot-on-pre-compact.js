#!/usr/bin/env node
// PreCompact hook — primary snapshot path. Fires before context compaction
// begins. Uses the same buildSnapshot/writeSnapshot logic as the per-turn
// backup hook (snapshot-on-turn-end.js) but tags the snapshot with the
// matcher_value ("manual" or "auto") so the rehydration reminder knows how
// the snapshot was triggered.
//
// Payload (stdin) from Claude Code's PreCompact event:
//   {
//     session_id, transcript_path, cwd,
//     hook_event_name: "PreCompact",
//     matcher_value: "manual" | "auto"
//   }
//
// We do NOT block compaction (the event supports decision: "block" but
// that's not what we want — compaction is a legitimate user action).
// We snapshot, exit 0, and let compaction proceed.
//
// Additionally, we try to capture the last assistant thought by tailing
// the transcript file (best-effort — if it doesn't work, the field stays
// empty and the rehydration reminder still surfaces the operational state).

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();

// Reuse the snapshot-builder from the backup hook so the two paths stay in sync.
let snapshotter;
try {
  snapshotter = require("./snapshot-on-turn-end.js");
} catch {
  // If the require fails for any reason, fall back to a minimal inline shape.
  snapshotter = null;
}

function inlineBuildSnapshot(sessionId, trigger) {
  const safeReadJson = (file) => {
    try { return JSON.parse(fs.readFileSync(file, "utf8")); } catch { return null; }
  };
  const playbookState = safeReadJson(path.join(PROJECT_DIR, ".claude", "state", "playbook", `${sessionId}.json`));
  return {
    session_id: sessionId,
    snapshot_timestamp: new Date().toISOString(),
    trigger,
    skills_fired: (playbookState && Array.isArray(playbookState.fired)) ? playbookState.fired : [],
    last_code_writes: (playbookState && Array.isArray(playbookState.writes_since_last_skill))
      ? playbookState.writes_since_last_skill.slice(-10) : [],
    open_deferrals: (playbookState && Array.isArray(playbookState.open_deferrals)) ? playbookState.open_deferrals : [],
    current_artifacts: {},
    last_assistant_thought: "",
    snapshot_version: 1,
  };
}

function inlineWriteSnapshot(sessionId, snapshot) {
  const dir = path.join(PROJECT_DIR, ".claude", "state", "state-rehydration", sessionId);
  try { fs.mkdirSync(dir, { recursive: true }); } catch {}
  try { fs.writeFileSync(path.join(dir, "snapshot.json"), JSON.stringify(snapshot, null, 2)); } catch {}
}

// Best-effort tail of the transcript to capture last assistant message.
function tailLastAssistantThought(transcriptPath) {
  if (!transcriptPath) return "";
  try {
    const content = fs.readFileSync(transcriptPath, "utf8");
    const lines = content.trim().split("\n");
    // Walk backwards; transcript is JSONL, each line a message envelope.
    for (let i = lines.length - 1; i >= 0; i--) {
      try {
        const obj = JSON.parse(lines[i]);
        // Look for an assistant role with a text content field.
        if (obj && (obj.role === "assistant" || obj.type === "assistant")) {
          const content = obj.content || obj.message?.content || obj.text || "";
          if (typeof content === "string" && content.trim()) {
            return content.slice(0, 500);
          }
          if (Array.isArray(content)) {
            for (const part of content) {
              if (part && typeof part === "object" && part.type === "text" && typeof part.text === "string") {
                return part.text.slice(0, 500);
              }
            }
          }
        }
      } catch {
        // not JSON; skip
      }
    }
  } catch {
    // unreadable; return empty
  }
  return "";
}

// Exported for __tests__/state-rehydration.test.js.
if (typeof module !== "undefined" && module.exports) {
  module.exports = { tailLastAssistantThought, inlineBuildSnapshot, inlineWriteSnapshot };
}

let raw = "";
if (require.main === module) {
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try { input = JSON.parse(raw || "{}"); } catch { process.exit(0); }

  const sessionId = input.session_id;
  if (!sessionId) process.exit(0);

  const matcher = input.matcher_value || input.matcher || "unknown";
  const trigger = `PreCompact-${matcher}`;

  const builder = snapshotter && snapshotter.buildSnapshot ? snapshotter.buildSnapshot : inlineBuildSnapshot;
  const writer = snapshotter && snapshotter.writeSnapshot ? snapshotter.writeSnapshot : inlineWriteSnapshot;

  const snapshot = builder(sessionId, trigger);
  snapshot.last_assistant_thought = tailLastAssistantThought(input.transcript_path);

  writer(sessionId, snapshot);

  // Allow compaction to proceed. (We intentionally do NOT emit decision: "block".)
  process.exit(0);
});
}
