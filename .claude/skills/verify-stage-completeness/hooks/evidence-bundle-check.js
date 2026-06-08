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
const TURN_MARKER = path.join(PROJECT_DIR, ".claude", "state", "run-tests", "last.log");

// Diff-shape → required slots. Each predicate runs on the list of changed files.
const SLOT_TABLE = [
  {
    slot: "walk-summary.json",
    when: (files) => files.some((f) => /^ProjectCeres\.Client\/src\//.test(f) || /^ProjectCeres\/Controllers\//.test(f)),
    reason: "diff touches user-facing surface (Client/src or Controllers)",
  },
  {
    slot: "trace.zip",
    when: (files) => files.some((f) => /^ProjectCeres\.Client\/src\//.test(f) || /^ProjectCeres\/Controllers\//.test(f)),
    reason: "diff touches user-facing surface (Client/src or Controllers)",
  },
  {
    slot: "console.json",
    when: (files) => files.some((f) => /^ProjectCeres\.Client\/src\//.test(f) || /^ProjectCeres\/Controllers\//.test(f)),
    reason: "diff touches user-facing surface (Client/src or Controllers)",
  },
  {
    slot: "network.json",
    when: (files) => files.some((f) => /^ProjectCeres\.Client\/src\//.test(f) || /^ProjectCeres\/Controllers\//.test(f)),
    reason: "diff touches user-facing surface (Client/src or Controllers)",
  },
  {
    slot: "curl-transcript.txt",
    when: (files) => files.some((f) => /^ProjectCeres\/Controllers\//.test(f) || /^ProjectCeres\/Endpoints\//.test(f)),
    reason: "diff touches HTTP surface (Controllers or Endpoints)",
  },
  {
    slot: "rls-audit.psql",
    when: (files) => files.some((f) => /^ProjectCeres\/Models\//.test(f) || /^ProjectCeres\/Migrations\//.test(f)),
    reason: "diff touches Models (IUserOwned entities) or Migrations",
  },
  {
    slot: "build-matrix.json",
    when: () => true,
    reason: "always required when code changes",
  },
  {
    slot: "registry-sweep.json",
    when: (files) => files.some((f) =>
      /^ProjectCeres\/Models\//.test(f) ||
      /^ProjectCeres\/Resources\//.test(f) ||
      /^ProjectCeres\/Common\/Email\/EmailTemplateKey\.cs$/.test(f) ||
      /^ProjectCeres\/Models\/AuditLog\.cs$/.test(f)
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
    when: (files) => files.some((f) =>
      /^ProjectCeres\/Common\/Authentication\//.test(f) ||
      /^ProjectCeres\/Migrations\//.test(f) ||
      /^ProjectCeres\/Models\//.test(f)),
    reason: "diff touches auth, migrations, or IUserOwned models — 3-agent reviewer pipeline required",
  },
];

const DOCS_CONFIG_RE = /^(docs\/|\.claude\/|README\.md$|\.gitignore$|CHANGELOG\.md$|.*\.md$)/;

function safeExec(cmd) {
  try { return cp.execSync(cmd, { cwd: PROJECT_DIR, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] }).trim(); }
  catch { return ""; }
}

function appendLog(file, entry) {
  try {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.appendFileSync(file, JSON.stringify(entry) + "\n");
  } catch { /* fail-open */ }
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

function getChangedFiles() {
  const out = safeExec("git diff --name-only HEAD~1 HEAD 2>/dev/null");
  return out ? out.split("\n").filter(Boolean) : [];
}

function getStageId() {
  // Try the most recent commit first, then walk back up to 5 commits.
  for (let i = 0; i < 5; i++) {
    const msg = safeExec(`git log -1 --skip=${i} --format=%B 2>/dev/null`);
    if (!msg) continue;
    const m = msg.match(/^(?:feat|fix|chore|refactor|test|docs|perf|build|ci|style)\(([0-9]+(?:[.\w-]+)?)\):/m);
    if (m) return m[1];
  }
  return null;
}

function validateTurnShape(shapePath) {
  const gaps = [];
  let shape;
  try { shape = JSON.parse(fs.readFileSync(shapePath, "utf8")); }
  catch (e) { return [`turn-shape.json: invalid JSON (${e.message})`]; }
  if (!shape || typeof shape !== "object") return ["turn-shape.json: not an object"];
  for (const arrName of ["fix_mentions", "confidence_claims", "runtime_assertions"]) {
    const arr = shape[arrName];
    if (!Array.isArray(arr)) { gaps.push(`turn-shape.json: missing/invalid '${arrName}' array`); continue; }
    arr.forEach((entry, i) => {
      const snip = (entry && entry.snippet) ? `"${String(entry.snippet).slice(0, 60)}..."` : `[${i}]`;
      if (arrName === "fix_mentions" && entry.co_located_edit !== true)
        gaps.push(`turn-shape.json: fix_mentions[${i}] has co_located_edit: false (${snip} had no Edit in this turn)`);
      if (arrName === "confidence_claims" && entry.co_located_research !== true)
        gaps.push(`turn-shape.json: confidence_claims[${i}] has co_located_research: false (${snip})`);
      if (arrName === "runtime_assertions") {
        if (entry.co_located_query !== true)
          gaps.push(`turn-shape.json: runtime_assertions[${i}] has co_located_query: false (${snip})`);
        else if (!entry.query_output || !fs.existsSync(path.join(PROJECT_DIR, entry.query_output)))
          gaps.push(`turn-shape.json: runtime_assertions[${i}].query_output missing on disk (${entry.query_output || "<unset>"})`);
      }
    });
  }
  return gaps;
}

function validateReviewerPipeline(slotPath, headSha) {
  const gaps = [];
  let data;
  try { data = JSON.parse(fs.readFileSync(slotPath, "utf8")); }
  catch (e) { return [`reviewer-pipeline.json: invalid JSON (${e.message})`]; }
  if (!data || typeof data !== "object") return ["reviewer-pipeline.json: not an object"];

  const roles = Array.isArray(data.reviewers) ? data.reviewers.map((r) => r && r.role) : [];
  for (const required of ["writer", "security", "playwright-test-audit"]) {
    if (!roles.includes(required)) gaps.push(`reviewer-pipeline.json: missing reviewer role '${required}'`);
  }
  if (headSha && data.diff_sha !== headSha) {
    gaps.push(`reviewer-pipeline.json: diff_sha '${data.diff_sha || "<unset>"}' does not match HEAD '${headSha}' (reviewers read a stale diff)`);
  }
  for (const r of (data.reviewers || [])) {
    if (r && r.verdict === "block") gaps.push(`reviewer-pipeline.json: reviewer '${r.role}' returned verdict: block (resolve before Stop)`);
  }
  return gaps;
}

function fmtTs(ms) { return ms ? new Date(ms).toISOString() : "<unknown>"; }

function runMain() {
  // Read & discard stdin payload (Stop event fields not strictly required for the gate logic).
  let payload = {};
  try {
    const raw = fs.readFileSync(0, "utf8");
    if (raw) payload = JSON.parse(raw);
  } catch { /* fail-open */ }
  const sessionId = payload.session_id || "unknown";

  if (process.env.CERES_SKIP_EVIDENCE_BUNDLE_HOOK === "1") {
    process.stderr.write("verify-stage-completeness — evidence-bundle hook bypassed via CERES_SKIP_EVIDENCE_BUNDLE_HOOK=1\n");
    appendLog(LOG_PATH, { ts: new Date().toISOString(), sessionId, outcome: "bypassed", gaps: [], stageId: null });
    process.exit(0);
  }

  const changedFiles = getChangedFiles();
  if (changedFiles.length === 0) {
    appendLog(LOG_PATH, { ts: new Date().toISOString(), sessionId, outcome: "skipped:no-commit", gaps: [], stageId: null });
    process.exit(0);
  }

  const codeFiles = changedFiles.filter((f) => !DOCS_CONFIG_RE.test(f));
  if (codeFiles.length === 0) {
    appendLog(LOG_PATH, { ts: new Date().toISOString(), sessionId, outcome: "skipped:docs-only", gaps: [], stageId: null });
    process.exit(0);
  }

  const stageId = getStageId();
  if (!stageId) {
    appendLog(ORPHAN_LOG, { ts: new Date().toISOString(), sessionId, outcome: "orphan:no-stage-id", changedFiles });
    process.stderr.write("verify-stage-completeness — no stage-id in recent commit messages; not gating non-stage commits.\n");
    process.exit(0);
  }

  const bundleDir = path.join(EVIDENCE_DIR, `stage-${stageId}`);
  const turnStart = getTurnStart();
  const required = SLOT_TABLE.filter((s) => s.when(codeFiles));
  const gaps = [];

  for (const { slot, reason } of required) {
    const full = path.join(bundleDir, slot);
    let st;
    try { st = fs.statSync(full); }
    catch { gaps.push(`Missing: ${slot} (required because ${reason})`); continue; }
    if (turnStart && st.mtimeMs <= turnStart) {
      gaps.push(`Stale: ${slot} (mtime ${fmtTs(st.mtimeMs)}, turn-start ${fmtTs(turnStart)})`);
    }
  }

  const shapePath = path.join(bundleDir, "turn-shape.json");
  if (fs.existsSync(shapePath)) gaps.push(...validateTurnShape(shapePath));

  const reviewerPath = path.join(bundleDir, "reviewer-pipeline.json");
  if (required.some((s) => s.slot === "reviewer-pipeline.json") && fs.existsSync(reviewerPath)) {
    const headSha = safeExec("git rev-parse HEAD 2>/dev/null");
    gaps.push(...validateReviewerPipeline(reviewerPath, headSha));
  }

  const outcome = gaps.length === 0 ? "allowed" : "blocked";
  appendLog(LOG_PATH, { ts: new Date().toISOString(), sessionId, outcome, gaps, stageId });

  if (gaps.length === 0) process.exit(0);

  const stage = `stage-${stageId}`;
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
  module.exports = { validateReviewerPipeline, SLOT_TABLE };
}
