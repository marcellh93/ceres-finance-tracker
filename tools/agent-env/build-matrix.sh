#!/usr/bin/env bash
# Generates build-matrix.json — the evidence-bundle slot that captures
# exit codes + truncated output from dotnet build, pnpm build, pnpm test,
# dotnet test. Consumed by .claude/skills/verify-stage-completeness/hooks/
# evidence-bundle-check.js as a required slot when any code change is committed.
#
# Usage:
#   build-matrix.sh <stage-id> [--no-dotnet-test]
#
# Output:
#   .claude/state/evidence/stage-<stage-id>/build-matrix.json
#
# Exit codes:
#   0 — all four commands exited 0
#   1 — at least one command exited non-zero (the json is still written;
#       caller decides whether non-zero is acceptable, e.g. mid-stage)
#   2 — usage error or environment problem (no json written)
#
# --no-dotnet-test skips the full xUnit suite (~4 min). Default behaviour
# runs all four. For mid-stage probes the caller may pass the flag; the
# evidence-bundle hook does NOT relax the requirement.

set -euo pipefail

# ──────────────────────────────────────────────────────────────────────────
# Arg parsing
# ──────────────────────────────────────────────────────────────────────────

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <stage-id> [--no-dotnet-test]" >&2
  exit 2
fi

STAGE_ID="$1"
shift || true

RUN_DOTNET_TEST=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-dotnet-test) RUN_DOTNET_TEST=0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

REPO_ROOT="${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
EVIDENCE_DIR="$REPO_ROOT/.claude/state/evidence/stage-$STAGE_ID"
OUT_FILE="$EVIDENCE_DIR/build-matrix.json"
TMP_DIR="$(mktemp -d -t build-matrix.XXXXXXXX)"
trap 'rm -rf "$TMP_DIR"' EXIT

mkdir -p "$EVIDENCE_DIR"

# ──────────────────────────────────────────────────────────────────────────
# Run the four builds — truncate logs to the last TAIL_LINES lines for JSON.
# ──────────────────────────────────────────────────────────────────────────

TAIL_LINES=80

cd "$REPO_ROOT"

# Plain variables (macOS ships bash 3.2 which lacks associative arrays).

echo "[build-matrix] running dotnet build…" >&2
DOTNET_BUILD_LOG="$TMP_DIR/dotnet_build.log"
DOTNET_BUILD_CODE=0
dotnet build ProjectCeres/ProjectCeres.csproj >"$DOTNET_BUILD_LOG" 2>&1 || DOTNET_BUILD_CODE=$?
DOTNET_BUILD_TAIL="$(tail -n "$TAIL_LINES" "$DOTNET_BUILD_LOG")"

echo "[build-matrix] running pnpm build…" >&2
PNPM_BUILD_LOG="$TMP_DIR/pnpm_build.log"
PNPM_BUILD_CODE=0
pnpm --dir ProjectCeres.Client build >"$PNPM_BUILD_LOG" 2>&1 || PNPM_BUILD_CODE=$?
PNPM_BUILD_TAIL="$(tail -n "$TAIL_LINES" "$PNPM_BUILD_LOG")"

echo "[build-matrix] running pnpm test…" >&2
PNPM_TEST_LOG="$TMP_DIR/pnpm_test.log"
PNPM_TEST_CODE=0
pnpm --dir ProjectCeres.Client test --run >"$PNPM_TEST_LOG" 2>&1 || PNPM_TEST_CODE=$?
PNPM_TEST_TAIL="$(tail -n "$TAIL_LINES" "$PNPM_TEST_LOG")"

if (( RUN_DOTNET_TEST == 1 )); then
  echo "[build-matrix] running dotnet test (full suite — this may take several minutes)…" >&2
  DOTNET_TEST_LOG="$TMP_DIR/dotnet_test.log"
  DOTNET_TEST_CODE=0
  dotnet test --no-build >"$DOTNET_TEST_LOG" 2>&1 || DOTNET_TEST_CODE=$?
  DOTNET_TEST_TAIL="$(tail -n "$TAIL_LINES" "$DOTNET_TEST_LOG")"
else
  DOTNET_TEST_CODE="skipped"
  DOTNET_TEST_TAIL="(skipped via --no-dotnet-test)"
fi

# ──────────────────────────────────────────────────────────────────────────
# Emit JSON
# ──────────────────────────────────────────────────────────────────────────

TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
GIT_COMMIT="$(git rev-parse HEAD 2>/dev/null || echo 'unknown')"

# Build the JSON via node (already a project dep) for safe escaping.
node -e '
const fs = require("fs");
const out = {
  stage_id: process.argv[1],
  generated_at: process.argv[2],
  git_commit: process.argv[3],
  results: {
    dotnet_build: { exit: process.argv[4], tail: process.argv[5] },
    pnpm_build:   { exit: process.argv[6], tail: process.argv[7] },
    pnpm_test:    { exit: process.argv[8], tail: process.argv[9] },
    dotnet_test:  { exit: process.argv[10], tail: process.argv[11] },
  },
};
const allGreen = ["dotnet_build", "pnpm_build", "pnpm_test", "dotnet_test"]
  .every(k => out.results[k].exit === "0" || out.results[k].exit === "skipped");
out.all_green = allGreen;
fs.writeFileSync(process.argv[12], JSON.stringify(out, null, 2));
' \
  "$STAGE_ID" \
  "$TIMESTAMP" \
  "$GIT_COMMIT" \
  "$DOTNET_BUILD_CODE" "$DOTNET_BUILD_TAIL" \
  "$PNPM_BUILD_CODE"   "$PNPM_BUILD_TAIL" \
  "$PNPM_TEST_CODE"    "$PNPM_TEST_TAIL" \
  "$DOTNET_TEST_CODE"  "$DOTNET_TEST_TAIL" \
  "$OUT_FILE"

echo "[build-matrix] wrote $OUT_FILE" >&2

# Exit non-zero if any of the four exited non-zero. Caller decides whether
# to treat this as a Stop-blocking event; the file itself is written either
# way so the evidence bundle has the slot.
ANY_FAILED=0
for pair in "dotnet_build:$DOTNET_BUILD_CODE" "pnpm_build:$PNPM_BUILD_CODE" "pnpm_test:$PNPM_TEST_CODE" "dotnet_test:$DOTNET_TEST_CODE"; do
  label="${pair%%:*}"
  code="${pair#*:}"
  if [[ "$code" != "0" && "$code" != "skipped" ]]; then
    ANY_FAILED=1
    echo "[build-matrix] $label exited $code" >&2
  fi
done

if (( ANY_FAILED == 1 )); then
  exit 1
fi
exit 0
