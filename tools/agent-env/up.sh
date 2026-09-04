#!/usr/bin/env bash
# up.sh — boot an isolated ephemeral agent environment.
#
# Generates a fresh per-session schema (agent_<8hex>), runs migrations into it
# as the ceres_migrator role, then boots `dotnet run --launch-profile Smoke`
# on a free port in 5100..5499. Polls /api/health until 2xx or 30s timeout.
#
# Stdout is machine-readable (SCHEMA=, PORT=, PID=, APP_LOG=, APP_URL=).
# Logs and state live under .claude/state/runtime/ (gitignored).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RUNTIME_DIR="$REPO_ROOT/.claude/state/runtime"
DOWN_SCRIPT="$SCRIPT_DIR/down.sh"

mkdir -p "$RUNTIME_DIR"

err() { echo "[agent-env up] $*" >&2; }

# ──────────────────────────────────────────────────────────────────────────
# Step 0. Orphan cleanup. Before claiming a new SCHEMA, sweep dead PIDs
# and any pid file older than 60 minutes (crash-resilience).
# ──────────────────────────────────────────────────────────────────────────
cleanup_orphans() {
  shopt -s nullglob
  local pid_file pid mtime now age_minutes schema
  now="$(date +%s)"
  for pid_file in "$RUNTIME_DIR"/agent_*.pid; do
    schema="$(basename "$pid_file" .pid)"
    pid="$(cat "$pid_file" 2>/dev/null || true)"
    mtime="$(stat -f %m "$pid_file" 2>/dev/null || echo "$now")"
    age_minutes=$(( (now - mtime) / 60 ))

    if [[ -z "${pid:-}" ]] || ! kill -0 "$pid" 2>/dev/null; then
      err "orphan cleanup: PID '${pid:-<empty>}' for $schema is dead, tearing down"
      "$DOWN_SCRIPT" "$schema" || err "  down.sh failed for $schema (continuing)"
      continue
    fi
    if (( age_minutes > 60 )); then
      err "orphan cleanup: $schema is ${age_minutes}m old (>60), defensive teardown"
      "$DOWN_SCRIPT" "$schema" || err "  down.sh failed for $schema (continuing)"
    fi
  done
  shopt -u nullglob
}
cleanup_orphans

# ──────────────────────────────────────────────────────────────────────────
# Step 1. Generate SCHEMA.
# ──────────────────────────────────────────────────────────────────────────
if ! command -v uuidgen >/dev/null 2>&1; then
  err "uuidgen not found on PATH"
  exit 1
fi
SCHEMA="agent_$(uuidgen | tr -d '-' | tr '[:upper:]' '[:lower:]' | head -c 8)"
if [[ ! "$SCHEMA" =~ ^agent_[a-f0-9]{8}$ ]]; then
  err "generated SCHEMA '$SCHEMA' failed shape check"
  exit 1
fi

# ──────────────────────────────────────────────────────────────────────────
# Step 2. Find a free port in 5100..5499.
# ──────────────────────────────────────────────────────────────────────────
PORT=""
for candidate in $(seq 5100 5499); do
  if ! nc -z localhost "$candidate" >/dev/null 2>&1; then
    PORT="$candidate"
    break
  fi
done
if [[ -z "$PORT" ]]; then
  err "no free port available in 5100..5499 (all 400 are in use)"
  exit 1
fi

# ──────────────────────────────────────────────────────────────────────────
# Step 3. Read base connection strings from user-secrets and append Search Path.
# ──────────────────────────────────────────────────────────────────────────
if ! command -v dotnet >/dev/null 2>&1; then
  err "dotnet not found on PATH"
  exit 1
fi

SECRETS_RAW="$(dotnet user-secrets --project "$REPO_ROOT/ProjectCeres" list 2>/dev/null)" || {
  err "failed to read user-secrets from ProjectCeres"
  exit 1
}

BASE_APP_CONN="$(printf '%s\n' "$SECRETS_RAW" | sed -n 's/^ConnectionStrings:ApplicationConnection = //p')"
BASE_MIGRATE_CONN="$(printf '%s\n' "$SECRETS_RAW" | sed -n 's/^ConnectionStrings:MigrationConnection = //p')"
# AdminConnection (ceres_admin / BYPASSRLS) was never wired here. Registration
# does not touch it, but login does — AdminDbContext is resolved on that path —
# so the app threw "Format of the initialization string does not conform to
# specification" on an empty string and every login 500'd.
BASE_ADMIN_CONN="$(printf '%s\n' "$SECRETS_RAW" | sed -n 's/^ConnectionStrings:AdminConnection = //p')"

