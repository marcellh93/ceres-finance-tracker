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

  // Scope rule: only flag pending lines whose stage is referenced by ONE
  // of the new commits this turn. This prevents noise from unrelated stages
  // (e.g. Stage 12 always shows "Pending" until it ships months from now;
  // a commit on Stage 9.1.5.h should not be blocked by Stage 12's status).
  //
  // Stage IDs in commit messages look like `stage-9.1.5.h` (kebab) or
  // `Stage 9.1.5.h` (titled). Extract the numeric path and stem-match.
  let touchedStages = new Set();
  try {
    const range = `${storedHead}..${currentHead}`;
    // Read SUBJECT LINES only (--format=%s), not full bodies. Commit subjects
    // name their primary stage; bodies frequently cross-reference other
    // stages ("deferred to Stage 9.8", "follows Stage 6.4's pattern") which
    // are NOT in scope of this commit and should not be flagged as pending.
    //
    // Origin: 2026-05-18. The 9.6 implementation commit's subject was
    // `feat(stage-9.6): ...` but its body contained "Closes Stage 9
    // sub-stage 9.6" and "deferred to Stage 9.8". The hook scanning bodies
    // pulled in Stage 9 (parent, legitimately Pending) and Stage 9.8 (not
    // touched, also legitimately Pending), then blocked the Stop with a
    // false positive.
    const subjects = execSync(`git log --format=%s ${range}`, {
      cwd: projectDir,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
    });
    const stageRefs = subjects.match(/\b[Ss]tage[-\s]+(\d+(?:\.\d+)*[a-z]?)/g) || [];
    for (const ref of stageRefs) {
      const m = ref.match(/(\d+(?:\.\d+)*[a-z]?)/);
      if (m) {
        const id = m[1];
        // Add the exact stage ID AND its immediate parent if the leaf is a
        // letter suffix (sub-stage). E.g. "9.1.5.h" → also add "9.1.5"
        // (the batch-stage that 9.1.5.h is a member of), but NOT "9" or
        // "9.1" (those are separately-statused parents that 9.1.5.h does
        // not own). This matches the project's convention where sub-stages
        // a/b/c/.../i belong to a batch like 9.1.5, but 9.1 / 9.X / Stage 9
        // are independent stages with their own ❌ Pending statuses.
        touchedStages.add(id);
        const letterStrip = id.match(/^(.+)\.[a-z]$/);
        if (letterStrip) touchedStages.add(letterStrip[1]);
      }
    }
  } catch {
    // If we can't read the commit range, fall back to checking everything
    // (safe-but-noisy default). Empty set below would skip ALL lines.
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
    "This hook fires on commit boundaries (not text patterns) so it's noise-free during brainstorm Q&A.",
    "",
    "Recovery:",
    "  • Edit the listed line(s): change `- [ ]` to `- [x]` and rewrite the 'pending' text.",
    "  • Commit the roadmap change (a small docs commit is fine).",
    "  • Re-attempt the Stop.",
    "",
    "If a recent commit is unrelated to any pending verification (e.g. a hook/tooling fix made between stages), set CERES_SKIP_ROADMAP_VERIFY_HOOK=1 to bypass for this Stop (logged as outcome=bypassed).",
  ].join("\n");

  process.stderr.write(reason);
  process.exit(2);
});
