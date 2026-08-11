#!/usr/bin/env node
// PostToolUse hook — runs after Edit/Write/MultiEdit to a spec file under
// docs/superpowers/specs/*.md. Scans the new content for frontend mentions
// and emits an advisory if `frontend-orchestrator` has not fired this
// session.
//
// Advisory only — emits additionalContext, does NOT block.
//
// Belt-and-braces companion to frontend-touch-detect.js: catches the case
// where the user's prompt was generic ("write the next spec") but the
// spec's content turned out to be frontend-shaped.
//
// No-op when:
//   - the tool wasn't Edit/Write/MultiEdit
//   - the file isn't under docs/superpowers/specs/
//   - frontend-orchestrator already fired this session
//   - the spec content has no frontend signals

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const SPEC_DIR_REL = "docs/superpowers/specs";

const FRONTEND_SIGNALS = [
  /\bProjectCeres\.Client\b/i,
  /\b\w+\.tsx?\b/,
  // `@/...` aliases need their own alternative: a leading \b before `@`
  // requires a word char immediately before it, so it never matched in real
  // usage ("import from @/lib/utils"). Split out 2026-08-11.
  /\b(shadcn|tailwind|vite|vitest)\b|@\/(components|lib|hooks|ui)\b/i,
  /\b(component|drawer|dialog|modal|sheet|design system|design tokens|empty state|error state|loading state)\b/i,
  /\b(React|JSX|TSX|useState|useEffect|useMemo|useCallback)\b/,
  /\bdocs\/design-system\.md\b/i,
];

function hasFiredFrontendOrchestrator(sessionId) {
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
    return fired.includes("frontend-orchestrator");
  } catch {
    return false;
  }
}

function isSpecPath(filePath) {
  if (!filePath) return false;
  const abs = path.resolve(filePath);
  const specAbs = path.resolve(PROJECT_DIR, SPEC_DIR_REL);
  return (
    abs.startsWith(specAbs + path.sep) && abs.toLowerCase().endsWith(".md")
  );
}

function readNewContent(toolName, toolInput, filePath) {
  // Prefer the in-payload new content (avoids a disk re-read race).
  if (toolName === "Write") return String(toolInput.content || "");
  if (toolName === "Edit") return String(toolInput.new_string || "");
  if (toolName === "MultiEdit") {
    const edits = Array.isArray(toolInput.edits) ? toolInput.edits : [];
    return edits.map((e) => String(e.new_string || "")).join("\n");
  }
  // Fallback: read the file from disk.
  try {
    return fs.readFileSync(filePath, "utf8");
  } catch {
    return "";
  }
}

// Exported for __tests__/playbook-postchecks.test.js.
if (typeof module !== "undefined" && module.exports) {
  module.exports = { FRONTEND_SIGNALS, isSpecPath, readNewContent, hasFiredFrontendOrchestrator };
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
  if (!["Edit", "Write", "MultiEdit"].includes(toolName)) process.exit(0);

  const toolInput = input.tool_input || {};
  const filePath = toolInput.file_path;
  if (!isSpecPath(filePath)) process.exit(0);

  const sessionId = String(input.session_id || "");
  if (hasFiredFrontendOrchestrator(sessionId)) process.exit(0);

  const content = readNewContent(toolName, toolInput, filePath);
  if (!content) process.exit(0);

  const matched = FRONTEND_SIGNALS.filter((re) => re.test(content)).map((re) =>
    re.toString()
  );
  if (matched.length === 0) process.exit(0);

  const relPath = path.relative(PROJECT_DIR, path.resolve(filePath));

  const additionalContext = [
    "🎨 playbook: frontend signals detected in a spec write.",
    "",
    `File: ${relPath}`,
    `Matched ${matched.length} signal(s): ${matched.slice(0, 4).join(", ")}${matched.length > 4 ? ", …" : ""}`,
    "",
    "Phase A′ in playbook/references/constitution.md: when a spec under `docs/superpowers/specs/` contains frontend signals, invoke `frontend-orchestrator` to route the pipeline BEFORE the spec is reviewed. The orchestrator's discovery phase enforces a read of `docs/design-system.md` so the spec uses existing tokens/recipes instead of hand-rolling.",
    "",
    "Next move: invoke `frontend-orchestrator`, then return to spec review. If the signals were incidental (e.g. a backend spec that merely names a frontend file) say so explicitly.",
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
