# Project Ceres

Personal finance tracker for individuals and freelancers. Track accounts, transactions, transfers, budgets, and net worth across multiple currencies. Replaces spreadsheets.

## Stack

- **Backend:** ASP.NET Core MVC (.NET 10) — server-side rendering, no separate API
- **Database:** PostgreSQL
- **ORM:** Entity Framework Core (Npgsql provider)
- **CSS (Razor layer):** Tailwind CSS v3 — built via pnpm + Tailwind CLI
- **React client:** React 19 + Vite + TypeScript + Tailwind CSS v4 + shadcn/ui
- **Tests:** xUnit, Moq, FluentAssertions (server) · Vitest + React Testing Library (client)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org) + [pnpm](https://pnpm.io) (`npm install -g pnpm`)
- **PostgreSQL** — either:
  - Homebrew: `brew install postgresql@16 && brew services start postgresql@16`
  - Or [Postgres.app](https://postgresapp.com) — menubar app, zero config

## Quick Start

**1. Start PostgreSQL and create the database**

```bash
# If using Homebrew (start once; runs on login automatically after)
brew services start postgresql@16

# Create the database
createdb project_ceres
```

If using Postgres.app, start it from the menubar, then run `createdb project_ceres` in the terminal.

**2. Create the three Postgres roles (Stage 7.5 / ADR-0068 — RLS)**

The application uses three roles for Row-Level Security defence-in-depth:

- `ceres_app` — runtime role, subject to RLS (no `BYPASSRLS`)
- `ceres_admin` — admin services + background jobs, `BYPASSRLS`
- `ceres_migrator` — `dotnet ef database update` only, DDL + `BYPASSRLS`

Run the idempotent setup script once per database (safe to re-run):

```bash
psql -d project_ceres      -f scripts/setup-postgres-roles.sql
psql -d project_ceres_test -f scripts/setup-postgres-roles.sql
```

Default passwords are committed for local dev. Override in production by setting `CERES_APP_PASSWORD`, `CERES_ADMIN_PASSWORD`, `CERES_MIGRATOR_PASSWORD` before running the script.

**3. Configure your connection strings via User Secrets**

```bash
dotnet user-secrets set "ConnectionStrings:ApplicationConnection" \
  "Host=localhost;Database=project_ceres;Username=ceres_app;Password=ceres_app_dev_password" \
  --project ProjectCeres

dotnet user-secrets set "ConnectionStrings:AdminConnection" \
  "Host=localhost;Database=project_ceres;Username=ceres_admin;Password=ceres_admin_dev_password" \
  --project ProjectCeres

dotnet user-secrets set "ConnectionStrings:MigrationConnection" \
  "Host=localhost;Database=project_ceres;Username=ceres_migrator;Password=ceres_migrator_dev_password" \
  --project ProjectCeres
```

**4. Apply migrations** (uses `MigrationConnection` → `ceres_migrator`)

```bash
dotnet ef database update --project ProjectCeres
```

**5. Run the app**

```bash
dotnet run --project ProjectCeres
```

Open `http://localhost:5000` in your browser.

## Key Commands

```bash
# React client (run from ProjectCeres.Client/)
pnpm dev          # start Vite dev server
pnpm build        # type-check + production build
pnpm test         # run Vitest tests

# Razor CSS
pnpm --dir ProjectCeres run watch:css   # watch and rebuild Tailwind CSS

# .NET
dotnet test                             # run all server tests
dotnet ef migrations add <Name>         # create a migration
dotnet ef database update               # apply migrations
```

## Docs

- `docs/planning.md` — Phase 1 features, scope, working assumptions
- `docs/planning-phase2.md` — Phase 2 features (current phase)
- `docs/models.md` — data model, entities, relationships, deletion rules
- `docs/architecture.md` — layer model, request flow, how the architecture evolves across phases
- `docs/security-model.md` — threat model, data protection rules, access control rules
- `docs/api-contract.md` — API conventions, response shape, versioning strategy (Phase 3+)
- `docs/multi-tenancy-strategy.md` — Phase 3 migration plan for scoping all data to users
- `docs/legal.md` — GDPR obligations, data retention policy
- `docs/decisions/` — Architecture Decision Records
