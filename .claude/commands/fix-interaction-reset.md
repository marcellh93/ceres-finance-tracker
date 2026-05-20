Manually clear the `fix-interaction` state machine for this session.

Use this when the state machine appears stuck — the gate keeps blocking turns waiting for an answer to a question that no longer applies to what we're discussing. Defense (c) of three escape hatches per `.claude/skills/fix-interaction/references/dead-lock-defenses.md` §5.

Run the following via Bash:

```bash
SESSION_STATE="$CLAUDE_PROJECT_DIR/.claude/state/fix-interaction/$CLAUDE_SESSION_ID.json"
if [ -f "$SESSION_STATE" ]; then
  # Log the reset to the audit trail before deleting.
  RESET_LOG="$CLAUDE_PROJECT_DIR/.claude/state/fix-interaction/log.jsonl"
  TS=$(date -u +"%Y-%m-%dT%H:%M:%S.%3NZ" 2>/dev/null || date -u +"%Y-%m-%dT%H:%M:%SZ")
  printf '{"ts":"%s","from":"%s","to":"none","trigger":"manual-reset","reason":"slash-command"}\n' \
    "$TS" "$(jq -r '.awaiting // "unknown"' "$SESSION_STATE" 2>/dev/null || echo "unknown")" >> "$RESET_LOG"
  rm "$SESSION_STATE"
  echo "fix-interaction state cleared for this session."
else
  echo "No fix-interaction state found for this session — nothing to clear."
fi
```

After running, the next Stop hook starts with `awaiting=none` and the gate is closed until the next fix-mention triggers it.

If `jq` is not installed, the audit-log line records `"unknown"` for the prior state — this is non-fatal; the reset itself still happens.

For deeper escape hatches:
- **Per-session bypass** — set `CERES_SKIP_FIX_INTERACTION_HOOK=1` in the environment before starting the session; the three hooks all exit 0 unconditionally.
- **Disable entirely** — comment out the three hook entries in `.claude/settings.json` under `Stop` and `UserPromptSubmit` matchers referencing `fix-interaction/hooks/`.

The slash command is the in-chat option. The env var is the durable option. Disabling in settings.json is the nuclear option.
