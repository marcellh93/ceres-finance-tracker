#!/usr/bin/env node
// Stop hook — warns when this turn wrote React SPA source but the served
// bundle in wwwroot/dist/ is now stale.
//
// Why: the app serves the SPA shell + hashed assets from
// ProjectCeres/wwwroot/dist/ via MapFallbackToFile("dist/app.html"). That
// directory is refreshed automatically ONLY by the BuildSpaClient MSBuild
// target, gated to Release builds (Stage 11 — Debug uses the Vite dev server).
// So a Debug-served app WITHOUT a Vite process running serves whatever was last
// staged, which can be days stale with no error — the documented stale-SPA-
// bundle trap (docs/runbooks/local-dev-troubleshooting.md). This bit a real
// /support verification on 2026-08-28.
//
// ADVISORY, never blocks (exit 0). Normal local dev runs the Vite dev server
// (appsettings.Development.json § Vite), which serves live and never touches
// wwwroot/dist/ — for that workflow this note is a harmless no-op the agent can
// ignore. It exists to catch the verify-against-a-static-build case, where a
// stale bundle silently shows old code.
//
// Fires only when BOTH hold:
//   1. this turn wrote a non-test file under ProjectCeres.Client/src/, AND
//   2. wwwroot/dist/app.html is older than the newest such source file
//      (or is missing entirely).
//
// Fix it names: tools/stage-spa.sh (pnpm build + stage into wwwroot/dist/).

const fs = require("fs");
const path = require("path");

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const DIST_MARKER = path.join(PROJECT_DIR, "ProjectCeres", "wwwroot", "dist", "app.html");
const SRC_ROOT = path.join("ProjectCeres.Client", "src"); // repo-relative prefix

// Repo-relative files this turn wrote, from the same PostToolUse tracker the
// test/evidence hooks read. Only .cs/.ts/.tsx/.csproj/.sln are tracked, which
// is exactly the set we care about here (.ts/.tsx). Fail-open on any error.
function sessionWrittenFiles(sessionId) {
  if (!sessionId || sessionId === "unknown") return [];
  const dir = path.join(PROJECT_DIR, ".claude", "state", "run-tests");
  const out = new Set();
  for (const name of [`${sessionId}.json`, `${sessionId}.lastturn.json`]) {
    try {
      const raw = fs.readFileSync(path.join(dir, name), "utf8");
      if (!raw.trim()) continue;
      const state = JSON.parse(raw);
      if (Array.isArray(state.files)) for (const f of state.files) out.add(f);
    } catch { /* fail-open */ }
  }
  return [...out];
}

// A non-test SPA source file — the kind that changes what the built bundle
// serves. Test files (.test.ts/.tsx) don't ship in the bundle, so a turn that
// only touched tests must not trigger the warning.
function isShippingSpaSource(relPath) {
  const p = relPath.replace(/\\/g, "/");
  if (!p.startsWith(SRC_ROOT.replace(/\\/g, "/") + "/")) return false;
  if (!/\.(ts|tsx)$/.test(p)) return false;
  if (/\.test\.tsx?$/.test(p)) return false;
  return true;
}

function mtimeMs(absPath) {
  try {
    return fs.statSync(absPath).mtimeMs;
  } catch {
    return null; // missing
  }
}

function runMain() {
  let payload = {};
  try {
    const raw = fs.readFileSync(0, "utf8");
    if (raw) payload = JSON.parse(raw);
  } catch { /* fail-open */ }
  const sessionId = payload.session_id || "unknown";

  const written = sessionWrittenFiles(sessionId).filter(isShippingSpaSource);
  if (written.length === 0) process.exit(0); // no SPA source written this turn

  const distMtime = mtimeMs(DIST_MARKER);

  // Newest source mtime among the files this turn wrote (that still exist).
  let newestSrcMtime = 0;
  let newestSrc = null;
  for (const rel of written) {
    const m = mtimeMs(path.join(PROJECT_DIR, rel));
    if (m !== null && m > newestSrcMtime) {
      newestSrcMtime = m;
      newestSrc = rel;
    }
  }
  if (newestSrc === null) process.exit(0); // all written files gone (moved/deleted)

  // Fresh enough? A small skew allowance so a stage run milliseconds before the
  // last edit doesn't false-positive.
  const SKEW_MS = 2000;
  if (distMtime !== null && distMtime + SKEW_MS >= newestSrcMtime) process.exit(0);

  const reason =
    distMtime === null
      ? "ProjectCeres/wwwroot/dist/ has no built bundle"
      : "ProjectCeres/wwwroot/dist/ is older than SPA source you changed this turn";

  const lines = [
    "spa-dist-freshness — advisory:",
    "",
    `${reason}.`,
    `Newest changed SPA source: ${newestSrc}`,
    "",
    "The app serves the SPA from wwwroot/dist/ (MapFallbackToFile). In Debug",
    "WITHOUT a Vite dev server running, it serves this stale bundle — old code,",
    "no error. If you're verifying a change against a non-Vite app (dotnet run",
    "or the agent-env), stage the current build first:",
    "",
    "    tools/stage-spa.sh",
    "",
    "If you're running the Vite dev server (dotnet run / tools/dev-watch.sh in",
    "Development), it serves live and this note is a no-op — ignore it.",
    "",
  ];
  process.stderr.write(lines.join("\n"));
  process.exit(0); // advisory — never blocks
}

if (require.main === module) {
  runMain();
}

module.exports = { isShippingSpaSource, sessionWrittenFiles };
