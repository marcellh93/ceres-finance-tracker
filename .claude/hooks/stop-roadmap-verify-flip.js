#!/usr/bin/env node
// Stop hook — blocks the Stop event when the user has just confirmed a manual
// verification but at least one roadmap verification line still says
// "Manual browser verification pending user run" (or equivalent pending markers).
//
// Why this hook exists:
//   The roadmap has lines like:
//     - [ ] 9.1.5.e — ... **Manual browser verification pending user run** ...
//   When the user replies "all works" / "confirmed" / "logged in fine", the
//   assistant is supposed to flip those [ ] to [x] in the next response.
//   This rule has failed in practice — multiple sub-stages of the 9.1.5 batch
//   (e, f, i) stayed [ ] for many turns after browser confirmation, until the
//   user explicitly called it out. Memory rules don't enforce this because
//   the assistant has to choose to apply them; a hook fires deterministically.
//
// Behavior:
//   1. Read the last assistant message and the last user message from the transcript.
//   2. If the assistant message DOES NOT contain a verification request marker
//      (e.g. "Manual browser verification", "please verify", "please re-test"),
//      exit 0 — there's nothing to flip.
//   3. If the user message DOES NOT contain a confirmation phrase, exit 0 —
//      no flip is owed yet.
//   4. If the user IS correcting / pushing back, exit 0 — confirmation conditional.
//   5. Else, scan the roadmap-phase-three.md file for any `- [ ]` line that
//      contains a pending-verification marker. If any such line exists, BLOCK
//      the Stop with stderr listing the unticked lines.
//
// Bypass:
//   - CERES_SKIP_ROADMAP_VERIFY_HOOK=1 in env (escape hatch).
//
// Logged outcomes go to .claude/state/roadmap-verify-flip/log.jsonl.

const fs = require("fs");
const path = require("path");

// Phrases in the assistant's recent message that say "I'm asking the user to
// verify something." If none of these is present, the hook has no precondition.
const ASSISTANT_VERIFICATION_REQUEST = [
  /manual browser verification/i,
  /please (re-?)?(test|verify|confirm)/i,
  /please re-?test/i,
  /verify in (the )?browser/i,
  /(once|after) you confirm/i,
  /\[ \] .{0,200}pending user run/i,            // pasted the unticked roadmap line
  /flip .{0,30}\[?x\]?/i,                        // mentioned flipping the box
  /tick (the )?(line|box|verification)/i,
];

// User confirmation phrases — must be a positive ack, not a question or correction.
const USER_CONFIRMATION = [
  /\ball (works|fixed|good|green|confirmed|verified|tested|done)\b/i,
  /\beverything (works|is fine|is good|looks good|checks out)\b/i,
  /\bworks (as expected|fine|now|correctly|well)\b/i,
  /\b(it|that) (works|is fixed|is good|logged in|signs in)\b/i,
  /\b(login|sign[- ]?in|logout|sign[- ]?out) (works|succeeded|ok)\b/i,
  /\b(re-?tested|just tested|tested it).{0,40}\b(works|good|green|ok|fine|confirmed)\b/i,
  /\b(good|ok|confirmed|verified|done|fixed) (to go|here|on (my|your) end|across the board)\b/i,
  /\ball (three|four|five|six) (work|works|are fine|confirmed|fixed)\b/i,
  /\bworked (as expected|fine|correctly)\b/i,
  /\blogged in (as expected|fine|correctly|successfully|without issue)\b/i,
];

