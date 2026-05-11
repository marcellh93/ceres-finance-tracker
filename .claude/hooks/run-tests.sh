#!/usr/bin/env bash
# .claude/hooks/run-tests.sh
#
# Stop hook: blocks Claude from ending its turn until tests pass.
# Exit 0 = allow stop. Exit 2 = block, stderr is shown to Claude.
#
# To bypass during exploration, set CERES_SKIP_STOP_HOOK=1 in your shell
# before launching Claude Code.

set -uo pipefail

# Read stdin so Claude Code doesn't get a SIGPIPE; we don't currently use it.
cat > /dev/null

# Escape hatch for exploratory sessions where you don't want test enforcement.
if [[ "${CERES_SKIP_STOP_HOOK:-0}" == "1" ]]; then
  exit 0
fi

# Skip if the working tree has no code changes vs HEAD.
# Brainstorming/discussion turns don't touch files; no point running tests.
if git rev-parse --git-dir >/dev/null 2>&1; then
  CHANGED=$(git diff --name-only HEAD -- '*.cs' '*.ts' '*.tsx' '*.csproj' '*.sln' 2>/dev/null)
  CHANGED_UNTRACKED=$(git ls-files --others --exclude-standard -- '*.cs' '*.ts' '*.tsx' 2>/dev/null)
  if [[ -z "$CHANGED" ]] && [[ -z "$CHANGED_UNTRACKED" ]]; then
    exit 0
  fi
fi

# Only run if there's a .NET solution in the repo root.
if ! ls *.sln >/dev/null 2>&1 && ! ls *.csproj >/dev/null 2>&1; then
  exit 0
fi

# --nologo: quieter output. --verbosity quiet: don't dump build output on success.
TEST_OUTPUT=$(dotnet test --nologo --verbosity quiet 2>&1)
TEST_EXIT=$?

# Detect skipped tests even on a passing run.
SKIPPED_COUNT=$(echo "$TEST_OUTPUT" | grep -oE "Skipped:[[:space:]]*[0-9]+" | grep -oE "[0-9]+" | head -1)
SKIPPED_COUNT=${SKIPPED_COUNT:-0}

if [[ $TEST_EXIT -ne 0 ]]; then
  {
    echo "BLOCKED: dotnet test failed. Fix the production code, not the tests."
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
