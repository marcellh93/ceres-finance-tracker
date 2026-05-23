#!/usr/bin/env node
// Stop hook — fires when 3+ commits share the same conventional-commit scope
// in the trailing 24 hours, AND the user's most recent message suggests the
// underlying problem persists. Forces deep-fix-mode.
//
// Why this hook exists:
//   2026-05-23 Remember-Me debug session: 5 commits with scope `fix(auth):`
//   landed in <24h, each claiming to fix the same user-reported logout bug.
//   The bug persisted across every commit. The loop-fingerprint hook didn't
//   trip because each commit touched different files at a different layer
//   (middleware, then api-client, then auth-context, then api-client again).
//   The same-target-edit-count hook didn't trip for the same reason.
//
// This hook catches the missed pattern: "I keep shipping fixes for the same
// thing, and the thing keeps not being fixed."
//
// Trigger:
//   - `git log --since=24.hours --format=%s` returns >=3 commits whose
//     conventional-commit scope matches (e.g. `fix(auth):...` x 5).
//   - User's most recent message contains a persistence/frustration phrase
//     (avoids firing on legitimately-clustered work like "5 docs commits in
//     a row for the same stage"). Phrases overlap with frustration-detect's
//     extended list but specifically lean toward "the original bug remains".
//
// Bypass: CERES_SKIP_REPEAT_SCOPE_HOOK=1 in env.

const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const COMMIT_THRESHOLD = 3;
const WINDOW_HOURS = 24;

