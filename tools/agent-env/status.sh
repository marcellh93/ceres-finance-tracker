#!/usr/bin/env bash
# status.sh — list live agent environments.
# Prints SCHEMA, PID, PORT, AGE, HEALTH for each pid file under
# .claude/state/runtime/.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RUNTIME_DIR="$REPO_ROOT/.claude/state/runtime"

printf '%-20s %-6s %-6s %-6s %s\n' "SCHEMA" "PID" "PORT" "AGE" "HEALTH"

if [[ ! -d "$RUNTIME_DIR" ]]; then
  exit 0
fi

shopt -s nullglob
NOW="$(date +%s)"
FOUND=0
for pid_file in "$RUNTIME_DIR"/agent_*.pid; do
  FOUND=1
  SCHEMA="$(basename "$pid_file" .pid)"
  PID="$(cat "$pid_file" 2>/dev/null || true)"
  MTIME="$(stat -f %m "$pid_file" 2>/dev/null || echo "$NOW")"
  AGE_SEC=$(( NOW - MTIME ))
  if (( AGE_SEC < 60 )); then
    AGE="${AGE_SEC}s"
  elif (( AGE_SEC < 3600 )); then
    AGE="$(( AGE_SEC / 60 ))m"
  else
    AGE="$(( AGE_SEC / 3600 ))h"
  fi

  PORT="?"
  APP_LOG="$RUNTIME_DIR/$SCHEMA.app.log"
  if [[ -f "$APP_LOG" ]]; then
    # Pull "http://localhost:<port>" from the dotnet startup banner.
    PORT_MATCH="$(grep -oE 'http://localhost:[0-9]+' "$APP_LOG" 2>/dev/null | head -1 || true)"
    if [[ -n "$PORT_MATCH" ]]; then
      PORT="${PORT_MATCH##*:}"
    fi
  fi

  HEALTH="dead-pid"
  if [[ -n "${PID:-}" ]] && kill -0 "$PID" 2>/dev/null; then
    if [[ "$PORT" != "?" ]]; then
      CODE="$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 \
               "http://localhost:$PORT/api/health" 2>/dev/null || echo 000)"
      if [[ "$CODE" =~ ^2[0-9][0-9]$ ]]; then
        HEALTH="ok"
      else
        HEALTH="unhealthy($CODE)"
      fi
    else
      HEALTH="alive"
    fi
  fi

  printf '%-20s %-6s %-6s %-6s %s\n' \
    "$SCHEMA" "${PID:-?}" "$PORT" "$AGE" "$HEALTH"
done
shopt -u nullglob

if (( FOUND == 0 )); then
  echo "(no live agent environments)"
fi

exit 0
