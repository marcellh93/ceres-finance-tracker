#!/usr/bin/env node
// PostToolUse advisory — nudges toward /sync-docs when a schema- or
// behaviour-bearing file changes.
//
// Read process.argv[2] until 2026-08-12 while wired with no arguments, so the
// path was always "" and it never fired. PostToolUse payloads arrive on stdin.

const DOC_WORTHY = [/Models\//i, /Controllers\//i, /Data\//i, /migrations/i];

function extractPath(input) {
  const ti = (input && input.tool_input) || {};
  return ti.file_path || ti.notebook_path || "";
}

function isDocWorthy(file) {
  if (!file) return false;
  return DOC_WORTHY.some((p) => p.test(file));
}

if (typeof module !== "undefined" && module.exports) {
  module.exports = { isDocWorthy, extractPath, DOC_WORTHY };
}

if (require.main === module) {
  let raw = "";
  process.stdin.on("data", (c) => (raw += c));
  process.stdin.on("end", () => {
    let input;
    try { input = JSON.parse(raw || "{}"); } catch { process.exit(0); }

    const file = extractPath(input);
    if (!isDocWorthy(file)) process.exit(0);

    process.stdout.write(
      JSON.stringify({
        hookSpecificOutput: {
          hookEventName: "PostToolUse",
          additionalContext:
            `📄 ${file} was modified — consider running the \`sync-docs\` skill if this ` +
            `involved a schema or behaviour change.`,
        },
      })
    );
    process.exit(0);
  });
}