if [[ -z "$BASE_APP_CONN" ]]; then
  err "ConnectionStrings:ApplicationConnection not found in user-secrets"
  exit 1
fi
if [[ -z "$BASE_ADMIN_CONN" ]]; then
  err "no ConnectionStrings:AdminConnection in user secrets — login and every admin-context path will 500."
  exit 2
fi
if [[ -z "$BASE_MIGRATE_CONN" ]]; then
  err "ConnectionStrings:MigrationConnection not found in user-secrets"
  exit 1
fi

APP_CONN_WITH_SCHEMA="${BASE_APP_CONN};Search Path=${SCHEMA},public"
MIGRATION_CONN_WITH_SCHEMA="${BASE_MIGRATE_CONN};Search Path=${SCHEMA},public"
ADMIN_CONN_WITH_SCHEMA="${BASE_ADMIN_CONN};Search Path=${SCHEMA},public"

# ──────────────────────────────────────────────────────────────────────────
# Step 4. CREATE SCHEMA and grant ceres_app the access it needs.
# We connect via the OS account (Postgres.app local trust) to sidestep
# the role-vs-owner permission tangle. The schema is owned by ceres_migrator
# so EF migrations (which run as ceres_migrator) can create tables in it.
# ──────────────────────────────────────────────────────────────────────────
if ! psql -d project_ceres -v ON_ERROR_STOP=1 <<SQL >/dev/null 2>&1
CREATE SCHEMA "$SCHEMA" AUTHORIZATION ceres_migrator;
GRANT USAGE ON SCHEMA "$SCHEMA" TO ceres_app;
GRANT USAGE ON SCHEMA "$SCHEMA" TO ceres_admin;
GRANT CREATE ON SCHEMA "$SCHEMA" TO ceres_migrator;
-- USAGE on the schema is not access to the TABLES in it. The migration below runs
-- as ceres_migrator and creates every table owned by that role, so without this
-- ceres_app hits "42501: permission denied for table AspNetUsers" on the first
-- query and every authenticated flow 500s. ALTER DEFAULT PRIVILEGES applies to
-- objects created AFTER it, which is exactly the migration that follows.
ALTER DEFAULT PRIVILEGES FOR ROLE ceres_migrator IN SCHEMA "$SCHEMA"
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ceres_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ceres_migrator IN SCHEMA "$SCHEMA"
  GRANT USAGE, SELECT ON SEQUENCES TO ceres_app;
ALTER DEFAULT PRIVILEGES FOR ROLE ceres_migrator IN SCHEMA "$SCHEMA"
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ceres_admin;
ALTER DEFAULT PRIVILEGES FOR ROLE ceres_migrator IN SCHEMA "$SCHEMA"
  GRANT USAGE, SELECT ON SEQUENCES TO ceres_admin;
SQL
then
  err "CREATE SCHEMA \"$SCHEMA\" failed"
  exit 1
fi

# ──────────────────────────────────────────────────────────────────────────
# Step 5. Apply EF migrations into the new schema.
# ──────────────────────────────────────────────────────────────────────────
MIGRATE_LOG="$RUNTIME_DIR/$SCHEMA.migrate.log"
if ! dotnet ef database update \
      --project "$REPO_ROOT/ProjectCeres" \
      --context AppDbContext \
      --connection "$MIGRATION_CONN_WITH_SCHEMA" \
      >"$MIGRATE_LOG" 2>&1; then
  err "dotnet ef database update failed (see $MIGRATE_LOG)"
  "$DOWN_SCRIPT" "$SCHEMA" >/dev/null 2>&1 || true
  exit 1
fi

# ──────────────────────────────────────────────────────────────────────────
# Step 6. Boot the app in the background with the Smoke profile.
# We export ConnectionStrings__ApplicationConnection so the running app
# uses the schema-scoped connection; user-secrets stays untouched.
# ──────────────────────────────────────────────────────────────────────────
APP_LOG="$RUNTIME_DIR/$SCHEMA.app.log"
PID_FILE="$RUNTIME_DIR/$SCHEMA.pid"

