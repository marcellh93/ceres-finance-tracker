#!/usr/bin/env bash
# Runs the Claude Code hook unit suites (node:test, no deps, ~60ms).
#
# Hooks gate every turn, so a broken hook blocks all work — but they are .js,
# which run-tests.sh does not track, so they never rode the .NET tier system.
# The 2026-08-07 evidence-bundle loop shipped through that gap: tests existed
# under __tests__/ and nothing ran them.
#
# Two runners: node:test for the .js suites, plus any *.test.sh harnesses for
# the bash hooks (node:test cannot drive those). Both must be green.
#
# Usage: scripts/test-hooks.sh

set -uo pipefail
cd "$(dirname "$0")/.."

# Discover with `find`, not fixed globs: the first version of this script listed
# two glob patterns and silently missed .claude/hooks/__tests__/, reporting green
# while 46 tests never ran. Any __tests__ dir under .claude/ is picked up now.
SUITES=()
while IFS= read -r f; do SUITES+=("$f"); done < <(
  find .claude -type d -name node_modules -prune -o -type f -name '*.test.js' -print | sort
)

if [[ ${#SUITES[@]} -eq 0 ]]; then
  echo "[test-hooks] no hook test suites found" >&2
  exit 0
fi

# Shell-hook harnesses run under bash, not node:test.
SH_SUITES=()
while IFS= read -r f; do SH_SUITES+=("$f"); done < <(
  find .claude -type d -name node_modules -prune -o -type f -name '*.test.sh' -print | sort
)

echo "[test-hooks] running ${#SUITES[@]} node suite(s) + ${#SH_SUITES[@]} shell suite(s)"
node --test "${SUITES[@]}"
STATUS=$?

for sh in ${SH_SUITES[@]+"${SH_SUITES[@]}"}; do
  bash "$sh" || STATUS=1
done

if [[ $STATUS -ne 0 ]]; then
  echo "[test-hooks] FAILED — a hook is broken; fix before relying on the gates." >&2
fi
exit $STATUS
