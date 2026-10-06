#!/usr/bin/env node
// Stop hook — enforces that declining to fix a KNOWN defect is explicit and
// justified, via a marker convention (NOT prose scanning).
//
// WHY (2026-10-06): the rule — "the age of a defect is irrelevant to whether it
// gets fixed when you are touching the file" (deep-fix-mode anti-pattern list +
// feedback_no_flag_without_action) — was talked past in prose. The first version
// of this hook SCANNED prose for "pre-existing / not mine" + a refusal verb. It
// then false-positived on the assistant's own META-DISCUSSION of the rule (a reply
// that quoted the forbidden phrase while explaining it). That is the exact limit
// documented in stop-image-claim-detect.js's header: a prose matcher "loose enough
// to catch real findings, but tight enough to skip prose that contains the same
// words" cannot be reconciled in regex.
//
// REWRITE — convention, not prose (mirrors the image-hook's own fix):
// When the assistant declines to fix a known defect this turn, it MUST wrap the
// decision in an explicit marker:
//
//   <defer-defect reason="tooling-gap">
//     <the defect> — <the specific missing tool/dep/infra, with evidence>
//   </defer-defect>
//
//   <defer-defect reason="already-scheduled">
//     <the defect> — <the receiving stage + its `[ ]` line that covers this>
//   </defer-defect>
//
// The ONLY two valid reasons are "tooling-gap" and "already-scheduled" (the
// no-unjustified-deferrals pair). The hook blocks the Stop event when a
// <defer-defect> block is present with a missing/invalid reason, or with a reason
// but no substantiating body. Prose that merely discusses deflection — or quotes
// the forbidden phrase while explaining the rule — contains no marker and never
// trips. Deciding NOT to fix a defect without any marker is still forbidden by the
// CLAUDE.md rule + deep-fix-mode; this hook mechanically catches the marker misuse,
// and the absence-of-marker case is caught by review + the prose rule, not here
// (that case cannot be regex-detected without the false positives this rewrite
// removes).
//
// Bypass: CERES_SKIP_DEFLECTION_HOOK=1 in the session env.

const fs = require("fs");
const path = require("path");

const SESSION_BYPASS = process.env.CERES_SKIP_DEFLECTION_HOOK === "1";

const VALID_REASONS = new Set(["tooling-gap", "already-scheduled"]);
const MARKER_RE = /<defer-defect(\s+reason="([^"]*)")?\s*>([\s\S]*?)<\/defer-defect>/gi;

// Returns an array of problems, one per malformed <defer-defect> block. Empty
// array = no blocks, or every block is valid. Exported for tests.
function findMarkerProblems(text) {
  if (!text) return [];
  const problems = [];
  const re = new RegExp(MARKER_RE.source, "gi");
  let m;
  while ((m = re.exec(text)) !== null) {
    const reason = (m[2] || "").trim();
    const body = (m[3] || "").trim();
    if (!reason) {
      problems.push({ kind: "missing-reason", snippet: body.slice(0, 120) });
    } else if (!VALID_REASONS.has(reason)) {
      problems.push({ kind: "invalid-reason", reason, snippet: body.slice(0, 120) });
    } else if (body.length < 15) {
      // A valid reason still needs substantiation: the tool gap / the receiving line.
      problems.push({ kind: "empty-body", reason, snippet: body.slice(0, 120) });
    }
  }
  return problems;
}

function lastAssistantText(lines) {
  for (let i = (lines || []).length - 1; i >= 0; i--) {
    let entry;
    try { entry = JSON.parse(lines[i]); } catch { continue; }
    if (entry.type !== "assistant") continue;
    const msg = entry.message;
    if (msg && Array.isArray(msg.content)) {
      const text = msg.content
        .filter((c) => c && c.type === "text" && typeof c.text === "string")
        .map((c) => c.text)
        .join("\n");
      if (text) return text;
    }
  }
  return "";
}

if (typeof module !== "undefined" && module.exports) {
  module.exports = { findMarkerProblems, VALID_REASONS, MARKER_RE };
}

if (require.main === module) {
  let raw = "";
  process.stdin.on("data", (c) => (raw += c));
  process.stdin.on("end", () => {
    let input;
    try { input = JSON.parse(raw || "{}"); } catch { process.exit(0); }

    if (input.stop_hook_active) process.exit(0);
    if (SESSION_BYPASS) process.exit(0);

    const transcriptPath = input.transcript_path || "";
    if (!transcriptPath || !fs.existsSync(transcriptPath)) process.exit(0);

    let lines;
    try { lines = fs.readFileSync(transcriptPath, "utf8").trim().split("\n"); }
    catch { process.exit(0); }

    const text = lastAssistantText(lines);
    if (!text) process.exit(0);

    const problems = findMarkerProblems(text);
    const outcome = problems.length === 0 ? "passed" : "blocked";

    try {
      const stateDir = path.join(
        process.env.CLAUDE_PROJECT_DIR || process.cwd(),
        ".claude/state/deflection-detect",
      );
      fs.mkdirSync(stateDir, { recursive: true });
      fs.appendFileSync(
        path.join(stateDir, "log.jsonl"),
        JSON.stringify({
          ts: new Date().toISOString(),
          sessionId: input.session_id || "",
          hook: "deflection-detect",
          outcome,
          problems: problems.map((p) => p.kind),
        }) + "\n",
      );
    } catch { /* best-effort */ }

    if (problems.length === 0) process.exit(0);

    const reason = [
      "🛑 <defer-defect> marker with a missing or invalid justification.",
      "",
      "Declining to fix a known defect must cite one of exactly two valid reasons —",
      'reason="tooling-gap" or reason="already-scheduled" — with a substantiating body:',
      ...problems.map((p) => {
        if (p.kind === "missing-reason") return `  • missing reason=  (body: “${p.snippet}”)`;
        if (p.kind === "invalid-reason") return `  • invalid reason="${p.reason}" — only tooling-gap | already-scheduled are valid`;
        return `  • reason="${p.reason}" but the body is empty — name the tool gap OR the receiving [ ] line`;
      }),
      "",
      "The age of a defect is irrelevant to whether it gets fixed when you are touching",
      "the file. The valid exits are: FIX IT NOW, or defer with one of the two reasons",
      "above and real substantiation (the specific missing tool, or the receiving stage's",
      "`[ ]` line). 'Pre-existing' / 'not mine' / 'out of scope' is never a reason.",
      "",
      "Recovery:",
      "  • Fix the defect this turn and remove the marker.",
      '  • OR complete the marker: reason="tooling-gap" + the missing tool/evidence, or',
      '    reason="already-scheduled" + the receiving stage and its `[ ]` line.',
      "  • Session bypass (sparingly): CERES_SKIP_DEFLECTION_HOOK=1.",
      "",
      "Audit: .claude/state/deflection-detect/log.jsonl",
    ].join("\n");

    process.stderr.write(reason);
    process.exit(2);
  });
}
