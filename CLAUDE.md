# Project Ceres — Claude Code Guide

## Project Overview

Personal finance tracker for individuals and freelancers (autónomos). Replaces spreadsheets.
Tracks assets, liabilities, net worth, income, expenses, transfers, and goal budgets.
Single-entry bookkeeping — no double-entry, no debits/credits.

## Current Phase

**Phase 3 — Hosted Beta.** Phase 1 (Local MVP) and Phase 2 (Local Extended) are complete. Phase 3 moves the app from local to a hosted server, introducing authentication, multi-tenancy, and the MVC → SPA migration. See `docs/planning-phase3.md` for scope and open decisions.

## Tech Stack

- **Runtime:** .NET 10 on macOS
- **Framework:** ASP.NET Core MVC — server-side rendering via Razor (`.cshtml`). No separate API for the Razor layer.
- **ORM:** Entity Framework Core with PostgreSQL provider (Npgsql)
- **Database:** PostgreSQL — install locally via Homebrew (`brew install postgresql@16`) or Postgres.app. No Docker required for local development.
- **CSS (Razor layer):** Tailwind CSS v3 — utility-first CSS, built via pnpm + Tailwind CLI. Input: `ProjectCeres/Styles/app.css`. Output: `ProjectCeres/wwwroot/css/site.css`. Build is triggered automatically by `dotnet build` via an MSBuild pre-build target.
- **React client (`ProjectCeres.Client/`):** React 19 + Vite + TypeScript. Tailwind CSS v4 (via `@tailwindcss/vite`). shadcn/ui with `base-nova` style, Lucide icons, CSS variables enabled. Component aliases: `@/components`, `@/lib/utils`, `@/components/ui`, `@/lib`, `@/hooks`. Tests: Vitest + React Testing Library.
- **Package manager (frontend):** pnpm
- **Tests:** xUnit + Moq + FluentAssertions (server); Vitest + React Testing Library (client)

## Key Commands

```bash
# React client (run from ProjectCeres.Client/)
pnpm dev                                     # start Vite dev server
pnpm build                                   # type-check + production build
pnpm test                                    # run Vitest tests
pnpm dlx shadcn add <component>              # add a shadcn/ui component

# .NET / Razor
dotnet run --project ProjectCeres            # start the app (also builds CSS)
dotnet ef migrations add <Name>              # create a migration
dotnet ef database update                    # apply migrations
dotnet test                                  # run all server tests
pnpm --dir ProjectCeres run watch:css        # watch and rebuild Razor CSS on view changes
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
- Do not implement currency conversion — explicitly out of scope
- Do not support cross-currency transfers — out of scope
- Do not store derived values (net worth, account balance, budget actual spend) as columns
- Do not store file attachments as BLOBs in the database
- Do not use SMS for MFA when it is eventually built — TOTP only
- Do not hard delete Accounts or Categories — they have transaction history attached

## Docs

- `docs/planning.md` — Phase 1 features, working assumptions, open questions. Phase 2+: see `planning-phase2.md`, `planning-phase3.md`, `planning-future.md`
- `docs/testing.md` — testing strategy, TDD workflow (required), CI/CD scope
- `docs/models.md` — all entities, relationships, normalization, deletion rules
- `docs/architecture.md` — layer model, request flow, how the architecture evolves across phases
- `docs/security-model.md` — threat model, data protection rules, access control rules (unified view)
- `docs/api-contract.md` — API conventions, response shape, versioning strategy (Phase 3+)
- `docs/multi-tenancy-strategy.md` — Phase 3 migration plan for scoping all data to users
- `docs/legal.md` — GDPR checklist, data retention policy (required before Phase 3)
- `docs/business-model.md` — freemium tiers (Phase 5, not yet active)
- When an open question in any planning doc (`docs/planning.md`, `docs/planning-phase2.md`, `docs/planning-phase3.md`, `docs/planning-future.md`) is resolved, remove it from Open Questions, mark it `[x]`, and append it to `docs/planning-resolved.md`. If the decision is architectural, execute the sync-docs skill.

## After Completing Any Stage

After finishing a stage implementation, regardless of phase:
1. Identify the current phase from this file's **Current Phase** section
2. Look for a roadmap doc in `docs/` matching that phase (e.g. `roadmap-phase-two.md`, `roadmap-phase-three.md`). If none exists, note that no roadmap checklist is available.
3. Find the completed stage's section in that roadmap and its verification checklist items
4. Mark any items now covered by automated tests as `[x]`; leave manual browser-only items as `[ ]`
5. Flag explicitly any checklist items the implementation did not address
