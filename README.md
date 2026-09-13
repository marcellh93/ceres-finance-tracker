# Project Ceres

Personal finance tracker for individuals and freelancers (autónomos). Track accounts, transactions, transfers, budgets, and net worth across multiple currencies. Replaces spreadsheets.

Single-entry bookkeeping — no double-entry, no debits/credits.

**Current phase: Phase 3 — Hosted Beta.** Phases 1 (Local MVP) and 2 (Local Extended) are complete. Phase 3 adds authentication, multi-tenancy, PostgreSQL Row-Level Security, and an in-progress MVC → React SPA migration. See `docs/planning-phase3.md`.

## Stack

- **Backend:** ASP.NET Core (.NET 10) — a JSON API under `/api/*`. No server-rendered views; every page request falls through to the React SPA.
- **Database:** PostgreSQL, with Row-Level Security on all user-owned tables
- **ORM:** Entity Framework Core (Npgsql provider)
- **React client (`ProjectCeres.Client/`):** React 19 + Vite + TypeScript + Tailwind CSS v4 + shadcn/ui (`base-nova` style)
- **Legacy CSS pipeline:** a Tailwind v3 build (`Styles/app.css` → `wwwroot/css/site.css`) still runs on every `dotnet build`. It is a leftover from the pre-SPA Razor layer and no longer styles anything — see *Known cruft* below.
- **Analyzers:** custom Roslyn rules (`ProjectCeres.Analyzers`) that enforce project invariants at compile time
- **Tests:** xUnit, Moq, FluentAssertions (server) · Reqnroll (BDD, `ProjectCeres.Specs`) · Vitest + React Testing Library (client) · Playwright (E2E)
- **Container:** a hardened multi-stage `Dockerfile` builds a deployment image (SPA + CSS baked in, non-root, no secrets) — see [ADR-0081](docs/decisions/ADR-0081-docker-containerization.md). It is a **deployment** artifact, not the local-dev path: the image expects an external, already-migrated Postgres and runtime-injected config, so use the Quick Start below to run locally. A one-command local `docker compose` stack (app + Postgres + migrations) is deferred to Stage 16.

### Solution layout

| Project | Purpose |
| --- | --- |
| `ProjectCeres` | Web host — `/api/*` controllers, services, EF Core model, SPA fallback |
| `ProjectCeres.Client` | React SPA (Vite) |
| `ProjectCeres.Tests` | Server unit + integration tests |
| `ProjectCeres.Specs` | BDD executable specifications (Reqnroll + Gherkin `.feature` files), reusing the `ProjectCeres.Tests` harness |
| `ProjectCeres.Analyzers` | Roslyn analyzers + source generators (CER001–CER020) |
| `ProjectCeres.Analyzers.Annotations` | Attributes the analyzers key off (e.g. `[PreAuthScope]`, `[RlsBypassJustified]`) |
| `ProjectCeres.Analyzers.Tests` | Tests for the analyzers |

