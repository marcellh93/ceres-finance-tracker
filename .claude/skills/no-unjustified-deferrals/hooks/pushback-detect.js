#!/usr/bin/env node
// UserPromptSubmit hook — fires when the user pushes back about a previously
// deferred bug ("you deferred X again", "we agreed to fix Y", "still broken
// from last session"). This is the corrective trigger — the deferral already
// happened. The skill helps recover, not prevent.
//
// The hook PREPENDS context — does not block.

const PUSHBACK_PHRASES = [
  // Repeat-deferral callouts
  /\byou (deferred|punted|kicked|postponed)\b.*\b(again|once more|same|earlier)\b/i,
  /\b(this|that|it) keeps getting (deferred|punted|postponed|pushed)\b/i,
  /\byou forgot (to|about|that)\b/i,
  /\bI (told you|asked you|said) (to|that) (fix|address|handle|resolve)\b/i,

  // "We agreed to fix this"
  /\bwe (agreed|decided|said) (to|we'?d|we would) fix\b/i,
  /\bdidn'?t (we|you) (agree|say|decide) to fix\b/i,
  /\bI thought (we|you) (were going to|would|agreed to) fix\b/i,

  // "Still broken"
  /\bstill (broken|failing|not working|busted|wrong)\b.*\b(from|since) (last|the previous|earlier|that other)\b/i,
  /\b(this|that) is still broken\b/i,
  /\bthis (was|has been) broken for\b/i,

  // "Bring it back"
  /\b(bring|pull|surface) (this|that|it) back\b/i,
  /\bremember (we|that) (had|deferred|punted)\b/i,
  /\bgo back to (the|that) (deferred|punted|earlier) (item|bug|fix)\b/i,

  // "You told me you'd"
  /\byou told me you'?d (fix|address|handle)\b/i,
  /\byou promised\b/i,

  // Generic "we have a list of these"
  /\b(another|yet another|same) (deferral|postponement|delay)\b/i,
];

// Exported for __tests__/prompt-detectors.test.js.
if (typeof module !== "undefined" && module.exports) {
  module.exports = { PUSHBACK_PHRASES };
}

let raw = "";
if (require.main === module) {
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  const prompt = String(input.prompt || "");
  const matched = PUSHBACK_PHRASES.filter((re) => re.test(prompt)).map((re) => re.toString());
  if (matched.length === 0) process.exit(0);

  const additionalContext = [
    "🛑 Deferral-pushback trigger detected.",
    "",
    `User's message matched ${matched.length} pushback phrase(s): ${matched.join(", ")}`,
    "",
    "The user is bringing back a bug you previously deferred — which means a past deferral failed (it wasn't tracked, the tripwire was missing, or the receiving stage was never opened).",
    "",
    "MANDATORY before responding:",
    "  1. Invoke the `no-unjustified-deferrals` skill.",
    "  2. Find the original deferral (search docs/ for the bug name).",
    "  3. Identify which of the three deferral requirements failed:",
    "     • Cited reason missing → it was an unjustified deferral, not a real one",
    "     • Receiving-stage `[ ]` checkbox missing → no automated re-surface point",
    "     • Mechanical tripwire missing → no test/assertion/FIXME to fire when it should",
    "  4. Fix it NOW, in the current batch stage. Not deferred again.",
    "  5. After fixing, also fix the system failure: open or extend the active batch stage, add the `[ ]` line, add the tripwire that was missing the first time.",
    "",
    "Do NOT propose deferring this again. The user's pushback is evidence the deferral pattern is broken for this bug.",
  ].join("\n");

  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: { hookEventName: "UserPromptSubmit", additionalContext },
    })
  );
  process.exit(0);
});
}
