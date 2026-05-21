#!/usr/bin/env node
// Stop hook — blocks the Stop event when the assistant's last message asserts
// a runtime-state value (database row, env var, file contents, process state)
// without co-located evidence from an actual query.
//
// Why this hook exists:
//   2026-05-18: I told the user their AspNetUsers.TwoFactorEnabled was `false`
//   based on the absence of a grep hit in SeedDevUser.cs. Source-code inspection
//   tells you what code does; it does NOT tell you what the runtime DB row says.
//   The seed file's silence about TwoFactorEnabled is not evidence — the row
//   could have been touched by migrations, manual SQL, prior tests, or anything.
//
// Architecture (mirrors stop-chat-deferral-detect.js):
//   - On Stop, read the most recent assistant message from the transcript.
//   - Scan for TIGHT runtime-state-claim patterns (DB row values, env runtime
//     values, file-existence-on-disk claims).
//   - Apply guards before blocking:
//       Guard 1: EVIDENCE-CO-LOCATED — the same message contains a psql / EF
//                query / file-read output that backs the claim. Markers:
//                * SQL output table separator: "----+----" / "(N rows)"
//                * EF/dotnet output: "Microsoft.EntityFrameworkCore"
//                * shell prompts + output blocks (```), command starting with
//                  psql/sqlite3/dotnet/cat/ls in the same code block
//       Guard 2: EXPLICIT-ASSUMPTION — the claim is hedged with "if/assuming/
//                please confirm/needs verification" + a command for the user
//                to run.
//       Guard 3: USER-AUTH — user authorized the assumption in their last
//                message ("just assume X", "trust me, it's Y", "yes that's
//                the value").
//   - Only block if a pattern matches AND none of the guards apply.

const fs = require("fs");
const path = require("path");