# Roadmap § 12.12: ASP.NET loads user secrets ONLY under Development, and the Smoke
# profile is not Development — so Authentication:TokenLookupSecret:Secret arrives as
# the "configure-via-user-secrets" placeholder from appsettings.json, and every
# token-hashing path (register, login, password reset, email change) dies with
# "FormatException: not a valid Base-64 string" behind a generic 400. Read the
# developer's secret here and pass it through explicitly.
SECRETS_FILE="$HOME/.microsoft/usersecrets/fed8e2ba-0214-40f7-9bcf-edbed7627ca1/secrets.json"
TOKEN_LOOKUP_SECRET=""
if [[ -f "$SECRETS_FILE" ]]; then
  TOKEN_LOOKUP_SECRET=$(python3 -c "
import json,sys
try:
    d=json.load(open(sys.argv[1], encoding='utf-8-sig'))
    print(d.get('Authentication:TokenLookupSecret:Secret',''))
except Exception:
    print('')
" "$SECRETS_FILE" 2>/dev/null || echo "")
fi
if [[ -z "$TOKEN_LOOKUP_SECRET" ]]; then
  err "no Authentication:TokenLookupSecret:Secret in user secrets — auth flows will 400."
  err "set one with: dotnet user-secrets --project ProjectCeres set 'Authentication:TokenLookupSecret:Secret' \"\$(openssl rand -base64 32)\""
  exit 2
fi

(
  cd "$REPO_ROOT"
  ConnectionStrings__ApplicationConnection="$APP_CONN_WITH_SCHEMA" \
  ConnectionStrings__MigrationConnection="$MIGRATION_CONN_WITH_SCHEMA" \
  ConnectionStrings__AdminConnection="$ADMIN_CONN_WITH_SCHEMA" \
  Authentication__TokenLookupSecret__Secret="$TOKEN_LOOKUP_SECRET" \
  nohup dotnet run \
    --project "$REPO_ROOT/ProjectCeres" \
    --launch-profile Smoke \
    --urls "https://localhost:$PORT" \
    >"$APP_LOG" 2>&1 &
  echo $! >"$PID_FILE"
) </dev/null

# Give the shell a moment to flush the PID file.
sleep 0.1
if [[ ! -s "$PID_FILE" ]]; then
  err "failed to capture PID for dotnet run"
  "$DOWN_SCRIPT" "$SCHEMA" >/dev/null 2>&1 || true
  exit 2
fi
PID="$(cat "$PID_FILE")"

# ──────────────────────────────────────────────────────────────────────────
# Step 7. Poll /api/health (or fallback /) for up to 30s.
# ──────────────────────────────────────────────────────────────────────────
# HTTPS, not HTTP: all three auth cookies carry the __Host- prefix
# (SessionConstants.cs), which browsers refuse to store over plain HTTP. Serving
# HTTP here meant no cookie ever persisted — every state-changing request was
# rejected as an antiforgery failure, and agent-walk could never reach an
# authenticated page. --insecure on the probes: the dev cert is self-signed.
HEALTH_URL="https://localhost:$PORT/api/health"
DEADLINE=$(( $(date +%s) + 30 ))
HEALTHY=0
while (( $(date +%s) < DEADLINE )); do
  if ! kill -0 "$PID" 2>/dev/null; then
    err "dotnet process exited before becoming healthy (see $APP_LOG)"
    "$DOWN_SCRIPT" "$SCHEMA" >/dev/null 2>&1 || true
    exit 2
  fi
  CODE="$(curl -s -k -o /dev/null -w '%{http_code}' --max-time 2 "$HEALTH_URL" || echo 000)"
  if [[ "$CODE" =~ ^2[0-9][0-9]$ ]]; then
    HEALTHY=1
    break
  fi
  sleep 0.25
done

if (( HEALTHY == 0 )); then
  err "health probe at $HEALTH_URL never returned 2xx within 30s (last code: ${CODE:-n/a}, see $APP_LOG)"
  "$DOWN_SCRIPT" "$SCHEMA" >/dev/null 2>&1 || true
  exit 2
fi

# ──────────────────────────────────────────────────────────────────────────
# Step 8. Emit machine-readable handles on stdout.
# ──────────────────────────────────────────────────────────────────────────
RUNTIME_REL=".claude/state/runtime"
printf 'SCHEMA=%s\n' "$SCHEMA"
printf 'PORT=%s\n' "$PORT"
printf 'PID=%s\n' "$PID"
printf 'APP_LOG=%s/%s.app.log\n' "$RUNTIME_REL" "$SCHEMA"
printf 'APP_URL=https://localhost:%s\n' "$PORT"

exit 0
