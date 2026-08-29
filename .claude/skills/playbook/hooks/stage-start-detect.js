#!/usr/bin/env node
// UserPromptSubmit hook — detects stage-start phrasing and reminds the
// agent to invoke superpowers:brainstorming first (Phase A in the
// playbook constitution).
//
// Advisory only — prepends additionalContext, does NOT block.
//
// Why: memory rule feedback_brainstorm_spec_plan_execute_flow (pinned
// 2026-05-15) says "On 'let's move on with stage X' / 'let's build
// feature Y', the FIRST tool call is superpowers:brainstorming, NOT a
// plan-mode plan."

const STAGE_START_PHRASES = [
  /\blet'?s (start|move on (to|with)|begin) stage \w+/i,
  /\bbuild (the )?feature \w+/i,
  /\bstage \d+(\.\d+)?\s*[:—-]?\s*(start|begin)/i,
  /\bmoving on to stage \w+/i,
  /\bkick off stage/i,
  /\bproceed with stage \w+/i,
  // `let's build` needs a roadmap-shaped object. Bare `\w+` fired on any noun
  // ("let's build confidence in the hooks"), so ordinary talk paid the ~1.2KB.
  /\blet'?s build (the )?(stage \w+|feature \w+)/i,
  /\blet'?s build (the )?(\w+ )?(page|component|drawer|dialog|modal|sheet|form|layout|view|screen|panel|card|table|list|menu|nav|sidebar|header|footer)\b/i,
  /\bimplement (the )?\w+ from the roadmap/i,
  /\bimplement (the )?next/i,
  // `what's next` only as the whole request — as a trailing aside it fires
  // mid-task, when the stage is already open and brainstorming has run.
  /^\s*(so |ok(ay)?,? |right,? )?what'?s next\b[\s?!.]*$/i,
  /\bnext step[s]? for\b/i,
];

// Exported for __tests__/prompt-detectors.test.js.
if (typeof module !== "undefined" && module.exports) {
  module.exports = { STAGE_START_PHRASES };
}

let raw = "";
if (require.main === module) {
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let input;
  try { input = JSON.parse(raw || "{}"); } catch { process.exit(0); }

  const prompt = String(input.prompt || "");
  const matched = STAGE_START_PHRASES
    .filter((re) => re.test(prompt))
    .map((re) => re.toString());

  if (matched.length === 0) process.exit(0);

  const additionalContext = [
    "🚦 playbook: stage-start detected.",
    "",
    `Matched ${matched.length} phrase(s): ${matched.join(", ")}`,
    "",
    "Phase A in playbook/references/constitution.md says: the FIRST tool call is `superpowers:brainstorming`, NOT a plan-mode plan, NOT a Write, NOT an immediate Edit.",
    "",
    "Memory rule (feedback_brainstorm_spec_plan_execute_flow, pinned 2026-05-15): on 'let's move on with stage X' / 'let's build feature Y', brainstorm first — the user reviews the spec before planning, and reviews the plan before execution.",
    "",
    "For a non-trivial stage, consider dispatching the `ceres-researcher` agent as brainstorming's first step — a read-first fact-find (subsystems / prior art / conventions / unknowns) so the design starts from evidence. Skip for small/config-only stages, same as the brainstorm skip rule.",
    "",
    "If the request is genuinely small (e.g. a config-only follow-up), say so explicitly and skip the brainstorm. Otherwise: invoke `superpowers:brainstorming` next.",
  ].join("\n");

  process.stdout.write(JSON.stringify({
    hookSpecificOutput: { hookEventName: "UserPromptSubmit", additionalContext },
  }));
  process.exit(0);
});
}
