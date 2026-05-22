#!/usr/bin/env node
// Stop hook — runs at turn end when state.awaiting is "fixing" or
// "documenting", or transitions out of "resume".
//
//   awaiting === "fixing": if this turn included an Edit/Write/MultiEdit
//     tool call, transition to "resume". Otherwise, re-block the Stop with
//     a reminder.
//
//   awaiting === "documenting": if this turn included an Edit/Write to a
//     path under docs/**, transition to "resume". Otherwise re-block.
//
//   awaiting === "resume": emit additionalContext with the verbatim prior
//     user message and transition to "none".
//
// Fail-open on error.

const fs = require("fs");
const path = require("path");

const STATE_DIR_NAME = path.join("state", "fix-interaction");
const TTL_HOURS = 24;

function projectRoot() {
  return process.env.CLAUDE_PROJECT_DIR || process.cwd();
}
function stateDir() {
  return path.join(projectRoot(), ".claude", STATE_DIR_NAME);
}
function statePath(sessionId) {
  return path.join(stateDir(), `${sessionId}.json`);
}
function logPath() {
  return path.join(stateDir(), "log.jsonl");
}
function errorLogPath() {
  return path.join(stateDir(), "errors.log");
}
function ensureDir() {
  fs.mkdirSync(stateDir(), { recursive: true });
}

function readState(sessionId) {
  const p = statePath(sessionId);
  if (!fs.existsSync(p)) return { awaiting: "none" };
  try {
    return JSON.parse(fs.readFileSync(p, "utf-8"));
  } catch {
    process.stderr.write(
      `⚠ fix-interaction (check-resolution): state file ${p} corrupt; treating as fresh.\n`
    );
    return { awaiting: "none" };
  }
}

function writeStateAtomic(sessionId, state) {
  const final = statePath(sessionId);
  const tmp = final + ".tmp";
  fs.writeFileSync(tmp, JSON.stringify(state, null, 2));
  fs.renameSync(tmp, final);
}

function appendLog(entry) {
  try {
    fs.appendFileSync(logPath(), JSON.stringify(entry) + "\n");
  } catch {}
}

function ttlExpired(state) {
  if (state.awaiting === "none" || !state.started_at) return false;
  const started = Date.parse(state.started_at);
  if (Number.isNaN(started)) return false;
  return Date.now() - started > TTL_HOURS * 60 * 60 * 1000;
}

function readTranscriptMessages(transcriptPath, limit = 20) {
  if (!transcriptPath || !fs.existsSync(transcriptPath)) return null;
  const raw = fs.readFileSync(transcriptPath, "utf-8");
  const lines = raw.split("\n").filter((l) => l.trim().length > 0);
  const tail = lines.slice(-limit);
  const entries = [];
  for (const line of tail) {
    try {
      entries.push(JSON.parse(line));
    } catch {}
  }
  return entries;
}

function lastAssistantToolUses(entries) {
  for (let i = entries.length - 1; i >= 0; i--) {
    const e = entries[i];
    if (e.type !== "assistant") continue;
    const msg = e.message;
    if (!msg || !Array.isArray(msg.content)) continue;
    const tools = msg.content.filter((c) => c.type === "tool_use");
    return tools;
  }
  return [];
}

// Collect every tool_use across all assistant entries in the window. Used to
// detect fixes that landed in a PRIOR assistant turn (e.g. when the user
// followed up with a question and the current turn has no tool calls).
// This is the fix for the 2026-05-22 deadlock where the resolution check
// only inspected the latest assistant message.
function allAssistantToolUses(entries, sinceTimestamp) {
  const tools = [];
  for (const e of entries) {
    if (e.type !== "assistant") continue;
    if (sinceTimestamp && e.timestamp && Date.parse(e.timestamp) < sinceTimestamp) continue;
    const msg = e.message;
    if (!msg || !Array.isArray(msg.content)) continue;
    for (const c of msg.content) {
      if (c.type === "tool_use") tools.push(c);
    }
  }
  return tools;
}

function toolUseTargetsDocs(toolUse) {
  // Edit/Write/MultiEdit on a file under docs/**.
  if (!/^(Edit|Write|MultiEdit)$/.test(toolUse.name || "")) return false;
  const ti = toolUse.input || {};
  const filePath = ti.file_path || "";
  if (!filePath) {
    // MultiEdit doesn't always carry file_path at root; check edits[].
    if (Array.isArray(ti.edits)) {
      // No top-level path — defer to checking all edits' (implicit) target.
      return false;
    }
    return false;
  }
  return /(^|\/)docs(\/|$)/.test(filePath);
}

