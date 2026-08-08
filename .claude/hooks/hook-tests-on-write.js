#!/usr/bin/env node
// PostToolUse hook — runs the hook unit suites when a hook script is edited.
//
// Hooks gate every turn, so a broken hook blocks all work. They are .js/.sh/.py,
// which run-tests.sh does not track, so they never rode the .NET tier system.
// The 2026-08-07 evidence-bundle loop shipped through exactly that gap: tests
// existed under __tests__/ and nothing ran them.
//
// Advisory, not blocking: PostToolUse cannot deny a completed write, and a
// failing suite here should surface immediately rather than at turn end.
// Exit 0 always; failures go to stderr.

const cp = require("child_process");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  let payload;
  try { payload = JSON.parse(raw || "{}"); } catch { process.exit(0); }

  if (!["Edit", "Write", "MultiEdit"].includes(payload.tool_name)) process.exit(0);

  const filePath = payload.tool_input?.file_path || "";
  if (!filePath) process.exit(0);

  // Only fire for hook/skill scripts — not docs, not project source.
  const rel = path.relative(PROJECT_DIR, path.resolve(filePath));
  const isHookScript =
    (rel.startsWith(".claude/") || rel.startsWith(path.join(".claude", ""))) &&
    /\.(js|sh|py)$/.test(rel);
  if (!isHookScript) process.exit(0);

  const runner = path.join(PROJECT_DIR, "scripts", "test-hooks.sh");
  const res = cp.spawnSync("bash", [runner], {
    cwd: PROJECT_DIR,
    encoding: "utf8",
    timeout: 60000,
  });

  if (res.status !== 0) {
    const tail = String(res.stdout || "").split("\n").filter((l) => /^(✖|not ok|ℹ fail)/.test(l)).slice(0, 15);
    process.stderr.write(
      `[hook-tests] FAILING after edit to ${rel}\n` +
      (tail.length ? tail.join("\n") + "\n" : String(res.stderr || "").slice(0, 800) + "\n") +
      `Run scripts/test-hooks.sh for the full output.\n`
    );
  }

  process.exit(0);
});
