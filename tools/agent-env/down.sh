#!/usr/bin/env bash
# down.sh — tear down an ephemeral agent environment.
# Usage: down.sh <schema-name>
# Idempotent: missing schema or dead PID still exits 0.
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <schema-name>" >&2
  exit 64
fi

SCHEMA="$1"

# Validate schema name shape so we never feed garbage to psql or rm.
if [[ ! "$SCHEMA" =~ ^agent_[a-f0-9]{8}$ ]]; then
  echo "error: schema name must match 'agent_[a-f0-9]{8}', got '$SCHEMA'" >&2
  exit 65
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RUNTIME_DIR="$REPO_ROOT/.claude/state/runtime"
PID_FILE="$RUNTIME_DIR/$SCHEMA.pid"
APP_LOG="$RUNTIME_DIR/$SCHEMA.app.log"
MIGRATE_LOG="$RUNTIME_DIR/$SCHEMA.migrate.log"

# 1. Stop the dotnet process (SIGTERM, then SIGKILL after 5s grace).
if [[ -f "$PID_FILE" ]]; then
  PID="$(cat "$PID_FILE" 2>/dev/null || true)"
  if [[ -n "${PID:-}" ]] && kill -0 "$PID" 2>/dev/null; then
    kill "$PID" 2>/dev/null || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do
      if ! kill -0 "$PID" 2>/dev/null; then
        break
      fi
      sleep 0.5
    done
    if kill -0 "$PID" 2>/dev/null; then
      echo "warn: PID $PID did not exit after SIGTERM, sending SIGKILL" >&2
      kill -9 "$PID" 2>/dev/null || true
    fi
  fi
fi

# 2. Drop the schema. IF EXISTS keeps this idempotent.
#    We connect as the OS superuser (your OS user) via the default
#    Postgres.app socket — no password needed locally, and we sidestep the
#    role-vs-owner question entirely.
if command -v psql >/dev/null 2>&1; then
  if ! psql -d project_ceres -v ON_ERROR_STOP=1 \
       -c "DROP SCHEMA IF EXISTS \"$SCHEMA\" CASCADE;" >/dev/null 2>&1; then
    echo "warn: DROP SCHEMA \"$SCHEMA\" failed (continuing)" >&2
  fi
else
  echo "warn: psql not on PATH; skipping schema drop" >&2
fi

# 3. Remove runtime state files.
rm -f "$PID_FILE" "$APP_LOG" "$MIGRATE_LOG"

exit 0