// Phrases in the user's most recent message that indicate "the bug we've
// been working on still exists". Without this gate, the hook would fire on
// every legitimate cluster of commits sharing a scope.
const PERSISTENCE_PHRASES = [
  /\bstill (kicking|disconnecting|logging|broken|not working|not fixed|failing)\b/i,
  /\b(it|that) (still|hasn'?t)\b/i,
  /\bdoesn'?t work\b/i,
  /\bnot working\b/i,
  /\bhasn'?t worked\b/i,
  /\b(came|come) back .{0,40}(logged out|kicked out|signed out|disconnected)\b/i,
  /\bsame (bug|issue|problem|behavior|behaviour)\b/i,
  /\bsame thing\b/i,
  /\bagain\b[\s\S]{0,40}\b(broken|failing|logged out|kicked out|crashed)\b/i,
];

const SESSION_BYPASS = process.env.CERES_SKIP_REPEAT_SCOPE_HOOK === "1";

function parseScope(subject) {
  // Conventional-commit scope: type(scope): message
  // Returns null if not conventional.
  const m = subject.match(/^[a-z]+\(([^)]+)\):/);
  return m ? m[1] : null;
}

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

  let lastUserText = "";
  try {
    const lines = fs.readFileSync(transcriptPath, "utf8").trim().split("\n");
    for (let i = lines.length - 1; i >= 0; i--) {
      let entry;
      try {
        entry = JSON.parse(lines[i]);
      } catch {
        continue;
      }
      if (entry.type !== "user") continue;
      const msg = entry.message;
      if (msg && typeof msg.content === "string") {
        lastUserText = msg.content;
        break;
      }
      if (msg && Array.isArray(msg.content)) {
        const text = msg.content
          .filter((c) => c && (c.type === "text" || typeof c === "string"))
          .map((c) => (typeof c === "string" ? c : c.text))
          .filter(Boolean)
          .join("\n");
        if (text) {
          lastUserText = text;
          break;
        }
      }
    }
  } catch {
    process.exit(0);
  }

  if (!lastUserText) process.exit(0);

  // Gate: user message must indicate persistence. Without this guard, the
  // hook would fire on legitimate clusters (e.g. 5 docs commits for the
  // same stage).
  const persistenceMatched = PERSISTENCE_PHRASES.some((re) => re.test(lastUserText));
  if (!persistenceMatched) process.exit(0);

  // Look at the trailing 24h of commits.
  const projectDir = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  let subjects = [];
  try {
    const since = `${WINDOW_HOURS}.hours.ago`;
    const out = execSync(
      `git log --since="${since}" --format=%s --no-merges`,
      {
        cwd: projectDir,
        encoding: "utf8",
        stdio: ["ignore", "pipe", "ignore"],
      },
    );
    subjects = out.split("\n").map((s) => s.trim()).filter(Boolean);
  } catch {
    // Not a git repo or git unavailable - exit clean.
    process.exit(0);
  }

  if (subjects.length === 0) process.exit(0);

  // Bucket by conventional-commit scope.
  const scopeCounts = new Map();
  for (const subject of subjects) {
    const scope = parseScope(subject);
    if (!scope) continue;
    scopeCounts.set(scope, (scopeCounts.get(scope) || 0) + 1);
  }

  // Find the largest bucket above threshold.
  let topScope = null;
  let topCount = 0;
  for (const [scope, count] of scopeCounts.entries()) {
    if (count >= COMMIT_THRESHOLD && count > topCount) {
      topScope = scope;
      topCount = count;
    }
  }

  if (!topScope) process.exit(0);

  // Collect the matching commit subjects for the deny reason.
  const matchingSubjects = subjects.filter((s) => parseScope(s) === topScope);

  try {
    const stateDir = path.join(projectDir, ".claude/state/deep-fix-mode");
    fs.mkdirSync(stateDir, { recursive: true });
    fs.appendFileSync(
      path.join(stateDir, "repeat-scope.log.jsonl"),
      JSON.stringify({
        ts: new Date().toISOString(),
        sessionId: input.session_id || "",
        hook: "repeat-scope-commits",
        outcome: "blocked",
        scope: topScope,
        count: topCount,
        windowHours: WINDOW_HOURS,
        userPersistencePhraseMatched: true,
        matchingSubjects: matchingSubjects.slice(0, 10),
        userSnippet: lastUserText.slice(0, 240),
      }) + "\n",
    );
  } catch {
    /* best-effort */
  }

  const reasonLines = [
    "🛑 deep-fix-mode trigger: same-scope commit loop detected.",
    "",
    `Scope: ${topScope} - ${topCount} commits in trailing ${WINDOW_HOURS}h.`,
    "User's most recent message signals the underlying problem persists.",
    "",
    "Recent commits with this scope:",
  ];
  for (const s of matchingSubjects.slice(0, 6)) {
    reasonLines.push(`  - ${s.slice(0, 100)}`);
  }
  if (matchingSubjects.length > 6) {
    reasonLines.push(`  - ... and ${matchingSubjects.length - 6} more`);
  }
  reasonLines.push(
    "",
    "This pattern - multiple distinct commits all claiming to fix the same bug",
    "while the bug persists - is the loop-fingerprint hook's failure mode at the",
    "commit granularity. Each commit touches different files at different layers,",
    "so the per-tool fingerprint algorithm doesn't trip. But the user is still",
    "reporting the same symptom, which means none of the fixes addressed root cause.",
    "",
    "MANDATORY: Invoke the `deep-fix-mode` skill BEFORE responding. Do not propose,",
    "edit, or write anything until the skill's six-step procedure completes:",
    "  1. Stop. No mutations.",
    "  2. Write the failed-attempts table (one row per commit above).",
    "  3. Name the pattern from references/loop-patterns.md.",
    "  4. Identify the surface vs. root layer - likely root is one layer deeper.",
    "  5. Do external research at the documented authority bar.",
    "  6. Write the finished diagnosis in the diagnosis-template format.",
    "",
    "Recovery if this is a false positive:",
    "  - The user's message used persistence language but the commits are actually",
    "    unrelated - confirm explicitly and set CERES_SKIP_REPEAT_SCOPE_HOOK=1.",
    "  - Stage-of-work pattern (5 docs commits, 4 spec commits) is legitimately",
    "    clustered - same bypass.",
    "",
    "See .claude/state/deep-fix-mode/repeat-scope.log.jsonl for the audit trail.",
  );

  process.stderr.write(reasonLines.join("\n"));
  process.exit(2);
});
