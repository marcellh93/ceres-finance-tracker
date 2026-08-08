#!/usr/bin/env bash
# Runs the Claude Code hook unit suites (node:test, no deps, ~60ms).
#
# Hooks gate every turn, so a broken hook blocks all work — but they are .js,
# which run-tests.sh does not track, so they never rode the .NET tier system.
# The 2026-08-07 evidence-bundle loop shipped through that gap: tests existed
# under __tests__/ and nothing ran them.
#
# Usage: scripts/test-hooks.sh

set -uo pipefail
cd "$(dirname "$0")/.."

shopt -s nullglob
SUITES=(.claude/hooks/lib/__tests__/*.test.js .claude/skills/*/hooks/__tests__/*.test.js)
shopt -u nullglob

if [[ ${#SUITES[@]} -eq 0 ]]; then
  echo "[test-hooks] no hook test suites found" >&2
  exit 0
fi

echo "[test-hooks] running ${#SUITES[@]} suite(s)"
node --test "${SUITES[@]}"
STATUS=$?

if [[ $STATUS -ne 0 ]]; then
  echo "[test-hooks] FAILED — a hook is broken; fix before relying on the gates." >&2
fi
exit $STATUS
