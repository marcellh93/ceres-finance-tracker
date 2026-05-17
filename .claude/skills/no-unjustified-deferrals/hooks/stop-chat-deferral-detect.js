#!/usr/bin/env node
// Stop hook — scans the assistant's MOST RECENT message for conversational
// deferral language and BLOCKS the Stop event when the deferral is unauthorized
// and not paired with fix-context.
//
// Why this hook exists:
//   The existing pre-write-deferral.js hook only fires on docs/* writes. The
//   actual failure mode the user caught (2026-05-17, post-9.1.5.f) was a
//   chat-only deferral ("Filed for later" / "queue it" / "separate scope") —
//   none of which were docs writes, so the pre-write hook never had a chance.
//   This hook fills that gap.
//
// Architecture (after 2026-05-17 false-positive minimization audit):
//   - On Stop, read both:
//       (a) the most recent USER message (to detect explicit user authorization)
//       (b) the most recent ASSISTANT message (the content under scrutiny)
//   - Scan (b) for a TIGHT set of deferral phrases — narrow enough that
//     matching them in non-defer context is rare.
//   - Apply THREE guards before blocking:
//       Guard 1: USER-AUTH — user's last message contains explicit defer verb
//                ("defer this" / "skip this" / "queue this" / "later" /
//                 "not now" / "leave that") → user authorized, exit 0.
//       Guard 2: FIX-CONTEXT — assistant message ALSO contains active-work
//                markers (commit SHA, file path, ``` code block, "running
//                tests", "committing") → assistant is working on the issue,
//                not punting, exit 0.
//       Guard 3: DECISION-QUESTION — assistant message ends with a "?" in
//                the last 200 chars → asking the user to decide, not
//                deferring unilaterally, exit 0.
//   - Only if a phrase matches AND none of the three guards apply do we
//     block. That should mean: I made an unauthorized scope call, did
//     no inline work, and didn't ask permission. Exactly the bypass.
//
// Mode flag:
//   The env var CERES_DEFERRAL_HOOK_MODE controls behavior:
//     "block" (default) — exit 2 + stderr to block the Stop when guards fail.
//                         This is the safe default; the bypass below covers
//                         false positives.
//     "log"             — write match info to .claude/state/deferral-detect/
//                         log.jsonl but ALWAYS exit 0. Use only for studying
//                         match patterns; does NOT prevent bypasses.
//
// Per-session bypass for confirmed false positives:
//   - CERES_SKIP_DEFERRAL_CHAT_HOOK=1 → exit 0 unconditionally (bypass).
//
// The hook always writes to the log regardless of mode — log mode just means
// "log only, don't block." Block mode logs AND blocks when guards fail.

const fs = require("fs");
const path = require("path");

// TIGHT regex list — only phrases that are almost-always deferral intent
// in chat output. Pairs each potentially-ambiguous verb with a temporal
// anchor ("later" / "future" / "separate stage") so neutral usage doesn't
// flip on the verb alone.
const PHRASES = [
  // "filed for/under later/separate" — original bypass pattern
  /\bfiled (for|under) (later|a (new|separate|future) (stage|phase|sprint))\b/i,
  // "queue it for later" / "queue it to a future stage"
  /\bqueue (it|this|that) (for|to) (later|a future|the next session)\b/i,
  // "separate stage's worth" / "separate stage of work" — exact bypass phrasing
  /\bseparate stage'?s? worth of work\b/i,
  /\bseparate (stage|sprint) of work\b/i,
  // "real but not a regression, [defer verb]" — the scoping dodge
  /\bnot a regression\b[\s\S]{0,80}\b(separate|filed|queue|punt|skip|move on)\b/i,
  // "out of scope of this stage" + later (the exact reframing dodge)
  /\bout of scope (of|for) (this|the current) (task|stage|sprint)\b[\s\S]{0,80}\b(later|future|next stage|separate)\b/i,
  // "Phase X polish" — generic deferral bucket from cited regression
  /\bPhase \d+ polish (\+|and) bugfix follow[- ]?up\b/i,
  // "kicked / punted / deferred to later" — but allow "to Stage X"
  /\b(kicked|punted|deferred) to (later|a (new|separate|future|next) (stage|phase|sprint))\b/i,
];

