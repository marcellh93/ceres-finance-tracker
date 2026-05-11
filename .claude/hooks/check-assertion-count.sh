#!/usr/bin/env bash
# .claude/hooks/check-assertion-count.sh
#
# PostToolUse hook on Edit|Write|MultiEdit.
# If the edited file is a test file, compare assertion count before vs after.
# A drop in assertions without a corresponding drop in test methods means
# Claude weakened existing tests rather than removing whole tests.
#
# PostToolUse cannot block (the edit has already happened), but its stderr
# is shown to Claude, who can then choose to revert.

set -uo pipefail

INPUT=$(cat)

FILE_PATH=$(echo "$INPUT" | python3 -c "
import json, sys
try:
    data = json.load(sys.stdin)
    print(data.get('tool_input', {}).get('file_path', ''))
except Exception:
    print('')
" 2>/dev/null)

if [[ -z "$FILE_PATH" ]] || [[ ! -f "$FILE_PATH" ]]; then
  exit 0
fi

# Same test-file detection as warn-on-test-edit.sh.
IS_TEST=0
if [[ "$FILE_PATH" =~ /Tests?/ ]] \
  || [[ "$FILE_PATH" =~ Tests\.cs$ ]] \
  || [[ "$FILE_PATH" =~ /__tests__/ ]] \
  || [[ "$FILE_PATH" =~ \.(test|spec)\.(ts|tsx|js|jsx)$ ]]; then
  IS_TEST=1
fi

if [[ $IS_TEST -eq 0 ]]; then
  exit 0
fi

# Get the file's content at HEAD (the version before this edit landed).
# If the file is new (not in HEAD), there's nothing to compare against.
if ! git rev-parse --git-dir >/dev/null 2>&1; then
  exit 0
fi

REL_PATH=$(git ls-files --full-name "$FILE_PATH" 2>/dev/null || echo "")
if [[ -z "$REL_PATH" ]]; then
  # New file, nothing to compare.
  exit 0
fi

OLD_CONTENT=$(git show "HEAD:$REL_PATH" 2>/dev/null || echo "")
if [[ -z "$OLD_CONTENT" ]]; then
  exit 0
fi

# Count assertions: xUnit Assert.*, FluentAssertions .Should(), Vitest expect().
count_assertions() {
  echo "$1" | grep -cE '(\bAssert\.[A-Z]|\.Should\(\)|\bexpect\()' || true
}

# Count test methods: [Fact], [Theory], it(, test(, describe(.
count_tests() {
  echo "$1" | grep -cE '(\[Fact\b|\[Theory\b|\bit\(|\btest\(|\bdescribe\()' || true
}

OLD_ASSERTS=$(count_assertions "$OLD_CONTENT")
NEW_ASSERTS=$(count_assertions "$(cat "$FILE_PATH")")
OLD_TESTS=$(count_tests "$OLD_CONTENT")
NEW_TESTS=$(count_tests "$(cat "$FILE_PATH")")

ASSERT_DELTA=$((NEW_ASSERTS - OLD_ASSERTS))
TEST_DELTA=$((NEW_TESTS - OLD_TESTS))

# If assertions dropped, but the test count didn't drop proportionally,
# existing tests were weakened.
if [[ $ASSERT_DELTA -lt 0 ]] && [[ $TEST_DELTA -ge 0 ]]; then
  {
    echo "WARNING: Test file assertion count dropped in $FILE_PATH"
    echo "  Assertions: $OLD_ASSERTS → $NEW_ASSERTS (delta: $ASSERT_DELTA)"
    echo "  Test methods: $OLD_TESTS → $NEW_TESTS (delta: $TEST_DELTA)"
    echo ""
    echo "Assertions were removed from existing tests without removing the tests themselves."
    echo "Per docs/testing.md § Rules, this is the shape of weakening tests to make them pass."
    echo "Either restore the assertions or, if the contract intentionally changed, state which case applies (see § Rules) and explain the change."
  } >&2
  # Non-zero, non-2 = non-blocking error. PostToolUse cannot block anyway.
  exit 1
fi

exit 0
