#!/usr/bin/env node
// Stop hook — blocks the Stop event when the assistant emits a strong
// confidence claim ("found it", "root cause", "smoking gun", etc.) without
// authoritative research evidence in the same turn.
//
// Why this hook exists:
//   2026-05-23 Remember-Me debug session. Over six turns I emitted "Found it"
//   four times, each time naming a different file as the bug. None of the
//   four were correct. Only when the user demanded I "do my research properly"
//   did I dispatch a WebFetch/Agent investigation that read the actual
//   ASP.NET source — which immediately identified the real bug. Confidence
//   claims without research are anti-evidence; they anchor me on the wrong
//   diagnosis and waste the user's time.
//
// Per docs/hook-architecture-audit-2026-05-22.md, this hook applies the
// patterns from yesterday's audit:
//   - Discussion-frame strip (skip code blocks, quoted phrases, hook-talk).
//   - Meta-context short-circuit (don't fire on assistant explaining a
//     prior hook fire — recursion protection).
//   - Window-scan for evidence (not just current turn — covers the legit
//     case of "I researched two turns ago, now I'm reporting findings").
//
// Evidence bar (user-selected: STRICT — same-turn):
//   At least ONE of:
//     1. WebFetch / WebSearch tool_use in the same turn.
//     2. Agent / Task tool_use with description matching /research|audit|
//        investigate|verify/i in the same turn.
//   File Reads, Bash, Grep do NOT count — those are local-state inspection,
//   not authoritative external research. The whole point of this hook is
//   to require evidence from outside the model's own training corpus.
//
// Bypass: CERES_SKIP_CLAIM_WITHOUT_RESEARCH_HOOK=1 in env.

const fs = require("fs");
const path = require("path");
const { stripDiscussionFrames, isMetaContext } = require(
  path.join(
    process.env.CLAUDE_PROJECT_DIR || process.cwd(),
    ".claude/hooks/lib/discussion-frame-strip.js",
  ),
);

