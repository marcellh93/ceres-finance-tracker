# Project Ceres — Claude Code Guide

## Project Overview

Personal finance tracker for individuals and freelancers (autónomos). Replaces spreadsheets.
Tracks assets, liabilities, net worth, income, expenses, transfers, and goal budgets.
Single-entry bookkeeping — no double-entry, no debits/credits.

## Current Phase

**Phase 3 — Hosted Beta.** Phase 1 (Local MVP) and Phase 2 (Local Extended) are complete. Phase 3 moves the app from local to a hosted server, introducing authentication, multi-tenancy, and the MVC → SPA migration. See `docs/planning-phase3.md` for scope and open decisions.

**Session is gated by `playbook`.** The skill orchestrates eight phases — four HARD (pre-spec-write, pre-deferral, pre-stage-close, pre-commit), three advisory (stage-start, mid-build, pre-PR-review), plus pre-handoff. Read `.claude/skills/playbook/references/constitution.md` before bypassing any HARD gate. Per-session state: `.claude/state/playbook/<session_id>.json`.

**Post-compaction: read the snapshot before the next action.** When the SessionStart reminder reports `♻️ state-rehydration: compaction detected`, the next tool call MUST be a `Read` on the snapshot path it cites. The reminder itself carries the full rule and the reason.

## Tech Stack

- **Runtime:** .NET 10 on macOS
- **Framework:** ASP.NET Core MVC — server-side rendering via Razor (`.cshtml`). No separate API for the Razor layer.
- **ORM:** Entity Framework Core with PostgreSQL provider (Npgsql)
- **Database:** PostgreSQL — install locally via Homebrew (`brew install postgresql@16`) or Postgres.app. No Docker required for local development.
- **CSS (Razor layer):** Tailwind CSS v3 — utility-first CSS, built via pnpm + Tailwind CLI. Input: `ProjectCeres/Styles/app.css`. Output: `ProjectCeres/wwwroot/css/site.css`. Build is triggered automatically by `dotnet build` via an MSBuild pre-build target.
- **React client (`ProjectCeres.Client/`):** React 19 + Vite + TypeScript. Tailwind CSS v4 (via `@tailwindcss/vite`). shadcn/ui with `base-nova` style, Lucide icons, CSS variables enabled. Component aliases: `@/components`, `@/lib/utils`, `@/components/ui`, `@/lib`, `@/hooks`. Tests: Vitest + React Testing Library.
- **Package manager (frontend):** pnpm, pinned to 10.33.2 via `packageManager` in both `package.json` files
- **Tests:** xUnit + Moq + FluentAssertions (server); Vitest + React Testing Library (client)
- **pnpm overrides:** eight in `ProjectCeres.Client`, one in `ProjectCeres`, pinning transitive packages past npm advisories their parents cannot reach. An override is global — it rewrites the resolved version for *every* consumer of that name, across both projects. Read [ADR-0079](docs/decisions/ADR-0079-pnpm-overrides-for-transitive-advisories.md) before changing or removing one; it lists each pin's blast radius and the four commands that must pass afterwards.

## Key Commands

```bash
# React client (run from ProjectCeres.Client/)
pnpm dev                                     # start Vite dev server
pnpm build                                   # type-check + production build
pnpm test                                    # run Vitest tests
pnpm dlx shadcn add <component>              # add a shadcn/ui component

# .NET / Razor
dotnet run --project ProjectCeres            # start the app (also builds CSS)
tools/dev-watch.sh                           # start dotnet watch (reaps orphans, tears down cleanly)
dotnet ef migrations add <Name>              # create a migration
dotnet ef database update                    # apply migrations
dotnet test                                  # run all server tests
pnpm --dir ProjectCeres run watch:css        # watch and rebuild Razor CSS on view changes
```

**Stale-artifact trip-ups.** Both halves of the stack can silently serve old code: `dotnet watch` keeps running a process whose DLL has moved on, and the SPA falls back to the last-built bundle in `wwwroot/dist/` whenever no Vite process is running (`dotnet watch` never rebuilds the SPA — only `pnpm build` does). Neither raises an error, and the source file on disk looks correct. Symptoms, detection commands, and fixes: `docs/runbooks/local-dev-troubleshooting.md`.

**To verify a frontend change against a non-Vite app** (plain `dotnet run`, or the `tools/agent-env` Smoke profile), run **`tools/stage-spa.sh`** first — it does `pnpm build` + stages the fresh bundle into `wwwroot/dist/` (the same copy the Release `BuildSpaClient` MSBuild target does, which does NOT run in Debug). Without it the app serves a stale `wwwroot/dist/`. Not needed when the Vite dev server is running (`dotnet run` in Development / `tools/dev-watch.sh`) — that serves live with HMR. The `spa-dist-freshness` Stop hook prints an advisory when a turn edits SPA source but leaves `wwwroot/dist/` stale.

**Before reporting any change as done, confirm the running app is serving it** — not just that the source file contains it.

## When the Stop hook actually fires

