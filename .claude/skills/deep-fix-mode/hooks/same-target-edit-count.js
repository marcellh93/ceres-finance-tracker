#!/usr/bin/env node
// PostToolUse hook (Edit/Write) — counts DISTINCT edits per file per session.
//
// Two false-positive failure modes the v1 hook produced and v2 fixes:
//
//   1. Planned multi-edit sweeps on docs (doc moves, find-and-replace across
//      sections, large file polish). Each edit targets a different line; none
//      revisit the same content. The v1 hook fired at the same count regardless,
//      pressuring the agent to invoke deep-fix-mode on monotonically-converging
//      work. Fix: count only DISTINCT new_string hashes — repeated content is
//      what circling looks like, not repeated file paths.
//
//   2. Source-code files (.cs/.tsx/.ts) and doc files (.md/.json) have wildly
//      different "normal" edit counts. A spec being polished can land 8 edits
//      cleanly; a service class being thrashed at 8 edits is real Fixation.
//      Fix: tiered thresholds by extension.
//
// Advisory tone softened: drop "MANDATORY" imperative anchor. The advisory is a
// self-check prompt, not a command. The deep-fix-mode skill itself will reject
// false positives in step 2 (table-row gate).

const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

// Tiered thresholds: docs forgive multi-section edits; source code is stricter.
// Threshold is the count at which the ADVISORY fires; HARD_NUDGE is +3.
const DOC_EXTENSIONS = new Set([".md", ".json", ".yml", ".yaml", ".toml", ".txt"]);
const ADVISORY_AT_DOC = 10;
const ADVISORY_AT_CODE = 5;

const STATE_DIR = path.join(
  process.env.CLAUDE_PROJECT_DIR || process.cwd(),
  ".claude",
  "state",
  "deep-fix-mode"
);

function ensureDir(p) {
  try {
    fs.mkdirSync(p, { recursive: true });
  } catch {}
}

function hashContent(s) {
  return crypto.createHash("sha256").update(s || "").digest("hex").slice(0, 16);
}

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  const sessionId = input.session_id;
  const toolName = input.tool_name;
  const toolInput = input.tool_input || {};
  const filePath = toolInput.file_path;

  if (!sessionId || !filePath || (toolName !== "Edit" && toolName !== "Write")) {
    process.exit(0);
  }

  const ext = path.extname(filePath).toLowerCase();
  const advisoryAt = DOC_EXTENSIONS.has(ext) ? ADVISORY_AT_DOC : ADVISORY_AT_CODE;
  const hardNudgeAt = advisoryAt + 3;

  // Fingerprint the edit by its new_string hash so distinct edits stay distinct.
  // For Write, that's the full content; for Edit, the replacement string.
  const editFingerprint = hashContent(
    toolName === "Write" ? toolInput.content : toolInput.new_string
  );

  ensureDir(STATE_DIR);
  const stateFile = path.join(STATE_DIR, `${sessionId}-edits.json`);

  // State shape: { perFile: { "<path>": { totalEdits: N, distinctFingerprints: [hash, ...] } } }
  let state = { perFile: {} };
  try {
    state = JSON.parse(fs.readFileSync(stateFile, "utf8"));
    if (!state.perFile) state.perFile = {};
  } catch {}

  const entry = state.perFile[filePath] || { totalEdits: 0, distinctFingerprints: [] };
  entry.totalEdits += 1;
  if (!entry.distinctFingerprints.includes(editFingerprint)) {
    entry.distinctFingerprints.push(editFingerprint);
  }
  // Keep memory bounded.
  if (entry.distinctFingerprints.length > 50) {
    entry.distinctFingerprints = entry.distinctFingerprints.slice(-50);
  }
  state.perFile[filePath] = entry;

  try {
    fs.writeFileSync(stateFile, JSON.stringify(state));
  } catch {}

  // Detection signal: a high RATIO of total edits to distinct edits is the
  // real Fixation indicator. 10 edits across 10 fingerprints is convergent
  // sweep; 10 edits across 2 fingerprints is circling on the same content.
  const total = entry.totalEdits;
  const distinct = entry.distinctFingerprints.length;
  const repetitionRatio = distinct === 0 ? 0 : total / distinct;

  // Suppress entirely when edits are diverging (each edit lands different content).
  // The hook only fires when BOTH total crosses the threshold AND repetition is
  // present (ratio > 1.5, i.e. on average the same content is edited 1.5×).
  if (total < advisoryAt) process.exit(0);
  if (repetitionRatio <= 1.5) process.exit(0);

  const hard = total >= hardNudgeAt;
  const fileKind = DOC_EXTENSIONS.has(ext) ? "doc" : "source";
  const msg = hard
    ? [
        `🔁 ${total} edits to ${filePath} (${distinct} distinct) — repetition ratio ${repetitionRatio.toFixed(1)}× on a ${fileKind} file.`,
        "",
        "The same content fingerprint is reappearing across edits — that's Fixation (Zhou et al. 2026, CB6). When monotonic doc-sweeps and refactor passes trip this, they typically have distinct fingerprints per edit (ratio ≈ 1.0); a ratio above 1.5 indicates the same block is being rewritten.",
        "",
        "Consider invoking `deep-fix-mode` to do the layer-naming exercise. If you're confident this is a planned sweep with content drift, name that explicitly in the next reply and continue — the skill's step-2 table check will accept that exit reason.",
      ].join("\n")
    : [
        `📝 ${total} edits to ${filePath} (${distinct} distinct, ${repetitionRatio.toFixed(1)}× repetition).`,
        "",
        "Self-check: are you converging or circling? Distinct-content edits look like a sweep; same-block re-edits look like Fixation. If the original symptom is still present, the root cause is likely outside this file.",
        "",
        "Not blocking — advisory only.",
      ].join("\n");

  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: { hookEventName: "PostToolUse", additionalContext: msg },
    })
  );
  process.exit(0);
});
