# Finance Tracker

Personal finance tracker for individuals and freelancers. Track accounts, transactions, transfers, budgets, and net worth across multiple currencies. Replaces spreadsheets.

## Stack

- **Backend:** ASP.NET Core MVC (.NET 9) — server-side rendering, no separate API
- **Database:** PostgreSQL
- **ORM:** Entity Framework Core (Npgsql provider)
- **Frontend:** Razor views (`.cshtml`) — HTML with C# templating, no JS framework
- **Tests:** xUnit, Moq, FluentAssertions

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- **PostgreSQL** — either:
  - Homebrew: `brew install postgresql@16 && brew services start postgresql@16`
  - Or [Postgres.app](https://postgresapp.com) — menubar app, zero config

## Quick Start

**1. Start PostgreSQL and create the database**

```bash
# If using Homebrew (start once; runs on login automatically after)
brew services start postgresql@16

# Create the database
createdb finance_tracker
```

If using Postgres.app, start it from the menubar, then run `createdb finance_tracker` in the terminal.

**2. Apply migrations**

```bash
dotnet ef database update --project FinanceTracker
```

**3. Run the app**

```bash
dotnet run --project FinanceTracker
```

Open `http://localhost:5000` in your browser.

## Docs

- `docs/planning.md` — feature phases, scope, project structure
- `docs/models.md` — data model, entities, relationships, deletion rules
- `docs/legal.md` — GDPR obligations, data retention policy
- `docs/decisions/` — Architecture Decision Records
