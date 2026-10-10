#!/usr/bin/env node
// Stop hook — Phase 1 9.5a evidence-bundle gate.
//
// Replaces ~10 lexical-prose Stop hooks with a single artifact-existence +
// freshness check. The agent's tool calls populate
// .claude/state/evidence/stage-<id>/ during the turn (Playwright walks,
// psql queries, curl probes, dotnet runs, turn-shape generator). On Stop,
// this hook verifies the bundle is present, complete, and fresh.
//
// Fires only when the turn produced material code changes worth verifying.
// Docs/config-only turns and no-commit turns exit 0 ("evidence not required").
//
// Bypass: CERES_SKIP_EVIDENCE_BUNDLE_HOOK=1
// Log:    .claude/state/evidence/_log.jsonl

const fs = require("fs");
const path = require("path");
const cp = require("child_process");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const EVIDENCE_DIR = path.join(PROJECT_DIR, ".claude", "state", "evidence");
const LOG_PATH = path.join(EVIDENCE_DIR, "_log.jsonl");
const ORPHAN_LOG = path.join(EVIDENCE_DIR, "_orphan", "log.jsonl");
const TURN_MARKER = path.join(
  PROJECT_DIR,
  ".claude",
  "state",
  "run-tests",
  "last.log",
);

// Identical blocks escalate to a pass after this many strikes. See __tests__/no-block-loop.test.js.
const MAX_STRIKES = 2;

// Session writes come from track-session-writes.js (PostToolUse), same state
// dir run-tests.sh tiers on. Absent / empty / malformed all mean "no writes".
//
// Two files, because run-tests.sh is an EARLIER Stop hook and truncates the
// live file before this one runs; it leaves `.lastturn.json` as the breadcrumb.
// Either showing writes means this turn wrote code.
function sessionWroteCode(projectDir, sessionId) {
  if (!sessionId || sessionId === "unknown") return false;
  const dir = path.join(projectDir, ".claude", "state", "run-tests");
  const candidates = [`${sessionId}.json`, `${sessionId}.lastturn.json`];
  return candidates.some((name) => {
    try {
      const raw = fs.readFileSync(path.join(dir, name), "utf8");
      if (!raw.trim()) return false;
      const state = JSON.parse(raw);
      return Array.isArray(state.files) && state.files.length > 0;
    } catch {
      return false;
    }
  });
}

// Repo-relative paths this session's turn actually wrote, from the same
// tracker sessionWroteCode reads. Only .cs/.ts/.tsx/.csproj/.sln are tracked,
// so this is a floor on the turn's writes, never the whole picture — callers
// must union it with the working tree.
function sessionWrittenFiles(projectDir, sessionId) {
  if (!sessionId || sessionId === "unknown") return [];
  const dir = path.join(projectDir, ".claude", "state", "run-tests");
  const out = new Set();
  for (const name of [`${sessionId}.json`, `${sessionId}.lastturn.json`]) {
    try {
      const raw = fs.readFileSync(path.join(dir, name), "utf8");
      if (!raw.trim()) continue;
      const state = JSON.parse(raw);
      if (Array.isArray(state.files)) for (const f of state.files) out.add(f);
    } catch {
      /* fail-open */
    }
  }
  return [...out];
}

