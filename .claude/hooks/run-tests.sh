#!/usr/bin/env bash
# .claude/hooks/run-tests.sh
#
# Stop hook: blocks Claude from ending its turn until the relevant tests
# pass. The hook picks the SMALLEST sufficient test scope based on which
# files the session actually wrote — every turn should not pay for the
# full 4-minute integration suite.
#
# Exit 0 = allow stop. Exit 2 = block, stderr is shown to Claude.
#
# Bypass options:
#   - CERES_SKIP_STOP_HOOK=1 in shell env before launching Claude Code
#   - touch .claude/state/run-tests/.paused for an in-session pause
#     (rm to resume; no restart required)
#
# Trigger rule:
#   - The hook fires ONLY when this session wrote tracked code SINCE the
#     last Stop event. After each Stop check, the session's write list is
#     cleared so a pure-discussion turn that follows exits 0 immediately.
#   - "Tracked code" = .cs / .ts / .tsx / .csproj / .sln, recorded by
#     .claude/hooks/track-session-writes.js.
#
# Tier rule (NEW 2026-05-17):
#   TIER 0 (skip)           — only .tsx / .ts changed.  No .NET impact, exit 0.
#   TIER 1 (unit-only)      — only files under ProjectCeres/ (production .cs / .csproj)
#                             AND no test files / no .sln.  Run unit tests only.
#   TIER 2 (full suite)     — any test file, .sln, or anything ambiguous.
#                             Run the whole suite.
#
# Filter strategy:
#   - Unit tests live under ProjectCeres.Tests/Unit/  (namespace ProjectCeres.Tests.Unit.*)
#   - Integration tests live under ProjectCeres.Tests/Integration/
#   - "Common" / "Filters" directories are framework helpers and ride with TIER 2.
#   - --filter "FullyQualifiedName~ProjectCeres.Tests.Unit" runs only the Unit
#     namespace.  No [Trait] attributes exist in this codebase (verified
#     2026-05-17 via rg).

set -uo pipefail

# Read stdin once — we extract session_id and reuse the payload nowhere else.
STDIN_PAYLOAD=$(cat)
SESSION_ID=$(printf '%s' "$STDIN_PAYLOAD" | python3 -c 'import json,sys
try:
  d = json.load(sys.stdin)
  print(d.get("session_id",""))
except Exception:
  print("")' 2>/dev/null)

# Env-var escape hatch (requires restart to toggle).
if [[ "${CERES_SKIP_STOP_HOOK:-0}" == "1" ]]; then
  exit 0
fi

# In-session pause toggle (no restart required).
if [[ -e ".claude/state/run-tests/.paused" ]]; then
  exit 0
fi

# Per-session write check.
#
# If THIS session has no recorded writes since the last Stop check, exit
# cleanly. After we confirm writes exist, we CLEAR the list before running
# tests so the next turn starts from empty (and a pure-discussion turn
# exits 0).
#
# Fallback to working-tree check ONLY when session_id is unavailable.
STATE_FILE=""
SESSION_FILES_JSON=""
if [[ -n "$SESSION_ID" ]]; then
  STATE_FILE=".claude/state/run-tests/${SESSION_ID}.json"
  if [[ ! -s "$STATE_FILE" ]]; then
    exit 0
  fi

  SESSION_FILES_JSON=$(cat "$STATE_FILE")

  # Session HAS writes. Clear the list now so the next turn only fires if
  # NEW writes come in. Done BEFORE running tests so a long dotnet test
  # doesn't leave stale state.
  : > "$STATE_FILE"
else
  # No session_id — fall back to git working tree. Always TIER 2 in this
  # fallback path because we have no per-file change list to tier on.
  if git rev-parse --git-dir >/dev/null 2>&1; then
    CHANGED=$(git diff --name-only HEAD -- '*.cs' '*.ts' '*.tsx' '*.csproj' '*.sln' 2>/dev/null)
    CHANGED_UNTRACKED=$(git ls-files --others --exclude-standard -- '*.cs' '*.ts' '*.tsx' 2>/dev/null)
    if [[ -z "$CHANGED" ]] && [[ -z "$CHANGED_UNTRACKED" ]]; then
      exit 0
    fi
  fi
fi

# Only run if there's a .NET solution in the repo root.
if ! ls *.sln >/dev/null 2>&1 && ! ls *.csproj >/dev/null 2>&1; then
  exit 0
fi

# Decide tier from the session's file list.
#
# TIER classification (computed by python3 — keeps the shell glob-free):
#   0 = skip dotnet test (frontend-only)
#   1 = unit tests only (production .cs / .csproj inside ProjectCeres/)
#   2 = full suite      (test files, .sln, or any ambiguous mix)
#
# Empty file list (legacy fallback path) defaults to TIER 2.
TIER=2
if [[ -n "$SESSION_FILES_JSON" ]]; then
  TIER=$(printf '%s' "$SESSION_FILES_JSON" | python3 -c '
import json, sys
try:
  d = json.load(sys.stdin)
  files = d.get("files", []) if isinstance(d, dict) else []
except Exception:
  print(2); sys.exit(0)

if not files:
  print(2); sys.exit(0)

has_dotnet = False
has_test = False
has_sln = False
has_csproj_app = False

for f in files:
  ext = f.rsplit(".", 1)[-1].lower() if "." in f else ""
  if ext == "sln":
    has_sln = True
  if ext == "cs":
    has_dotnet = True
    if f.startswith("ProjectCeres.Tests/"):
      has_test = True
  if ext == "csproj":
    has_dotnet = True
    if f.startswith("ProjectCeres.Tests/"):
      has_test = True
    else:
      has_csproj_app = True

# TIER 0 — purely frontend (.tsx / .ts), no dotnet impact at all.
if not has_dotnet and not has_sln:
  print(0); sys.exit(0)

# TIER 2 — anything touching test code, the solution file, or both.
if has_test or has_sln:
  print(2); sys.exit(0)

# TIER 1 — only production .cs / .csproj under ProjectCeres/.
print(1)
' 2>/dev/null || echo 2)
fi

case "$TIER" in
  0)
    echo "[stop-hook] tier 0: frontend-only writes — skipping dotnet test." >&2
    exit 0
    ;;
  1)
    SCOPE="unit"
    FILTER_ARGS=(--filter "FullyQualifiedName~ProjectCeres.Tests.Unit")
    ;;
  *)
    SCOPE="full"
    FILTER_ARGS=()
    ;;