// Anti-confirmation: explicit pushback / question / negative report.
// If present, treat the user message as NOT a confirmation and exit 0.
const ANTI_CONFIRMATION = [
  /\bdoesn'?t (work|load|render|sign)\b/i,
  /\b(still )?broken\b/i,
  /\b(not|isn'?t) (working|fixed|loading)\b/i,
  /\bgot (a |an )?(error|400|401|403|404|500|exception)\b/i,
  /\bfailed (to|on)\b/i,
  /\bwhy (is|are|does|doesn'?t)\b/i,                // diagnostic question
  /\?\s*$/,                                          // user's message ends with ?
];

// Roadmap location: configurable via env, defaults to phase-three.
const DEFAULT_ROADMAP = "docs/roadmap-phase-three.md";

// Patterns inside roadmap lines that mark "pending user-run verification."
const PENDING_VERIFY_LINE = /^- \[ \].{0,2000}(pending user run|pending browser confirm|pending manual verif)/im;

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  if (process.env.CERES_SKIP_ROADMAP_VERIFY_HOOK === "1") process.exit(0);

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

  // Find most recent assistant message AND most recent user message.
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

  if (!lastAssistantText || !lastUserText) process.exit(0);

  // Precondition: assistant must have asked for verification recently.
  const assistantAsked = ASSISTANT_VERIFICATION_REQUEST.some((re) => re.test(lastAssistantText));
  if (!assistantAsked) process.exit(0);

  // Anti-confirmation: user is pushing back / asking a question / reporting failure.
  const negative = ANTI_CONFIRMATION.some((re) => re.test(lastUserText));
  if (negative) process.exit(0);

  // Confirmation present?
  const confirmed = USER_CONFIRMATION.some((re) => re.test(lastUserText));
  if (!confirmed) process.exit(0);

  // Check the roadmap for unticked pending-verification lines.
  const projectDir = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  const roadmapPath = path.join(projectDir, process.env.CERES_ROADMAP_PATH || DEFAULT_ROADMAP);

  let roadmapContents = "";
  try {
    roadmapContents = fs.readFileSync(roadmapPath, "utf8");
  } catch {
    // Roadmap not readable / not at expected path; can't enforce, exit clean.
    process.exit(0);
  }

  const roadmapLines = roadmapContents.split("\n");
  const pendingLines = [];
  for (let i = 0; i < roadmapLines.length; i++) {
    const line = roadmapLines[i];
    if (PENDING_VERIFY_LINE.test(line)) {
      // Capture the line number (1-indexed) and a short identifier from the line.
      const idMatch = line.match(/-\s*\[\s\]\s+(\S[^—]{0,40})/);
      const id = idMatch ? idMatch[1].trim() : "(unknown)";
      pendingLines.push({ lineNumber: i + 1, id });
    }
  }

  // Always log the outcome.
  const outcome = pendingLines.length > 0 ? "blocked" : "passed";
  try {
    const stateDir = path.join(projectDir, ".claude/state/roadmap-verify-flip");
    fs.mkdirSync(stateDir, { recursive: true });
    const logEntry = {
      ts: new Date().toISOString(),
      sessionId: input.session_id || "",
      outcome,
      roadmapPath,
      pendingLines,
      assistantSnippet: lastAssistantText.slice(0, 240),
      userSnippet: lastUserText.slice(0, 240),
    };
    fs.appendFileSync(path.join(stateDir, "log.jsonl"), JSON.stringify(logEntry) + "\n");
  } catch {
    // logging is best-effort
  }

  if (pendingLines.length === 0) process.exit(0);

  // BLOCK with stderr listing unticked lines.
  const lines_summary = pendingLines
    .map((p) => `  • line ${p.lineNumber}: ${p.id}`)
    .join("\n");

  const reason = [
    "🛑 User confirmed a manual verification, but roadmap still has unticked pending-verification line(s).",
    "",
    `Roadmap: ${path.relative(projectDir, roadmapPath)}`,
    `Pending lines (${pendingLines.length}):`,
    lines_summary,
    "",
    "Per feedback_finished_stages_have_no_unchecked_items and feedback_no_flag_without_action: when the user confirms a verification you asked them to run, flip the corresponding `- [ ]` line to `- [x]` and replace the 'pending user run' text with the confirmed verification summary BEFORE the turn ends.",
    "",
    "Commonly missed by the assistant because the rule lives only in memory; this hook enforces it deterministically.",
    "",
    "Recovery:",
    "  • Edit the listed line(s): change `- [ ]` to `- [x]` and rewrite the 'pending' text.",
    "  • Commit the roadmap change (typically a small docs commit).",
    "  • Re-attempt the Stop.",
    "",
    "If the user's confirmation was for something OTHER than this verification line (false-positive), set CERES_SKIP_ROADMAP_VERIFY_HOOK=1 for the session to bypass.",
  ].join("\n");

  process.stderr.write(reason);
  process.exit(2);
});
