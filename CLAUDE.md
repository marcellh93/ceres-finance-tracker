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

## When the Stop hook actually fires

The Stop hook (`.claude/hooks/run-tests.sh`) is the project's `dotnet test` gate. Three facts to keep straight, because earlier drafts of Phase 3 specs got them wrong:

1. **It fires on the Stop event (turn-end), NOT on `git commit`.** Claude Code triggers it when the agent ends its turn. A commit is just a Bash call; the hook does not run as part of it.
2. **It tiers by the session's *tracked-extension* writes** (`.cs`/`.ts`/`.tsx`/`.csproj`/`.sln`, populated by `track-session-writes.js`):
   - **Tier 0** — only `.tsx`/`.ts` touched, OR no tracked-extension writes at all (e.g. docs-only) → exit 0, no `dotnet test` runs.
   - **Tier 1** — only `ProjectCeres/` `.cs`/`.csproj` (no test files, no `.sln`) → `dotnet test --filter "FullyQualifiedName~ProjectCeres.Tests.Unit"` (~30s).
   - **Tier 2** — test files, `.sln`, or mixed → full suite (~4 min).
3. **Do NOT run `dotnet test` preemptively in plans or specs for frontend-only / docs-only stages.** The hook would skip it; running it manually is wasted minutes. Run it only when the .NET suite is suspected red from a prior change, or when the diff genuinely touches `.cs`/`.csproj`/`.sln`.

Live log at `.claude/state/run-tests/last.log` (truncated each run; tail-able from another terminal). Tier decision printed to stderr at start: `[stop-hook] tier N (scope) — running dotnet test ...` or `[stop-hook] tier 0: no .NET-impacting writes this turn — skipping dotnet test.`

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

- Do not modify, skip, or weaken tests to make them pass. If a test fails, fix the production code, or state which legitimate case applies before editing the test (see `docs/testing.md` § Rules)
- Do not add authentication — Phase 3 only
- Do not implement currency conversion — explicitly out of scope
- Do not support cross-currency transfers — out of scope
- Do not store derived values (net worth, account balance, budget actual spend) as columns
- Do not store file attachments as BLOBs in the database
- Do not use SMS for MFA when it is eventually built — TOTP only
- Do not hard delete Accounts or Categories — they have transaction history attached

## Docs

- `docs/planning.md` — Phase 1 features, working assumptions, open questions. Phase 2+: see `planning-phase2.md`, `planning-phase3.md`, `planning-future.md`
- `docs/testing.md` — testing strategy, TDD workflow (required), CI/CD scope. **Binding rules — read § Rules before writing or modifying any test.**
- `docs/models.md` — all entities, relationships, normalization, deletion rules
- `docs/architecture.md` — layer model, request flow, how the architecture evolves across phases
- `docs/security-model.md` — threat model, data protection rules, access control rules (unified view)
- `docs/api-contract.md` — API conventions, response shape, versioning strategy (Phase 3+)
- `docs/multi-tenancy-strategy.md` — Phase 3 migration plan for scoping all data to users
- `docs/design-system.md` — React client design system: tokens, primitives, recipes. Source of truth for UI look-and-feel.
- `docs/legal.md` — GDPR checklist, data retention policy (required before Phase 3)
- `docs/business-model.md` — freemium tiers (Phase 5, not yet active)
- When an open question in any planning doc (`docs/planning.md`, `docs/planning-phase2.md`, `docs/planning-phase3.md`, `docs/planning-future.md`) is resolved, remove it from Open Questions, mark it `[x]`, and append it to `docs/planning-resolved.md`. If the decision is architectural, execute the sync-docs skill.

## Frontend Work

For any change to `ProjectCeres.Client/` (React/TS, styling, layout, copy):

1. Read `docs/design-system.md` — use existing tokens/recipes; never hard-code values.
2. Invoke `frontend-design` for visual design and `vercel-react-best-practices` for React perf patterns before proposing.
3. Show the result; wait for approval before committing.
4. Before commit, run `web-design-guidelines` against the changed files as a final audit.
5. **After implementing, run the UX/UI verification checklist** (see `docs/design-system.md` § Working rules). Start the dev server, open every changed page in the browser, and explicitly verify: golden path, layout context (sticky ancestors don't obscure content), empty state, error state, mobile at 375px, and all navigation links. If browser access is unavailable, say so and hand the checklist to the user with specific URLs to check.

## After Completing Any Stage

After finishing a stage implementation, regardless of phase:

1. Identify the current phase from this file's **Current Phase** section
2. Look for a roadmap doc in `docs/` matching that phase (e.g. `roadmap-phase-two.md`, `roadmap-phase-three.md`). If none exists, note that no roadmap checklist is available.
3. Find the completed stage's section in that roadmap and its verification checklist items
4. Mark any items now covered by automated tests as `[x]`; leave manual browser-only items as `[ ]`
5. Flag explicitly any checklist items the implementation did not address

## Before Writing a New Spec

Any new file under `docs/superpowers/specs/*.md` is gated by a `PreToolUse` hook (`.claude/hooks/require-verify-against-codebase-before-spec.js`) that **denies the Write tool call** unless the `verify-against-codebase` skill has been invoked earlier in the session. The hook exists because Stage 6b.1's first spec draft proposed a homegrown `MfaTicketService` + "issue then undo" pattern that duplicated framework features — the skill catches that class of error. Bypass: invoke `verify-against-codebase` before retrying the Write, or rewrite an existing spec (the hook allows path-already-exists writes).
