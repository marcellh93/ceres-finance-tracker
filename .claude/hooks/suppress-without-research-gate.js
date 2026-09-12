#!/usr/bin/env node
// PreToolUse hook on Edit / Write / MultiEdit — HARD gate (decision: "deny").
//
// Fires when an edit INTRODUCES a warning/error/test suppression, and blocks it
// unless the edit ALSO carries evidence the underlying cause was researched and a
// root-cause fix was weighed first. The one rule it enforces:
//
//   A compiler warning, analyzer diagnostic, failing test, or flake is a DEFECT
//   SIGNAL. You may only silence it after researching the cause and deciding —
//   on the evidence — that silencing (not fixing) is correct. "It's a safe,
//   intentional pattern" asserted WITHOUT that research is exactly the reflex
//   this gate exists to stop.
//
// Why this exists (2026-09-12): asked to clear a CS9107 warning, the assistant
// steered straight to `<NoWarn>CS9107</NoWarn>` / a scoped severity=none,
// rationalizing the flagged pattern as "safe, codebase-wide" without researching
// what CS9107 actually warns about (a real double-capture hazard with an
// idiomatic fix). The user called this out: "if the warning is firing, the
// pattern may not be good, and being stubborn about not addressing it helps no
// one." This gate makes that reflex cost a deliberate override, not a silent slip.
//
// It is NOT a ban on suppression. Suppression is sometimes right (a genuine false
// positive, a third-party quirk). It is a ban on suppression AS THE FIRST MOVE,
// before the cause is understood. The escape hatch is cheap and honest: state the
// research + why fixing is worse than silencing, in the same edit or the turn.

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "suppress-gate");

// Patterns that INTRODUCE a suppression. Each is a defect-signal being silenced.
const SUPPRESSION_PATTERNS = [
  { re: /\bNoWarn\b/, what: "MSBuild <NoWarn> (silences a compiler/analyzer warning)" },
  { re: /#pragma\s+warning\s+disable/, what: "#pragma warning disable" },
  { re: /dotnet_diagnostic\.[A-Z0-9]+\.severity\s*=\s*(none|silent)/i, what: "analyzer severity = none/silent in .editorconfig" },
  { re: /\[\s*SuppressMessage/, what: "[SuppressMessage] attribute" },
  { re: /\[\s*Fact\s*\(\s*Skip\s*=/, what: "[Fact(Skip=...)] (skips a test)" },
  { re: /\[\s*Theory\s*\(\s*Skip\s*=/, what: "[Theory(Skip=...)] (skips a test)" },
  { re: /\bit\.skip\b|\btest\.skip\b|\bdescribe\.skip\b|\.skip\(/, what: "a skipped JS test (.skip)" },
  { re: /eslint-disable/, what: "eslint-disable (silences a lint rule)" },
  { re: /@ts-(ignore|expect-error|nocheck)/, what: "@ts-ignore / @ts-expect-error / @ts-nocheck" },
  { re: /dangerouslyIgnoreUnhandledErrors/, what: "dangerouslyIgnoreUnhandledErrors (hides Vitest unhandled errors)" },
  { re: /biome-ignore/, what: "biome-ignore" },
];

// Evidence in the SAME edit text that research/root-cause reasoning accompanies the
// suppression. If any of these is present the edit passes — the author has shown work.
// (Kept generous: the goal is to force a documented reason, not to grade its prose.)
const RESEARCH_EVIDENCE = [
  /root[\s-]?cause/i,
  /false[\s-]?positive/i,
  /researched|investigat/i,
  /verified|reproduc/i,
  /why (fixing|the fix) (is|would)/i,
  /intentional[\s\S]{0,80}(because|since|verified|proven)/i,
  /see (docs|http|https|ADR|the .* study)/i,
  /quarantin/i, // an explicit quarantine-with-ticket is the sanctioned path
  /trade[\s-]?off/i,
];

// A separate, stronger signal: a tracked owed-fix line accompanying the suppression
// (a roadmap [ ], a TODO with a ticket, an owed-fix reference). Presence = quarantine.
const OWED_FIX = [
  /-\s*\[\s*\]/, // a markdown [ ] checkbox in the edit
  /TODO\([^)]+\)/, // TODO(owner/ticket)
  /owed fix|tracked (at|in)|follow[\s-]?up (line|ticket|\[ \])/i,
];

function introducesSuppression(text) {
  if (!text || typeof text !== "string") return null;
  for (const p of SUPPRESSION_PATTERNS) if (p.re.test(text)) return p.what;
  return null;
}

function hasResearchEvidence(text) {
  if (!text || typeof text !== "string") return false;
  return RESEARCH_EVIDENCE.some((re) => re.test(text)) || OWED_FIX.some((re) => re.test(text));
}

// The text being ADDED by this edit (not the whole file). For Edit: new_string;
// MultiEdit: all edits' new_string; Write: content. We compare against old text
// where available so we only flag NEWLY-introduced suppressions, not edits to a
// file that already had one.
function addedText(input) {
  const ti = input.tool_input || {};
  if (input.tool_name === "Write") return { added: ti.content || "", removed: "" };
  if (input.tool_name === "Edit") return { added: ti.new_string || "", removed: ti.old_string || "" };
  if (input.tool_name === "MultiEdit") {
    const edits = Array.isArray(ti.edits) ? ti.edits : [];
    return {
      added: edits.map((e) => e.new_string || "").join("\n"),
      removed: edits.map((e) => e.old_string || "").join("\n"),
    };
  }
  return { added: "", removed: "" };
}

module.exports = { introducesSuppression, hasResearchEvidence, addedText };

if (require.main !== module) return;

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  if (!["Edit", "Write", "MultiEdit"].includes(input.tool_name)) process.exit(0);

  const { added, removed } = addedText(input);
  const suppression = introducesSuppression(added);
  if (!suppression) process.exit(0);

  // Only flag a NEWLY-introduced suppression: if the same suppression construct was
  // already in the replaced text, this edit is modifying around it, not adding it.
  if (introducesSuppression(removed)) process.exit(0);

  // Research/quarantine evidence present in the same edit → allow. Show your work, pass.
  if (hasResearchEvidence(added)) process.exit(0);

  const reason = [
    `suppress-without-research-gate: this edit introduces ${suppression},`,
    "which silences a defect signal (a warning / analyzer rule / failing test / flake).",
    "",
    "Blocked because the edit carries no evidence the cause was researched first.",
    "A firing diagnostic is the tool telling you something — silencing it before you",
    "understand it is the exact reflex this gate stops (see docs/testing-flakiness.md",
    "§ 7 and docs/testing.md § Flaky tests).",
    "",
    "To proceed, do ONE of:",
    "  1. Fix the ROOT CAUSE instead of suppressing (usually the right answer — e.g.",
    "     CS9107's idiomatic fix is one source of truth, not <NoWarn>).",
    "  2. If suppression is genuinely correct (a real false positive / third-party",
    "     quirk), say so IN THE EDIT: name the root cause you found, why fixing is",
    "     worse than silencing, and a link/reference (root-cause, false-positive,",
    "     verified, see <doc/URL>, or a tracked owed-fix [ ] line for a quarantine).",
    "",
    "Research the cause, then re-issue the edit with that reasoning included.",
  ].join("\n");

  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: "PreToolUse",
        permissionDecision: "deny",
        permissionDecisionReason: reason,
      },
    })
  );
  process.exit(0);
});