// Strong-confidence claim phrases. Each one asserts certainty about a
// diagnosis — exactly the kind of assertion that should be backed by
// research, not by code inspection alone.
const CONFIDENCE_PHRASES = [
  /\b(i )?found it\b/i,
  /\bfound the (bug|root cause|issue|problem|smoking gun)\b/i,
  /\b(the |i found the )?root cause\b/i,
  /\bsmoking gun\b/i,
  /\b(now |so )?(i know|it'?s clear|it'?s obvious) (exactly )?what(\'s| is) (wrong|happening|going on|the bug)\b/i,
  /\bthat'?s the bug\b/i,
  /\bthe (actual |real )?bug is\b/i,
  /\bthat'?s (it|exactly it|the one)\b/i,
  /\bthe culprit (is|was)\b/i,
  /\bdiagnosed (it|the (bug|issue))\b/i,
  /\b(this|that) (is|explains) (exactly )?(why|what)\b[\s\S]{0,80}\b(bug|breaking|failing|broken|fails?)\b/i,
];

// Evidence markers — tool_use blocks serialized into the transcript as JSON.
// We check the assistant message's content array for tool_use blocks of
// the right type. The transcript serializes each tool_use as JSON we can
// pattern-match against.
const WEB_TOOLS = ["WebFetch", "WebSearch"];
const AGENT_TOOLS = ["Agent", "Task"];
const AGENT_RESEARCH_DESC = /\b(research|audit|investigate|verify|check)\b/i;

// 2026-05-24 false-positive guard — commit-reference / past-tense / already-
// validated context. The hook fired twice on phrases like "that's the one"
// and "the bug is" being used to REFER to an already-committed and user-
// validated fix, not to assert a fresh diagnosis. Match common shapes:
//   - 7-40 char hex SHA near the matched phrase
//   - "validated", "verified", "shipped", "landed", "committed" with the bug
//   - Past tense "was" / "kept" / "had been" near the bug noun
//   - "the validated fix" / "the fix that addresses" idiom
// Each indicates the assistant is summarising / referring rather than
// freshly diagnosing. Sibling to isMetaContext (which catches hook-talk).
const PAST_TENSE_OR_REFERENCE_MARKERS = [
  /\b[0-9a-f]{7,40}\b/,                                   // commit SHA
  /\b(committed|landed|shipped|validated|verified|merged|already (committed|landed|shipped|fixed|addressed)) (as |in |the |that |which )/i,
  /\b(the |that |which ) ?(validated|shipped|landed|committed|merged) (fix|commit|change|patch)\b/i,
  /\bthe bug (was|that|that kept|that caused|that broke|whose root)\b/i,
  /\b(the |this )(actual |real )?(fix|change|patch|commit) (that |which )(addressed|fixed|landed|shipped|closed)\b/i,
  /\b(addressed|closed|resolved) (by|in|via) (commit |PR |pr )?[`#]?[0-9a-f]{6,}/i,
  /\bcommit (`|')?[0-9a-f]{6,}/i,
];

const SESSION_BYPASS = process.env.CERES_SKIP_CLAIM_WITHOUT_RESEARCH_HOOK === "1";

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  if (SESSION_BYPASS) process.exit(0);

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

  // Find the most recent assistant text + collect its tool_uses, plus the
  // tool_uses from the most recent assistant message (the same Stop event
  // covers them since they're part of the same turn).
  let lastAssistantText = "";
  const sameTurnToolUses = [];
  let foundAssistant = false;

  // Walk backward; collect all assistant entries until we hit the prior user.
  for (let i = lines.length - 1; i >= 0; i--) {
    let entry;
    try {
      entry = JSON.parse(lines[i]);
    } catch {
      continue;
    }
    if (entry.type === "user" && foundAssistant) break;
    if (entry.type !== "assistant") continue;
    foundAssistant = true;
    const msg = entry.message;
    if (!msg || !Array.isArray(msg.content)) continue;
    for (const c of msg.content) {
      if (c.type === "text" && typeof c.text === "string" && !lastAssistantText) {
        lastAssistantText = c.text;
      }
      if (c.type === "tool_use") {
        sameTurnToolUses.push(c);
      }
    }
  }

  if (!lastAssistantText) process.exit(0);

  // Short-circuit on meta-context — talking ABOUT a hook fire shouldn't
  // trigger another fire. Same shape as stop-runtime-state-claim.js.
  if (isMetaContext(lastAssistantText)) process.exit(0);

  // Short-circuit on past-tense / commit-reference context. When the message
  // is summarising an already-shipped fix (commit SHA present, "the validated
  // fix", "the bug that kept you logged out was..."), the confidence phrase
  // is referring to a settled diagnosis, not asserting a fresh one. Pattern
  // C from the 2026-05-24 false-positive log.
  if (PAST_TENSE_OR_REFERENCE_MARKERS.some((re) => re.test(lastAssistantText))) {
    process.exit(0);
  }

  // Strip discussion frames so quoted "found it" inside a code block or
  // blockquote doesn't fire.
  const scanText = stripDiscussionFrames(lastAssistantText);

  const matched = CONFIDENCE_PHRASES.filter((re) => re.test(scanText)).map((re) =>
    re.toString().slice(0, 80),
  );
  if (matched.length === 0) process.exit(0);

  // Check evidence: any WebFetch/WebSearch tool_use in the same turn, OR
  // any Agent/Task tool_use whose description matches the research pattern.
  const hasWebTool = sameTurnToolUses.some((t) => WEB_TOOLS.includes(t.name));
  const hasResearchAgent = sameTurnToolUses.some(
    (t) =>
      AGENT_TOOLS.includes(t.name) &&
      typeof t.input?.description === "string" &&
      AGENT_RESEARCH_DESC.test(t.input.description),
  );
  const hasResearch = hasWebTool || hasResearchAgent;

  const outcome = hasResearch ? "passed" : "blocked";

  try {
    const stateDir = path.join(
      process.env.CLAUDE_PROJECT_DIR || process.cwd(),
      ".claude/state/deep-fix-mode",
    );
    fs.mkdirSync(stateDir, { recursive: true });
    fs.appendFileSync(
      path.join(stateDir, "claim-without-research.log.jsonl"),
      JSON.stringify({
        ts: new Date().toISOString(),
        sessionId: input.session_id || "",
        hook: "claim-without-research",
        outcome,
        matched,
        hasWebTool,
        hasResearchAgent,
        toolUseNames: sameTurnToolUses.map((t) => t.name),
        assistantSnippet: lastAssistantText.slice(0, 360),
      }) + "\n",
    );
  } catch {
    /* best-effort */
  }

  if (hasResearch) process.exit(0);

  const reason = [
    "🛑 Confidence claim emitted without authoritative-research evidence in the same turn.",
    "",
    `Matched phrase(s): ${matched.join(", ")}`,
    "",
    "Per the deep-fix-mode discipline, claims like 'found it' / 'root cause' / 'smoking gun'",
    "must be backed by EXTERNAL evidence in the same turn. File Reads, Bash inspection, and",
    "Grep do not count — those are local-state checks; they cannot disambiguate which of",
    "several plausible interpretations of the local code is actually correct at runtime.",
    "",
    "Required: at least one of",
    "  • WebFetch / WebSearch tool_use targeting vendor / RFC / framework documentation",
    "  • Agent / Task tool_use whose description names research/audit/investigate/verify",
    "",
    "Why this hook exists: 2026-05-23 Remember-Me debug session. I emitted 'Found it' four",
    "times across six turns, each time naming a different file as the bug. None were correct.",
    "Only when the user demanded I 'do my research properly' did I dispatch a real research",
    "agent — which read the ASP.NET source and immediately identified the real bug. Confidence",
    "claims without research are anti-evidence; they anchor me on the wrong diagnosis and waste",
    "the user's time.",
    "",
    "Recovery options:",
    "  • Dispatch a research Agent or WebFetch BEFORE re-asserting the diagnosis. Then",
    "    re-state the conclusion in the same turn as the research result.",
    "  • Reframe the claim as a HYPOTHESIS ('one hypothesis is X — let me verify') rather",
    "    than a confident finding. Hypotheses don't trigger this hook.",
    "  • If you genuinely have prior-turn research that backs the claim, restate the source",
    "    URL or agent's summary in the same turn so the assertion is auditable.",
    "  • If the claim is clearly false-positive (you weren't asserting a diagnosis, you were",
    "    quoting / paraphrasing / explaining), reword to avoid the trigger phrases.",
    "  • Session bypass (use sparingly): CERES_SKIP_CLAIM_WITHOUT_RESEARCH_HOOK=1.",
    "",
    "See .claude/state/deep-fix-mode/claim-without-research.log.jsonl for the audit trail.",
  ].join("\n");

  process.stderr.write(reason);
  process.exit(2);
});