function toolUseIsCodeWrite(toolUse) {
  return /^(Edit|Write|MultiEdit|NotebookEdit)$/.test(toolUse.name || "");
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  try {
    if (process.env.CERES_SKIP_FIX_INTERACTION_HOOK === "1") {
      process.exit(0);
    }

    let input;
    try {
      input = JSON.parse(raw || "{}");
    } catch {
      process.exit(0);
    }

    const sessionId = input.session_id;
    if (!sessionId) process.exit(0);
    ensureDir();

    const state = readState(sessionId);

    // === TTL sweep ===
    if (ttlExpired(state)) {
      appendLog({
        ts: new Date().toISOString(),
        interaction_id: state.interaction_id || null,
        from: state.awaiting,
        to: "none",
        trigger: "check-resolution",
        reason: "ttl-expired",
      });
      writeStateAtomic(sessionId, { awaiting: "none" });
      process.exit(0);
    }

    if (state.awaiting === "none" || state.awaiting === "fix-or-document" || state.awaiting === "where") {
      // Not this hook's job; either none or being handled by other hooks.
      process.exit(0);
    }

    // === Handle resume: emit the prior user message, transition to none ===
    if (state.awaiting === "resume") {
      const priorMsg = state.prior_user_message || "(prior user message not captured)";
      const ctx = [
        "✓ fix-interaction: fix detour resolved. Resuming the prior thread.",
        "",
        "User's prior message (before the detour):",
        "---",
        priorMsg,
        "---",
        "",
        "Continue from there. If the prior thread had a next-step you were about to take, take it now.",
      ].join("\n");

      appendLog({
        ts: new Date().toISOString(),
        interaction_id: state.interaction_id || null,
        from: "resume",
        to: "none",
        trigger: "check-resolution",
      });
      writeStateAtomic(sessionId, { awaiting: "none" });

      process.stdout.write(
        JSON.stringify({
          hookSpecificOutput: { hookEventName: "Stop", additionalContext: ctx },
        })
      );
      process.exit(0);
    }

    // === Handle fixing / documenting: did the expected tool call land? ===
    const entries = readTranscriptMessages(input.transcript_path, 30);
    if (!entries || entries.length === 0) process.exit(0);
    const tools = lastAssistantToolUses(entries);

    if (state.awaiting === "fixing") {
      // Scan ALL assistant tool_uses since the state was set, not just the
      // current turn's. Fixes the 2026-05-22 deadlock where the user followed
      // up with a question after the fix had already landed in the prior
      // turn, the current turn had no code-write, and the hook re-blocked.
      const sinceTs = state.started_at ? Date.parse(state.started_at) : 0;
      const windowTools = allAssistantToolUses(entries, sinceTs);
      const landed = tools.some(toolUseIsCodeWrite) || windowTools.some(toolUseIsCodeWrite);
      if (landed) {
        const next = {
          ...state,
          awaiting: "resume",
          started_at: new Date().toISOString(),
          previous_state: "fixing",
        };
        writeStateAtomic(sessionId, next);
        appendLog({
          ts: next.started_at,
          interaction_id: next.interaction_id,
          from: "fixing",
          to: "resume",
          trigger: "check-resolution",
        });
        // Don't block the Stop; the next turn's Stop will fire and emit the resume context.
        // But we also want to surface the resume immediately if possible — emit it here.
        const priorMsg = state.prior_user_message || "(prior user message not captured)";
        const ctx = [
          "✓ fix-interaction: fix landed. Next turn resumes the prior thread.",
          "",
          "User's prior message (before the detour):",
          "---",
          priorMsg,
          "---",
        ].join("\n");
        // Auto-advance to none since we already emitted the resume context.
        writeStateAtomic(sessionId, { awaiting: "none" });
        appendLog({
          ts: new Date().toISOString(),
          interaction_id: state.interaction_id,
          from: "resume",
          to: "none",
          trigger: "check-resolution-immediate",
        });
        process.stdout.write(
          JSON.stringify({
            hookSpecificOutput: { hookEventName: "Stop", additionalContext: ctx },
          })
        );
        process.exit(0);
      }
      // No Edit/Write this turn — block with a reminder.
      const reason = [
        "🛑 fix-interaction: state is 'fixing' but no Edit/Write/MultiEdit tool call landed this turn.",
        "",
        "The user chose FIX. Your response must include the actual tool call making the fix.",
        "If you cannot do it (sandbox denied, file outside repo, dependency missing), state the named blocker explicitly.",
        "",
        `Interaction ${state.interaction_id || "?"}. Recovery: /fix-interaction-reset.`,
      ].join("\n");
      process.stdout.write(JSON.stringify({ decision: "block", reason }));
      process.exit(0);
    }

    if (state.awaiting === "documenting") {
      // Same fix as the fixing branch: scan tool_uses across the window
      // since state was set, not just the current turn.
      const sinceTs = state.started_at ? Date.parse(state.started_at) : 0;
      const windowTools = allAssistantToolUses(entries, sinceTs);
      const landed = tools.some(toolUseTargetsDocs) || windowTools.some(toolUseTargetsDocs);
      if (landed) {
        const priorMsg = state.prior_user_message || "(prior user message not captured)";
        const ctx = [
          "✓ fix-interaction: doc entry landed. Next turn resumes the prior thread.",
          "",
          "User's prior message (before the detour):",
          "---",
          priorMsg,
          "---",
        ].join("\n");
        writeStateAtomic(sessionId, { awaiting: "none" });
        appendLog({
          ts: new Date().toISOString(),
          interaction_id: state.interaction_id,
          from: "documenting",
          to: "none",
          trigger: "check-resolution-immediate",
        });
        process.stdout.write(
          JSON.stringify({
            hookSpecificOutput: { hookEventName: "Stop", additionalContext: ctx },
          })
        );
        process.exit(0);
      }
      const reason = [
        "🛑 fix-interaction: state is 'documenting' but no Edit/Write to docs/** landed this turn.",
        "",
        "The user named a destination. Your response must include the Edit/Write that adds the `[ ]` line at that destination.",
        "",
        `Destination snippet: "${(state.destination_snippet || "").slice(0, 200)}"`,
        `Interaction ${state.interaction_id || "?"}. Recovery: /fix-interaction-reset.`,
      ].join("\n");
      process.stdout.write(JSON.stringify({ decision: "block", reason }));
      process.exit(0);
    }

    process.exit(0);
  } catch (err) {
    try {
      ensureDir();
      fs.appendFileSync(
        errorLogPath(),
        JSON.stringify({
          ts: new Date().toISOString(),
          hook: __filename,
          error: err.message,
          stack: err.stack,
        }) + "\n"
      );
    } catch {}
    process.stderr.write(
      `⚠ fix-interaction check hook errored; gate open this turn. See ${errorLogPath()}.\n`
    );
    process.exit(0);
  }
});
