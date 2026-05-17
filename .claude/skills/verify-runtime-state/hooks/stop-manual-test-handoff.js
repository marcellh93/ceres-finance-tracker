#!/usr/bin/env node
// Stop hook — blocks the Stop event when the assistant's last message hands
// the user a manual-test checklist without a prerequisite-audit block.
//
// Why this hook exists:
//   2026-05-18: I drafted a 17-step manual TOTP-flow test list whose Step 1
//   required a TOTP-enabled user, but the SPA has no TOTP enrolment page and
//   the seeded user doesn't have it enabled. The test list was uncrawlable.
//   The user asked me to design+wire the route first or skip the steps.
//
// What this hook enforces:
//   When the assistant message contains manual-test-handoff fingerprints,
//   require either:
//     (a) An explicit "Prerequisites" / "Before you start" / "Required UI"
//         section near the top of the checklist that audits each entry-point
//         step, OR
//     (b) An explicit blocked-step marker ("⚠ blocked — UI not built yet",
//         "skip if X doesn't exist", "prerequisite not in scope this session")
//         on every step whose entry point is missing.
//
// Architecture mirrors stop-chat-deferral-detect.js exactly.

const fs = require("fs");
const path = require("path");

// TIGHT regex list — manual-test-handoff fingerprints. Phrases I use when
// dumping a test list on the user.
const PHRASES = [
  /\b(manual|browser) (tests?|checklist|steps?) (you (have to|need to|should) (do|run|perform|verify)|to (do|run|perform|verify))\b/i,
  /\bwhat to test (on your end|manually|in the browser)\b/i,
  /\b(things|steps) to (manually )?(verify|test|check) in the browser\b/i,
  /\bhere'?s your (manual|browser) (test|verification) (list|checklist)\b/i,
  /\bend[- ]of[- ]batch (manual|browser) (verification|checklist|handoff)\b/i,
  /\bUX\/UI verification checklist\b/i,
  // Numbered-list fingerprint that follows a "manual test" heading:
  // "1. ... 2. ... 3. ..." with at least 5 numbered items and the word
  // "test" or "verify" or "click" in the first 200 chars after the
  // first item.
  // (Conservative: require the explicit phrase AND a 5+-item list.)
];