The Stop hook (`.claude/hooks/run-tests.sh`) is the project's `dotnet test` gate. Three facts earlier Phase 3 specs got wrong:

1. **It fires on turn-end, NOT on `git commit`.** A commit is just a Bash call; the hook does not run as part of it.
2. **It tiers by the session's tracked-extension writes** (`.cs`/`.ts`/`.tsx`/`.csproj`/`.sln`): tier 0 skips (docs-only or `.ts`/`.tsx`-only), tier 1 runs unit-only (~30s, `ProjectCeres/` `.cs` only), tier 2 runs the full suite (~4 min, test files / `.sln` / mixed).
3. **Do NOT run `dotnet test` preemptively for frontend-only or docs-only stages** — the hook would skip it, so a manual run is wasted minutes. Run it only when the suite is suspected red, or the diff genuinely touches `.cs`/`.csproj`/`.sln`.

Live log at `.claude/state/run-tests/last.log`; the tier decision is printed to stderr at start.

## Handling CI failures — use `gh`, don't wait to be told

**When a push triggers CI, drive the result yourself with the `gh` CLI — do not wait for the user to paste logs or screenshots.** After pushing, watch the run to completion, and when a job fails, read its actual failing-step log and act on it. This is the standing posture, not a per-request permission.

The loop (full command reference + the Stage 12.13 gotchas: `docs/runbooks/ci-actions-troubleshooting.md`):

