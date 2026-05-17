#!/usr/bin/env node
// PostToolUse hook on `Bash` — watches build/test command output for warnings
// that don't surface as compile errors. Advisory only; never blocks.
//
// Triggers: dotnet build, dotnet test, pnpm build, pnpm test,
//           pnpm --dir ProjectCeres.Client *
//
// Corpus + remediation actions live in
//   .claude/skills/playbook/references/build-warnings.md
// (this file's logic; that file's data).
//
// Per-session deduplication: .claude/state/surface-build-warnings/<session_id>.json
// stores the set of already-surfaced warning IDs. Once surfaced, a warning
// won't re-fire in the same session — that prevents nagging on every build.

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const STATE_DIR = path.join(PROJECT_DIR, ".claude", "state", "surface-build-warnings");

const BUILD_COMMAND_RE = /\b(dotnet\s+(build|test)|pnpm\s+(--dir\s+\S+\s+)?(build|test|run\s+))/;

function ensureDir(p) {
  try {
    fs.mkdirSync(p, { recursive: true });
  } catch {}
}

function readState(sessionId) {
  const file = path.join(STATE_DIR, `${sessionId}.json`);
  try {
    const j = JSON.parse(fs.readFileSync(file, "utf8"));
    if (!Array.isArray(j.surfaced)) j.surfaced = [];
    return j;
  } catch {
    return { surfaced: [] };
  }
}

function writeState(sessionId, state) {
  ensureDir(STATE_DIR);
  const file = path.join(STATE_DIR, `${sessionId}.json`);
  try {
    fs.writeFileSync(file, JSON.stringify(state));
  } catch {}
}

// Stat helper — returns size in bytes, or null if missing.
function fileSize(absPath) {
  try {
    return fs.statSync(absPath).size;
  } catch {
    return null;
  }
}

// Stat helper — returns mtime in ms, or null if missing.
function fileMtimeMs(absPath) {
  try {
    return fs.statSync(absPath).mtimeMs;
  } catch {
    return null;
  }
}

// Parse `Total time:` or `Elapsed time:` lines from `dotnet test` output.
// Returns total seconds, or null if not found.
function parseDotnetTestElapsedSeconds(output) {
  // Common dotnet-test summary patterns:
  //   "Total time: 1.2345 Seconds"     (msbuild)
  //   "Elapsed: 00:03:30.45"            (vstest)
  //   "Total tests: ... Passed: ... Failed: ... Skipped: ... Time: 3.5 min"
  const elapsedColon = /Elapsed\b.*?(\d{1,2}):(\d{2}):(\d{2})(?:\.\d+)?/i;
  const totalSeconds = /Total time:\s*([\d.]+)\s*Seconds/i;
  const minSuffix = /Time:\s*([\d.]+)\s*min/i;

  let m;
  if ((m = output.match(elapsedColon))) {
    return Number(m[1]) * 3600 + Number(m[2]) * 60 + Number(m[3]);
  }
  if ((m = output.match(totalSeconds))) {
    return Number(m[1]);
  }
  if ((m = output.match(minSuffix))) {
    return Number(m[1]) * 60;
  }
  return null;
}

