#!/usr/bin/env node
// Stop hook — fires when the assistant's final message mentions a fix
// without making the corresponding Edit/Write in the same turn.
//
// Skip when:
//   - FIX-CONTEXT guard: an Edit/Write/MultiEdit tool call appears in this
//     turn's tool_uses (fix already happened).
//   - STATE-NOT-NONE guard: the state machine is mid-flow; another hook
//     will resolve it.
//   - CERES_SKIP_FIX_INTERACTION_HOOK=1 env var set.
//   - State file is corrupt/missing: fail-open, log to stderr, exit 0.
//
// On match: write state.awaiting = "fix-or-document", capture the user's
// verbatim prior message + the matched passage, block the Stop with a
// deny reason.

const fs = require("fs");
const path = require("path");

// === Fix-mention phrase list (mirrors SKILL.md §6) ===
//
// 2026-05-21 audit: tightened to reduce predicted misfires before the hook
// engages on real traffic. Two changes:
//   1. Dropped `/we need to add/i` outright — it fires on "we need to add
//      a CHANGELOG entry", "we need to add the migration", "we need to add
//      this to the spec" etc., none of which are bug-fix language. Replaced
//      with a narrower variant that requires a fix-adjacent noun.
//   2. Added a DISCUSSION_MARKERS / QUOTATION negative guard list (applied
//      below) so brainstorming-mode option proposals and quoted text don't
//      fire the hook. The phrases themselves stay loose because the hook
//      is most useful when the language sounds like a commitment.
const FIX_MENTION_PHRASES = [
  /\bthe fix is\b/i,
  /\bthe right fix\b/i,
  /\bI'?d fix this by\b/i,
  /\bI would fix this\b/i,
  /\bI'?ll fix this\b/i,
  /\beasy fix:?\b/i,
  /\bthat'?s a bug\b/i,
  /\bthe bug is\b/i,
  /\bthis is broken because\b/i,
  /\bwe should fix\b/i,
  /\bwe need to fix\b/i,
  /\bwe need to add (a fix|handling|guard|guard rail|check|validation|null check|defensive)\b/i,
  /\bone-line fix\b/i,
  /\bquick fix:?\b/i,
  /\bthe right move is\b/i,
  /\bthe (correct|proper) approach is\b/i,
];

// Negative guards — if any of these markers appear in the assistant message,
// the matched phrase is discussion or quotation, not a fix commitment.
const DISCUSSION_MARKERS = [
  /\bbrainstorm(ing)?\b/i,
  /\boption [A-C]\b/i,
  /\bA\/B\/C\b/i,
  /\btrade-?off(s)?\b/i,
  /\bthe alternatives? (are|include)\b/i,
  /\bif we chose\b/i,
  /\blet me think through\b/i,
  /\bone option\b/i,
  /\banother option\b/i,
  /\bweighing (the )?options\b/i,
];

function inQuotationContext(text, hitPhrase) {
  // True if the matched phrase appears inside a markdown blockquote or backticks.
  // We approximate by scanning lines: any line that starts with `> ` and
  // contains the phrase, or any inline-code/backtick run containing it.
  if (!hitPhrase) return false;
  const escaped = hitPhrase.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const blockquoteRe = new RegExp(`^>\\s.*${escaped}`, "im");
  if (blockquoteRe.test(text)) return true;
  const backtickRe = new RegExp("`[^`\\n]*" + escaped + "[^`\\n]*`", "i");
  if (backtickRe.test(text)) return true;
  return false;
}

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
    // Corrupt file — fail-open, treat as fresh state.
    process.stderr.write(
      `⚠ fix-interaction: state file ${p} corrupt; treating as fresh.\n`
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
  } catch {
    // Audit log failures are non-fatal.
  }
}

function ttlExpired(state) {
  if (state.awaiting === "none" || !state.started_at) return false;
  const started = Date.parse(state.started_at);
  if (Number.isNaN(started)) return false;
  const ageMs = Date.now() - started;
  return ageMs > TTL_HOURS * 60 * 60 * 1000;
}

function readTranscriptMessages(transcriptPath, limit = 20) {
  // Read the last N JSONL entries from the transcript file to find:
  //   - The assistant's final message (for phrase matching).
  //   - The user's prior message (for resume anchor).
  //   - Whether any Edit/Write/MultiEdit tool_use appears in the assistant's
  //     final response (FIX-CONTEXT guard).
  if (!transcriptPath || !fs.existsSync(transcriptPath)) return null;

  const raw = fs.readFileSync(transcriptPath, "utf-8");
  const lines = raw.split("\n").filter((l) => l.trim().length > 0);
  const tail = lines.slice(-limit);
  const entries = [];
  for (const line of tail) {
    try {
      entries.push(JSON.parse(line));
    } catch {
      // Skip malformed lines.
    }
  }
  return entries;
}

function lastAssistantText(entries) {
  // Walk back from end, find the latest assistant message.
  for (let i = entries.length - 1; i >= 0; i--) {
    const e = entries[i];
    if (e.type !== "assistant") continue;
    const msg = e.message;
    if (!msg || !Array.isArray(msg.content)) continue;
    const text = msg.content
      .filter((c) => c.type === "text")
      .map((c) => c.text)
      .join("\n");
    return { text, content: msg.content, index: i };
  }
  return null;
}

function lastUserText(entries) {
  for (let i = entries.length - 1; i >= 0; i--) {
    const e = entries[i];
    if (e.type !== "user") continue;
    const msg = e.message;
    if (!msg) continue;
    if (typeof msg.content === "string") return msg.content;
    if (Array.isArray(msg.content)) {
      const text = msg.content
        .filter((c) => c.type === "text" || typeof c === "string")
        .map((c) => (typeof c === "string" ? c : c.text))
        .join("\n");
      return text;
    }
  }
  return "";
}