esac

# Stream output to a tailable log file so you can watch progress in another
# terminal:
#
#   tail -f .claude/state/run-tests/last.log
#
# We still capture the output for failure-tail printing and skipped-count
# detection below.
LOG_DIR=".claude/state/run-tests"
LOG_FILE="$LOG_DIR/last.log"
mkdir -p "$LOG_DIR"
: > "$LOG_FILE"

echo "[stop-hook] tier $TIER ($SCOPE) — running dotnet test ${FILTER_ARGS[*]:-(no filter)}  ->  tail -f $LOG_FILE" >&2

# --nologo: drop the .NET banner. --verbosity normal: per-test progress in
# the log. `tee` writes live; PIPESTATUS[0] preserves dotnet's exit code.
TEST_OUTPUT=$(dotnet test --nologo --verbosity normal "${FILTER_ARGS[@]}" 2>&1 | tee "$LOG_FILE")
TEST_EXIT=${PIPESTATUS[0]}

# Detect skipped tests even on a passing run.
SKIPPED_COUNT=$(echo "$TEST_OUTPUT" | grep -oE "Skipped:[[:space:]]*[0-9]+" | grep -oE "[0-9]+" | head -1)
SKIPPED_COUNT=${SKIPPED_COUNT:-0}

if [[ $TEST_EXIT -ne 0 ]]; then
  {
    echo "BLOCKED: dotnet test ($SCOPE scope) failed. Fix the production code, not the tests."
    echo ""
    echo "Test output (tail):"
    echo "$TEST_OUTPUT" | tail -40
    echo ""
    echo "Per docs/testing.md § Rules, state which case applies:"
    echo "  (1) Production code is wrong → fix it."
    echo "  (2) Test's expected value was wrong → name the behavior, then update."
    echo "  (3) Contract intentionally changed → name the change, then update."
  } >&2
  exit 2
fi

if [[ $SKIPPED_COUNT -gt 0 ]]; then
  {
    echo "BLOCKED: $SKIPPED_COUNT test(s) skipped. Definition of Done requires zero skipped tests."
    echo "Either un-skip them, or add an inline comment with a tracked issue link to each Skip attribute."
  } >&2
  exit 2
fi

exit 0