// TIGHT regex list — runtime-state assertion patterns. Each one is paired
// with an entity name (table.column, env var, file path) so neutral usage
// of the same words doesn't false-positive.
const PHRASES = [
  // "<Table>.<Column> = <value>" / "<Table>.<Column> is <value>" — bare DB-row claim
  /\b[A-Z][A-Za-z]+\.[A-Z][A-Za-z]+\s*(=|is|reads|has value|carries)\s*(true|false|null|\d+|"[^"]+"|'[^']+')/,
  // "the <noun> row has <Column> = <value>" / "the seed user has X enabled/disabled"
  /\bthe (seed|seeded|dev|test|first|only) (user|row|record)('s)?\s+(has|carries|shows|reads|is)\b[^.]{0,80}\b(enabled|disabled|true|false|null|set to|unset)\b/i,
  // "<Column> is set to <value>" (Identity-style flags)
  /\b(TwoFactorEnabled|EmailConfirmed|LockoutEnd|AccessFailedCount|IsActive|IsDeleted|ConsumedAt|MfaVerifiedAt|EmailConfirmationToken|SecurityStamp)\s+(is|=|reads|shows)\s+(true|false|null|0|set|unset|present|missing)/i,
  // Bare "your <Column> is <value>" / "the row's <Column> is <value>"
  /\b(your|the row's|that user's)\s+[A-Z][A-Za-z]+\s+(is|reads)\s+(true|false|null)/i,
  // "the database has no <noun>" / "no rows match" (existence-claim without query)
  /\b(the (database|table) has no|there are no|zero rows? (for|matching|in)|database is empty of)\b/i,
  // File-state claims on disk: "the cache file <path> exists/contains/is empty"
  /\b(the (cache|state|lock|log) file|the file at)\s+\S+\.(json|jsonl|log|lock|sqlite|db)\s+(exists|contains|is empty|holds|carries)\b/i,
  // Process-state claims: "the dev server is running on port N", "no other process is bound to..."
  /\b(the (dev server|process|service) is (running|listening|bound) on port \d+|no other (process|worker) is (bound to|listening on))\b/i,
];

// Guard 1 — evidence co-located in the same message.
// At least one of these must be present near a matching claim for the
// message to pass.
const EVIDENCE_MARKERS = [
  // SQL output table separator
  /-+\+-+/,                                       // psql ASCII column rule
  /\(\d+ rows?\)/i,                               // psql "(N rows)" footer
  /\b\d+\s+rows?\s+(returned|affected|deleted|updated|inserted)\b/i,
  // EF / dotnet output
  /Microsoft\.EntityFrameworkCore/,
  /Executed DbCommand/i,
  // shell command + output pattern (any psql/sqlite/dotnet command + a code block)
  /```[\s\S]{0,400}(psql|sqlite3|dotnet ef|dotnet user-secrets|cat|ls -|stat |jq )[\s\S]{0,1500}```/,
  // file content quoted (heredoc/triple-backtick) accompanied by a real path
  /```[\s\S]{20,}```[\s\S]{0,200}\/[\w./-]+\.(cs|ts|tsx|json|md|jsonl|log)/,
  // tool-call output quoted directly: "Bash output:", "Read result:"
  /\b(Bash|Read|Grep|Glob) (output|result|returned)\b:/i,
];

// Guard 2 — explicit assumption marker.
// Same message hedges the claim and asks the user to verify, OR proposes
// a command they should run.
const ASSUMPTION_MARKERS = [
  /\b(assuming|if the seed (didn't|did not)|if X is true)\b[\s\S]{0,200}\b(please (confirm|verify)|let me know|tell me|run this)\b/i,
  /\bI (can'?t|cannot) (verify|confirm|read) (that|this)( without| from)?\b/i,
  /\bplease (run|confirm|check|verify)\b[\s\S]{0,200}(`[^`]+`|```[\s\S]+?```)/i,  // proposes a command
  /\b(needs|requires) (verification|confirmation|a manual check)\b/i,
  /\bI (haven't|have not) verified (this|that|the (db|database|row|value))\b/i,
];

// Guard 3 — user authorized the assumption / waived verification.
const USER_AUTH = [
  /\bjust assume\b/i,
  /\b(yes|that's|that is) the value\b/i,
  /\btrust me\b/i,
  /\b(skip|don't worry about) (the (db|database) check|verification|the query)\b/i,
  /\bI'?ll (verify|check) (that|it) myself\b/i,
  /\b(already|i) (checked|verified|confirmed) (that|the (db|row|value))\b/i,
];

// 2026-05-21 audit added guards 4 + 5 after a 50% production misfire rate.
//
// Guard 4 — STRUCTURAL-CONTEXT: the matched identifier appears in
// schema/code-structure discussion ("IsActive is a bool", "TwoFactorEnabled
// is the column we just added"), not in a claim about a specific runtime
// row value. If any of these markers is present in the message, the hook
// exits 0 — the claim is about the type/column/migration, not the row.
const STRUCTURAL_CONTEXT_MARKERS = [
  /\b(column|field|property|attribute) (called |named |is )?[A-Z][A-Za-z]+/i,
  /\b(the (type|schema|model|entity|migration|column|field)|EF defines|in the entity|nullable|not nullable|non-nullable|default value)\b/i,
  /\b(bool|string|int|Guid|DateTime|DateOnly|enum)\s+(not\s+)?nullable\b/i,
  /\bbacking (column|field)\b/i,
];

// Guard 5 — META-CONTEXT: the message is discussing the hook itself, not
// asserting a runtime claim. Quoting the matched phrase to talk about why
// the hook fired (or didn't) re-fires the hook recursively. Skip in that
// case.
const META_CONTEXT_MARKERS = [
  /\bthe (hook|regex|guard|matcher) (fired|matched|caught|missed)\b/i,
  /\bfalse[- ]positive\b/i,
  /\baudit trail\b/i,
  /\b(this|that) (will|would|did) (re-?)?fire the hook\b/i,
  /\bhook recursion\b/i,
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

  const evidenceCoLocated = EVIDENCE_MARKERS.some((re) => re.test(lastAssistantText));
  const explicitAssumption = ASSUMPTION_MARKERS.some((re) => re.test(lastAssistantText));
  const userAuthorized = USER_AUTH.some((re) => re.test(lastUserText));
  const structuralContext = STRUCTURAL_CONTEXT_MARKERS.some((re) => re.test(lastAssistantText));
  const metaContext = META_CONTEXT_MARKERS.some((re) => re.test(lastAssistantText));

  const guardsPassed =
    evidenceCoLocated ||
    explicitAssumption ||
    userAuthorized ||
    structuralContext ||
    metaContext ||
    SESSION_BYPASS;

  const outcome = SESSION_BYPASS ? "bypassed" : guardsPassed ? "passed" : "blocked";
  try {
    const stateDir = path.join(process.env.CLAUDE_PROJECT_DIR || process.cwd(), ".claude/state/runtime-state-verify");
    fs.mkdirSync(stateDir, { recursive: true });
    const logEntry = {
      ts: new Date().toISOString(),
      sessionId: input.session_id || "",
      hook: "runtime-state-claim",
      outcome,
      matched,
      guards: {
        evidenceCoLocated,
        explicitAssumption,
        userAuthorized,
        structuralContext,
        metaContext,
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
    "🛑 Runtime-state claim detected without co-located evidence.",
    "",
    `Matched patterns: ${matched.join(", ")}`,
    "",
    "Five guards were checked AND ALL FAILED:",
    `  • Evidence-co-located guard: ${evidenceCoLocated ? "PASS" : "fail"} (your message contains no psql / EF / file-read / tool-output marker backing the claim)`,
    `  • Explicit-assumption guard: ${explicitAssumption ? "PASS" : "fail"} (your message does not hedge the claim or propose a verification command)`,
    `  • User-authorization guard: ${userAuthorized ? "PASS" : "fail"} (user did not waive verification in their last message)`,
    `  • Structural-context guard: ${structuralContext ? "PASS" : "fail"} (the matched identifier appears as a runtime-value claim, not as schema/type discussion)`,
    `  • Meta-context guard: ${metaContext ? "PASS" : "fail"} (the message asserts runtime state, not discusses the hook itself)`,
    "",
    "The `verify-runtime-state` skill applies to ANY claim about a runtime value",
    "(database row, env var, file contents on disk, process state). Code",
    "inspection does NOT satisfy this — a seed file that doesn't enable TOTP",
    "doesn't mean the existing row has TwoFactorEnabled=false. Migrations,",
    "manual SQL, prior tests, and other tools all touch runtime state.",
    "",
    "Recovery options:",
    "  • Run the verification command (psql / dotnet ef / cat / ls) and quote the actual",
    "    output in your message, OR",
    "  • Rewrite the claim as an explicit assumption with a command for the user to run",
    "    (e.g. \"if X is the case, please confirm with `psql -c '...'`\"), OR",
    "  • Use AskUserQuestion if the command needs explicit consent (production DB reads,",
    "    secret files).",
    "  • If you're certain and the user already knows, set CERES_SKIP_RUNTIME_STATE_HOOK=1.",
    "",
    "See .claude/state/runtime-state-verify/log.jsonl for the audit trail.",
  ].join("\n");

  process.stderr.write(reason);
  process.exit(2);
});