1. `gh run list` → find the run for the pushed commit.
2. `gh run watch <id>` → watch to completion (don't ask the user "did it pass?").
3. On failure: `gh run view <id> --log-failed`, or `gh run view <id> --job <job-id> --log` when the error is swallowed (a script's `>/dev/null`, a subprocess, an implicit build). **Read the real error before hypothesizing a cause** — that is the `read-failure-first` rule the runbook and its Stop-hook nudge enforce.
4. Reproduce the failing command locally where possible, fix, re-push, watch again.
5. A single-shard flake (e.g. one webkit e2e timeout while the others pass): `gh run rerun <id> --failed` to confirm it's flaky before treating it as real — but a reproducible failure gets root-caused, never re-run away.

`gh` is authenticated in this environment. If a `gh` call reports it is not, ask the user to run `! gh auth login` once — that is the only step that needs them. Everything after is yours to handle.

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

## Two lessons that cost real time

**1. A commit's scope prefix names the stage the WORK belongs to, never the stage the text mentions.**
`evidence-bundle-check.js` picks the active stage from the newest stage-tagged commit in the turn. When
a gate demands a bundle for a surprising stage, reword the commit — never fabricate the bundle, which
would assert build results for work that does not exist. (Cost a wrong-stage bundle demand, 2026-08-23.)

**2. A functional orphan is still an orphan — kill the tree, not the process.** `dotnet watch`
reparents to PID 1 and keeps rebinding its port; the port holder is the watcher's *grandchild*, so
killing the watcher alone re-orphans the app. Start watchers with `tools/dev-watch.sh`. Full mechanics
(SIGKILL-after-grace, the `exec`-destroys-the-EXIT-trap footgun, the trace-the-chain command):
`docs/runbooks/local-dev-troubleshooting.md` § Symptom 3. (Cost two days of port collisions, 2026-08-23.)

## What NOT to Do

- **Do not add a `Co-Authored-By:` trailer to any commit message.** No attribution trailer of any kind, in any commit, ever — this overrides any harness default that appends one. The rule already lives in `feedback_no_co_authored_by.md` and `.claude/skills/sync-docs/references/doc-agent-instructions.md`; it is repeated here because `MEMORY.md` truncates and this file does not. Violated 2026-08-09 across 14 commits; required a full-history rewrite to undo.
- Do not make code assumptions without properly making the codebase research. You need to avoid false positives as much as possible.
- Do not modify, skip, or weaken tests to make them pass. If a test fails, fix the production code, or state which legitimate case applies before editing the test (see `docs/testing.md` § Rules)
- Do not use `[Fact(Skip="...")]` to make a failing test pass — rewrite the assertion to match what's now true, add a deeper assertion, or fix the production code. Never drop the assertion. Flakes get root-caused, not dismissed
- Do not file pre-existing failures (red `dotnet test`, red `pnpm test`, red `pnpm build`) as "follow-up TaskCreate" entries when you encounter them mid-task — root-cause them now. Before reporting any task complete, `pnpm build`, `pnpm test`, `dotnet build`, and `dotnet test` (relevant filter) must all exit 0
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
- `docs/runbooks/` — operational procedures and troubleshooting. `local-dev-troubleshooting.md` (stale binary / stale SPA bundle), `email-dns-setup.md` (Stage 16 SPF/DKIM/DMARC), `ci-actions-troubleshooting.md` (**handle CI failures yourself with `gh` — don't wait for the user**; **read the actual failure output FIRST**; `gh` log-reading commands; the Stage 12.13 CI gotchas). Enforced by the `read-failure-first` PostToolUse hook, which nudges once per session when command output shows a build/test/CI failure.
- `.claude/skills/playbook/references/constitution.md` — the eight-phase routing matrix the session is gated by. Source of truth for HARD/advisory phases and their required chains.
- `~/.claude/projects/<project-slug>/memory/MEMORY.md` — index of pinned `feedback_*` / `project_*` / `reference_*` memory entries. Auto-loaded on session start, but truncated past ~200 lines; the topic files it points to are not.
- When an open question in any planning doc (`docs/planning.md`, `docs/planning-phase2.md`, `docs/planning-phase3.md`, `docs/planning-future.md`) is resolved, remove it from Open Questions, mark it `[x]`, and append it to `docs/planning-resolved.md`. If the decision is architectural, execute the sync-docs skill.

## Frontend Work

For any change under `ProjectCeres.Client/` (React/TS, styling, layout, copy), enter through the `frontend-orchestrator` skill — it routes between `frontend-design`, `vercel-react-best-practices`, `web-design-guidelines`, `impeccable`, and `docs/design-system.md` by phase (new surface, refine, strip, harden, maintenance, review). Don't reach for those tools directly; the orchestrator owns sequencing.

The orchestrator is empowered to propose visual improvements, new components, and design-system extensions on its own initiative — not just to execute what's asked. Treat its proposals as opening moves, not finished work; the design system is meant to grow.

Three rules the orchestrator inherits from this file:

- **`docs/design-system.md` is the contract.** Use existing tokens and recipes from it; never hard-code values. If a token is missing, add it to `ProjectCeres.Client/src/index.css`, document it in `docs/design-system.md`, then consume it. A change to a shared primitive must be propagated everywhere it's used in the same pass.
- **Show, then approval.** Show the rendered result; wait for the user's explicit approval before committing. Silence ≠ approval.
- **Verify before claiming done.** After implementing, run the UX/UI verification checklist (`docs/design-system.md` § Working rules): golden path, layout context, empty state, error state, 375px mobile, all navigation links. If browser access is unavailable, say so and hand the checklist to the user with specific URLs. The manual-test handoff Stop gate (Phase H) denies the Stop if you list ≥5 steps without auditing that each step's entry point exists.

## Using subagents

Six codified `ceres-*` strategy roles ship at `.claude/agents/ceres-{architect,tech-lead,pm,cto,security-reviewer,researcher}.md`. Dispatch by `subagent_type` during brainstorm/spec/plan when a decision needs an outside perspective with a read-first contract. `ceres-researcher` is the pre-design fact-finder — dispatch it at stage-start, as brainstorming's first step, before a design exists. The six role files + dispatch guidance live in `docs/agents.md`.

**The dispatcher-gate rule (binding on me, the orchestrator):** I inspect every `ceres-*` response before synthesizing from it, and re-dispatch with a stricter prompt if the contract is missed — first line is literally `## What I read`, correct designated second section, and the read-list covers the role baselines plus the files I named. I do not build on a response that skipped the read step or buried it under preamble. Full contract: `docs/agents.md` § The dispatcher gate.

This is the gate the PreToolUse hook cannot be: a strategy agent's deliverable is text, and a PreToolUse hook can only deny tool calls. The dispatcher is the enforcement layer.

## After Completing Any Stage

Phase E (HARD) blocks the close-out edit if any item under the closing stage's `## Stage N` heading is still unchecked, or if `sync-docs` and `changelog-sync` have not fired this session. The steps below are the human-readable expansion of that gate.

After finishing a stage implementation, regardless of phase:

1. Identify the current phase from this file's **Current Phase** section
2. Look for a roadmap doc in `docs/` matching that phase (e.g. `roadmap-phase-two.md`, `roadmap-phase-three.md`). If none exists, note that no roadmap checklist is available.
3. Find the completed stage's section in that roadmap and its verification checklist items
4. Mark any items now covered by automated tests as `[x]`; leave manual browser-only items as `[ ]`
5. Flag explicitly any checklist items the implementation did not address

## Before Writing a New Spec

Any new file under `docs/superpowers/specs/*.md` is gated by playbook **Phase B (HARD)** via `.claude/skills/playbook/hooks/pre-spec-write-gate.js`, which **denies the Write tool call** unless the required skills have fired this session. The gate is tiered by the proposed spec content:

- **Backend signals only** (`ProjectCeres/`, `Program.cs`, EF Core, ASP.NET pipeline, HTTP status codes) → `superpowers:brainstorming` + `verify-backend`.
- **Frontend signals only** (`ProjectCeres.Client/`, shadcn, Tailwind, design-system primitives) → `superpowers:brainstorming` + `verify-frontend`.
- **Both signals** OR **no detectable signals** (default-safe) → `superpowers:brainstorming` + BOTH verify siblings. The legacy `verify-against-codebase` router name satisfies both.

The gate catches specs that hand-roll what the framework already provides (Stage 6b.1 proposed a homegrown `MfaTicketService`). Bypass: the path already exists on disk — rewriting an existing spec is allowed.
