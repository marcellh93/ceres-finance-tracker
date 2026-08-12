#!/usr/bin/env bash
# Tests for the two bash hooks. node:test cannot drive these, so this is a
# minimal TAP-ish harness with the same pass/fail contract as the JS suites.
#
# warn-on-test-edit.sh   — PreToolUse advisory, must never block.
# check-assertion-count.sh — PostToolUse, exits 1 when assertions were removed
#   from existing tests without removing the tests themselves. That exit code is
#   the whole point: it is how a weakened test surfaces to the agent.

set -uo pipefail
cd "$(dirname "$0")/../../.."

PASS=0; FAIL=0
ok()   { PASS=$((PASS+1)); echo "✔ $1"; }
nope() { FAIL=$((FAIL+1)); echo "✖ $1"; echo "    $2"; }

payload() { printf '{"tool_name":"Edit","tool_input":{"file_path":"%s"}}' "$1"; }

# ── warn-on-test-edit: fires on test files ───────────────────────────────
warn_fires() {
  payload "$1" | bash .claude/hooks/warn-on-test-edit.sh 2>/dev/null | grep -q additionalContext
}

for f in \
  "/r/ProjectCeres.Tests/Unit/SavingsRateTests.cs" \
  "/r/ProjectCeres.Client/src/__tests__/App.test.tsx" \
  "/r/a/b.test.ts" \
  "/r/a/b.spec.tsx" \
  "/r/a/b.test.js" ; do
  if warn_fires "$f"; then ok "warn fires: $f"; else nope "warn fires: $f" "expected additionalContext"; fi
done

for f in \
  "/r/ProjectCeres/Program.cs" \
  "/r/ProjectCeres.Client/src/App.tsx" \
  "/r/docs/testing.md" \
  "/r/ProjectCeres/Services/TestDataSeeder.cs" ; do
  if warn_fires "$f"; then nope "warn quiet: $f" "fired on a non-test file"; else ok "warn quiet: $f"; fi
done

# Advisory contract: it must never block, whatever it is handed.
for p in '{"tool_name":"Edit","tool_input":{"file_path":"/r/x.test.ts"}}' \
         '{"tool_name":"Edit","tool_input":{}}' \
         '{}' \
         'not json' ; do
  printf '%s' "$p" | bash .claude/hooks/warn-on-test-edit.sh >/dev/null 2>&1
  rc=$?
  if [[ $rc -eq 0 ]]; then ok "warn exits 0 for: ${p:0:38}"; else nope "warn exits 0 for: ${p:0:38}" "exit=$rc"; fi
done

# ── check-assertion-count: the weakening detector ────────────────────────
TD=$(mktemp -d)
REPO="$TD/repo"
mkdir -p "$REPO"
git -C "$REPO" init -q
git -C "$REPO" config user.email t@t
git -C "$REPO" config user.name t
mkdir -p "$REPO/Tests"

commit_baseline() {
  printf '%s' "$1" > "$REPO/Tests/XTests.cs"
  git -C "$REPO" add -A >/dev/null 2>&1
  git -C "$REPO" commit -qm base >/dev/null 2>&1
}

run_check() {
  ( cd "$REPO" && printf '{"tool_name":"Edit","tool_input":{"file_path":"%s"}}' "$REPO/Tests/XTests.cs" \
    | bash "$OLDPWD/.claude/hooks/check-assertion-count.sh" 2>&1 )
  return $?
}

BASE='[Fact] public void A(){ Assert.Equal(1,1); Assert.True(true); }
[Fact] public void B(){ Assert.Equal(2,2); }'

# 1. assertions removed, tests kept -> must warn (exit 1)
commit_baseline "$BASE"
printf '%s' '[Fact] public void A(){ Assert.Equal(1,1); }
[Fact] public void B(){ }' > "$REPO/Tests/XTests.cs"
OUT=$(run_check); RC=$?
if [[ $RC -eq 1 ]] && grep -q "assertion count dropped" <<<"$OUT"; then
  ok "flags assertions removed while tests kept"
else
  nope "flags assertions removed while tests kept" "exit=$RC out=${OUT:0:80}"
fi

# 2. whole test removed -> legitimate, must stay silent
commit_baseline "$BASE"
printf '%s' '[Fact] public void A(){ Assert.Equal(1,1); Assert.True(true); }' > "$REPO/Tests/XTests.cs"
OUT=$(run_check); RC=$?
if [[ $RC -eq 0 ]]; then ok "silent when a whole test is removed"; else nope "silent when a whole test is removed" "exit=$RC"; fi

# 3. assertions added -> silent
commit_baseline "$BASE"
printf '%s' "$BASE
[Fact] public void C(){ Assert.Equal(3,3); }" > "$REPO/Tests/XTests.cs"
OUT=$(run_check); RC=$?
if [[ $RC -eq 0 ]]; then ok "silent when assertions are added"; else nope "silent when assertions are added" "exit=$RC"; fi

# 4. unchanged -> silent
commit_baseline "$BASE"
OUT=$(run_check); RC=$?
if [[ $RC -eq 0 ]]; then ok "silent when the file is unchanged"; else nope "silent when the file is unchanged" "exit=$RC"; fi

# 5. untracked (new) test file -> nothing to compare, silent
printf '%s' "$BASE" > "$REPO/Tests/NewTests.cs"
OUT=$( cd "$REPO" && printf '{"tool_name":"Edit","tool_input":{"file_path":"%s"}}' "$REPO/Tests/NewTests.cs" \
  | bash "$OLDPWD/.claude/hooks/check-assertion-count.sh" 2>&1 ); RC=$?
if [[ $RC -eq 0 ]]; then ok "silent for a brand-new test file"; else nope "silent for a brand-new test file" "exit=$RC"; fi

# 6. non-test file -> silent even if content shrinks
mkdir -p "$REPO/src"
printf 'Assert.Equal(1,1); Assert.True(true);' > "$REPO/src/Prog.cs"
git -C "$REPO" add -A >/dev/null 2>&1; git -C "$REPO" commit -qm p >/dev/null 2>&1
printf 'nothing' > "$REPO/src/Prog.cs"
OUT=$( cd "$REPO" && printf '{"tool_name":"Edit","tool_input":{"file_path":"%s"}}' "$REPO/src/Prog.cs" \
  | bash "$OLDPWD/.claude/hooks/check-assertion-count.sh" 2>&1 ); RC=$?
if [[ $RC -eq 0 ]]; then ok "silent for a non-test file"; else nope "silent for a non-test file" "exit=$RC"; fi

# 7. malformed payload -> silent
OUT=$(printf 'not json' | bash .claude/hooks/check-assertion-count.sh 2>&1); RC=$?
if [[ $RC -eq 0 ]]; then ok "silent on a malformed payload"; else nope "silent on a malformed payload" "exit=$RC"; fi

# 8. nonexistent path -> silent
OUT=$(payload /no/such/Tests/X.cs | bash .claude/hooks/check-assertion-count.sh 2>&1); RC=$?
if [[ $RC -eq 0 ]]; then ok "silent for a nonexistent file"; else nope "silent for a nonexistent file" "exit=$RC"; fi

rm -rf "$TD"

echo
echo "ℹ tests $((PASS+FAIL))"
echo "ℹ pass $PASS"
echo "ℹ fail $FAIL"
[[ $FAIL -eq 0 ]]