function assistantHasEditTool(content) {
  // FIX-CONTEXT guard: did the assistant make any Edit/Write/MultiEdit tool
  // call in this response? If yes, the fix happened; skip the hook.
  if (!Array.isArray(content)) return false;
  for (const c of content) {
    if (c.type !== "tool_use") continue;
    const name = c.name || "";
    if (/^(Edit|Write|MultiEdit|NotebookEdit)$/.test(name)) return true;
  }
  return false;
}

function matchedPhrases(text) {
  const hits = [];
  for (const re of FIX_MENTION_PHRASES) {
    if (re.test(text)) hits.push(re.toString());
  }
  return hits;
}

function newInteractionId() {
  const d = new Date();
  const stamp = d.toISOString().slice(0, 10);
  const rand = Math.random().toString(16).slice(2, 6);
  return `fix-${stamp}-${rand}`;
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

    // === TTL sweep ===
    const currentState = readState(sessionId);
    if (ttlExpired(currentState)) {
      appendLog({
        ts: new Date().toISOString(),
        interaction_id: currentState.interaction_id || null,
        from: currentState.awaiting,
        to: "none",
        trigger: "detect-fix-mention",
        reason: "ttl-expired",
      });
      writeStateAtomic(sessionId, { awaiting: "none" });
    }

    // Re-read after possible TTL release.
    const state = readState(sessionId);

    // === STATE-NOT-NONE guard ===
    if (state.awaiting !== "none") {
      // Another hook owns this state; don't double-fire.
      process.exit(0);
    }

    // === Read transcript ===
    const entries = readTranscriptMessages(input.transcript_path, 30);
    if (!entries || entries.length === 0) process.exit(0);

    const lastAssistant = lastAssistantText(entries);
    if (!lastAssistant) process.exit(0);

    // === FIX-CONTEXT guard ===
    if (assistantHasEditTool(lastAssistant.content)) {
      process.exit(0);
    }

    // === Phrase match ===
    const hits = matchedPhrases(lastAssistant.text);
    if (hits.length === 0) process.exit(0);

    // === Discussion-mode guard (2026-05-21) ===
    // If the message is brainstorming options or weighing trade-offs, the
    // matched phrase is exploration, not commitment.
    if (DISCUSSION_MARKERS.some((re) => re.test(lastAssistant.text))) {
      process.exit(0);
    }

    // === Quotation guard (2026-05-21) ===
    // If every matched phrase is inside a blockquote or backticks, the
    // phrase is being discussed, not asserted. Find one phrase that is NOT
    // quoted to keep the hook engaged; if all hits are quoted, exit 0.
    const liveHits = hits.filter((hitStr) => {
      // hitStr looks like "/the fix is/i" — extract the inner pattern.
      const m = /^\/(.*)\/[a-z]*$/.exec(hitStr);
      if (!m) return true;
      const innerPattern = m[1];
      // Find the literal text from the assistant message that matched, by
      // re-running the regex.
      const re = new RegExp(innerPattern, "i");
      const match = re.exec(lastAssistant.text);
      if (!match) return true;
      return !inQuotationContext(lastAssistant.text, match[0]);
    });
    if (liveHits.length === 0) process.exit(0);

    // === Match — write state, block Stop ===
    const priorUserMessage = lastUserText(entries.slice(0, lastAssistant.index));
    const interactionId = newInteractionId();
    const newState = {
      awaiting: "fix-or-document",
      started_at: new Date().toISOString(),
      interaction_id: interactionId,
      matched_phrases: hits,
      fix_mention_snippet: lastAssistant.text.slice(0, 600),
      prior_user_message: priorUserMessage,
    };
    writeStateAtomic(sessionId, newState);

    appendLog({
      ts: newState.started_at,
      interaction_id: interactionId,
      from: "none",
      to: "fix-or-document",
      trigger: "detect-fix-mention",
      matched_phrases: hits,
    });

    const reason = [
      "🛑 fix-interaction: you mentioned a fix without making the Edit/Write in this turn.",
      "",
      `Matched phrase(s): ${hits.join(", ")}`,
      "",
      "Before ending the turn, ask the user explicitly:",
      "  \"Fix this now, or document it?\"",
      "",
      "If the user says fix → make the Edit in your next response.",
      "If the user says document → ask 'Where?' and land a `[ ]` line in the named doc.",
      "After the chosen path completes, the resume mechanic will inject the prior user message back into your context so you can continue the original thread.",
      "",
      "Recovery if this is a false positive:",
      "  • Set CERES_SKIP_FIX_INTERACTION_HOOK=1 for the session.",
      "  • Run /fix-interaction-reset to clear the state.",
      "  • Or say in your next response: \"this was a false-positive match; here is the actual fix\" and include the Edit/Write in the same response — the FIX-CONTEXT guard will pass.",
      "",
      `See ${logPath()} for the audit trail.`,
    ].join("\n");

    process.stdout.write(
      JSON.stringify({
        decision: "block",
        reason,
      })
    );
    process.exit(0);
  } catch (err) {
    // === FAIL-OPEN ===
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
    } catch {
      // Logging the error failed; nothing more to do.
    }
    process.stderr.write(
      `⚠ fix-interaction detect hook errored; gate open this turn. See ${errorLogPath()}.\n`
    );
    process.exit(0);
  }
});
