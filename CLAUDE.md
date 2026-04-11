# Finance Tracker — Claude Code Guide

## Project Overview

Personal finance tracker for individuals and freelancers (autónomos). Replaces spreadsheets.
Tracks assets, liabilities, net worth, income, expenses, transfers, and goal budgets.
Single-entry bookkeeping — no double-entry, no debits/credits.

## Current Phase

**Phase 1 — Local MVP.** Single user, no auth, runs locally only.
Do not build Phase 2+ features until Phase 1 is complete and in daily use.

## Tech Stack

- **Runtime:** .NET 9 on macOS
- **Framework:** ASP.NET Core MVC — server-side rendering via Razor (`.cshtml`). No separate API.
- **ORM:** Entity Framework Core with PostgreSQL provider (Npgsql)
- **Database:** PostgreSQL — install locally via Homebrew (`brew install postgresql@16`) or Postgres.app. No Docker required for local development.
- **Tests:** xUnit + Moq + FluentAssertions

## Key Commands

```bash
dotnet run --project FinanceTracker          # start the app
dotnet ef migrations add <Name>              # create a migration
dotnet ef database update                    # apply migrations
dotnet test                                  # run all tests
```

## Architecture Rules (Non-Obvious)

**Data model:**

- `Account` balance is always derived (SUM of transactions) — never stored as a column
- `Transaction.Amount` is always positive — direction is inferred from `Category → CategoryType`
- Do NOT add a TransactionType column to Transaction — income/expense is derived via Category → CategoryType (3NF)
- `Transfer` has no category — it is excluded from all income/expense calculations and reports
- Currency conversion does not exist — reports filter by currency, never convert
- `Transfer` source and destination accounts must share the same currency — enforce at app level
- `CategoryBudget` only applies to Expense categories — enforce at app level
- `Budget` actual spend is derived (SUM of linked transactions) — never stored

**Deletion rules — critical:**

- `Account`, `Category`, `CategoryBudget`, `Budget`: deactivate (`IsActive = false`), never hard delete
- `SavedReport`: soft delete (`DeletedAt` timestamp) — must be restorable
- `AccountType`, `CategoryType`, `ReportType`, `Currency`: never deletable — system-defined
- `Transaction`, `Transfer`, `TransactionAttachment`: hard delete with confirmation prompt

**File attachments:**

- Store files on the filesystem, never as BLOBs in the database
- Store original filename (`FileName`) separately from the system-generated path (`StoredPath`)

**Settings table:**

- Always exactly one row in Phase 1
- In Phase 3 it migrates to a per-user preferences table — do not couple it to auth yet

## What NOT to Do

- Do not add authentication — Phase 3 only
- Do not add JavaScript or charting libraries — Phase 2 only
- Do not implement currency conversion — explicitly out of scope
- Do not support cross-currency transfers — out of scope
- Do not store derived values (net worth, account balance, budget actual spend) as columns
- Do not store file attachments as BLOBs in the database
- Do not use SMS for MFA when it is eventually built — TOTP only
- Do not hard delete Accounts or Categories — they have transaction history attached

## Docs

- `docs/planning.md` — feature phases, scope decisions, project structure
- `docs/models.md` — all entities, relationships, normalization, deletion rules
- `docs/architecture.md` — layer model, request flow, how the architecture evolves across phases
- `docs/security-model.md` — threat model, data protection rules, access control rules (unified view)
- `docs/api-contract.md` — API conventions, response shape, versioning strategy (Phase 3+)
- `docs/multi-tenancy-strategy.md` — Phase 3 migration plan for scoping all data to users
- `docs/legal.md` — GDPR checklist, data retention policy (required before Phase 3)
- `docs/business-model.md` — freemium tiers (Phase 5, not yet active)
- `doc-agent-instructions.md` — documentation working instructions (routing rules, ADR numbering, health checks)
