#!/usr/bin/env node
// UserPromptSubmit hook — detects circling-signal phrases and forces deep-fix-mode.
// Schema: receives { session_id, prompt, ... } on stdin; emits additionalContext.
// Doc: https://code.claude.com/docs/en/hooks

const TRIGGER_PHRASES = [
  /stop going in circles/i,
  /you('?| a)re going in circles/i,
  /ultrathink/i,
  /deep( |-)research( this)?/i,
  /fix it once and for all/i,
  /you keep messing( this)?( up)?/i,
  /you keep getting( this)?( wrong)?/i,
  /definitive (fix|solution|answer)/i,
  /\bthird time\b/i,
  /\bfourth time\b/i,
  /\bnth time\b/i,
  /we'?ve been here before/i,
  /this isn'?t working( again)?/i,
  /still broken/i,
  /still not (working|fixed)/i,
  /you (always|keep) (do|repeat|make|miss)/i,
  /please (actually|really) (read|think|research)/i,
  /not surface[- ]level/i,
  /properly research/i,
  // 2026-05-23 audit additions — the user typed all of the following across
  // the Remember-Me debug session before frustration-detect fired (it didn't;
  // none of the original phrases matched). Each shipped a sub-fix that
  // didn't address the root cause because deep-fix wasn't engaged. Cover
  // the actual lexicon the user reaches for when frustrated.
  /\bstill (kicking|disconnecting|logging) me out\b/i,
  /\b(are you|were you) (still )?guessing\b/i,
  /\bdo (the |your )?research( properly)?\b/i,
  /\bresearch (it )?properly\b/i,
  /\bdeal with it\b/i,
  /\b(no more|stop) guessing\b/i,
  /\bhalf[- ]?ass(ed)?\b/i,
  /\bfix it properly\b/i,
  /\bnot working( completely)?\b/i,
  /\bdoesn'?t work( either)?\b/i,
  /\bhasn'?t worked( for a while)?\b/i,
  /\bseveral workarounds\b/i,
  /\b(came|come) back .{0,40}(logged out|kicked out)\b/i,
];

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw || "{}");
  } catch {
    process.exit(0);
  }

  const prompt = String(input.prompt || "");
  const matched = TRIGGER_PHRASES.filter((re) => re.test(prompt)).map((re) =>
    re.toString()
  );

  if (matched.length === 0) {
    process.exit(0);
  }

  const additionalContext = [
    "🛑 deep-fix-mode trigger detected.",
    "",
    `The user's message matched ${matched.length} circling-signal phrase(s): ${matched.join(", ")}`,
    "",
    "MANDATORY: Invoke the `deep-fix-mode` skill BEFORE responding. Do not propose, edit, or write anything until the skill's six-step procedure completes:",
    "  1. Stop. No mutations.",
    "  2. Write the failed-attempts table.",
    "  3. Name the pattern from references/loop-patterns.md.",
    "  4. Identify the surface vs. root layer.",
    "  5. Do external research at the documented authority bar.",
    "  6. Write the finished diagnosis in the diagnosis-template format.",
    "",
    "The user signaled this because they've observed you circling. Your own assessment that 'I'm not circling' is the failure mode the skill exists to prevent. Trust the signal.",
    "",
    "Reference: Lou & Sun (2024) — chain-of-thought, reflection methods, and instructions-to-ignore-anchors are EMPIRICALLY INSUFFICIENT against anchoring bias. External evidence is the only way out. https://arxiv.org/abs/2412.06593",
  ].join("\n");

  const output = {
    hookSpecificOutput: {
      hookEventName: "UserPromptSubmit",
      additionalContext,
    },
  };

  process.stdout.write(JSON.stringify(output));
  process.exit(0);
});
