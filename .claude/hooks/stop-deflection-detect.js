#!/usr/bin/env node
// Stop hook — blocks turn-end when the assistant's final message deflects a
// KNOWN defect as "pre-existing / not mine / out of scope" AND couples that to
// an explicit refusal to fix it this turn.
//
// WHY (2026-10-06): the rule already lives in prose — deep-fix-mode's anti-pattern
// list ("the age of a bug is irrelevant… labeling a bug's history is a soft
// deflection even when it's factually true") and feedback_no_flag_without_action.
// The assistant talked past it anyway, committing a message that said: "not mine
// to fix in this commit… it's an independent change, pre-existing… I'll flag it,
// not fold it in." The user has corrected this language repeatedly. Prose was not
// enough; this is the mechanical catch.
//
// DESIGN — learn from stop-image-claim-detect.js's history: a LOOSE prose scan
// (the words "pre-existing" / "out of scope" alone) had a documented 100% misfire
// rate there, because those words have legitimate uses (a real, properly-deferred
// feature; merely NAMING a bug's age while fixing it). So this hook does NOT match
// the deflection words alone. It requires the forbidden MOVE: a
// not-mine/pre-existing/out-of-scope marker CO-OCCURRING with an explicit refusal
// verb ("I'll flag it not fix", "not mine to fix", "won't touch it", "leave it
// as-is", "not fold it in") within a short window. Mentioning a bug's age while
// fixing it, or deferring a real feature WITH a cited reason + receiving line, does
// not trip it.
//
// Bypass: CERES_SKIP_DEFLECTION_HOOK=1 in the session env (use sparingly — the
// honest path is to fix the defect or state a valid deferral reason, not bypass).

const fs = require("fs");
const path = require("path");

const SESSION_BYPASS = process.env.CERES_SKIP_DEFLECTION_HOOK === "1";

// A "deflection marker": framing a thing as not-this-turn's-responsibility.
const DEFLECTION_RE =
  /\b(not mine to fix|pre-?existing|predates (?:my|this|the current)|not (?:caused by|from) this (?:commit|turn|session|work|change)|out of scope for this (?:commit|turn|fix|change)|independent (?:change|issue|bug) (?:to|in))\b/i;

// A "refusal verb": an explicit decision NOT to act on it now.
const REFUSAL_RE =
  /\b(I'?ll flag it,? not|flag it,? not (?:fold|fix)|not fold it in|won'?t (?:touch|fix|fold|change) it|leav(?:e|ing) it (?:as[- ]is|alone|be|untouched)|not (?:going to|gonna) (?:fix|touch|fold)|defer(?:ring)? (?:it|this) (?:without|with no)|so I'?m not (?:gonna|going to) fix)\b/i;

// An "it's actually being fixed / validly deferred" acquittal within the window —
// suppresses a false positive when the same breath shows action or a valid reason.
// NEGATION TRAP: "fold it in" / "fix it now" must NOT be immediately negated —
// "not fold it in" is a REFUSAL, not an acquittal. The (?<!\bnot )-style guard
// rejects an acquittal phrase preceded by "not"/"won't"/"can't" within a few words.
// (This was the bug that let the real offense slip: "I'll flag it, not fold it in"
// matched the bare "fold it in" acquittal and suppressed the block.)
const ACQUITTAL_RE =
  /\b(?<!\bnot )(?<!\bnot to )(?<!\bwon'?t )(?<!\bcan'?t )(fix(?:ing|ed)? it now|fold(?:ing)? it in|in the same (?:commit|turn|pass)|receiving (?:stage|line|\[ ?\])|tooling gap|already[- ]scheduled|CERES_SKIP|added a \[ ?\] line)\b/i;

// Split text into PARAGRAPH windows so a deflection and its coupled refusal are
// scored together (they routinely span several sentences within one paragraph —
// the real offense was "not mine to fix… it is pre-existing… I'll flag it, not
// fold it in", three sentences in one paragraph), while a deflection in paragraph
// A and an unrelated refusal in far-away paragraph Z do NOT couple. A paragraph is
// a run of lines between blank lines; very long paragraphs are additionally capped
// into overlapping ~400-char sub-windows so one giant paragraph can't smuggle a
// coupling across unrelated spans.
function windows(text) {
  if (!text) return [];
  const paras = text.split(/\n\s*\n/).map((p) => p.trim()).filter(Boolean);
  const out = [];
  const CAP = 400;
  for (const p of paras) {
    if (p.length <= CAP) { out.push(p); continue; }
    for (let i = 0; i < p.length; i += CAP / 2) out.push(p.slice(i, i + CAP));
  }
  return out;
}

// Returns the offending window text, or null. Exported for tests.
function findDeflection(text) {
  for (const w of windows(text)) {
    if (DEFLECTION_RE.test(w) && REFUSAL_RE.test(w) && !ACQUITTAL_RE.test(w)) {
      return w.trim();
    }
  }
  return null;
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
  module.exports = { findDeflection, windows, DEFLECTION_RE, REFUSAL_RE, ACQUITTAL_RE };
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

    const offending = findDeflection(text);
    const outcome = offending ? "blocked" : "passed";

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
          snippet: offending ? offending.slice(0, 200) : "",
        }) + "\n",
      );
    } catch { /* best-effort */ }

    if (!offending) process.exit(0);

    const reason = [
      "🛑 Pre-existing / not-mine deflection on a KNOWN defect.",
      "",
      "Your final message couples a 'not this turn's responsibility' framing with an",
      "explicit refusal to fix it:",
      `  “${offending.slice(0, 180)}”`,
      "",
      "The age of a defect is irrelevant to whether it gets fixed when you are touching",
      "the file. 'Pre-existing', 'not mine', 'out of scope for this commit' + 'I'll flag",
      "it, not fold it in' is the exact soft-deflection deep-fix-mode and",
      "feedback_no_flag_without_action forbid — repeatedly corrected by the user.",
      "",
      "Recovery:",
      "  • Fix the defect now, in this turn, and say so (then this passes).",
      "  • OR state a VALID deferral: a tooling gap with evidence, OR an already-scheduled",
      "    receiving `[ ]` line — not merely that it 'predates my work'.",
      "  • OR, if it is genuinely a separate FEATURE (not a defect) the user chose to defer,",
      "    phrase it as that explicit user decision + a receiving line, without the refusal verb.",
      "  • Session bypass (sparingly): CERES_SKIP_DEFLECTION_HOOK=1.",
      "",
      "Audit: .claude/state/deflection-detect/log.jsonl",
    ].join("\n");

    process.stderr.write(reason);
    process.exit(2);
  });
}
