#!/usr/bin/env node
// UserPromptSubmit hook — detects decision-asking prompts and reminds the
// assistant to invoke decision-mode for the three-layer style.
//
// The hook PREPENDS context — it does not block. False positives cost one
// extra reminder paragraph; false negatives cost the user a re-read.

const DECISION_PHRASES = [
  // Should-we / is-it-worth. `should we` needs a decision-shaped object —
  // bare `\bshould (we|i)\b` fired on procedural asides ("should we run the
  // tests first?"), which are task talk, not a call the user has to make.
  /\bshould (we|i)\b.*\b(defer|ship|skip|drop|split|merge|refactor|rewrite|migrate|adopt|use|go with|wait|hold off|block|gate|scope|cut)\b/i,
  /\bshould (we|i)\b.*\b(or|vs\.?|versus)\b/i,
  /\bshould (we|i)\b.*\b(stage \d|phase \d|sprint|roadmap|scope|now or)\b/i,
  /\bis it worth\b/i,
  /\bworth (doing|the time|it)\b/i,
  /\bmake (the |a )?call\b/i,
  /\byour recommendation\b/i,
  /\bwhat do you (think|recommend|suggest)\b/i,
  /\bwhat would you do\b/i,

  // Fit / scope
  /\bdoes (this|that|it) fit\b/i,
  /\bfit (the |this )?(sprint|stage|phase|roadmap|scope)\b/i,
  /\b(in|out of) scope\b/i,
  /\bship (this|that|it) (now|today)\b/i,
  /\bdefer (this|that|it)\b/i,
  /\bcurrent (sprint|stage|phase)\b/i,

  // Cost / impact
  /\bwhat'?s the (impact|cost|risk|tradeoff)\b/i,
  /\bhow big (is|of)\b/i,
  /\bhow much (work|effort|time)\b/i,
  /\bis this a refactor\b/i,
  /\brefactor or (feature|fix)\b/i,
  /\bbreaking change\b/i,

  // Option-comparison
  /\boption[s]? a (and|or|vs|versus)/i,
  /\bA\/B\/C\b/,
  /\b(option|approach|path) 1 (or|vs|versus) (option|approach|path)? ?2\b/i,
  /\bwhich (option|approach|path|one)\b/i,

  // "Explain so I can decide"
  /\bexplain.*so (i|we) can (decide|make)\b/i,
  /\bin plain (english|terms|words)\b/i,
  /\btoo much jargon\b/i,
  /\bwithout the jargon\b/i,
];

// Exported for __tests__/prompt-detectors.test.js.
if (typeof module !== "undefined" && module.exports) {
  module.exports = { DECISION_PHRASES };
}

let raw = "";
if (require.main === module) {
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try { input = JSON.parse(raw || "{}"); } catch { process.exit(0); }

  const prompt = String(input.prompt || "");
  const matched = DECISION_PHRASES.filter((re) => re.test(prompt)).map((re) => re.toString());
  if (matched.length === 0) process.exit(0);

  const additionalContext = [
    "🎯 decision-mode trigger detected.",
    "",
    `User's message matched ${matched.length} decision-asking phrase(s): ${matched.join(", ")}`,
    "",
    "The user is in tech-lead / PM mode and needs to make a fast call. Invoke the `decision-mode` skill and structure your reply using the three-layer pattern:",
    "  • Layer 1 — CONTAINER: name the larger system the term lives inside, in plain English",
    "  • Layer 2 — TERM: define each technical term mechanically; NO jargon inside the definition (see references/forbidden-words.md)",
    "  • Layer 3 — WHY-HERE: state why this term shows up in THIS decision, with a cost signal (sprint-sized / stage-sized / phase-sized / refactor-class / unknown)",
    "",
    "Lead with the decision in ONE sentence (plain English, no class names). Then the options table. Then your recommendation. Pull the technical detail to the bottom. The user can decode jargon but it eats decision time — don't make them.",
  ].join("\n");

  process.stdout.write(JSON.stringify({
    hookSpecificOutput: { hookEventName: "UserPromptSubmit", additionalContext },
  }));
  process.exit(0);
});
}