// User-authorization markers — if user's last message contains any of these,
// they explicitly approved deferral and the hook exits 0.
const USER_AUTH = [
  /\b(defer|skip|queue|leave|postpone) (this|that|it)\b/i,
  /\b(do|fix|address) (this|that|it) later\b/i,
  /\bnot (now|this session)\b/i,
  /\bsave (this|that|it) for (later|the next|another)\b/i,
  /\bmove on\b/i,                          // "let's move on" from user = explicit
  /\bcall it (for the session|here|done)\b/i,
  /\bnext (task|stage|item|thing)\b/i,
];

// Fix-context markers — if assistant message contains any of these, I'm
// clearly doing work, not punting.
const FIX_CONTEXT = [
  /\bcommitt(ed|ing)?\b/i,
  /\bcommit SHA\b/i,
  /\b[0-9a-f]{7,40}\b.*\b(landed|committed)/i,  // commit hash + landed/committed
  /\b(running|ran) (the )?(pnpm|dotnet|test|build|lint)/i,
  /```[\s\S]{20,}```/,                          // any code block with 20+ chars
  /\b(passed|failed) \(\d+\)/i,                 // "915 passed (915)" — running tests
  /\bediting\b.*\b(file|path|component)/i,
  /\.(tsx?|cs|json|md):\d+/,                    // file:line reference
  /\bfix(ing|ed) it now\b/i,
  /\bdoing (it|this|that) (now|inline)\b/i,
  /\bexecuting (the|this) fix\b/i,
  /\bgoing inline\b/i,
  // Properly-formed deferral entry (Stage + checkbox + reason in same message)
  /Stage \d+(\.\d+)?\b[\s\S]{0,400}- \[ \][\s\S]{0,400}(tooling gap|already scheduled|scheduled in)/i,
  /Stage \d+(\.\d+)?\b[\s\S]{0,400}(tooling gap|already scheduled|scheduled in)[\s\S]{0,400}- \[ \]/i,
];

