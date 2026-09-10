#!/usr/bin/env node
// PostToolUse advisory — enforces the roadmap's own "Checklist-marker & deferral rule"
// (docs/roadmap-phase-three.md, top-of-file blockquote). Fires on an Edit/Write to any
// docs/roadmap-phase-*.md and flags three mechanically-checkable inconsistencies that
// manual passes keep missing:
//
//   1. HEADING vs STATUS — a stage heading marked ✅/Done/CLOSED whose "**Status:**" line
//      says ❌/Open/Deferred (or the reverse). The three defects that prompted this hook
//      (§12.5, §12.8.1, §12.8.3) were all this shape: heading flipped, Status left stale.
//   2. [x] WITH DEFERRED/OPEN TEXT — a checked item whose text still says it is deferred /
//      not built / still open / moved elsewhere. [x] means DONE-HERE; a deferral is [→].
//   3. [→] MISSING WHY OR WHERE — a deferral marker must name BOTH the reason and the exact
//      receiving §stage that now carries the matching [ ].
//
// Advisory only (never blocks): it prints findings as additionalContext so they are seen in
// the SAME turn the edit lands. The hard gate stays the Phase E pre-stage-close check.

const ROADMAP_RE = /docs\/roadmap-phase-[a-z0-9-]+\.md$/i;

const HEADING_RE = /^#{2,4}\s+(?:Stage\s+)?\d/;            // a stage heading (## / ### with a number)
const DONE_MARK_RE = /✅|\bDone\b|\bCLOSED\b|\bShipped\b|\bFixed\b/;
// For STATUS-line verdicts, a bare "Deferred"/"Open" word IS the verdict.
const OPEN_MARK_RE = /❌|\bOpen\b|\bDeferred\b|\bPending\b|\(not scheduled\)/;
// For HEADINGS, only a status GLYPH (❌) or a parenthetical suffix counts as "open" — a bare
// word like "Deferred" in a stage TITLE ("Stage 9.1.7 — Deferred code-simplification sweeps")
// is descriptive, not a status. This avoids flagging a title that merely contains the word.
const HEADING_OPEN_RE = /❌|\((?:not scheduled|Open|Deferred|Pending)\)/;
// Matches "**Status:** ❌ Open" (verdict after the bold) and "**Status: ❌ Open.**"
// (verdict inside the bold). Capture everything after the "Status:" colon; statusVerdict()
// then trims to the leading sentence, so a trailing "**" / narrative is harmless.
const STATUS_RE = /\*\*Status:\s*(.*)$/;

function extractPath(input) {
  const ti = (input && input.tool_input) || {};
  return ti.file_path || ti.notebook_path || "";
}
function isRoadmap(file) { return !!file && ROADMAP_RE.test(file); }

// Split a Status line's leading verdict (before the first sentence's end) from its
// narrative tail, so the word "deferred" inside history ("Originally deferred 2026-06-30")
// does not read as the current verdict. The verdict is whatever sits before the first period.
function statusVerdict(statusText) {
  const firstSentence = statusText.split(/\.\s|\.\*\*|\.$/)[0] || statusText;
  return firstSentence;
}

// Does an [x] item's text betray that it is actually deferred/undone? Allow a documented
// sub-part deferral ("... the UI toggle is deferred TO Stage 12" when the item itself
// shipped) — that is legitimate iff it NAMES A DESTINATION. Flag only when the item carries
// deferral language with NO receiving home, i.e. it reads as genuinely not-done-yet-marked-x.
const X_CONTRADICTION_RE = /\b(?:is\s+deferred|remains?\s+deferred|not\s+built|still\s+open|not\s+scheduled|—\s*moved to|owed at)\b/i;
// A destination somewhere in the same item makes the deferral a homed sub-part, not a lie.
const X_HAS_DESTINATION_RE = /\b(?:deferred|moved|homed)\s+(?:to|at|under|in(?:to)?)\b|→\s*(?:Stage|§)|Stage\s+\d|§\d/i;
// A [→] deferral must carry both a why and a where.
const HAS_WHY_RE = /\bWhy:|\breason\b|user-authoris?ed|tooling gap|does not exist/i;
const HAS_WHERE_RE = /\bWhere:|→\s*(?:Stage|§)|homed (?:at|under)|receiving `?\[ \]`?|Deferred in from|lives at\b/i;

function scan(text) {
  const lines = text.split(/\r?\n/);
  const findings = [];

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];

    // (1) heading ↔ status
    if (HEADING_RE.test(line)) {
      const headingDone = DONE_MARK_RE.test(line);
      const headingOpen = HEADING_OPEN_RE.test(line);
      // find the Status line within the next 3 non-empty lines
      let status = null;
      for (let j = i + 1; j <= i + 4 && j < lines.length; j++) {
        const m = lines[j].match(STATUS_RE);
        if (m) { status = m[1]; break; }
        if (HEADING_RE.test(lines[j])) break; // hit the next heading first
      }
      if (status) {
        const verdict = statusVerdict(status);
        const statusDone = DONE_MARK_RE.test(verdict);
        const statusOpen = OPEN_MARK_RE.test(verdict);
        if (headingDone && statusOpen && !statusDone)
          findings.push(`L${i + 1}: heading says DONE but Status says open/deferred — "${verdict.trim().slice(0, 50)}"`);
        if (headingOpen && statusDone && !headingDone)
          findings.push(`L${i + 1}: heading says open/deferred but Status says DONE — reconcile the heading suffix`);
      }
    }

    // (2) [x] with deferred/open text — but only when NO destination is named (a homed
    // sub-part like "the UI toggle is deferred to Stage 12" is a legitimate [x]).
    if (/^\s*-\s*\[x\]/i.test(line) && X_CONTRADICTION_RE.test(line) && !X_HAS_DESTINATION_RE.test(line))
      findings.push(`L${i + 1}: a [x] item says it is deferred/not-built with no receiving §stage — use [→] or name where it went`);

    // (3) [→] missing why or where
    if (/^\s*-\s*\[→\]/.test(line)) {
      if (!HAS_WHY_RE.test(line)) findings.push(`L${i + 1}: [→] deferral has no WHY (reason)`);
      if (!HAS_WHERE_RE.test(line)) findings.push(`L${i + 1}: [→] deferral has no WHERE (receiving §stage / destination)`);
    }
  }
  return findings;
}

if (typeof module !== "undefined" && module.exports) {
  module.exports = { scan, isRoadmap, extractPath, statusVerdict };
}

if (require.main === module) {
  const fs = require("fs");
  let raw = "";
  process.stdin.on("data", (c) => (raw += c));
  process.stdin.on("end", () => {
    let input;
    try { input = JSON.parse(raw || "{}"); } catch { process.exit(0); }
    const file = extractPath(input);
    if (!isRoadmap(file)) process.exit(0);

    let text;
    try { text = fs.readFileSync(file, "utf8"); } catch { process.exit(0); }
    const findings = scan(text);
    if (findings.length === 0) process.exit(0);

    process.stdout.write(JSON.stringify({
      hookSpecificOutput: {
        hookEventName: "PostToolUse",
        additionalContext:
          `⚠️ roadmap-consistency-check — ${findings.length} marker/status inconsistency(ies) in ${file.split("/").pop()} ` +
          `(the roadmap's own checklist-marker & deferral rule):\n  - ${findings.join("\n  - ")}\n` +
          `Fix these in this turn — a heading, its Status line, and its [x]/[→]/[ ] markers must agree, ` +
          `and every [→] deferral must name both its Why and its Where.`,
      },
    }));
    process.exit(0);
  });
}
