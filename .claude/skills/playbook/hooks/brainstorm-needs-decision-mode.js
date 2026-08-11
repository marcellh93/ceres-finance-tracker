#!/usr/bin/env node
// PostToolUse hook — fires on Skill invocations. When the user invokes
// `superpowers:brainstorming` (directly or via the stage-start advisory)
// and `decision-mode` has not fired this session, emits an advisory
// reminding the agent that every proposal/option/trade-off in the next
// response must follow decision-mode style.
//
// Advisory only — emits additionalContext, does NOT block.
//
// Why: `superpowers:brainstorming` instructs the agent to propose 2-3
// approaches with trade-offs, but is silent on language style. Without
// `decision-mode` active, proposals leak framework jargon into the
// trade-off discussion (middleware, predicate, lifecycle, DbContext,
// query filter, etc.). User pushed back 2026-05-17: "the brainstorm
// superpower skill is still presenting the proposal to me in jargon."
//
// State read: .claude/state/playbook/<session_id>.json.
// Suppressed when `decision-mode` is already in `fired`.

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();

function hasFiredDecisionMode(sessionId) {
  if (!sessionId) return false;
  const statePath = path.join(
    PROJECT_DIR,
    ".claude",
    "state",
    "playbook",
    `${sessionId}.json`
  );
  try {
    const state = JSON.parse(fs.readFileSync(statePath, "utf8"));
    const fired = Array.isArray(state.fired) ? state.fired : [];
    return fired.includes("decision-mode");
  } catch {
    return false;
  }
}


const BRAINSTORM_SKILL = "superpowers:brainstorming";

// Is this tool_input a fire of the brainstorming skill? Matches the prefixed
// name only — an unprefixed match would fire on a same-named skill from
// another plugin. Pinned by __tests__/playbook-postchecks.test.js.
function isBrainstormSkill(toolInput) {
  const ti = toolInput || {};
  const name = ti.skill || ti.name || (ti.args && ti.args.skill) || "";
  return name === BRAINSTORM_SKILL;
}

if (typeof module !== "undefined" && module.exports) {
  module.exports = { isBrainstormSkill, BRAINSTORM_SKILL };
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

  const toolName = input.tool_name || "";
  if (toolName !== "Skill") process.exit(0);

  if (!isBrainstormSkill(input.tool_input)) process.exit(0);

  const sessionId = String(input.session_id || "");
  if (hasFiredDecisionMode(sessionId)) process.exit(0);

  const additionalContext = [
    "🧠 playbook: brainstorming fired without decision-mode.",
    "",
    "Phase A″ in playbook/references/constitution.md: every proposal, option, and trade-off emitted during a brainstorm must follow `decision-mode` style — three-layer Container → Term → Why-here, no forbidden words inside Layer 2 unless defined in the same paragraph.",
    "",
    "Forbidden in Layer 2 unless defined inline (full list in decision-mode/references/forbidden-words.md): middleware, pipeline, primitive, predicate, handler, principal, lifecycle, scope (noun), DbContext, query filter, idempotent, route, endpoint, +20 others.",
    "",
    "Reply shape for proposals: decision-in-one-sentence → options table (each option in plain English) → recommendation → technical detail at bottom. Brief the lead, not the implementer — concept before name, one new term per paragraph.",
    "",
    "Memory: [[feedback_status_updates_in_plain_language]] (\"brief the tech lead, not your peer\") and [[reference_decision_mode_skill]] (full style rules). User pushed back 2026-05-17 that brainstorm proposals were still jargon-heavy — this advisory is the fix.",
    "",
    "If a proposal genuinely cannot be stated without framework names (e.g. \"should we use Identity's `SignInManager` or a homegrown shim?\"), define the term inline the first time it appears.",
  ].join("\n");

  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: "PostToolUse",
        additionalContext,
      },
    })
  );
  process.exit(0);
});
}
