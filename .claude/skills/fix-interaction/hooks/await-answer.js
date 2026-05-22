#!/usr/bin/env node
// UserPromptSubmit hook — reads the user's reply when the state machine
// is awaiting an answer. Two states it handles:
//
//   awaiting === "fix-or-document"
//     fix / now / do it    → transition to "fixing"
//     document / doc it / log / queue → transition to "where"
//     topic-shift          → release to "none"
//     (none of the above)  → keep state; emit advisory reminder
//
//   awaiting === "where"
//     names a doc/stage    → transition to "documenting" (capture destination)
//     topic-shift          → release to "none"
//     (none of the above)  → keep state; emit advisory reminder
//
// Always idempotent (compare-and-set).
// Fail-open on any error.

const fs = require("fs");
const path = require("path");

const STATE_DIR_NAME = path.join("state", "fix-interaction");
const TTL_HOURS = 24;

// Engagement matchers — require multi-word phrases that are unambiguously
// "fix it now" / "document this" engagement. Bare "now" or "yes" or "go" no
// longer counts: those words occur constantly in normal conversation
// ("now I'll explain", "yes that's right but...", "go on") and would
// false-transition the state machine. Pattern C in the 2026-05-22 hook
// architecture audit.
const ENGAGE_FIX = /\b(fix (it|that|this|the bug)( now)?|do (it|that) now|go ahead (and fix|with the fix)|yes,? (fix|do it|please fix|let'?s fix)|proceed with the fix|right now,? fix)\b/i;
const ENGAGE_DOCUMENT = /\b(document (it|this|that|the bug)|doc it|doc that|log it|queue it|open a line|track it|file it|note it|add (a|the) (\[ \]|checkbox) line)\b/i;
const ENGAGE_WHERE = /\b(stage|roadmap|planning|spec|adr|docs?\/|under|in)\b/i;
const TOPIC_SHIFT = /\b(actually|let'?s (talk|move|do|switch)|move on|different question|forget that|nevermind|drop it|skip that|something else|new topic|change of subject)\b/i;

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
      `⚠ fix-interaction (await-answer): state file ${p} corrupt; treating as fresh.\n`
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

// Compare-and-set state transition (idempotent).
function transition(currentState, expectedFrom, to, extras = {}) {
  if (currentState.awaiting !== expectedFrom) {
    return null; // re-fire / race condition; no-op
  }
  return {
    ...currentState,
    awaiting: to,
    started_at: new Date().toISOString(),
    previous_state: expectedFrom,
    ...extras,
  };
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
    const prompt = input.prompt || "";
    if (!prompt) process.exit(0);

    ensureDir();
    const state = readState(sessionId);

    // === TTL sweep ===
    if (ttlExpired(state)) {
      appendLog({
        ts: new Date().toISOString(),
        interaction_id: state.interaction_id || null,
        from: state.awaiting,
        to: "none",
        trigger: "await-answer",
        reason: "ttl-expired",
      });
      writeStateAtomic(sessionId, { awaiting: "none" });
      process.exit(0); // user prompt proceeds unblocked
    }

    if (state.awaiting === "none") process.exit(0);
    if (state.awaiting === "fixing" || state.awaiting === "documenting") {
      // The assistant is now expected to act; this hook doesn't gate.
      process.exit(0);
    }

    // === Topic-shift check ===
    const topicShift = TOPIC_SHIFT.test(prompt);
    const engageFix = ENGAGE_FIX.test(prompt);
    const engageDoc = ENGAGE_DOCUMENT.test(prompt);
    const engageWhere = ENGAGE_WHERE.test(prompt);
    const anyEngagement = engageFix || engageDoc || engageWhere;

    if (topicShift && !anyEngagement) {
      appendLog({
        ts: new Date().toISOString(),
        interaction_id: state.interaction_id || null,
        from: state.awaiting,
        to: "none",
        trigger: "await-answer",
        reason: "topic-shift",
        user_msg_snippet: prompt.slice(0, 200),
      });
      writeStateAtomic(sessionId, { awaiting: "none" });
      // Emit advisory so the assistant knows the gate released.
      const ctx = [
        "ℹ fix-interaction: gate released — your message shifted topic.",
        `Prior interaction ${state.interaction_id || "?"} was awaiting "${state.awaiting}".`,
        "If the original fix still matters, surface it again later.",
      ].join("\n");
      process.stdout.write(
        JSON.stringify({
          hookSpecificOutput: { hookEventName: "UserPromptSubmit", additionalContext: ctx },
        })
      );
      process.exit(0);
    }

    // === State-specific transition logic ===
    if (state.awaiting === "fix-or-document") {
      if (engageFix && !engageDoc) {
        const next = transition(state, "fix-or-document", "fixing", {
          user_choice: "fix",
        });
        if (!next) process.exit(0);
        writeStateAtomic(sessionId, next);
        appendLog({
          ts: next.started_at,
          interaction_id: next.interaction_id,
          from: "fix-or-document",
          to: "fixing",
          trigger: "await-answer",
          user_msg_snippet: prompt.slice(0, 200),
        });
        const ctx = [
          "✓ fix-interaction: user chose FIX.",
          "Your next response MUST include the Edit/Write/MultiEdit tool call that makes the fix.",
          "If you cannot do it (sandbox denied, file outside repo, etc.), say so explicitly with the named blocker — do not emit 'I'll fix it' without acting.",
        ].join("\n");
        process.stdout.write(
          JSON.stringify({
            hookSpecificOutput: { hookEventName: "UserPromptSubmit", additionalContext: ctx },
          })
        );
        process.exit(0);
      }
      if (engageDoc && !engageFix) {
        const next = transition(state, "fix-or-document", "where", {
          user_choice: "document",
        });
        if (!next) process.exit(0);
        writeStateAtomic(sessionId, next);
        appendLog({
          ts: next.started_at,
          interaction_id: next.interaction_id,
          from: "fix-or-document",
          to: "where",
          trigger: "await-answer",
          user_msg_snippet: prompt.slice(0, 200),
        });
        const ctx = [
          "✓ fix-interaction: user chose DOCUMENT.",
          "Your next response MUST ask the user: \"Where? Which doc + which stage?\"",
          "Then wait for the destination before writing.",
        ].join("\n");
        process.stdout.write(
          JSON.stringify({
            hookSpecificOutput: { hookEventName: "UserPromptSubmit", additionalContext: ctx },
          })
        );
        process.exit(0);
      }
      // Ambiguous: neither, both, or unclear engagement.
      const ctx = [
        "⏳ fix-interaction: still awaiting answer.",
        `State: ${state.awaiting} (interaction ${state.interaction_id || "?"})`,
        "User's last reply did not clearly choose FIX or DOCUMENT.",
        "Ask the user one more time, explicitly: \"Fix now, or document it?\"",
        "Or use /fix-interaction-reset if this thread has moved on.",
      ].join("\n");
      process.stdout.write(
        JSON.stringify({
          hookSpecificOutput: { hookEventName: "UserPromptSubmit", additionalContext: ctx },
        })
      );
      process.exit(0);
    }

    if (state.awaiting === "where") {
      // The user is naming the destination. Capture it; transition to documenting.
      if (engageWhere || engageDoc) {
        const next = transition(state, "where", "documenting", {
          destination_snippet: prompt.slice(0, 400),
        });
        if (!next) process.exit(0);
        writeStateAtomic(sessionId, next);
        appendLog({
          ts: next.started_at,
          interaction_id: next.interaction_id,
          from: "where",
          to: "documenting",
          trigger: "await-answer",
          user_msg_snippet: prompt.slice(0, 200),
        });
        const ctx = [
          "✓ fix-interaction: destination captured.",
          `User said: "${prompt.slice(0, 200)}"`,
          "Your next response MUST land a `[ ]` line in the named doc via an Edit/Write tool call.",
          "Include the bug summary, the receiving stage, and a tripwire (failing test / FIXME / arch assertion) per `no-unjustified-deferrals` template.",
        ].join("\n");
        process.stdout.write(
          JSON.stringify({
            hookSpecificOutput: { hookEventName: "UserPromptSubmit", additionalContext: ctx },
          })
        );
        process.exit(0);
      }
      // No engagement, no topic shift. Re-ask.
      const ctx = [
        "⏳ fix-interaction: still awaiting destination.",
        `State: where (interaction ${state.interaction_id || "?"})`,
        "Ask the user explicitly: \"Which doc and which stage should the `[ ]` line land in?\"",
      ].join("\n");
      process.stdout.write(
        JSON.stringify({
          hookSpecificOutput: { hookEventName: "UserPromptSubmit", additionalContext: ctx },
        })
      );
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
      `⚠ fix-interaction await hook errored; gate open this turn. See ${errorLogPath()}.\n`
    );
    process.exit(0);
  }
});
