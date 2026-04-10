# Finance Tracker

Personal finance tracker for individuals and freelancers. Track accounts, transactions, transfers, budgets, and net worth across multiple currencies. Replaces spreadsheets.

## Stack

- **Backend:** ASP.NET Core MVC (.NET 9) — server-side rendering, no separate API
- **Database:** Microsoft SQL Server (via Docker)
- **ORM:** Entity Framework Core
- **Frontend:** Razor views (`.cshtml`) — HTML with C# templating, no JS framework
- **Tests:** xUnit, Moq, FluentAssertions

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop) — pull the correct SQL Server image for your Mac:

```bash
# Intel Mac
docker pull mcr.microsoft.com/mssql/server:2022-latest

# Apple Silicon (M1/M2/M3/M4)
docker pull mcr.microsoft.com/azure-sql-edge
```

## Quick Start

**1. Start the database**

```bash
# Intel Mac
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourPassword123!" \
  -p 1433:1433 --name finance-sql -d mcr.microsoft.com/mssql/server:2022-latest

# Apple Silicon
docker run -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=YourPassword123!" \
  -p 1433:1433 --name finance-sql -d mcr.microsoft.com/azure-sql-edge
```

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
