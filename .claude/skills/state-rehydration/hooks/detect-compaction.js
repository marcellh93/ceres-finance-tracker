#!/usr/bin/env node
// SessionStart hook — fires on every session start. When source === "compact",
// the session is resuming AFTER a compaction event; read the snapshot file
// and surface a pointer to it via additionalContext.
//
// Payload (stdin):
//   {
//     session_id, transcript_path, cwd,
//     hook_event_name: "SessionStart",
//     source: "startup" | "resume" | "compact" | ...
//   }
//
// On non-"compact" source: silent exit.
// On "compact" source: read snapshot, summarize, emit additionalContext.

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();

function summarize(snapshot) {
  const lines = [];
  if (snapshot.trigger) {
    lines.push(`  • trigger: ${snapshot.trigger} (snapshot written ${snapshot.snapshot_timestamp})`);
  }
  const sf = Array.isArray(snapshot.skills_fired) ? snapshot.skills_fired : [];
  lines.push(`  • skills_fired: ${sf.length} entries${sf.length ? " — " + sf.join(", ") : ""}`);

  const lw = Array.isArray(snapshot.last_code_writes) ? snapshot.last_code_writes : [];
  // Every field here comes from a JSON file written by another process — a null
  // or shapeless entry must not throw. This runs at SessionStart right after a
  // compaction, when the snapshot is the only surviving state.
  const mostRecent = lw.length ? ((lw[lw.length - 1] || {}).file || "(unknown)") : "";
  lines.push(`  • last_code_writes: ${lw.length} entries${lw.length ? " — most recent: " + mostRecent : ""}`);

  const od = Array.isArray(snapshot.open_deferrals) ? snapshot.open_deferrals : [];
  lines.push(`  • open_deferrals: ${od.length} entries${od.length ? " — " + od.map((d) => (d || {}).stage || (d || {}).file || "(unknown)").join(", ") : ""}`);

  const ca = snapshot.current_artifacts || {};
  if (ca.open_spec) lines.push(`  • open_spec: ${ca.open_spec.path}`);
  if (ca.open_plan) lines.push(`  • open_plan: ${ca.open_plan.path}`);
  if (ca.open_roadmap) lines.push(`  • open_roadmap: ${ca.open_roadmap.path}`);

  if (snapshot.last_assistant_thought) {
    const t = String(snapshot.last_assistant_thought).slice(0, 200).replace(/\s+/g, " ");
    lines.push(`  • last_assistant_thought: "${t}…"`);
  }

  return lines.join("\n");
}

// Exported for __tests__/state-rehydration.test.js.
if (typeof module !== "undefined" && module.exports) {
  module.exports = { summarize };
}

let raw = "";
if (require.main === module) {
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try { input = JSON.parse(raw || "{}"); } catch { process.exit(0); }

  const source = input.source || "";
  if (source !== "compact") process.exit(0);

  const sessionId = input.session_id;
  if (!sessionId) process.exit(0);

  const snapshotFile = path.join(
    PROJECT_DIR, ".claude", "state", "state-rehydration", sessionId, "snapshot.json"
  );

  let snapshot;
  try {
    snapshot = JSON.parse(fs.readFileSync(snapshotFile, "utf8"));
  } catch {
    // No snapshot for this session — emit a softer hint.
    const additionalContext = [
      "♻️ state-rehydration: SessionStart fired with source=\"compact\" but no snapshot was found for this session.",
      "",
      `Looked for: ${snapshotFile}`,
      "",
      "If the prior turn had operational state worth preserving (open spec, open deferrals, mid-procedure skill), reconstruct it from the visible transcript before the next non-trivial action.",
    ].join("\n");
    process.stdout.write(JSON.stringify({
      hookSpecificOutput: { hookEventName: "SessionStart", additionalContext },
    }));
    process.exit(0);
  }

  const additionalContext = [
    "♻️ state-rehydration: compaction detected. Operational snapshot available.",
    "",
    `Snapshot file: ${snapshotFile}`,
    "Summary:",
    summarize(snapshot),
    "",
    "Your NEXT tool call must be a Read on that snapshot path. No Edit, no Bash, no spec/plan writing, no `git commit` before it.",
    "",
    "The summary above is deliberately terse — the snapshot carries fields it omits, and the compacted conversation will usually have drifted from disk state on at least one of open deferrals / open spec / open plan / last code writes. Acknowledging this reminder in text without opening the file is the failure mode it exists to prevent (logged 2026-05-19).",
  ].join("\n");

  process.stdout.write(JSON.stringify({
    hookSpecificOutput: { hookEventName: "SessionStart", additionalContext },
  }));
  process.exit(0);
});
}
