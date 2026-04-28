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

**2. Configure your connection string via User Secrets**

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Database=project_ceres;Username=postgres;Password=<your-password>" \
  --project ProjectCeres
```

**3. Apply migrations**

```bash
dotnet ef database update --project ProjectCeres
```

**4. Run the app**

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
