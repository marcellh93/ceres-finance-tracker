#!/usr/bin/env node
// Stop hook — blocks the Stop event when the assistant emits an explicit
// <image-finding> marker block WITHOUT having Read the cited image file in
// the same turn.
//
// 2026-05-22 REWRITE — convention-scanning, not prose-scanning.
//
// The prior version scanned assistant prose for surface verbs ("overflows",
// "is missing", "F1 — ...") combined with bare numeric tokens (#NN) as a
// proxy for image-grounded findings. That approach had a 100% production
// misfire rate (see docs/hook-architecture-audit-2026-05-22.md). The
// fundamental tension: the matcher had to be loose enough to catch real
// findings, but tight enough to skip backend prose that contains the same
// words. Those two requirements cannot be reconciled in regex.
//
// New rule: when the assistant intends to make image findings, it wraps them
// in an explicit marker block:
//
//   <image-finding image="/abs/path/to/image.png">
//     F1 — the OTP cell at the right edge clips the card padding.
//   </image-finding>
//
// Each marker block declares the image path. The hook checks that the same
// turn's tool trail contains a Read tool_use with file_path === the declared
// image path. If yes, pass. If no, block with a clear deny reason naming the
// specific image path that was claimed but not Read.
//
// Bypass: CERES_SKIP_IMAGE_CLAIMS_HOOK=1 in the session env.
//
// Soft-delete option: the hook can be removed from .claude/settings.json
// entirely; the SKILL.md prose still documents the convention.

const fs = require("fs");
const path = require("path");

const MARKER_RE = /<image-finding\s+image="([^"]+)"\s*>([\s\S]*?)<\/image-finding>/gi;
const SESSION_BYPASS = process.env.CERES_SKIP_IMAGE_CLAIMS_HOOK === "1";

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
  if (SESSION_BYPASS) process.exit(0);

  const transcriptPath = input.transcript_path || "";
  if (!transcriptPath || !fs.existsSync(transcriptPath)) process.exit(0);

  let lines;
  try {
    lines = fs.readFileSync(transcriptPath, "utf8").trim().split("\n");
  } catch {
    process.exit(0);
  }

  let lastAssistantText = "";
  let lastAssistantIdx = -1;
  let lastUserIdx = -1;

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
        if (text) {
          lastAssistantText = text;
          lastAssistantIdx = i;
        }
      }
    }
    if (entry.type === "user" && lastUserIdx === -1) {
      lastUserIdx = i;
    }
    if (lastAssistantText && lastUserIdx >= 0) break;
  }

  if (!lastAssistantText) process.exit(0);

  // Extract all <image-finding> marker blocks from the assistant text.
  const claimed = [];
  let m;
  MARKER_RE.lastIndex = 0;
  while ((m = MARKER_RE.exec(lastAssistantText)) !== null) {
    claimed.push({ imagePath: m[1].trim(), findingSnippet: m[2].trim().slice(0, 200) });
  }
  if (claimed.length === 0) process.exit(0);

  // For each claimed image, scan the turn's tool trail for a Read on the
  // same absolute path. The Read tool_use is serialized as JSON; check both
  // the tool name and the file_path argument.
  const readPaths = new Set();
  if (lastUserIdx >= 0 && lastAssistantIdx >= 0) {
    for (let i = lastUserIdx + 1; i <= lastAssistantIdx; i++) {
      const blob = lines[i] || "";
      if (!/"name"\s*:\s*"Read"/.test(blob)) continue;
      const pathMatch = blob.match(/"file_path"\s*:\s*"([^"]+)"/);
      if (pathMatch) readPaths.add(pathMatch[1]);
    }
  }

  const missing = claimed.filter((c) => !readPaths.has(c.imagePath));
  const outcome = missing.length === 0 ? "passed" : "blocked";

  try {
    const stateDir = path.join(
      process.env.CLAUDE_PROJECT_DIR || process.cwd(),
      ".claude/state/image-claims-verify",
    );
    fs.mkdirSync(stateDir, { recursive: true });
    fs.appendFileSync(
      path.join(stateDir, "log.jsonl"),
      JSON.stringify({
        ts: new Date().toISOString(),
        sessionId: input.session_id || "",
        hook: "image-claim-detect",
        outcome,
        claimedCount: claimed.length,
        missingCount: missing.length,
        missingPaths: missing.map((c) => c.imagePath),
      }) + "\n",
    );
  } catch {
    /* best-effort */
  }

  if (missing.length === 0) process.exit(0);

  const reason = [
    "🛑 Image-finding marker(s) emitted without a co-located Read of the cited image.",
    "",
    `Found ${claimed.length} <image-finding> block(s); ${missing.length} cite image(s) that were not Read this turn:`,
    ...missing.map((c) => `  • ${c.imagePath}`),
    "",
    "Per the `verify-image-claims` skill: every <image-finding> block must be paired",
    "with a Read tool_use targeting the declared image path in the same turn.",
    "",
    "Recovery options:",
    "  • Add the missing Read calls and re-emit findings anchored to what the pixels show.",
    "  • Reframe a claim as a question ('can you confirm whether X appears below the crop?')",
    "    — solicitation does not need a marker block.",
    "  • Reframe a claim as expectation ('the spec says X should render — please confirm')",
    "    — expectations do not need a marker block.",
    "  • Session bypass: set CERES_SKIP_IMAGE_CLAIMS_HOOK=1 (use sparingly).",
    "",
    "See .claude/state/image-claims-verify/log.jsonl for the audit trail.",
  ].join("\n");

  process.stderr.write(reason);
  process.exit(2);
});
