#!/usr/bin/env bash
# run-server.sh — Playwright webServer command for Stage 9.11 E2E.
# Bootstraps + migrates + wipes project_ceres_e2e, builds + stages the SPA bundle,
# asserts the manifest, then boots the app under ASPNETCORE_ENVIRONMENT=E2E over HTTPS.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DB="project_ceres_e2e"
APP_URL="${E2E_APP_URL:-https://localhost:7299}"

APP_CONN="Host=localhost;Database=${DB};Username=ceres_app;Password=ceres_app_dev_password"
ADMIN_CONN="Host=localhost;Database=${DB};Username=ceres_admin;Password=ceres_admin_dev_password"
MIGRATE_CONN="Host=localhost;Database=${DB};Username=ceres_migrator;Password=ceres_migrator_dev_password"
MIGRATOR_URI="postgresql://ceres_migrator:ceres_migrator_dev_password@localhost/${DB}"

err() { echo "[e2e/run-server] $*" >&2; }

# 1-3. Create DB (if absent) + provision roles + migrate — shared with CI.
"$REPO_ROOT/tools/ci/setup-test-db.sh" "$DB"

# 4. Guarded wipe — assert the target DB before any TRUNCATE. Connect as ceres_migrator
# (the table owner — TRUNCATE needs ownership). Spare lookup + migrations-history tables.
ACTUAL_DB="$(psql "$MIGRATOR_URI" -tAc 'SELECT current_database()')"
if [[ "$ACTUAL_DB" != "$DB" ]]; then
  err "FATAL: wipe target is '$ACTUAL_DB', expected '$DB' — refusing to delete"
  exit 1
fi
err "wiping user + auth data (sparing lookup tables)"
psql "$MIGRATOR_URI" -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
DO $$
DECLARE
  spare text[] := ARRAY['AccountTypes','CategoryTypes','Currencies','ReportTypes','__EFMigrationsHistory'];
  r record;
BEGIN
  FOR r IN
    SELECT tablename FROM pg_tables
    WHERE schemaname = 'public' AND tablename <> ALL(spare)
  LOOP
    EXECUTE format('TRUNCATE TABLE public.%I RESTART IDENTITY CASCADE', r.tablename);
  END LOOP;
END $$;
SQL

# 5. Build the SPA and stage it under wwwroot/dist for manifest-mode serving.
err "building SPA bundle"
pnpm --dir "$REPO_ROOT/ProjectCeres.Client" build >/dev/null
DEST="$REPO_ROOT/ProjectCeres/wwwroot/dist"
rm -rf "$DEST"
mkdir -p "$DEST"
cp -R "$REPO_ROOT/ProjectCeres.Client/dist/." "$DEST/"

# 6. Assert the manifest has BOTH keys the Razor host views look up.
MANIFEST="$DEST/.vite/manifest.json"
for key in "src/app/main.tsx" "src/main.tsx"; do
  if ! grep -q "\"$key\"" "$MANIFEST"; then
    err "FATAL: manifest missing key '$key' — SPA host would render blank. See vite.config.ts rollup inputs."
    exit 1
  fi
done

# 7. Boot the app under E2E over HTTPS. Secrets via env vars (user-secrets do not load
# outside Development); appsettings.E2E.json carries the non-secret config.
EMAIL_SINK="$REPO_ROOT/.e2e/emails"
rm -rf "$EMAIL_SINK"; mkdir -p "$EMAIL_SINK"

cd "$REPO_ROOT"
export ASPNETCORE_ENVIRONMENT=E2E
export ConnectionStrings__ApplicationConnection="$APP_CONN"
export ConnectionStrings__AdminConnection="$ADMIN_CONN"
TOKEN_LOOKUP_SECRET="$(openssl rand -base64 32)"  # separate assign so a failed openssl trips set -e
export Authentication__TokenLookupSecret__Secret="$TOKEN_LOOKUP_SECRET"
export Email__FileSink__Directory="$EMAIL_SINK"
err "booting app on $APP_URL"
exec dotnet run --project "$REPO_ROOT/ProjectCeres" --no-launch-profile --urls "$APP_URL"