// Guard A — prerequisite-audit block is present at the top of the message.
const PREREQ_AUDIT_MARKERS = [
  /\bprerequisites?\b[\s\S]{0,40}(?::|\n)/i,
  /\bbefore you (start|begin)\b[\s\S]{0,40}(?::|\n)/i,
  /\brequired (entry points?|UI|wiring|routes?)\b[\s\S]{0,40}(?::|\n)/i,
  /\bsetup needed\b[\s\S]{0,40}(?::|\n)/i,
  /\bthis assumes\b[\s\S]{0,120}\b(exists?|is built|is mounted|is reachable)\b/i,
  // Explicit "I audited the prerequisites" / "verified each entry point exists"
  /\bI (audited|verified|confirmed|checked) (each|every|the) (prerequisite|entry point|step's? prerequisite|upstream UI)\b/i,
];

// Guard B — explicit blocked-step / missing-prerequisite marker on every step.
// We treat presence of ANY of these as "the assistant is aware of missing UI"
// since they trigger a manual review by the user anyway.
const BLOCKED_STEP_MARKERS = [
  /\b(blocked|skip|cannot test) (until|because|since)\b[\s\S]{0,80}\b(not (yet )?built|doesn'?t exist|not (yet )?wired|missing UI|missing route|missing page)\b/i,
  /\bprerequisite (— |- |: |not |missing)\b/i,
  /\b⚠.{0,80}\b(not (yet )?built|doesn'?t exist|missing|not (yet )?wired)\b/i,
  /\bno SPA (page|route|UI|flow) (for|to) (reach|enable|trigger)\b/i,
];

// Guard C — user authorized the handoff anyway.
const USER_AUTH = [
  /\bjust (give|hand|send) me the (list|checklist)\b/i,
  /\bI'?ll (figure out|sort) the prerequisites?\b/i,
  /\bskip the prerequisite (check|audit)\b/i,
  /\btrust me on the prerequisites?\b/i,
];

const SESSION_BYPASS = process.env.CERES_SKIP_RUNTIME_STATE_HOOK === "1";

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
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

  // Additional fingerprint: require a real numbered checklist (>=5 numbered items)
  // before we count this as a manual-test handoff. Without this we'd flag every
  // mention of the phrase "manual test" in passing.
  const numberedItems = (lastAssistantText.match(/^\s*\d+\.\s+/gm) || []).length;
  if (numberedItems < 5) {
    // Phrase matched but list isn't long enough to be a real handoff — let it pass.
    process.exit(0);
  }

  const prereqAuditPresent = PREREQ_AUDIT_MARKERS.some((re) => re.test(lastAssistantText));
  const blockedStepMarker = BLOCKED_STEP_MARKERS.some((re) => re.test(lastAssistantText));
  const userAuthorized = USER_AUTH.some((re) => re.test(lastUserText));

  const guardsPassed = prereqAuditPresent || blockedStepMarker || userAuthorized || SESSION_BYPASS;

  const outcome = SESSION_BYPASS ? "bypassed" : guardsPassed ? "passed" : "blocked";
  try {
    const stateDir = path.join(process.env.CLAUDE_PROJECT_DIR || process.cwd(), ".claude/state/runtime-state-verify");
    fs.mkdirSync(stateDir, { recursive: true });
    const logEntry = {
      ts: new Date().toISOString(),
      sessionId: input.session_id || "",
      hook: "manual-test-handoff",
      outcome,
      matched,
      numberedItems,
      guards: {
        prereqAuditPresent,
        blockedStepMarker,
        userAuthorized,
        passed: guardsPassed,
      },
      assistantSnippet: lastAssistantText.slice(0, 320),
      userSnippet: lastUserText.slice(0, 240),
    };
    fs.appendFileSync(path.join(stateDir, "log.jsonl"), JSON.stringify(logEntry) + "\n");
  } catch {
    /* best-effort log */
  }

  if (guardsPassed) process.exit(0);

  const reason = [
    "🛑 Manual-test-handoff detected without prerequisite-audit block.",
    "",
    `Matched fingerprints: ${matched.join(", ")} (numbered items: ${numberedItems})`,
    "",
    "Three guards were checked AND ALL FAILED:",
    `  • Prerequisite-audit guard: ${prereqAuditPresent ? "PASS" : "fail"} (your message has no \"Prerequisites\" / \"Before you start\" / \"Required UI\" section or \"I audited the prerequisites\" marker)`,
    `  • Blocked-step guard: ${blockedStepMarker ? "PASS" : "fail"} (your message has no \"blocked — not yet built\" / \"missing UI\" / \"prerequisite missing\" marker on the affected steps)`,
    `  • User-authorization guard: ${userAuthorized ? "PASS" : "fail"} (user did not authorize the handoff without prerequisites)`,
    "",
    "The `verify-runtime-state` skill Rule B applies to ANY manual-test handoff.",
    "Before drafting a checklist, audit the prerequisite UI graph: for each step,",
    "name the entry point (route, button, email link) the user reaches it from,",
    "and confirm the entry point exists in COMMITTED code (not in spec, not in",
    "planning — in code). If any prerequisite is missing, either build it first",
    "or mark the affected steps as blocked.",
    "",
    "Recovery options:",
    "  • Add a \"Prerequisites\" section at the top of the checklist that audits each",
    "    entry point's existence, OR",
    "  • Mark the steps blocked on missing UI with an explicit \"⚠ blocked — UI not yet",
    "    built\" marker, OR",
    "  • Build the missing prerequisite UI first and ship a real test list, OR",
    "  • If user authorized the handoff without prerequisites, set CERES_SKIP_RUNTIME_STATE_HOOK=1.",
    "",
    "See .claude/state/runtime-state-verify/log.jsonl for the audit trail.",
  ].join("\n");

  process.stderr.write(reason);
  process.exit(2);
});
