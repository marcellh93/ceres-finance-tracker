#!/usr/bin/env node
// Stop hook — blocks the Stop event when a commit happened this turn AND the
// roadmap still has at least one unticked "pending user run" verification line.
//
// Why this hook exists:
//   The roadmap has lines like:
//     - [ ] 9.1.5.e — ... **Manual browser verification pending user run** ...
//   When the user confirms a verification, the assistant is supposed to flip
//   those [ ] to [x]. This rule has failed in practice across the 9.1.5 batch
//   (sub-stages e, f, i stayed [ ] for many turns after browser confirmation).
//
// Trigger: action-based, not text-based.
//   - First-pass design (text-based) keyed on user "confirmation" phrases + my
//     verification-request phrases, but had false-positives during brainstorm
//     Q&A where the user says "yes" to a design question.
//   - Current design: the hook fires on Stop, compares current git HEAD against
//     the SHA stored at the last hook run. If they differ, a commit happened
//     this turn. That's the moment to check the roadmap — committing is a
//     definite assistant action, never a brainstorm-Q&A side effect.
//
// Scope (2026-05-22 rewrite): touched stages are now derived from the actual
// roadmap diff, not from commit-message subject parsing. Two sources:
//   1. Structured close-out markers `<!-- closes: stage-N -->` in the diff
//      (highest authority — explicit declaration).
//   2. Stage headings whose section had any line changed.
// If the commit did not change the roadmap at all, NOTHING closed and the
// hook exits silently. This replaces the commit-subject regex which was
// silently bypassed 8 times across 3 sessions per the 2026-05-22 hook audit.
//
// Behavior:
//   1. Read current git HEAD SHA.
//   2. Read stored SHA from .claude/state/roadmap-verify-flip/last-checked-commit.txt
//      (empty file or missing file = treat as new commit, but only check the
//      roadmap if HEAD's parent commit is not the same as HEAD — i.e. there's
//      actually a commit history. First-ever run on a clean tree exits 0.)
//   3. If SHAs match: no commit this turn → ALWAYS update the stored SHA (no-op)
//      and exit 0.
//   4. If SHAs differ: a commit happened. Scan the roadmap for any
//      `- [ ] ... pending user run` line. If none, update stored SHA and exit 0.
//      If any exist, BLOCK with stderr listing the unticked lines.
//   5. On block, DO NOT update the stored SHA — the next Stop should re-check.
//      The assistant is expected to fix the roadmap + commit (or set the bypass
//      env var), at which point the new commit's SHA matches HEAD on the next
//      hook run and the cycle resets.
//
// Bypass:
//   - CERES_SKIP_ROADMAP_VERIFY_HOOK=1 in env (escape hatch — also updates
//     the stored SHA so the bypass is one-shot per Stop).
//
// Logged outcomes go to .claude/state/roadmap-verify-flip/log.jsonl.

const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const DEFAULT_ROADMAP = "docs/roadmap-phase-three.md";

// Patterns that mark a roadmap item as "still pending" — both the checkbox
// shape AND the status-line shape (e.g. `**Status: ❌ Pending.**` below a
// `## Stage X` heading). The hook missed the second shape in its first pass
// and let an entire stage stay marked Pending after a clean batch close-out.
const PENDING_VERIFY_LINE =
  /^- \[ \].{0,2000}(pending user run|pending browser confirm|pending manual verif)/im;
