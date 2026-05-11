#!/usr/bin/env bash
# .claude/hooks/warn-on-test-edit.sh
#
# PreToolUse hook on Edit|Write|MultiEdit.
# When the target file is a test file, injects a reminder via additionalContext.
# Never blocks — only reminds.

set -uo pipefail

INPUT=$(cat)

# Extract the file_path from the tool_input JSON.
# Works for Edit, Write, MultiEdit (all use tool_input.file_path).
FILE_PATH=$(echo "$INPUT" | python3 -c "
import json, sys
try:
    data = json.load(sys.stdin)
    print(data.get('tool_input', {}).get('file_path', ''))
except Exception:
    print('')
" 2>/dev/null)

# Project Ceres test file conventions:
#   - Server: anything under Tests/ or matching *Tests.cs
#   - Client: anything under __tests__/ or matching *.test.ts(x) / *.spec.ts(x)
if [[ -z "$FILE_PATH" ]]; then
  exit 0
fi

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

# Emit JSON additionalContext per hooks reference. Claude Code wraps this
# in a system-reminder and inserts it at the point the hook fired.
cat <<'EOF'
{
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "additionalContext": "You are about to modify a test file. Per docs/testing.md § Rules, before editing, state which case applies: (1) you are adding a NEW test, (2) the production CONTRACT intentionally changed, or (3) the test's EXPECTED VALUE was wrong. Do not modify a test solely to make a failing implementation pass. Do not add [Fact(Skip=...)], do not comment out assertions, do not wrap calls in try/catch to silence failures."
  }
}
EOF
exit 0
