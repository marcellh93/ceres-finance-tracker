-- Project Ceres — PostgreSQL role setup for Row-Level Security (Stage 7.5, ADR-0068).
--
-- Creates three roles:
--   ceres_app       — application runtime. DML only, no DDL, NO BYPASSRLS. Subject to RLS.
--   ceres_admin     — Admin services + IUserJobRunner background work. DML only, BYPASSRLS.
--   ceres_migrator  — `dotnet ef database update` only. DDL + BYPASSRLS.
--
-- Idempotent: safe to run twice. Each DO block existence-checks before CREATE; passwords
-- are rotated via ALTER ROLE on every run (so changing the env var updates the password).
--
-- Usage (local dev):
--   psql -d project_ceres      -f scripts/setup-postgres-roles.sql
--   psql -d project_ceres_test -f scripts/setup-postgres-roles.sql
--
-- Production: the deploy operator overrides the three passwords via env vars before
-- running the script, e.g.:
--   CERES_APP_PASSWORD='<strong>' CERES_ADMIN_PASSWORD='<strong>' \
--   CERES_MIGRATOR_PASSWORD='<strong>' \
--   psql -d project_ceres -f scripts/setup-postgres-roles.sql
--
-- The default passwords below are for LOCAL DEVELOPMENT ONLY. Override in production.

\set ON_ERROR_STOP on

-- Materialise env-var-or-default into a temp table so the DO blocks can read them.
-- psql :variable substitution does not work inside DO $$ ... $$ blocks because the
-- contents are passed to the server as an opaque string literal.
CREATE TEMP TABLE _ceres_role_setup (role_name text PRIMARY KEY, password text);

\set ceres_app_password      `echo "${CERES_APP_PASSWORD:-ceres_app_dev_password}"`
\set ceres_admin_password    `echo "${CERES_ADMIN_PASSWORD:-ceres_admin_dev_password}"`
\set ceres_migrator_password `echo "${CERES_MIGRATOR_PASSWORD:-ceres_migrator_dev_password}"`

INSERT INTO _ceres_role_setup (role_name, password) VALUES
    ('ceres_app',      :'ceres_app_password'),
    ('ceres_admin',    :'ceres_admin_password'),
    ('ceres_migrator', :'ceres_migrator_password');

-- ceres_app — runtime role, subject to RLS.
DO $$
DECLARE
    pw text;
BEGIN
    SELECT password INTO pw FROM _ceres_role_setup WHERE role_name = 'ceres_app';
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'ceres_app') THEN
        EXECUTE format(
            'CREATE ROLE ceres_app LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS',
            pw);
    ELSE
        EXECUTE format(
            'ALTER ROLE ceres_app WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS',
            pw);
    END IF;
END
$$;

-- ceres_admin — admin services + background jobs. BYPASSRLS, no DDL.
DO $$
DECLARE
    pw text;
BEGIN
    SELECT password INTO pw FROM _ceres_role_setup WHERE role_name = 'ceres_admin';
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'ceres_admin') THEN
        EXECUTE format(
            'CREATE ROLE ceres_admin LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE BYPASSRLS',
            pw);
    ELSE
        EXECUTE format(
            'ALTER ROLE ceres_admin WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE BYPASSRLS',
            pw);
    END IF;
END
$$;

-- ceres_migrator — `dotnet ef database update` only. DDL + BYPASSRLS.
DO $$
DECLARE
    pw text;
BEGIN
    SELECT password INTO pw FROM _ceres_role_setup WHERE role_name = 'ceres_migrator';
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'ceres_migrator') THEN
        EXECUTE format(
            'CREATE ROLE ceres_migrator LOGIN PASSWORD %L NOSUPERUSER CREATEDB NOCREATEROLE BYPASSRLS',
            pw);
    ELSE
        EXECUTE format(
            'ALTER ROLE ceres_migrator WITH LOGIN PASSWORD %L NOSUPERUSER CREATEDB NOCREATEROLE BYPASSRLS',
            pw);
    END IF;
END
$$;

DROP TABLE _ceres_role_setup;

-- Grant database CONNECT to all three roles.
GRANT CONNECT ON DATABASE :"DBNAME" TO ceres_app, ceres_admin, ceres_migrator;

-- Schema-level grants on the public schema.
GRANT USAGE ON SCHEMA public TO ceres_app, ceres_admin, ceres_migrator;
GRANT CREATE ON SCHEMA public TO ceres_migrator;

-- Default privileges on tables created by ceres_migrator: ceres_app + ceres_admin get DML.
ALTER DEFAULT PRIVILEGES FOR ROLE ceres_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ceres_app, ceres_admin;
ALTER DEFAULT PRIVILEGES FOR ROLE ceres_migrator IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO ceres_app, ceres_admin;

-- Existing tables: grant DML on tables already in the schema.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO ceres_app, ceres_admin;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO ceres_app, ceres_admin;

-- Sanity check: confirm role attributes are what we expect.
DO $$
DECLARE
    app_bypass     boolean;
    admin_bypass   boolean;
    migrator_super boolean;
BEGIN
    SELECT rolbypassrls INTO app_bypass     FROM pg_roles WHERE rolname = 'ceres_app';
    SELECT rolbypassrls INTO admin_bypass   FROM pg_roles WHERE rolname = 'ceres_admin';
    SELECT rolsuper     INTO migrator_super FROM pg_roles WHERE rolname = 'ceres_migrator';

    IF app_bypass THEN
        RAISE EXCEPTION 'ceres_app has BYPASSRLS — expected NOBYPASSRLS.';
    END IF;
    IF NOT admin_bypass THEN
        RAISE EXCEPTION 'ceres_admin lacks BYPASSRLS — expected BYPASSRLS.';
    END IF;
    IF migrator_super THEN
        RAISE EXCEPTION 'ceres_migrator is SUPERUSER — expected NOSUPERUSER.';
    END IF;
END
$$;
