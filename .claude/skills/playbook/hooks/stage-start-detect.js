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
  /\blet'?s build (the )?\w+/i,
  /\bimplement (the )?\w+ from the roadmap/i,
  /\bimplement (the )?next/i,
  /\bwhat'?s next\b/i,
  /\bnext step[s]? for\b/i,
];

let raw = "";
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
    "If the request is genuinely small (e.g. a config-only follow-up), say so explicitly and skip the brainstorm. Otherwise: invoke `superpowers:brainstorming` next.",
  ].join("\n");

  process.stdout.write(JSON.stringify({
    hookSpecificOutput: { hookEventName: "UserPromptSubmit", additionalContext },
  }));
  process.exit(0);
});