const PENDING_STATUS_LINE =
  /\*\*Status:\s*❌?\s*Pending\.?\*\*/i;

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  const bypassed = process.env.CERES_SKIP_ROADMAP_VERIFY_HOOK === "1";

  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  if (input.stop_hook_active) process.exit(0);

  const projectDir = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  const stateDir = path.join(projectDir, ".claude/state/roadmap-verify-flip");
  const sessionsDir = path.join(stateDir, "sessions");
  const sessionId = input.session_id || "default";
  // Per-session storage so concurrent sessions don't race each other's SHA cache.
  const stateFile = path.join(sessionsDir, `${sessionId}.txt`);

  // Get current HEAD SHA.
  let currentHead = "";
  try {
    currentHead = execSync("git rev-parse HEAD", {
      cwd: projectDir,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
    }).trim();
  } catch {
    // Not a git repo or git unavailable — exit clean.
    process.exit(0);
  }

  if (!currentHead) process.exit(0);

  // Read stored SHA.
  let storedHead = "";
  try {
    if (fs.existsSync(stateFile)) {
      storedHead = fs.readFileSync(stateFile, "utf8").trim();
    }
  } catch {
    // ignore
  }

  // If first run for this session: record current HEAD and exit. Don't fire
  // on session start (the user hasn't committed anything yet in this session).
  if (!storedHead) {
    try {
      fs.mkdirSync(sessionsDir, { recursive: true });
      fs.writeFileSync(stateFile, currentHead);
    } catch {
      // best-effort
    }
    process.exit(0);
  }

  // If no commit since last check: nothing to do.
  if (storedHead === currentHead) process.exit(0);

  // A commit happened this turn (or multiple commits). Scan the roadmap.
  const roadmapRel = process.env.CERES_ROADMAP_PATH || DEFAULT_ROADMAP;
  // Honor absolute paths (useful for tests and multi-roadmap setups).
  const roadmapPath = path.isAbsolute(roadmapRel)
    ? roadmapRel
    : path.join(projectDir, roadmapRel);

  let roadmapContents = "";
  try {
    roadmapContents = fs.readFileSync(roadmapPath, "utf8");
  } catch {
    // Roadmap configured but not readable. Don't silently pass — that hides
    // the misconfiguration. Block with a config-error message; the user can
    // fix the path or set the bypass env var.
    const reason = [
      "🛑 Roadmap-verify hook: configured roadmap file not readable.",
      "",
      `Configured path: ${roadmapRel}`,
      `Resolved to:     ${roadmapPath}`,
      "",
      "Either fix CERES_ROADMAP_PATH (or move the default file), or set",
      "CERES_SKIP_ROADMAP_VERIFY_HOOK=1 for the session to bypass.",
    ].join("\n");
    process.stderr.write(reason);
    process.exit(2);
  }

  // Scope rule: derive touched stages from what the commit ACTUALLY changed in
  // the roadmap, not from what its commit message says it changed. Two sources,
  // in order of authority:
  //
  //   1. Structured close-out markers `<!-- closes: stage-N -->` added to the
  //      roadmap diff. If a commit's diff added this marker, it's an
  //      explicit declaration of which stage closed. Highest authority.
  //
  //   2. Stage headings (`## Stage X`) whose body had any line modified in the
  //      commit's diff. If a stage's section wasn't touched, it can't have
  //      been closed.
  //
  // Commit-message subjects are no longer used to derive scope — they pulled in
  // stages that were merely cross-referenced (2026-05-18 audit), and even
  // after the strip-numeric-ancestor fix the user silently bypassed 8 times
  // across 3 sessions, which is the loud signal that the heuristic still
  // overfires. If the commit didn't change the roadmap at all, NOTHING closed
  // and the hook exits silently.
  let touchedStages = new Set();
  let roadmapTouched = false;
  try {
    const range = `${storedHead}..${currentHead}`;
    const roadmapRelForDiff = roadmapRel;
    // Get the unified diff of the roadmap file across the commit range.
    // -U0 — no context lines, only changed lines; cheaper to scan.
    const diff = execSync(
      `git diff -U0 ${range} -- ${roadmapRelForDiff}`,
      {
        cwd: projectDir,
        encoding: "utf8",
        stdio: ["ignore", "pipe", "ignore"],
      },
    );

    if (!diff.trim()) {
      // Roadmap not changed in this commit range — nothing closed.
      // Update the stored SHA and exit clean.
      try {
        fs.mkdirSync(sessionsDir, { recursive: true });
        fs.writeFileSync(stateFile, currentHead);
      } catch {}
      process.exit(0);
    }

    roadmapTouched = true;

    // Source 1: structured close-out markers added by this commit.
    // Format: `<!-- closes: stage-N -->` (e.g. `<!-- closes: stage-9.1.5 -->`,
    // `<!-- closes: stage-9.1.5.h -->`).
    const markerRe = /^\+.*<!--\s*closes:\s*stage-(\d+(?:\.\d+)*[a-z]?)\s*-->/gim;
    let mm;
    while ((mm = markerRe.exec(diff)) !== null) {
      touchedStages.add(mm[1]);
    }

    // Source 2: stage headings whose section had any change. Walk the diff
    // and accumulate the current `## Stage X` heading every changed line
    // appeared under. The diff hunk headers (`@@ -... +... @@ ## Stage X`)
    // also carry the heading for context — git emits the closest enclosing
    // section header. Read those.
    const hunkHeaderRe = /^@@[^@]*@@\s*##\s+Stage\s+(\S+)/gim;
    let hh;
    while ((hh = hunkHeaderRe.exec(diff)) !== null) {
      const id = hh[1].match(/(\d+(?:\.\d+)*[a-z]?)/);
      if (id) {
        touchedStages.add(id[1]);
        const letterStrip = id[1].match(/^(.+)\.[a-z]$/);
        if (letterStrip) touchedStages.add(letterStrip[1]);
      }
    }

    // Source 2 fallback: if the hunk-header parse missed (e.g. the change
    // was right at the file head), scan the ADDED-LINES `## Stage X`
    // headings directly. Less precise but a safety net.
    const addedHeadingRe = /^\+\s*##\s+Stage\s+(\d+(?:\.\d+)*[a-z]?)/gim;
    let ah;
    while ((ah = addedHeadingRe.exec(diff)) !== null) {
      touchedStages.add(ah[1]);
      const letterStrip = ah[1].match(/^(.+)\.[a-z]$/);
      if (letterStrip) touchedStages.add(letterStrip[1]);
    }
  } catch {
    // git diff failed — fall back to checking everything (safe-but-noisy).
    touchedStages = null;
  }

  const stageInTouched = (stageStr) => {
    if (!touchedStages) return true; // fallback: no scope filter
    if (touchedStages.size === 0) return false;
    // stageStr is like "Stage 9.1.5" or "9.1.5.h"; extract the numeric path.
    const m = stageStr.match(/(\d+(?:\.\d+)*[a-z]?)/);
    if (!m) return false;
    const id = m[1];
    // Match only on exact stage ID OR letter-suffix descent
    // (e.g. touched "9.1.5" matches roadmap line "9.1.5.e"). Do NOT
    // match parent stages of the touched one — those are independently
    // statused (Stage 9 ≠ Stage 9.1.5 even though "9.1.5" extends "9").
    for (const t of touchedStages) {
      if (id === t) return true;
      // A line for "9.1.5.e" matches a touched batch "9.1.5".
      if (id.startsWith(t + ".") && /^[a-z]$/.test(id.slice(t.length + 1))) {
        return true;
      }
    }
    return false;
  };

  const roadmapLines = roadmapContents.split("\n");
  const pendingLines = [];
  for (let i = 0; i < roadmapLines.length; i++) {
    const line = roadmapLines[i];
    if (PENDING_VERIFY_LINE.test(line)) {
      const idMatch = line.match(/-\s*\[\s\]\s+(\S[^—]{0,40})/);
      const id = idMatch ? idMatch[1].trim() : "(unknown)";
      if (stageInTouched(id)) {
        pendingLines.push({ lineNumber: i + 1, id, kind: "checkbox" });
      }
    }
    if (PENDING_STATUS_LINE.test(line)) {
      // Walk backwards up to 10 lines to find the nearest `## Stage X` H2.
      let stage = "(unknown stage)";
      for (let j = i - 1; j >= Math.max(0, i - 10); j--) {
        const m = roadmapLines[j].match(/^##\s+(Stage\s+\S+)/);
        if (m) { stage = m[1]; break; }
      }
      if (stageInTouched(stage)) {
        pendingLines.push({ lineNumber: i + 1, id: stage, kind: "status" });
      }
    }
  }

  // Always log the outcome.
  const outcome = bypassed
    ? "bypassed"
    : pendingLines.length > 0
      ? "blocked"
      : "passed";
  try {
    fs.mkdirSync(stateDir, { recursive: true });
    const logEntry = {
      ts: new Date().toISOString(),
      sessionId,
      outcome,
      storedHead,
      currentHead,
      roadmap: roadmapRel,
      pendingLines,
    };
    fs.appendFileSync(path.join(stateDir, "log.jsonl"), JSON.stringify(logEntry) + "\n");
  } catch {
    // best-effort
  }

  // If bypassed OR no pending lines: update SHA and exit 0.
  if (bypassed || pendingLines.length === 0) {
    try {
      fs.mkdirSync(sessionsDir, { recursive: true });
      fs.writeFileSync(stateFile, currentHead);
    } catch {}
    process.exit(0);
  }

  // BLOCK. DO NOT update the stored SHA — the next Stop after the fix should
  // re-check against the same stored SHA so the new fix commit triggers the
  // pass path and naturally advances the cache.
  const lines_summary = pendingLines
    .map((p) => `  • line ${p.lineNumber} (${p.kind}): ${p.id}`)
    .join("\n");

  const reason = [
    "🛑 Commit landed this turn, but the roadmap still has unticked pending-verification line(s).",
    "",
    `Roadmap: ${roadmapRel}`,
    `Stored HEAD (start of turn): ${storedHead.slice(0, 7)}`,
    `Current HEAD:                ${currentHead.slice(0, 7)}`,
    `Pending lines (${pendingLines.length}):`,
    lines_summary,
    "",
    "Per feedback_finished_stages_have_no_unchecked_items:",
    "  • `checkbox` kind — flip the `- [ ]` to `- [x]` and replace the 'pending user run' text with the confirmed verification summary.",
    "  • `status` kind — change `**Status: ❌ Pending.**` to `**Status: ✅ Done (<date>).**` under the relevant `## Stage X` heading.",
    "Make the edits in the SAME commit chain that closes the stage.",
    "",
    "This hook fires on commit boundaries (not text patterns) and scopes via",
    "the actual roadmap diff (not commit-message parsing), so it should only",
    "ever block when a commit genuinely closed a stage but left items unticked.",
    "",
    "Recovery:",
    "  • Edit the listed line(s): change `- [ ]` to `- [x]` and rewrite the 'pending' text.",
    "  • Commit the roadmap change (a small docs commit is fine).",
    "  • Re-attempt the Stop.",
    "",
    "Optional: add an explicit close-out marker to the stage heading:",
    "  `## Stage 9.1.5.h <!-- closes: stage-9.1.5.h -->`",
    "Markers are authoritative for scope — when present, the hook only checks",
    "lines under stages the marker names.",
    "",
    "If a recent commit is unrelated to any pending verification (e.g. a hook/tooling fix made between stages), set CERES_SKIP_ROADMAP_VERIFY_HOOK=1 to bypass for this Stop (logged as outcome=bypassed).",
  ].join("\n");

  process.stderr.write(reason);
  process.exit(2);
});
