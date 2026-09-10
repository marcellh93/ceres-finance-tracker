#!/usr/bin/env bash
# setup-test-db.sh — single source of Ceres database provisioning.
# Creates the named DB (if absent), applies the role/grant SQL, and migrates it
# as ceres_migrator. Consumed by both CI (.github/workflows/ci.yml) and the E2E
# webServer bootstrap (tools/e2e/run-server.sh), so the two never drift.
#
# Usage:  tools/ci/setup-test-db.sh <db-name>
# Assumes: psql, createdb, dotnet-ef on PATH; Postgres reachable at localhost:5432.
set -euo pipefail

DB="${1:?usage: setup-test-db.sh <db-name>}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
MIGRATE_CONN="Host=localhost;Database=${DB};Username=ceres_migrator;Password=ceres_migrator_dev_password"

log() { echo "[setup-test-db] $*" >&2; }

# 1. Create DB if missing.
if ! psql -lqt | cut -d '|' -f1 | grep -qw "$DB"; then
  log "creating database $DB"
  createdb "$DB"
fi

# 2. Provision roles/grants for THIS database (grants are per-database).
log "applying setup-postgres-roles.sql to $DB"
psql -d "$DB" -v ON_ERROR_STOP=1 -f "$REPO_ROOT/scripts/setup-postgres-roles.sql" >/dev/null

# 3. Migrate as ceres_migrator (the --connection flag is mandatory — no design-time factory).
log "migrating $DB"
dotnet ef database update --project "$REPO_ROOT/ProjectCeres" \
  --context AppDbContext --connection "$MIGRATE_CONN" >/dev/null

log "done: $DB provisioned + migrated"
