#!/usr/bin/env node
// PostToolUse hook — Steve Kinney fingerprint algorithm.
// Hashes (tool_name, normalized_tool_input, result_preview) and tracks per-session counts.
// Escalates: 2 matches = silent log; 3 matches = warn via additionalContext; 5 matches = block + force deep-fix-mode.
// Sources:
//   - https://stevekinney.com/writing/agent-loops
//   - https://github.com/NousResearch/hermes-agent/issues/481
//   - https://code.claude.com/docs/en/hooks (PostToolUse schema)

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const WARN_AT = 3;
const BLOCK_AT = 5;
const STATE_DIR = path.join(
  process.env.CLAUDE_PROJECT_DIR || process.cwd(),
  ".claude",
  "state",
  "deep-fix-mode"
);

function ensureDir(p) {
  try {
    fs.mkdirSync(p, { recursive: true });
  } catch {}
}

function normalizeInput(toolName, toolInput) {
  // Tool-specific normalization. Strip volatile fields, keep what identifies the action.
  if (!toolInput || typeof toolInput !== "object") return JSON.stringify(toolInput || null);

  switch (toolName) {
    case "Bash":
      return JSON.stringify({ command: (toolInput.command || "").trim() });
    case "Edit":
    case "Write":
      return JSON.stringify({
        file_path: toolInput.file_path,
        // Hash content only — don't store; we just want stable identity.
        new_string_hash: toolInput.new_string
          ? crypto.createHash("sha256").update(toolInput.new_string).digest("hex").slice(0, 16)
          : null,
        old_string_hash: toolInput.old_string
          ? crypto.createHash("sha256").update(toolInput.old_string).digest("hex").slice(0, 16)
          : null,
      });
    case "Read":
      return JSON.stringify({ file_path: toolInput.file_path, offset: toolInput.offset, limit: toolInput.limit });
    case "Grep":
      return JSON.stringify({ pattern: toolInput.pattern, path: toolInput.path, glob: toolInput.glob });
    case "Glob":
      return JSON.stringify({ pattern: toolInput.pattern, path: toolInput.path });
    default:
      return JSON.stringify(toolInput);
  }
}

function resultPreview(toolResponse) {
  if (toolResponse == null) return "";
  const s = typeof toolResponse === "string" ? toolResponse : JSON.stringify(toolResponse);
  // First 200 chars — discriminates success-vs-failure and same-vs-different output without sensitivity to whitespace tails.
  return s.replace(/\s+/g, " ").trim().slice(0, 200);
}

function fingerprint(toolName, toolInput, toolResponse) {
  const payload = `${toolName}|${normalizeInput(toolName, toolInput)}|${resultPreview(toolResponse)}`;
  return crypto.createHash("sha256").update(payload).digest("hex").slice(0, 24);
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  const sessionId = input.session_id;
  const toolName = input.tool_name;
  const toolInput = input.tool_input;
  const toolResponse = input.tool_response;

  if (!sessionId || !toolName) process.exit(0);

  // Only fingerprint tools where repetition is meaningfully mutating or stateful.
  //
  // Excluded by design (v2 fix, 2026-05-17):
  //   - Read, Grep, Glob — diligent pre-edit verification routinely produces
  //     repeated reads when an agent is being careful (re-reading after an edit
  //     to confirm, or scanning N matches of the same grep). Subagent flows
  //     especially hit this: a fresh subagent re-reads its target file before
  //     editing, which the v1 tracker counted as a "loop." See screenshot
  //     2026-05-17 of the "stalled on advisory" subagent incident.
  //   - Edit/Write are still tracked because content-identical mutations across
  //     turns IS a real Fixation signal.
  const TRACKED = new Set(["Bash", "Edit", "Write"]);
  if (!TRACKED.has(toolName)) process.exit(0);

  ensureDir(STATE_DIR);
  const stateFile = path.join(STATE_DIR, `${sessionId}.json`);

  let state = { fingerprints: {}, recent: [] };
  try {
    state = JSON.parse(fs.readFileSync(stateFile, "utf8"));
    if (!state.fingerprints) state.fingerprints = {};
    if (!state.recent) state.recent = [];
  } catch {}

  const fp = fingerprint(toolName, toolInput, toolResponse);
  state.fingerprints[fp] = (state.fingerprints[fp] || 0) + 1;
  state.recent.push(fp);
  if (state.recent.length > 20) state.recent.shift();

  const count = state.fingerprints[fp];

  // Also detect ping-pong: A→B→A→B in the recent window.
  const r = state.recent;
  const pingPong =
    r.length >= 4 &&
    r[r.length - 1] === r[r.length - 3] &&
    r[r.length - 2] === r[r.length - 4] &&
    r[r.length - 1] !== r[r.length - 2];

  try {
    fs.writeFileSync(stateFile, JSON.stringify(state));
  } catch {}

  if (count < WARN_AT && !pingPong) process.exit(0);

  const escalate = count >= BLOCK_AT;
  const mode = escalate ? "BLOCK" : "WARN";
  const reason = pingPong
    ? `Ping-pong loop detected: alternating two tool fingerprints ${r.slice(-4).join(" → ")}.`
    : `Same tool fingerprint ${fp} seen ${count} times this session — likely Fixation (Zhou et al. 2026, CB6).`;

  const guidance = [
    `🔁 deep-fix-mode auto-trigger — ${mode}`,
    "",
    reason,
    "",
    "Per Anthropic Fellows 2026 (https://alignment.anthropic.com/2026/hot-mess-of-ai/): the longer models spend on a task, the more incoherent their errors become. More retries ≠ better.",
    "",
    "Consider invoking `deep-fix-mode` to run the six-step procedure (stop → failed-attempts table → pattern match → layer naming → external research → finished diagnosis). If you believe this is a false positive (e.g. content-identical mutations are intentional, like a script that emits a fixed string), state that explicitly and proceed — the skill's step-2 table check accepts the exit reason. The escalation to BLOCK still applies at 5+ identical fingerprints because at that count the cost of a false-positive pause is lower than the cost of un-checked Fixation.",
  ].join("\n");

  if (escalate) {
    // PostToolUse cannot prevent the call that already ran, but can block subsequent flow.
    process.stdout.write(
      JSON.stringify({
        decision: "block",
        reason: guidance,
        hookSpecificOutput: { hookEventName: "PostToolUse", additionalContext: guidance },
      })
    );
  } else {
    process.stdout.write(
      JSON.stringify({
        hookSpecificOutput: { hookEventName: "PostToolUse", additionalContext: guidance },
      })
    );
  }
  process.exit(0);
});
