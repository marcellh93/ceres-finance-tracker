#!/usr/bin/env node
// UserPromptSubmit hook — detects frontend-domain phrasing in the user
// prompt and reminds the agent to invoke `frontend-orchestrator` to
// route the pipeline (Phase A′ in the playbook constitution).
//
// Advisory only — prepends additionalContext, does NOT block.
//
// No-op when `frontend-orchestrator` has already fired this session
// (state file at .claude/state/playbook/<session_id>.json).
//
// Why: frontend-orchestrator is the entrypoint that routes between
// frontend-design, vercel-react-best-practices, web-design-guidelines,
// impeccable, and docs/design-system.md according to which phase the
// work is in. Without this hook, the playbook's seven cross-cutting
// phases run but the frontend pipeline is invoked ad-hoc.

const fs = require("fs");
const path = require("path");

const FRONTEND_PHRASES = [
  // Path / tech mentions
  /\bProjectCeres\.Client\b/i,
  /\b\w+\.tsx?\b/,
  /\b(shadcn|tailwind|vite|vitest|react)\b/i,
  // Build verbs against UI nouns
  /\bbuild (the )?(\w+ )?(page|component|drawer|dialog|modal|sheet|form|layout|view|screen|panel|card|table|list|menu|nav|sidebar|header|footer|empty state|error state|loading state)\b/i,
  /\b(design|redesign|polish|refine|simplify|harden|tighten|extract) (the |this )?(\w+ )?(page|component|drawer|dialog|modal|sheet|form|layout|view|screen|panel|card|table|list|menu|nav|sidebar|header|footer|empty state|error state|loading state|ui|interface)\b/i,
  // Review verbs against UI nouns
  /\b(review|audit|check) (the |this |my )?(ui|interface|frontend|component|page|design|layout|accessibility|a11y|design system)\b/i,
  // Visual / tone adjustments
  /\bmake (this |it |the \w+ )?(bolder|quieter|tighter|looser|cleaner|more delightful|more compact|more spacious)\b/i,
  // Design-system maintenance
  /\b(design systems?|design tokens?|tokens?|primitives?|recipes?)\b/i,
  // Direct invocation phrasing
  /\b(frontend|front-end|client(-side)?|ui|ux) (work|change|task|review|polish|refactor|spec|plan|design)\b/i,
  // Explicit slash-style invocation in chat (the orchestrator's own triggers)
  /\bfix the empty state\b/i,
  /\bbefore merge\b.*\b(ui|frontend|component|page)\b/i,
];

function hasFiredFrontendOrchestrator(sessionId) {
  if (!sessionId) return false;
  const statePath = path.join(
    ".claude",
    "state",
    "playbook",
    `${sessionId}.json`
  );
  try {
    const raw = fs.readFileSync(statePath, "utf8");
    const state = JSON.parse(raw);
    const fired = Array.isArray(state.fired) ? state.fired : [];
    return fired.includes("frontend-orchestrator");
  } catch {
    return false;
  }
}

// Exported for __tests__/prompt-detectors.test.js.
if (typeof module !== "undefined" && module.exports) {
  module.exports = { FRONTEND_PHRASES };
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
  const sessionId = String(input.session_id || "");

  if (hasFiredFrontendOrchestrator(sessionId)) process.exit(0);

  const matched = FRONTEND_PHRASES.filter((re) => re.test(prompt)).map((re) =>
    re.toString()
  );

  if (matched.length === 0) process.exit(0);

  const additionalContext = [
    "🎨 playbook: frontend-touch detected.",
    "",
    `Matched ${matched.length} phrase(s): ${matched.slice(0, 4).join(", ")}${matched.length > 4 ? ", …" : ""}`,
    "",
    "Phase A′ in playbook/references/constitution.md says: when frontend work is in scope — building, designing, reviewing, refining, or speccing UI — invoke `frontend-orchestrator` to route the pipeline (discovery → build → refine → simplify → harden → system maintenance).",
    "",
    "This applies BEFORE writing a spec, BEFORE proposing a design, and BEFORE editing under `ProjectCeres.Client/`. The orchestrator routes between `frontend-design`, `vercel-react-best-practices`, `web-design-guidelines`, `impeccable`, and `docs/design-system.md` based on phase.",
    "",
    "If the request is genuinely backend-only or doc-only despite the phrasing match, say so explicitly and skip the orchestrator. Otherwise: invoke `frontend-orchestrator` next.",
  ].join("\n");

  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: "UserPromptSubmit",
        additionalContext,
      },
    })
  );
  process.exit(0);
});
}