// Fingerprints the IDENTITY of each gap, not its rendered text. Stale-gap
// messages embed the turn-start timestamp, which changes every turn, so hashing
// the raw text minted a fresh fingerprint per turn: strikes never accumulated
// and MAX_STRIKES stayed unreachable (unbounded block loop, 2026-08-21).
//
// Sorted so gap ordering cannot split the fingerprint. Separator is NUL, as
// before, since it cannot appear in a gap string.
function normalizeGap(gap) {
  const m = gap.match(/^(Stale|Missing): ([^\s(]+)/);
  return m ? `${m[1]}: ${m[2]}` : gap;
}

function gapsFingerprint(gaps) {
  const normalized = gaps.map(normalizeGap).sort();
  return require("crypto")
    .createHash("sha1")
    .update(normalized.join("\0"))
    .digest("hex")
    .slice(0, 12);
}

function strikeFile(projectDir) {
  return path.join(projectDir, ".claude", "state", "evidence", "_strikes.json");
}

function strikeKey(sessionId, stageId, fingerprint) {
  return `${sessionId}::${stageId}::${fingerprint}`;
}

function strikeCount(projectDir, sessionId, stageId, fingerprint) {
  try {
    const data = JSON.parse(fs.readFileSync(strikeFile(projectDir), "utf8"));
    return data[strikeKey(sessionId, stageId, fingerprint)] || 0;
  } catch {
    return 0;
  }
}

function recordStrike(projectDir, sessionId, stageId, fingerprint) {
  const f = strikeFile(projectDir);
  let data = {};
  try {
    data = JSON.parse(fs.readFileSync(f, "utf8"));
  } catch {
    /* fresh */
  }
  const key = strikeKey(sessionId, stageId, fingerprint);
  data[key] = (data[key] || 0) + 1;
  try {
    fs.mkdirSync(path.dirname(f), { recursive: true });
    fs.writeFileSync(f, JSON.stringify(data, null, 2));
  } catch {
    /* fail-open */
  }
  return data[key];
}

// A file is a user-facing UI surface only if it's a non-test Client/src file.
// A *.test.ts(x) change (e.g. a vitest timing-headroom edit) has no rendered surface to walk.
//
// Controllers are deliberately NOT a UI surface. This predicate dates from the Razor era,
// when a controller returned a rendered view; no Razor views remain (see Program.cs — the
// project is SPA-only), so every controller is now a JSON API with no page to walk, no
// console to capture, and no trace to record. Controllers still require curl-transcript.txt
// via the HTTP-surface slot below, which is the evidence that actually fits them.
// Narrowed 2026-08-23 after Stage 15.6 shipped an admin JSON API with no UI (the screen is
// Stage 15.8) and the old predicate demanded four browser artifacts that cannot exist.
const isTestFile = (f) => /\.test\.(ts|tsx)$/.test(f);
const touchesUiSurface = (files) =>
  files.some((f) => /^ProjectCeres\.Client\/src\//.test(f) && !isTestFile(f));

// Diff-shape → required slots. Each predicate runs on the list of changed files.
const SLOT_TABLE = [
  {
    slot: "walk-summary.json",
    when: touchesUiSurface,
    reason:
      "diff touches user-facing surface (non-test Client/src or Controllers)",
  },
  {
    slot: "trace.zip",
    when: touchesUiSurface,
    reason:
      "diff touches user-facing surface (non-test Client/src or Controllers)",
  },
  {
    slot: "console.json",
    when: touchesUiSurface,
    reason:
      "diff touches user-facing surface (non-test Client/src or Controllers)",
  },
  {
    slot: "network.json",
    when: touchesUiSurface,
    reason:
      "diff touches user-facing surface (non-test Client/src or Controllers)",
  },
  {
    slot: "curl-transcript.txt",
    when: (files) =>
      files.some(
        (f) =>
          /^ProjectCeres\/Controllers\//.test(f) ||
          /^ProjectCeres\/Endpoints\//.test(f),
      ),
    reason: "diff touches HTTP surface (Controllers or Endpoints)",
  },
  {
    slot: "rls-audit.psql",
    when: (files) =>
      files.some(
        (f) =>
          /^ProjectCeres\/Models\//.test(f) ||
          /^ProjectCeres\/Migrations\//.test(f),
      ),
    reason: "diff touches Models (IUserOwned entities) or Migrations",
  },
  {
    slot: "build-matrix.json",
    when: () => true,
    reason: "always required when code changes",
  },
  {
    slot: "registry-sweep.json",
    when: (files) =>
      files.some(
        (f) =>
          /^ProjectCeres\/Models\//.test(f) ||
          /^ProjectCeres\/Resources\//.test(f) ||
          /^ProjectCeres\/Common\/Email\/EmailTemplateKey\.cs$/.test(f) ||
          /^ProjectCeres\/Models\/AuditLog\.cs$/.test(f),
      ),
    reason: "diff touches Models, Resources, EmailTemplateKey, or AuditLog",
  },
  {
    slot: "turn-shape.json",
    when: () => true,
    reason: "always required when code changes",
  },
  {
    slot: "reviewer-pipeline.json",
    when: (files) =>
      files.some(
        (f) =>
          /^ProjectCeres\/Common\/Authentication\//.test(f) ||
          /^ProjectCeres\/Migrations\//.test(f) ||
          /^ProjectCeres\/Models\//.test(f),
      ),
    reason:
      "diff touches auth, migrations, or IUserOwned models — 3-agent reviewer pipeline required",
  },
];

const DOCS_CONFIG_RE =
  /^(docs\/|\.claude\/|README\.md$|\.gitignore$|CHANGELOG\.md$|.*\.md$)/;

// Resolved per call, not captured at module load, so tests can point the git
// helpers at a scratch repo via CLAUDE_PROJECT_DIR.
function projectDir() {
  return process.env.CLAUDE_PROJECT_DIR || PROJECT_DIR;
}

function safeExec(cmd) {
  try {
    return cp
      .execSync(cmd, {
        cwd: projectDir(),
        encoding: "utf8",
        stdio: ["ignore", "pipe", "ignore"],
      })
      .trim();
  } catch {
    return "";
  }
}

function appendLog(file, entry) {
  try {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.appendFileSync(file, JSON.stringify(entry) + "\n");
  } catch {
    /* fail-open */
  }
}

function getTurnStart() {
  // Prefer the run-tests log mtime (updated each turn). Fallback: parent commit time.
  try {
    return Math.floor(fs.statSync(TURN_MARKER).mtimeMs);
  } catch {
    const ts = safeExec("git log -1 --format=%ct HEAD~1 2>/dev/null");
    return ts ? parseInt(ts, 10) * 1000 : 0;
  }
}

// Files THIS turn put on disk — the union of:
//   1. the working tree (uncommitted edits, staged or not, plus untracked), and
//   2. commits made during this turn (HEAD back to the turn's starting commit).
//
// It deliberately does NOT use `git diff HEAD~1 HEAD`. That is a property of
// committed history, not of the turn: on a turn that commits nothing it audits
// somebody else's last commit, and on a turn that makes three commits it sees
// only the third. Both misfired — the 2026-08-07 unbounded-block loop and the
// 2026-08-21 misattribution of CSRF work to stage 12.1 were the same root cause.
// The commit HEAD pointed at when this turn began: walk back from HEAD while
// commits are newer than the turn marker; the first older commit is the start.
//
// Git commit times are second-granular while the marker is millisecond-granular,
// so a commit written in the SAME second as the marker is genuinely ambiguous.
// Resolve it by asking whether the commit touched a file this turn wrote —
// the write-tracker is turn-scoped and does not share git's rounding. Without
// that check a pre-existing commit made in the marker's second is read as part
// of the turn, which is the misattribution this whole function exists to stop.
//
// Returns null when nothing was committed this turn.
function getTurnStartSha(turnStart, sessionId) {
  if (!turnStart) return null;
  const cutoffSec = Math.floor(turnStart / 1000);
  const written = new Set(sessionWrittenFiles(projectDir(), sessionId));
  const lines = safeExec('git log -20 --format="%H %ct" 2>/dev/null')
    .split("\n")
    .filter(Boolean);

  for (const line of lines) {
    const [sha, ct] = line.split(" ");
    const sec = parseInt(ct, 10);
    if (sec < cutoffSec) return sha;
    if (sec === cutoffSec && written.size > 0) {
      // Same-second tie: this commit belongs to the turn only if it carries a
      // file the turn actually wrote. Otherwise treat it as the start point.
      const touched = safeExec(
        `git show --name-only --format= ${sha} 2>/dev/null`,
      )
        .split("\n")
        .filter(Boolean);
      if (!touched.some((f) => written.has(f))) return sha;
    }
  }
  // Every recent commit is newer than the marker — fall back to the oldest seen.
  return lines.length ? lines[lines.length - 1].split(" ")[0] : null;
}

// The turn window (getTurnStartSha) is the newest commit OLDER than the turn's
// start marker. On a long session that reaches back across earlier, finished
// work: on 2026-08-23 a migration-only turn was asked for Playwright traces and
// a curl transcript because the window still contained stage 12.1's SessionsPage
// and stage 12.3's SessionsApiController — both of which already had their own
// complete bundles. Once the stage id is known, narrow the file set to commits
// carrying that stage's scope prefix. Falls back to the wide set when the stage
// owns no commits in the window (uncommitted work), so nothing stops being gated.
function filesForStage(changedFiles, turnStartSha, stageId) {
  if (!turnStartSha || !stageId) return changedFiles;
  const re = new RegExp(
    `^(?:feat|fix|chore|refactor|test|docs|perf|build|ci|style)\\(${stageId.replace(/\./g, "\\.")}\\):`,
  );
  const shas = safeExec(`git rev-list ${turnStartSha}..HEAD 2>/dev/null`)
    .split("\n")
    .filter(Boolean);
  const owned = new Set();
  for (const sha of shas) {
    const subject = safeExec(`git log -1 --format=%s ${sha} 2>/dev/null`);
    if (!re.test(subject)) continue;
    for (const f of safeExec(
      `git show --name-only --format= ${sha} 2>/dev/null`,
    )
      .split("\n")
      .filter(Boolean)) {
      owned.add(f);
    }
  }
  if (owned.size === 0) return changedFiles;

  // Keep anything still uncommitted — it belongs to the turn regardless of which
  // commit claims it — plus every file the stage's own commits touched.
  const uncommitted = new Set(
    safeExec("git status --porcelain=v1 --untracked-files=all 2>/dev/null")
      .split("\n")
      .filter(Boolean)
      .map(porcelainPath),
  );
  return changedFiles.filter((f) => owned.has(f) || uncommitted.has(f));
}

// A file counts as substantively changed only if its diff since turn-start has at
// least one changed line that is NOT a using-directive and NOT pure whitespace. A
// `dotnet format style` using-removal or whitespace reflow is not something a spec,
// security, or test reviewer can comment on — the same judgment lastReviewableSha()
// already applies to the STALENESS check, now applied to the slot-REQUIREMENT check
// too (the two had drifted: a pure using-removal in Migrations/ or Services/ re-fired
// the 3-agent reviewer-pipeline requirement though the staleness half would have
// treated it as non-reviewable). Mirrors the DOCS_CONFIG_RE exclusion. Conservative:
// with no turn window, or on ANY non-trivial changed line, the file gates normally.
// Pure classifier (exported for tests): given the raw text of a unified diff, is
// EVERY changed (+/-) line trivial — blank/whitespace or a bare `using X.Y;`? Returns
// false for an empty changed-set (no evidence of trivial change) so a caller never
// relaxes the gate on a diff it couldn't read.
function diffIsTrivial(diffText) {
  if (!diffText) return false;
  const changed = diffText
    .split("\n")
    .filter((l) => /^[+-]/.test(l) && !/^(\+\+\+|---)/.test(l))
    .map((l) => l.slice(1)); // drop the +/- marker
  if (changed.length === 0) return false;
  const trivial = (line) => {
    const t = line.replace(/^﻿/, "").trim(); // strip BOM + surrounding ws
    return t === "" || /^using\s+[\w.]+\s*;$/.test(t);
  };
  return changed.every(trivial);
}

function isTriviallyChanged(file, turnStartSha) {
  if (!turnStartSha) return false; // no window → cannot prove trivial; gate normally
  return diffIsTrivial(
    safeExec(`git diff ${turnStartSha}..HEAD -- "${file}" 2>/dev/null`),
  );
}

// Parses one `git status --porcelain=v1` line to its destination path.
// safeExec trims output, which can strip the leading status space of the first line
// (" M path" -> "M path"), so a fixed slice(3) eats a real path character — it turned
// ".claude/..." into "claude/...", which then dodged the docs-config filter and made a
// docs-only turn look like a code turn (2026-08-27). Match the 2-char status + space
// prefix explicitly, tolerating 1 or 2 leading status chars.
function porcelainPath(line) {
  const m = line.match(/^.{1,2} (.*)$/);
  const p = m ? m[1] : line;
  const arrow = p.indexOf(" -> ");
  return arrow === -1 ? p : p.slice(arrow + 4);
}

function getChangedFiles(sessionId, turnStartSha) {
  const files = new Set();

  // Files this session is responsible for: its write-tracker (turn-scoped,
  // survives stash/checkout) ∪ files touched by commits made during this turn.
  // This is the session-scoped signal we filter the shared working tree against.
  const sessionOwned = new Set(sessionWrittenFiles(projectDir(), sessionId));
  if (turnStartSha) {
    const committed = safeExec(
      `git diff --name-only ${turnStartSha}..HEAD 2>/dev/null`,
    );
    for (const f of committed.split("\n").filter(Boolean)) {
      sessionOwned.add(f);
      files.add(f); // this turn's commits always count
    }
  }

  // Working tree (staged + unstaged + untracked) is SHARED across every terminal
  // on this repo, so a parallel session's uncommitted edits appear here too.
  // Count a working-tree file only if THIS session owns it (tracker or turn commit).
  // Fixes the cross-session false block (2026-10-06): a brainstorm turn that wrote
  // no code was gated for a parallel session's Dashboard edits. A file that is ONLY
  // in `git status` — uncommitted, not in this session's tracker, not in a turn
  // commit — belongs to another session and is dropped.
  const status = safeExec(
    "git status --porcelain=v1 --untracked-files=all 2>/dev/null",
  );
  for (const line of status.split("\n").filter(Boolean)) {
    const f = porcelainPath(line);
    if (sessionOwned.has(f)) files.add(f);
  }

  // The write-tracker is the floor: it survives a `git stash` or a checkout that
  // would otherwise erase a turn's work from the working tree.
  for (const f of sessionWrittenFiles(projectDir(), sessionId)) files.add(f);

  return [...files];
}

// The stage this turn is working on. Prefers commits made DURING the turn;
// falls back to walking recent history only when the turn committed nothing,
// since an uncommitted turn has no message of its own to read.
//
// Returns { stageId, source } so callers can tell a confident attribution from
// an inferred one — history-inferred stage ids gate nothing (see runMain).
function getStageId(turnStartSha) {
  const STAGE_RE =
    /^(?:feat|fix|chore|refactor|test|docs|perf|build|ci|style)\(([0-9]+(?:[.\w-]+)?)\):/m;

  // 1. Commits created in this turn, newest first.
  if (turnStartSha) {
    const shas = safeExec(`git rev-list ${turnStartSha}..HEAD 2>/dev/null`)
      .split("\n")
      .filter(Boolean);
    for (const sha of shas) {
      const m = safeExec(`git log -1 --format=%B ${sha} 2>/dev/null`).match(
        STAGE_RE,
      );
      if (m) return { stageId: m[1], source: "turn-commit" };
    }
  }

  // 2. Nothing committed this turn — infer from recent history.
  for (let i = 0; i < 5; i++) {
    const msg = safeExec(`git log -1 --skip=${i} --format=%B 2>/dev/null`);
    if (!msg) continue;
    const m = msg.match(STAGE_RE);
    if (m) return { stageId: m[1], source: "history" };
  }
  return { stageId: null, source: "none" };
}

function validateTurnShape(shapePath) {
  const gaps = [];
  let shape;
  try {
    shape = JSON.parse(fs.readFileSync(shapePath, "utf8"));
  } catch (e) {
    return [`turn-shape.json: invalid JSON (${e.message})`];
  }
  if (!shape || typeof shape !== "object")
    return ["turn-shape.json: not an object"];
  for (const arrName of [
    "fix_mentions",
    "confidence_claims",
    "runtime_assertions",
  ]) {
    const arr = shape[arrName];
    if (!Array.isArray(arr)) {
      gaps.push(`turn-shape.json: missing/invalid '${arrName}' array`);
      continue;
    }
    arr.forEach((entry, i) => {
      const snip =
        entry && entry.snippet
          ? `"${String(entry.snippet).slice(0, 60)}..."`
          : `[${i}]`;
      if (arrName === "fix_mentions" && entry.co_located_edit !== true)
        gaps.push(
          `turn-shape.json: fix_mentions[${i}] has co_located_edit: false (${snip} had no Edit in this turn)`,
        );
      if (arrName === "confidence_claims" && entry.co_located_research !== true)
        gaps.push(
          `turn-shape.json: confidence_claims[${i}] has co_located_research: false (${snip})`,
        );
      if (arrName === "runtime_assertions") {
        if (entry.co_located_query !== true)
          gaps.push(
            `turn-shape.json: runtime_assertions[${i}] has co_located_query: false (${snip})`,
          );
        else if (
          !entry.query_output ||
          !fs.existsSync(path.join(PROJECT_DIR, entry.query_output))
        )
          gaps.push(
            `turn-shape.json: runtime_assertions[${i}].query_output missing on disk (${entry.query_output || "<unset>"})`,
          );
      }
    });
  }
  return gaps;
}

// The newest commit that touched code a reviewer could actually comment on.
//
// The staleness check used to compare diff_sha against bare HEAD, so ANY commit
// invalidated a completed review — including docs, hook edits and settings, none of
// which a security or spec reviewer has anything to say about. On 2026-08-24 that
// turned one genuine review into an all-day re-review loop: every remediation commit,
// and even the commit recording the reviewers' own findings, re-triggered the gate.
//
// Reviewable = the same surfaces the reviewer-pipeline slot itself keys on, plus the
// application and test code a review reads. Falls back to HEAD when nothing matches,
// so a bundle can never go unchecked.
function lastReviewableSha() {
  const shas = safeExec("git rev-list -30 HEAD 2>/dev/null")
    .split("\n")
    .filter(Boolean);
  const reviewable = (f) =>
    /^ProjectCeres\/(?!Migrations\/.*Designer\.cs)/.test(f) ||
    /^ProjectCeres\.Client\/src\//.test(f) ||
    /^ProjectCeres\.Tests\//.test(f);
  for (const sha of shas) {
    const files = safeExec(`git show --name-only --format= ${sha} 2>/dev/null`)
      .split("\n")
      .filter(Boolean);
    if (files.some(reviewable)) return sha;
  }
  return shas[0] || null;
}

function validateReviewerPipeline(slotPath, headSha) {
  const gaps = [];
  let data;
  try {
    data = JSON.parse(fs.readFileSync(slotPath, "utf8"));
  } catch (e) {
    return [`reviewer-pipeline.json: invalid JSON (${e.message})`];
  }
  if (!data || typeof data !== "object")
    return ["reviewer-pipeline.json: not an object"];

  const roles = Array.isArray(data.reviewers)
    ? data.reviewers.map((r) => r && r.role)
    : [];
  for (const required of ["writer", "security", "playwright-test-audit"]) {
    if (!roles.includes(required))
      gaps.push(`reviewer-pipeline.json: missing reviewer role '${required}'`);
  }
  // Compare against the last commit that touched reviewable code, not bare HEAD:
  // docs/hooks/settings commits cannot invalidate a review. Falls back to the caller's
  // headSha when git is unavailable or nothing reviewable is found — which is also what
  // keeps this unit-testable with a synthetic sha.
  const reviewableSha = lastReviewableSha() || headSha;
  if (
    reviewableSha &&
    data.diff_sha !== reviewableSha &&
    data.diff_sha !== headSha
  ) {
    gaps.push(
      `reviewer-pipeline.json: diff_sha '${data.diff_sha || "<unset>"}' does not match ` +
        `'${reviewableSha}', the newest commit touching reviewable code (reviewers read a stale diff)`,
    );
  }
  for (const r of data.reviewers || []) {
    if (r && r.verdict === "block")
      gaps.push(
        `reviewer-pipeline.json: reviewer '${r.role}' returned verdict: block (resolve before Stop)`,
      );
  }
  return gaps;
}

function fmtTs(ms) {
  return ms ? new Date(ms).toISOString() : "<unknown>";
}

function runMain() {
  // Read & discard stdin payload (Stop event fields not strictly required for the gate logic).
  let payload = {};
  try {
    const raw = fs.readFileSync(0, "utf8");
    if (raw) payload = JSON.parse(raw);
  } catch {
    /* fail-open */
  }
  const sessionId = payload.session_id || "unknown";

  if (process.env.CERES_SKIP_EVIDENCE_BUNDLE_HOOK === "1") {
    process.stderr.write(
      "verify-stage-completeness — evidence-bundle hook bypassed via CERES_SKIP_EVIDENCE_BUNDLE_HOOK=1\n",
    );
    appendLog(LOG_PATH, {
      ts: new Date().toISOString(),
      sessionId,
      outcome: "bypassed",
      gaps: [],
      stageId: null,
    });
    process.exit(0);
  }

  // Scope guard: the gate is about code THIS turn shipped. A turn that wrote no
  // tracked file (read-only Q&A) is not gated, regardless of what HEAD~1..HEAD holds.
  if (!sessionWroteCode(PROJECT_DIR, sessionId)) {
    appendLog(LOG_PATH, {
      ts: new Date().toISOString(),
      sessionId,
      outcome: "skipped:no-session-writes",
      gaps: [],
      stageId: null,
    });
    process.exit(0);
  }

  const turnStart = getTurnStart();
  const turnStartSha = getTurnStartSha(turnStart, sessionId);

  const changedFiles = getChangedFiles(sessionId, turnStartSha);
  if (changedFiles.length === 0) {
    appendLog(LOG_PATH, {
      ts: new Date().toISOString(),
      sessionId,
      outcome: "skipped:no-turn-changes",
      gaps: [],
      stageId: null,
    });
    process.exit(0);
  }

  const { stageId, source: stageSource } = getStageId(turnStartSha);
  if (!stageId) {
    appendLog(ORPHAN_LOG, {
      ts: new Date().toISOString(),
      sessionId,
      outcome: "orphan:no-stage-id",
      changedFiles,
    });
    process.stderr.write(
      "verify-stage-completeness — no stage-id in recent commit messages; not gating non-stage commits.\n",
    );
    process.exit(0);
  }

  // Scope to THIS stage's own files BEFORE the docs-only decision. getChangedFiles
  // unions the session write-tracker, a session-lifetime floor that on a spec-only
  // turn opening a NEW stage still carries the PREVIOUS stage's .cs writes (verified
  // 2026-08-27: a docs(12.6) spec turn still saw ISupportTicketService.cs from the
  // 12.5 work). Deciding docs-only on that unscoped set made a pure spec commit look
  // like a code turn. filesForStage falls back to the wide set when the stage owns
  // no commits, so nothing that should be gated stops being gated.
  const stageScopedFiles = filesForStage(changedFiles, turnStartSha, stageId);
  const codeFiles = stageScopedFiles
    .filter((f) => !DOCS_CONFIG_RE.test(f))
    .filter((f) => !isTriviallyChanged(f, turnStartSha));
  if (codeFiles.length === 0) {
    appendLog(LOG_PATH, {
      ts: new Date().toISOString(),
      sessionId,
      outcome: "skipped:docs-only",
      gaps: [],
      stageId,
    });
    process.exit(0);
  }

  // A stage id inferred from history is a guess: the turn committed nothing, so
  // the newest stage-scoped commit may belong to unrelated earlier work. Gating
  // on it demands fresh evidence for a stage this turn never touched — which is
  // exactly how CSRF work was billed to stage 12.1 on 2026-08-21. Report, don't block.
  if (stageSource === "history") {
    appendLog(LOG_PATH, {
      ts: new Date().toISOString(),
      sessionId,
      outcome: "skipped:stage-inferred-from-history",
      gaps: [],
      stageId,
    });
    process.stderr.write(
      `verify-stage-completeness — this turn committed nothing; stage-${stageId} was inferred from ` +
        `earlier history, so the evidence bundle is not gated. Commit with a stage scope ` +
        `(e.g. "fix(${stageId}): ...") to enable the gate.\n`,
    );
    process.exit(0);
  }

  const bundleDir = path.join(EVIDENCE_DIR, `stage-${stageId}`);

  // Narrow to this stage's own files before choosing slots. The turn window is the
  // newest commit OLDER than the turn marker, so on a long session it reaches back
  // across earlier finished work and demands evidence already produced for THOSE
  // stages — a migration-only turn was asked for Playwright traces on 2026-08-23
  // because the window still held stage 12.1's SessionsPage.
  // codeFiles is already stage-scoped (above, before the docs-only gate).
  const required = SLOT_TABLE.filter((s) => s.when(codeFiles));
  const gaps = [];

  for (const { slot, reason } of required) {
    const full = path.join(bundleDir, slot);
    let st;
    try {
      st = fs.statSync(full);
    } catch {
      gaps.push(`Missing: ${slot} (required because ${reason})`);
      continue;
    }
    if (turnStart && st.mtimeMs <= turnStart) {
      gaps.push(
        `Stale: ${slot} (mtime ${fmtTs(st.mtimeMs)}, turn-start ${fmtTs(turnStart)})`,
      );
    }
  }

  const shapePath = path.join(bundleDir, "turn-shape.json");
  if (fs.existsSync(shapePath)) gaps.push(...validateTurnShape(shapePath));

  const reviewerPath = path.join(bundleDir, "reviewer-pipeline.json");
  if (
    required.some((s) => s.slot === "reviewer-pipeline.json") &&
    fs.existsSync(reviewerPath)
  ) {
    const headSha = safeExec("git rev-parse HEAD 2>/dev/null");
    gaps.push(...validateReviewerPipeline(reviewerPath, headSha));
  }

  if (gaps.length === 0) {
    appendLog(LOG_PATH, {
      ts: new Date().toISOString(),
      sessionId,
      outcome: "allowed",
      gaps: [],
      stageId,
    });
    process.exit(0);
  }

  // Escapability guard: a Stop hook fires AFTER the turn's tool calls, so the
  // agent cannot satisfy these slots from inside a blocked turn. Identical
  // gaps repeating means the block is unsatisfiable — escalate, don't loop.
  const fingerprint = gapsFingerprint(gaps);
  if (
    strikeCount(PROJECT_DIR, sessionId, stageId, fingerprint) >= MAX_STRIKES
  ) {
    appendLog(LOG_PATH, {
      ts: new Date().toISOString(),
      sessionId,
      outcome: "escalated",
      gaps,
      stageId,
    });
    process.stderr.write(
      `verify-stage-completeness — stage-${stageId} evidence bundle still incomplete after ` +
        `${MAX_STRIKES} identical blocks; escalating to a warning so the turn can end.\n` +
        gaps.map((g) => `  ✗ ${g}`).join("\n") +
        `\n\nThe debt is real and unpaid — resolve it at the next stage-${stageId} commit.\n`,
    );
    process.exit(0);
  }
  recordStrike(PROJECT_DIR, sessionId, stageId, fingerprint);

  appendLog(LOG_PATH, {
    ts: new Date().toISOString(),
    sessionId,
    outcome: "blocked",
    gaps,
    stageId,
  });

  const bundleRel = path.relative(PROJECT_DIR, bundleDir) + "/";
  const lines = [
    "verify-stage-completeness — evidence bundle audit:",
    "",
    `Stage: ${stageId}`,
    `Bundle: ${bundleRel}`,
    "",
    ...gaps.map((g) => `✗ ${g}`),
    "",
    `${gaps.length} gap(s) found. Stop blocked.`,
    "",
    "Recovery:",
    `  • Run tools/agent-env/up.sh; then APP_URL=... STAGE_ID=${stageId} pnpm --dir ProjectCeres.Client agent-walk`,
    `  • Run psql -U ceres_app -d project_ceres -c '\\dp public.*' > ${bundleRel}rls-audit.psql`,
    "  • Apply the fix you mentioned in the same turn it's mentioned, OR add a [ ] line in",
    "    the active batch stage and rerun turn-shape generation.",
    `  • If the diff touches auth/migrations/IUserOwned models: dispatch the 3 reviewers`,
    `    (reviewer-writer, reviewer-security, reviewer-playwright-test-audit) and write their`,
    `    verdicts to ${bundleRel}reviewer-pipeline.json (diff_sha = current HEAD, no surviving block).`,
    "",
    "Or bypass: CERES_SKIP_EVIDENCE_BUNDLE_HOOK=1 (logs as bypassed, doesn't block).",
    "",
  ];
  process.stderr.write(lines.join("\n"));
  process.exit(2);
}

if (require.main === module) {
  runMain();
}

if (typeof module !== "undefined" && module.exports) {
  module.exports = {
    validateReviewerPipeline,
    diffIsTrivial,
    isTriviallyChanged,
    porcelainPath,
    SLOT_TABLE,
    sessionWroteCode,
    sessionWrittenFiles,
    getChangedFiles,
    getStageId,
    getTurnStartSha,
    gapsFingerprint,
    normalizeGap,
    strikeCount,
    recordStrike,
    MAX_STRIKES,
  };
}
