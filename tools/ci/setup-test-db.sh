#!/usr/bin/env bash
# setup-test-db.sh — single source of Ceres database provisioning.
#
# Two modes:
#   setup-test-db.sh <db-name>
#     Legacy single-DB mode. Creates the named DB (if absent), applies the
#     role/grant SQL, and migrates it as ceres_migrator. Consumed by CI
#     (.github/workflows/ci.yml) and the E2E webServer bootstrap
#     (tools/e2e/run-server.sh) — unchanged behaviour, do not break it.
#
#   setup-test-db.sh --template --clones N
#     Stage 12.18 parallel-test provisioning. Migrates ONE template database
#     once, then clones it once per parallel bucket plus once per serial-collection
#     DB (see SERIAL_DB_SUFFIXES below for the current set) via
#     `CREATE DATABASE ... TEMPLATE`, which is a cheap filesystem copy
#     — far faster than running EF migrations that many times. Each clone still gets
#     its own role/grant pass, because grants are per-database and are not
#     guaranteed to survive the TEMPLATE copy.
#
# Assumes: psql, createdb, dotnet-ef on PATH; Postgres reachable at localhost:5432.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# The serial-collection databases — must match
# ProjectCeres.Tests/Integration/TestDatabaseRouter.cs SerialCollectionDatabases.
SERIAL_DB_SUFFIXES=(ratelimit mfaratelimit approle rls txfixture specs)

log() { echo "[setup-test-db] $*" >&2; }

create_if_absent() {  # $1 = db
  if ! psql -lqt | cut -d '|' -f1 | grep -qw "$1"; then
    log "creating database $1"
    createdb "$1"
  fi
}

provision_roles() {   # $1 = db
  log "applying setup-postgres-roles.sql to $1"
  psql -d "$1" -v ON_ERROR_STOP=1 -f "$REPO_ROOT/scripts/setup-postgres-roles.sql" >/dev/null
}

migrate_db() {        # $1 = db
  local conn="Host=localhost;Database=${1};Username=ceres_migrator;Password=ceres_migrator_dev_password"
  # Do NOT silence stdout: dotnet ef writes its build + migration errors there,
  # and a swallowed error turns a failed migration into an undiagnosable
  # "exit code 1" in CI.
  log "migrating $1"
  dotnet ef database update --project "$REPO_ROOT/ProjectCeres" \
    --context AppDbContext --connection "$conn"
}

provision_one() {     # $1 = db  — the full legacy single-DB flow
  create_if_absent "$1"
  provision_roles "$1"
  migrate_db "$1"
  log "done: $1 provisioned + migrated"
}

clone_from_template() {   # $1 = target db, $2 = template db
  # Drop any stale copy so the clone is deterministic run-to-run.
  psql -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"$1\";" >/dev/null
  log "cloning $1 from template $2"
  psql -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE \"$1\" TEMPLATE \"$2\";" >/dev/null
  # TEMPLATE copies schema + data + ownership, but role grants are per-database
  # and are not reliably carried over by the copy — re-apply them explicitly.
  provision_roles "$1"
}

if [[ "${1:-}" == "--template" ]]; then
  shift
  clones=1
  if [[ "${1:-}" == "--clones" ]]; then
    clones="${2:?--clones needs a count}"
  fi

  TEMPLATE="project_ceres_test_template"

  # 0. Re-enable connections on the template if a PRIOR run left it
  #    datallowconn=false (step 2 below). Postgres refuses ALL new connections
  #    to such a DB — superuser included — so without this, re-provisioning
  #    (provision_one → provision_roles/migrate_db, which both connect to the
  #    template) fails with "database is not currently accepting connections".
  #    This makes --template idempotent for local re-runs and persistent CI
  #    runners. No-op on a first run (the row simply doesn't exist yet).
  log "resetting $TEMPLATE datallowconn/datistemplate (if it exists from a prior run)"
  psql -d postgres -v ON_ERROR_STOP=1 -c \
    "UPDATE pg_database SET datistemplate=false, datallowconn=true WHERE datname='$TEMPLATE';" >/dev/null

  # 1. Build the template once (idempotent — full existing single-DB flow).
  provision_one "$TEMPLATE"

  # 2. Mark it a real template and forbid connections, so a concurrent
  #    CREATE DATABASE ... TEMPLATE is guaranteed to see zero other sessions
  #    on it (PG16 § 23.3 — a template with open connections cannot be cloned).
  log "marking $TEMPLATE as datistemplate=true, datallowconn=false"
  psql -d postgres -v ON_ERROR_STOP=1 -c \
    "UPDATE pg_database SET datistemplate=true, datallowconn=false WHERE datname='$TEMPLATE';" >/dev/null

  # 3. Clone the N bucket DBs.
  for k in $(seq 1 "$clones"); do
    clone_from_template "project_ceres_test_${k}" "$TEMPLATE"
  done

  # 4. Clone the serial-collection DBs (must match TestDatabaseRouter.SerialCollectionDatabases).
  for s in "${SERIAL_DB_SUFFIXES[@]}"; do
    clone_from_template "project_ceres_test_${s}" "$TEMPLATE"
  done

  # 5. Backstop: provision the legacy shared DB too. A handful of test classes have no
  #    [Collection] and build ad-hoc factories (ArchitectureTests, EmailChangeCancelTests,
  #    PreAuthWritesUnderRlsTests) — they fall through TestDatabaseRouter straight to
  #    LegacyDatabase regardless of CloneCount. They're serial/uncollected, so they don't
  #    race the bucket DBs; they just need project_ceres_test to actually EXIST in a clean
  #    --template run, which a stale local dev DB was previously masking.
  provision_one "project_ceres_test"

  log "done: template + ${clones} bucket DBs + ${#SERIAL_DB_SUFFIXES[@]} serial DBs + legacy DB provisioned"
else
  # Legacy single-DB mode (E2E uses this: setup-test-db.sh project_ceres_e2e).
  provision_one "${1:?usage: setup-test-db.sh <db-name>  |  setup-test-db.sh --template --clones N}"
fi