// Decision-question marker — assistant ends with a question in last 200 chars.
function endsWithQuestion(text) {
  const tail = text.slice(-200);
  // Look for ? near the end, but not inside a code block in the tail.
  return /\?\s*$/.test(tail.trim()) || /\?(\s*\*\*?)?\s*$/.test(tail.trim());
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  if (process.env.CERES_SKIP_DEFERRAL_CHAT_HOOK === "1") process.exit(0);
  const mode = process.env.CERES_DEFERRAL_HOOK_MODE || "block";

  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  if (input.stop_hook_active) process.exit(0);

  const transcriptPath = input.transcript_path || "";
  if (!transcriptPath || !fs.existsSync(transcriptPath)) process.exit(0);

  let lines;
  try {
    lines = fs.readFileSync(transcriptPath, "utf8").trim().split("\n");
  } catch {
    process.exit(0);
  }

  // Walk backwards: find most recent assistant message AND most recent user message.
  let lastAssistantText = "";
  let lastUserText = "";
  for (let i = lines.length - 1; i >= 0; i--) {
    let entry;
    try {
      entry = JSON.parse(lines[i]);
    } catch {
      continue;
    }
    if (entry.type === "assistant" && !lastAssistantText) {
      const msg = entry.message;
      if (msg && Array.isArray(msg.content)) {
        const text = msg.content
          .filter((c) => c && c.type === "text" && typeof c.text === "string")
          .map((c) => c.text)
          .join("\n");
        if (text) lastAssistantText = text;
      }
    }
    if (entry.type === "user" && !lastUserText) {
      const msg = entry.message;
      if (msg && typeof msg.content === "string") {
        lastUserText = msg.content;
      } else if (msg && Array.isArray(msg.content)) {
        const text = msg.content
          .filter((c) => c && (c.type === "text" || typeof c === "string"))
          .map((c) => (typeof c === "string" ? c : c.text))
          .filter(Boolean)
          .join("\n");
        if (text) lastUserText = text;
      }
    }
    if (lastAssistantText && lastUserText) break;
  }

  if (!lastAssistantText) process.exit(0);

  const matched = PHRASES.filter((re) => re.test(lastAssistantText)).map((re) => re.toString());
  if (matched.length === 0) process.exit(0);

  // Apply the three guards.
  const userAuthorized = USER_AUTH.some((re) => re.test(lastUserText));
  const inFixContext = FIX_CONTEXT.some((re) => re.test(lastAssistantText));
  const isDecisionQuestion = endsWithQuestion(lastAssistantText);

  const guardsPassed = userAuthorized || inFixContext || isDecisionQuestion;

  // Log every match (matched phrases + guard state) for calibration review.
  try {
    const stateDir = path.join(process.env.CLAUDE_PROJECT_DIR || process.cwd(), ".claude/state/deferral-detect");
    fs.mkdirSync(stateDir, { recursive: true });
    const logEntry = {
      ts: new Date().toISOString(),
      sessionId: input.session_id || "",
      mode,
      matched,
      guards: {
        userAuthorized,
        inFixContext,
        isDecisionQuestion,
        passed: guardsPassed,
      },
      // Snippet for review — first 240 chars of the assistant message
      assistantSnippet: lastAssistantText.slice(0, 240),
      userSnippet: lastUserText.slice(0, 240),
    };
    fs.appendFileSync(path.join(stateDir, "log.jsonl"), JSON.stringify(logEntry) + "\n");
  } catch {
    // logging is best-effort
  }

  // If guards passed OR we're in log mode, exit 0.
  if (guardsPassed || mode !== "block") process.exit(0);

  // BLOCK mode + guards failed: emit reason on stderr, exit 2.
  const reason = [
    "🛑 Unauthorized conversational deferral detected.",
    "",
    `Matched phrases: ${matched.join(", ")}`,
    "",
    "Three guards were checked AND ALL FAILED:",
    `  • User-authorization guard: ${userAuthorized ? "PASS" : "fail"} (user's last message had no explicit defer verb)`,
    `  • Fix-context guard: ${inFixContext ? "PASS" : "fail"} (your message had no commit / file path / code block / 'fixing now' marker)`,
    `  • Decision-question guard: ${isDecisionQuestion ? "PASS" : "fail"} (your message did not end with a question — you didn't ask user to decide, you decided unilaterally)`,
    "",
    "The `no-unjustified-deferrals` skill applies to ANY proposal to defer a discovered bug — including chat-only deferrals. The two valid reasons:",
    "  1. Tooling gap — a specific tool/dep/infra/feature is unavailable, with evidence.",
    "  2. Already-scheduled — the active batch stage has a `[ ]` line covering this.",
    "",
    "If neither holds, the bug gets fixed NOW or queued into the ACTIVE batch stage's checklist (with `[ ]` line + tripwire added in the same commit). The phrases that matched are documented bypass patterns from the 2026-05-17 audit.",
    "",
    "Recovery options:",
    "  • Rewrite your last message to either fix the issue inline (then the fix-context guard passes), OR ask the user to decide (decision-question guard passes), OR include a properly-formed deferral entry with Stage X reference + `- [ ]` line + Reason 1/2 marker.",
    "  • If the user already authorized the deferral and the user-auth regex missed it, set CERES_SKIP_DEFERRAL_CHAT_HOOK=1 to bypass this hook for the session.",
    "",
    "See .claude/state/deferral-detect/log.jsonl for the full log of matches + guard outcomes.",
  ].join("\n");

  process.stderr.write(reason);
  process.exit(2);
});