The analyzers are wired into `ProjectCeres` as an `Analyzer` project reference, so they run on every build. A violation surfaces as a `CERxxx` build diagnostic — that is expected behaviour, not a broken checkout.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org)
- [pnpm](https://pnpm.io) — **pinned to 10.33.2** via the `packageManager` field in both `package.json` files.

  The pin carries an integrity hash, so Corepack (bundled with Node 16.9+) will fetch and verify that exact version for you. This is the recommended path — it guarantees every contributor builds with the same pnpm:

  ```bash
  corepack enable pnpm
  ```

  If you'd rather install pnpm yourself, use Homebrew (`brew install pnpm`) or the official standalone installer (`curl -fsSL https://get.pnpm.io/install.sh | sh -`). Note that Homebrew's current stable is a major version ahead of the pin; that reads the v9 lockfile fine, but Corepack is the way to match exactly.

  Avoid `npm install -g pnpm` — it pulls a build tool through the npm registry and its dependency chain, which is a supply-chain surface this project doesn't need to take on.
- **PostgreSQL** — either:
  - Homebrew: `brew install postgresql@16 && brew services start postgresql@16`
  - Or [Postgres.app](https://postgresapp.com) — menubar app, zero config
- `dotnet-ef` CLI: `dotnet tool install --global dotnet-ef`

## Quick Start

**1. Start PostgreSQL and create both databases**

The test database is not optional — the integration suite connects to it by name.

```bash
# If using Homebrew (start once; runs on login automatically after)
brew services start postgresql@16

createdb project_ceres
createdb project_ceres_test
```

If using Postgres.app, start it from the menubar, then run the two `createdb` commands.

**2. Create the three Postgres roles (ADR-0068 — RLS)**

The application uses three roles for Row-Level Security defence-in-depth:

- `ceres_app` — runtime role, subject to RLS (no `BYPASSRLS`)
- `ceres_admin` — admin services + background jobs, `BYPASSRLS`
- `ceres_migrator` — schema migrations only, DDL + `BYPASSRLS`

Run the idempotent setup script against **both** databases (safe to re-run):

```bash
psql -d project_ceres      -f scripts/setup-postgres-roles.sql
psql -d project_ceres_test -f scripts/setup-postgres-roles.sql
```

Default passwords are committed for local dev. Override in production by setting `CERES_APP_PASSWORD`, `CERES_ADMIN_PASSWORD`, `CERES_MIGRATOR_PASSWORD` before running the script.

At startup the app refuses to boot if `ApplicationConnection` is wired to a role that has DDL rights — this is the privilege-leak check, and it is why the app connection must be `ceres_app`.

**3. Configure connection strings via User Secrets**

```bash
dotnet user-secrets set "ConnectionStrings:ApplicationConnection" \
  "Host=localhost;Database=project_ceres;Username=ceres_app;Password=ceres_app_dev_password" \
  --project ProjectCeres

dotnet user-secrets set "ConnectionStrings:AdminConnection" \
  "Host=localhost;Database=project_ceres;Username=ceres_admin;Password=ceres_admin_dev_password" \
  --project ProjectCeres
```

Migrations pass their connection on the command line instead (step 4), so no `MigrationConnection` secret is required for local dev.

**4. Install client dependencies**

```bash
pnpm install --dir ProjectCeres.Client
pnpm install --dir ProjectCeres
```

The second one is required even though its output is unused — an unconditional MSBuild target runs `pnpm run build:css` before every build, so a missing `node_modules` there fails `dotnet build`. See *Known cruft*.

**5. Apply migrations**

There are two `DbContext` types (`AdminDbContext` derives from `AppDbContext`), so `--context` is required. The connection must be passed explicitly — without it, EF resolves `ApplicationConnection` and runs as `ceres_app`, which has no DDL rights and will fail.

```bash
dotnet ef database update --project ProjectCeres --context AppDbContext \
  --connection "Host=localhost;Database=project_ceres;Username=ceres_migrator;Password=ceres_migrator_dev_password"
```

The **test** database migrates itself — the integration fixture applies migrations on first run, so no separate command is needed.

**6. Run the app**

```bash
dotnet run --project ProjectCeres --launch-profile https
```

Open **`https://localhost:7081`**.

Use the `https` profile. The Vite dev server proxies API calls to `https://localhost:7081`, so the plain-HTTP profile (`http://localhost:5248`) breaks the client's hot-reload path. If the browser rejects the certificate, run `dotnet dev-certs https --trust` once.

**You do not need to run `pnpm dev` separately.** In Development the app hosts the Vite dev server itself (`Vite.AspNetCore`), launching `pnpm dev` in `ProjectCeres.Client/` and proxying HMR through the .NET host. Run `pnpm dev` by hand only when working on the client in isolation.

## Key Commands

```bash
# .NET
dotnet run --project ProjectCeres --launch-profile https   # start the app (+ Vite dev server)
dotnet build                                               # build all projects, run analyzers
dotnet test                                                # run all server tests
dotnet ef migrations add <Name> --project ProjectCeres --context AppDbContext

# React client (run from ProjectCeres.Client/)
pnpm dev            # Vite dev server standalone
pnpm build          # type-check + production build + bundle-size check
pnpm test           # Vitest
pnpm lint           # ESLint
pnpm e2e            # Playwright golden-path suite

```

## Known cruft

Two leftovers from the pre-SPA architecture that a new contributor will otherwise find confusing:

- **The Tailwind v3 CSS build.** `ProjectCeres/Styles/app.css` compiles to `wwwroot/css/site.css` on every `dotnet build`, but there are no `.cshtml` views left to consume it. The React client has its own Tailwind v4 pipeline. Safe to ignore; a candidate for removal.
- **`AddControllersWithViews()`** (`Program.cs:30`) rather than `AddControllers()`. Kept deliberately — the comment above it notes it registers the antiforgery filter. No view rendering depends on it.

### Running the tests

`dotnet test` requires `project_ceres_test` to exist with the three roles (steps 1–2). A plain local `dotnet test` runs the integration suite **serially against that one database** (the default), so **do not run two `dotnet test` processes at once** — they will trample each other and produce failures unrelated to your change. The suite is also split into per-bucket collections that can run in **parallel, each against its own cloned database**, when clones are provisioned (`tools/ci/setup-test-db.sh --template --clones N` + `CERES_TEST_DB_CLONES`); that is how CI runs it. See `docs/testing.md` § Integration test collections for the DB-per-bucket model.

Before considering a change done, all four should exit 0: `dotnet build`, `dotnet test`, `pnpm build`, `pnpm test`.

## Docs

Start with `docs/architecture.md` for the layer model, then `docs/models.md` for the data model.

**Architecture & data**
- `docs/architecture.md` — layer model, request flow, how the architecture evolves across phases
- `docs/models.md` — entities, relationships, normalization, deletion rules
- `docs/security-model.md` — threat model, data protection, access control
- `docs/multi-tenancy-strategy.md` — how all data is scoped to users
- `docs/api-contract.md` — API conventions, response shape, versioning
- `docs/decisions/` — Architecture Decision Records

**Working in the codebase**
- `docs/testing.md` — testing strategy and TDD workflow. **Binding rules — read before writing or changing tests.**
- `docs/design-system.md` — React client tokens, primitives, and recipes. Source of truth for UI look-and-feel.
- `CHANGELOG.md` — what shipped, per stage

**Planning & roadmaps**
- `docs/PRODUCT.md` — product definition
- `docs/planning.md` · `docs/planning-phase2.md` · `docs/planning-phase3.md` · `docs/planning-future.md`
- `docs/planning-phase3-spa-migration.md` — MVC → SPA migration plan
- `docs/roadmap-phase-three.md` — current stage checklist
- `docs/planning-resolved.md` — decisions already made, with rationale

## Conventions worth knowing up front

These are enforced by tests, analyzers, or review — breaking them will fail the build:

- Account balances, net worth, and budget spend are **always derived** (SUM of transactions), never stored as columns
- `Transaction.Amount` is always positive; income vs. expense is derived through `Category → CategoryType`
- Transfers have no category and are excluded from all income/expense reporting
- No currency conversion anywhere — reports filter by currency, never convert
- Accounts, categories, and budgets are **deactivated, never hard-deleted**
- File attachments live on the filesystem, never as BLOBs in the database

## License

See `LICENSE`.