// The corpus — one row per detector. `check(input)` returns null if no fire,
// or a string remediation message if fire.
const CORPUS = [
  {
    id: "browserslist-outdated",
    check: (input) =>
      /Browserslist:\s*caniuse-lite is outdated/i.test(input.output) ||
      /caniuse-lite is outdated/i.test(input.output)
        ? "Browserslist outdated. Action: `pnpm dlx update-browserslist-db@latest` (or `pnpm update caniuse-lite browserslist`)."
        : null,
  },
  {
    id: "tailwind-no-utility-classes",
    check: (input) =>
      /no utility classes were detected/i.test(input.output)
        ? "Tailwind tree-shaker pruned all utility classes — check `tailwind.config.js` `content` glob. Razor `.cshtml` files alone aren't enough; `./Styles/**/*.css` must also be included so `@apply` directives are scanned. See commit `f8616087`."
        : null,
  },
  {
    id: "site-css-size-floor",
    check: (input) => {
      // Only fire if the command was a Razor build (dotnet build family,
      // which triggers the Tailwind pre-build target).
      if (!/dotnet\s+(build|run)/.test(input.command)) return null;
      const siteCssPath = path.join(PROJECT_DIR, "ProjectCeres", "wwwroot", "css", "site.css");
      const size = fileSize(siteCssPath);
      if (size === null) return null; // not built yet, ignore
      if (size >= 20_000) return null;
      return `\`site.css\` is ${size} bytes — below the 20 KB floor. Likely Tailwind content-glob regression (see commit \`f8616087\`). Compare with \`git -C ${PROJECT_DIR} log -p ProjectCeres/tailwind.config.js\`.`;
    },
  },
  {
    id: "stale-claude-lock",
    check: (_input) => {
      const claudeDir = path.join(PROJECT_DIR, ".claude");
      let lockFiles = [];
      try {
        lockFiles = fs
          .readdirSync(claudeDir)
          .filter((f) => f.endsWith(".lock"))
          .map((f) => path.join(claudeDir, f));
      } catch {
        return null;
      }
      const oneHourAgoMs = Date.now() - 60 * 60 * 1000;
      const stale = lockFiles.filter((f) => {
        const m = fileMtimeMs(f);
        return m !== null && m < oneHourAgoMs;
      });
      if (stale.length === 0) return null;
      return `Stale lock file(s): ${stale.join(", ")} — mtime > 1 hour. Investigate (process holding it may have crashed) before removing.`;
    },
  },
  {
    id: "dotnet-deprecation",
    check: (input) => {
      const matches = input.output.match(
        /warning\s+(NETSDK\d{4}|CS06\d{2}|CA\d{4}|MSB\d{4})/gi
      );
      if (!matches || matches.length === 0) return null;
      const codes = [...new Set(matches.map((m) => m.match(/(NETSDK\d{4}|CS06\d{2}|CA\d{4}|MSB\d{4})/i)[1].toUpperCase()))];
      return `.NET deprecation / obsolete warning(s) emitted: ${codes.join(", ")}. Surface the warning line(s) from the build output and resolve.`;
    },
  },
  {
    id: "test-runtime-regression",
    check: (input) => {
      if (!/dotnet\s+test/.test(input.command)) return null;
      const seconds = parseDotnetTestElapsedSeconds(input.output);
      if (seconds === null) return null;
      if (seconds <= 360) return null; // 6 min threshold
      const mins = (seconds / 60).toFixed(1);
      return `Test runtime regression: ${mins} min exceeds the 6 min threshold (baseline ~3:30 per \`project_test_suite_performance\` memory). Check whether \`RateLimitedAuthEndpointTests\` reverted from \`WithFreshRateLimiter()\` / \`WithShortLoginWindow()\` to real-wall-clock \`Task.Delay(70s)\` calls (see commit \`f8616087\`).`;
    },
  },
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

  if (input.tool_name !== "Bash") process.exit(0);

  const command = input.tool_input?.command || "";
  if (!BUILD_COMMAND_RE.test(command)) process.exit(0);

  // tool_response can be a string OR an object with a `stdout` field — handle both.
  const tr = input.tool_response;
  let output = "";
  if (typeof tr === "string") {
    output = tr;
  } else if (tr && typeof tr === "object") {
    output =
      (typeof tr.stdout === "string" ? tr.stdout : "") +
      (typeof tr.stderr === "string" ? "\n" + tr.stderr : "") +
      (typeof tr.output === "string" ? "\n" + tr.output : "");
  }

  const sessionId = input.session_id || "no-session";
  const state = readState(sessionId);

  const detectorInput = { command, output };
  const fired = [];
  for (const row of CORPUS) {
    if (state.surfaced.includes(row.id)) continue;
    const msg = row.check(detectorInput);
    if (msg) {
      fired.push({ id: row.id, msg });
      state.surfaced.push(row.id);
    }
  }

  if (fired.length === 0) process.exit(0);

  writeState(sessionId, state);

  const additionalContext = [
    "⚠️ Build/test invocation surfaced warning(s) that don't appear as compile errors:",
    "",
    ...fired.map((f) => `  • [${f.id}] ${f.msg}`),
    "",
    "Each warning is surfaced once per session. Corpus + remediation reference: `.claude/skills/playbook/references/build-warnings.md`.",
  ].join("\n");

  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: { hookEventName: "PostToolUse", additionalContext },
    })
  );
  process.exit(0);
});
