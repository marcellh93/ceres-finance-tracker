#!/usr/bin/env bash
# stage-spa.sh — build the React SPA and stage it into ProjectCeres/wwwroot/dist/.
#
# Why this exists:
#   The app serves the SPA shell + hashed asset bundles from wwwroot/dist/ via
#   MapFallbackToFile("dist/app.html"). That directory is refreshed automatically
#   ONLY by the BuildSpaClient MSBuild target, which is gated to Release builds
#   (Stage 11 — Debug inner-loop is meant to use the Vite dev server instead).
#
#   So a Debug-served app with NO Vite process running (e.g. plain `dotnet run`
#   without the dev server, or the agent-env Smoke profile) serves whatever was
#   last staged — which can be days stale, showing old code with no error. This
#   is the documented stale-SPA-bundle trap (docs/runbooks/local-dev-
#   troubleshooting.md § Symptom "SPA route shows old code").
#
#   This script does exactly what the Release target does — `pnpm build` then a
#   clean copy into wwwroot/dist/ — so verifying a frontend change against a
#   non-Vite Debug app is one command, not a hand-typed rm+cp.
#
# When you do NOT need this:
#   Normal local dev via `dotnet run` / tools/dev-watch.sh starts the Vite dev
#   server (appsettings.Development.json § Vite), which serves live with HMR.
#   Use this only to stage a static bundle for a non-Vite run or a verify pass.
#
# Usage:  tools/stage-spa.sh
# Exit:   0 on success; non-zero if the build fails (wwwroot/dist left untouched).

set -euo pipefail

REPO_ROOT="${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
CLIENT_DIR="$REPO_ROOT/ProjectCeres.Client"
SRC_DIST="$CLIENT_DIR/dist"
DEST_DIST="$REPO_ROOT/ProjectCeres/wwwroot/dist"

echo "[stage-spa] building the SPA (pnpm build)…"
pnpm --dir "$CLIENT_DIR" build

if [[ ! -f "$SRC_DIST/app.html" ]]; then
  echo "[stage-spa] ERROR: $SRC_DIST/app.html missing after build — not staging." >&2
  exit 1
fi

# Build succeeded — only now replace the served bundle, so a failed build never
# leaves wwwroot/dist half-copied. Clean-replace to drop old hashed chunks that
# a plain copy would leave behind (stale asset accumulation).
echo "[stage-spa] staging into wwwroot/dist/…"
rm -rf "$DEST_DIST"
mkdir -p "$DEST_DIST"
cp -R "$SRC_DIST/." "$DEST_DIST/"

echo "[stage-spa] done. wwwroot/dist/ now matches the current SPA build."
echo "[stage-spa] a non-Vite Debug app (dotnet run / agent-env) will serve it."
