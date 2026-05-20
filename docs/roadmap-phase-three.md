# Phase 3 Build Roadmap

> **Diataxis type:** Reference + how-to — sequenced delivery plan for Phase 3 (Hosted Beta), with verification checklists per stage. Mirrors the Phase 1 and Phase 2 roadmaps; consumed by `superpowers:executing-plans` and the after-stage verification step in `CLAUDE.md`.

> **Principles:** Spec-Driven Development (spec before code) · Test-Driven Development (failing test before implementation) · SOLID architecture maintained throughout · loud-failure over silent-failure (every cross-tenant boundary throws or logs explicitly).
>
> **TDD rule:** Every new service method, business rule, security mechanism, and financial calculation gets a failing test written first. The test must fail because the implementation does not exist — not because it is misconfigured. Controllers are excluded (thin HTTP handlers, verified via `WebApplicationFactory` integration tests). Infrastructure plumbing (migrations, DI wiring, reverse proxy) is verified by smoke tests, not by unit tests against the plumbing itself.
>
> **UI/UX rule:** Phase 3 introduces auth screens, onboarding, sessions/support pages, and the GDPR consent banner — every new SPA page is built to the design system at first ship (no second-pass redesign). For frontend changes, follow `CLAUDE.md` § Frontend Work: invoke `frontend-design`, run the UX/UI verification checklist in the browser, run `web-design-guidelines` before commit.
>
> **Security gate rule:** Stages that introduce or modify security-sensitive code (auth, multi-tenancy, sessions, email, headers) cannot be marked done without their verification checklist green. The verification list for each stage is drawn from `security-model.md`, `multi-tenancy-strategy.md`, and the ADRs — items there are not optional.
>
> **Responsive rule:** every SPA surface is built mobile-first as it ships. No surface is marked done without verifying its layout at three tiers: `mobile` (< 640px), `tablet` (640–1023px), and `desktop` (≥ 1024px). Touch targets ≥ 44×44px on mobile. No horizontal overflow at or above 320px. The strategy doc — [`planning-phase3-responsive.md`](planning-phase3-responsive.md) — defines the tier behaviour for navigation, tables (table → card collapse on mobile), forms (full-page on mobile, modal on desktop), and charts. Per-stage verification items below.

---

## Index

1. [Stage 0 — Phase 3 foundational decisions (Batch 3a)](#stage-0--phase-3-foundational-decisions-batch-3a)
2. [Stage 1 — SPA foundation (Batch 1)](#stage-1--spa-foundation-batch-1)
3. [Stage 2 — Movements + Budgets cutover (Batch 2 part 1)](#stage-2--movements--budgets-cutover-batch-2-part-1)
4. [Stage 3 — Management pages (Batch 2 part 2)](#stage-3--management-pages-batch-2-part-2)
5. [Stage 4 — Reports + Review + Import (Batch 2 part 3)](#stage-4--reports--review--import-batch-2-part-3)
6. [Stage 5 — Frontend polish + data-loading ease-in](#stage-5--frontend-polish--data-loading-ease-in)
7. [Stage 6 — Identity infrastructure (Batch 3b)](#stage-6--identity-infrastructure-batch-3b)
8. [Stage 7 — Multi-tenancy cutover (Batch 3c)](#stage-7--multi-tenancy-cutover-batch-3c)
9. [Stage 7.5 — PostgreSQL Row-Level Security (Batch 3c continued)](#stage-75--postgresql-row-level-security-batch-3c-continued)
10. [Stage 7.6 — Error legibility + OCP cleanup (Batch 3c continued)](#stage-76--error-legibility--ocp-cleanup-batch-3c-continued)
11. [Stage 8 — Email service + email security (Batch 3d)](#stage-8--email-service--email-security-batch-3d)
12. [Stage 9 — Auth SPA pages (Batch 3e)](#stage-9--auth-spa-pages-batch-3e)
13. [Stage 11 — Razor + URL cleanup (Batch 4)](#stage-11--razor--url-cleanup-batch-4)
14. [Stage 11.5 — Import sandbox + admin tooling (Batch 4)](#stage-115--import-sandbox--admin-tooling-batch-4)
15. [Stage 12 — Sessions + Support SPA pages (Batch 5)](#stage-12--sessions--support-spa-pages-batch-5)
16. [Stage 13 — GDPR baseline (Batch 5)](#stage-13--gdpr-baseline-batch-5)
17. [Stage 14 — HTTP security headers + CORS (Batch 5)](#stage-14--http-security-headers--cors-batch-5)
18. [Stage 15 — Identity masking, HMAC `UserRef` (Batch 5)](#stage-15--identity-masking-hmac-userref-batch-5)
19. [Stage 15.5 — Onboarding wizard (Batch 5)](#stage-155--onboarding-wizard-batch-5)
20. [Stage 16 — Hosting + ops (Batch 5)](#stage-16--hosting--ops-batch-5)
21. [Master pre-launch verification checklist](#master-pre-launch-verification-checklist)

---

> **Status legend used throughout this document:**
> - ✅ **Done** — shipped to main; commit hash referenced where useful
> - ⚠️ **In progress** — partially shipped; what's left is noted
> - ❌ **Pending** — not yet started
> - ❓ **Blocked** — waiting on a dependency; the dependency is named

---

## Stage 0 — Phase 3 foundational decisions (Batch 3a)

**Status: ✅ Done (2026-05-07).** Five ADRs locked; doc-sync complete.

Five architectural decisions were locked before any Batch 3 implementation began, in keeping with `multi-tenancy-strategy.md`'s requirement that "global query filters must be set up before any multi-user service code is written — retrofitting is harder than applying from the start."

### Sub-stages

| # | Decision | Outcome | Reference |
|---|---|---|---|
| 0.1 | Auth cookie `SameSite` posture | `Lax`, paired with CSRF tokens, `__Host-` prefix, `HttpOnly`, `Secure`, re-auth on sensitive ops | [ADR-0063](decisions/ADR-0063-cookie-samesite-lax-with-csrf-tokens.md) |
| 0.2 | Social login in scope for Phase 3? | Deferred to Phase 4; architecture stays open | [ADR-0064](decisions/ADR-0064-social-login-deferred-to-phase-4.md) |
| 0.3 | EF Core global query filters? | Adopted on every user-owned entity + explicit `.Where()` redundancy + `IgnoreQueryFilters()` reserved for `Admin/` namespace (architecture-test enforced) | [ADR-0065](decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md) |
| 0.4 | Sentinel-to-real-user migration approach | Remap sentinel to first registered user in a single transaction; preserves Phase 1/2 personal-finance history | [ADR-0066](decisions/ADR-0066-sentinel-remap-to-first-registered-user.md) |
| 0.5 | Background-process user resolution | `IUserScope.EnterAs(userId)` foundation + `IUserJobRunner.ForEachUserAsync` runner; `ICurrentUserAccessor` throws on missing context (no silent `Guid.Empty`) | [ADR-0067](decisions/ADR-0067-background-job-user-scope-with-iuserscope-and-runner.md) |

### Verification checklist

- [x] All five ADRs written and committed (commit `d2460d2`)
- [x] `multi-tenancy-strategy.md` § Background processes flipped from "open hazard" to "Resolved by ADR-0067"
- [x] `multi-tenancy-strategy.md` § Settings Table Migration flipped to remap-to-first-user
- [x] `multi-tenancy-strategy.md` § EF Core Global Query Filters flipped from "Optional Optimization" to adopted
- [x] `security-model.md` § Cookie Configuration `SameSite` line flipped from open-decision to `Lax` per ADR-0063
- [x] `planning-resolved.md` lists all five ADR decisions (commit `fd101d0`)
- [x] `planning-phase3.md` § Planned Features Authentication bullet reflects social-login deferral
- [x] `planning-phase3.md` § 14 Implementation order Settings/Sessions line points at Batch 5
- [x] `planning-phase3-spa-migration.md` Batch 3/4 paragraph cross-references the new `planning-phase3.md § Phase 3 execution batches` section

---

## Stage 1 — SPA foundation (Batch 1)

**Status: ✅ Done (2026-04-29).** Zero-visible-change foundation that everything downstream depends on.

### Sub-stages

| # | Sub-stage | Status | Spec / Plan |
|---|---|---|---|
| 1.1 | Design tokens + `docs/design-system.md` | ✅ 2026-04-29 | [`docs/superpowers/plans/2026-04-29-design-system-foundation.md`](superpowers/plans/2026-04-29-design-system-foundation.md) |
| 1.2 | App shell — sidebar, top bar, responsive behaviour | ✅ 2026-04-29 | [`docs/superpowers/specs/2026-04-29-app-shell-design.md`](superpowers/specs/2026-04-29-app-shell-design.md) · [`docs/superpowers/plans/2026-04-29-app-shell.md`](superpowers/plans/2026-04-29-app-shell.md) |
| 1.3 | Dashboard SPA — 5 chart endpoints + typed wrapper DTOs | ✅ 2026-04-29 | [`docs/superpowers/specs/2026-04-29-dashboard-phase-1-design.md`](superpowers/specs/2026-04-29-dashboard-phase-1-design.md) · [`docs/superpowers/specs/2026-04-29-dashboard-phase-2-design.md`](superpowers/specs/2026-04-29-dashboard-phase-2-design.md) · [`docs/superpowers/plans/2026-04-29-dashboard-phase-1.md`](superpowers/plans/2026-04-29-dashboard-phase-1.md) · [`docs/superpowers/plans/2026-04-29-dashboard-phase-2.md`](superpowers/plans/2026-04-29-dashboard-phase-2.md) |
| 1.4 | Dashboard polish — MTD tiles, health captions | ✅ 2026-04-29 | [`docs/superpowers/specs/2026-04-29-mtd-tiles-and-health-captions-design.md`](superpowers/specs/2026-04-29-mtd-tiles-and-health-captions-design.md) · [`docs/superpowers/specs/2026-04-29-dashboard-polish-design.md`](superpowers/specs/2026-04-29-dashboard-polish-design.md) |
| 1.5 | Design system v1.2 — Badge variants, Tile, CardError, showcase | ✅ 2026-04-30 | [`docs/superpowers/specs/2026-04-30-design-system-v1.2-design.md`](superpowers/specs/2026-04-30-design-system-v1.2-design.md) · [`docs/superpowers/plans/2026-04-30-design-system-v1.2.md`](superpowers/plans/2026-04-30-design-system-v1.2.md) |

### Verification checklist

- [x] Tokens shipped — color palette (deep teal primary + zinc neutrals + semantics + 8-color chart palette), typography (Inter + IBM Plex Mono), spacing, radius, shadow, motion
- [x] `/design-system.html` internal route renders every token for visual reference
- [x] All 5 dashboard chart endpoints ship typed wrapper DTOs with `currencyCode`/`currencySymbol`
- [x] React dashboard live at `/app/`
- [x] Razor dashboard view, partial, and controller deleted
- [x] 302 redirect from `/Dashboard` → `/app/` is live
- [x] Sidebar collapses to mobile drawer below `md` breakpoint
- [x] Top bar `+` button wired to QuickAddModal (deferred wiring lives with Stage 2.1)
- [x] `index.css` defines motion duration + easing tokens (`--motion-duration-fast/base/slow`, standard + emphasized easings)
- [x] `tw-animate-css` imported in `index.css`

Responsive (foundation tier — applies to every later stage):

- [x] Three breakpoint tiers wired in Tailwind config (`mobile` < 640px / `tablet` 640–1023px / `desktop` ≥ 1024px) per [`planning-phase3-responsive.md`](planning-phase3-responsive.md) § Breakpoint Strategy
- [x] Mobile drawer (shadcn `Sheet`, left side) for navigation below 1024px; closes on route change; backdrop dismiss; focus trap while open
- [x] Sidebar persistent on desktop (decision pending in `planning-phase3-responsive.md` § Open Questions for sidebar-vs-top-nav was resolved by shipping the sidebar)

---

## Stage 2 — Movements + Budgets cutover (Batch 2 part 1)

**Status: ✅ Done (2026-04-30 → 2026-05-01).** Highest daily-usage pages migrated first.

### Sub-stages

| # | Sub-stage | Status | Spec / Plan |
|---|---|---|---|
| 2.1 | Movements list page + quick-add modal | ✅ 2026-04-30 | [`docs/superpowers/specs/2026-04-30-movements-and-quickadd-design.md`](superpowers/specs/2026-04-30-movements-and-quickadd-design.md) · [`docs/superpowers/plans/2026-04-30-movements-and-quickadd.md`](superpowers/plans/2026-04-30-movements-and-quickadd.md) |
| 2.2 | Full Transactions / Transfers / LiabilityPayments CRUD | ✅ 2026-05-01 | [`docs/superpowers/specs/2026-04-30-movements-crud.md`](superpowers/specs/2026-04-30-movements-crud.md) · [`docs/superpowers/plans/2026-04-30-movements-crud-plan-1-api.md`](superpowers/plans/2026-04-30-movements-crud-plan-1-api.md) · [`docs/superpowers/plans/2026-04-30-movements-crud-plan-2-spa.md`](superpowers/plans/2026-04-30-movements-crud-plan-2-spa.md) · [`docs/superpowers/plans/2026-04-30-movements-crud-plan-3-finish.md`](superpowers/plans/2026-04-30-movements-crud-plan-3-finish.md) |
| 2.3 | Budgets — full CRUD with archive lifecycle (unified Category + Goal tabs) | ✅ 2026-05-01 | [`docs/superpowers/specs/2026-05-01-budgets-spa-design.md`](superpowers/specs/2026-05-01-budgets-spa-design.md) · [`docs/superpowers/plans/2026-05-01-budgets-spa-implementation.md`](superpowers/plans/2026-05-01-budgets-spa-implementation.md) |
| 2.4 | Per-table search + saved searches | ❌ Pending | Own brainstorm — no spec yet |

### Verification checklist

- [x] SPA Movements at `/app/movements` with text search + 3 filters
- [x] Quick-add modal wired to TopBar `+` and Movements page header
- [x] POST endpoints exist for Transactions, Transfers, Liability Payments
- [x] Sonner toasts on every mutation
- [x] Movement Create/Edit/Delete via routed pages at `/app/movements/new` and `/app/movements/:id/edit`
- [x] Attachment two-phase upload on Edit (upload → link)
- [x] Bulk-cleared and CSV export buttons in page header
- [x] Razor `MovementsController`, `TransactionsController`, `TransfersController` page actions 302-redirect to SPA
- [x] Unified `/app/budgets` page with Category and Goal tabs
- [x] `Settings.PeriodStartDay` (1–31) implemented; every monthly view (Cycle to Date, Spending by Category, Income vs. Avg, CategoryBudget actual-spend) respects the configured cycle
- [x] Razor `BudgetsController` 302-redirects to SPA
- [ ] Per-table search + saved searches — pending; needs brainstorm

---

## Stage 3 — Management pages (Batch 2 part 2)

**Status: ✅ Done (2026-05-02 → 2026-05-03).** Settings was the SPA-pattern pilot; Categories validated the pattern against more complex shapes; Accounts and Recurring extended it further.

### Sub-stages

| # | Sub-stage | Status | Spec / Plan |
|---|---|---|---|
| 3.1 | Settings — first SPA-pattern pilot | ✅ 2026-05-02 (commit `e842250`) | [`docs/superpowers/specs/2026-05-02-spa-page-pattern-and-settings-design.md`](superpowers/specs/2026-05-02-spa-page-pattern-and-settings-design.md) · [`docs/superpowers/plans/2026-05-02-spa-page-pattern-and-settings.md`](superpowers/plans/2026-05-02-spa-page-pattern-and-settings.md) |
| 3.2 | Categories — list + nested CRUD + archive lifecycle | ✅ 2026-05-02 (commit `9578f9a` + follow-ups `132d5df`, `87f6709`) | [`docs/superpowers/specs/2026-05-02-categories-spa-design.md`](superpowers/specs/2026-05-02-categories-spa-design.md) · [`docs/superpowers/plans/2026-05-02-categories-spa.md`](superpowers/plans/2026-05-02-categories-spa.md) |
| 3.3 | Accounts — list + Create/Edit + Ledger + payoff projection | ✅ 2026-05-03 (commit `03220b8`) | [`docs/superpowers/specs/2026-05-03-accounts-spa-design.md`](superpowers/specs/2026-05-03-accounts-spa-design.md) · [`docs/superpowers/plans/2026-05-03-accounts-spa.md`](superpowers/plans/2026-05-03-accounts-spa.md) |
| 3.4 | Recurring — list + Create/Edit + Confirm/Dismiss/Archive/Reactivate + topbar bell | ✅ 2026-05-03 | [`docs/superpowers/specs/2026-05-03-recurring-spa-design.md`](superpowers/specs/2026-05-03-recurring-spa-design.md) · [`docs/superpowers/plans/2026-05-03-recurring-spa.md`](superpowers/plans/2026-05-03-recurring-spa.md) |

### Verification checklist

- [x] Locked the `features/<area>/` + page+form split + Popover+Command picker idiom (Settings pilot)
- [x] AlertDialog-confirmed archive flow on Categories with 409 in-use error surfaced in toast
- [x] System category rows pinned with leading lock icon
- [x] `Save` + `Cancel` form pattern shared across all management pages
- [x] Accounts: per-currency subtotal strip rendered whenever the user has at least one account (single- and multi-currency)
- [x] Accounts: type-driven balance colour (Liability rows always render in destructive)
- [x] Accounts: conditional Asset/Liability fields with two-layer interest-rate normalisation (SPA submit-time clears rate when not Amortising; server policy rejects null repaymentType + non-null rate)
- [x] Accounts: SPA-side payoff projection for amortising-liability accounts (`projection.ts`)
- [x] Accounts: adaptive archive AlertDialog copy via `HasTransactions` field on `AccountListItemDto`
- [x] Accounts: `LiabilityProjectionService` (zero callers post-cutover) deleted
- [x] Recurring: AlertDialog-confirmed archive flow; Dismiss dialog accepts optional override next-due-date for ManualDate reminders
- [x] Recurring: Confirm dialog creates a Transaction
- [x] Recurring: `ReminderCountProvider` drives the TopBar bell badge with optimistic update on Confirm/Dismiss + refresh from `GET /api/dashboard/summary`
- [x] Recurring: `EstimatedAmount` is `decimal?` (null = amount varies)
- [x] Recurring: `SnapToCalendarDay` corrected for Weekly/Biweekly frequencies
- [x] Recurring: `PATCH /api/recurring-transactions/:id/reactivate` added
- [x] Recurring: TopBar bell count refetches after row actions (commit `c7a2913`)
- [x] All four Razor controllers slimmed to 302 redirects; Razor views, throwing CRUD service methods, and Razor-only ViewModels deleted
- [x] CategoriesCrudApiTests, AccountsCrudApiTests, RecurringTransactionsCrudApiTests cover the API-level behaviour the Razor views previously covered

---

## Stage 4 — Reports + Review + Import (Batch 2 part 3)

**Status: ✅ Done (2026-05-03 → 2026-05-06).** Most complex Batch 2 work — 8 report pages, the unified Review surface, and the import wizard + profiles.

### Sub-stages

| # | Sub-stage | Status | Spec / Plan |
|---|---|---|---|
| 4.1 | Reports — 8 report pages + shared layout + sticky filter bar + CSV export | ✅ 2026-05-03 | [`docs/superpowers/plans/2026-05-03-reports-spa.md`](superpowers/plans/2026-05-03-reports-spa.md) |
| 4.2 | Reports redesign — pinned tab+filter chrome, distinct icons, default period via `Settings.PeriodStartDay`, `chart-format` extracted | ✅ 2026-05-04 (commit `23d9db7`) | [`docs/superpowers/specs/2026-05-04-reports-redesign-design.md`](superpowers/specs/2026-05-04-reports-redesign-design.md) · [`docs/superpowers/plans/2026-05-04-reports-redesign.md`](superpowers/plans/2026-05-04-reports-redesign.md) |
| 4.3 | Review — unified `/app/review` with Reconciliations + Transfers tabs | ✅ 2026-05-06 | [`docs/superpowers/specs/2026-05-04-review-spa-design.md`](superpowers/specs/2026-05-04-review-spa-design.md) · [`docs/superpowers/plans/2026-05-04-review-spa.md`](superpowers/plans/2026-05-04-review-spa.md) |
| 4.4 | Import — wizard + profiles SPA, file-level cutover | ✅ 2026-05-06 (commit `7152eda`) | [`docs/superpowers/plans/2026-05-06-import-spa-cutover.md`](superpowers/plans/2026-05-06-import-spa-cutover.md) |
| 4.5 | Import polish — locale-safe parsing + LiabilityPayment/Transfer duplicate detection | ✅ 2026-05-06 (commit `4e4d7ee`) | (follow-up to 4.4) |

### Reports — verification checklist

- [x] SPA at `/app/reports` with `ReportsLayout` + `ReportsFilterBar` (sticky, slug-aware)
- [x] All 8 routes wired: net-worth, income-expense, expense-breakdown, transaction-history, budget-vs-actual, largest-expenses, monthly-cash-flow, net-worth-over-time
- [x] `DateRangePicker` extracted as generic shared component (also wraps Movements)
- [x] CSV export via `?format=csv` on each report endpoint
- [x] `ReportsController` actions → 302 redirects; all 9 Razor views deleted
- [x] Distinct lucide icon per report card (post-launch polish 2026-05-04)
- [x] Back-link gap fix
- [x] Equal filter widths
- [x] Default period respects `Settings.PeriodStartDay` via `src/app/lib/period.ts` TS helper
- [x] `budget-vs-actual` `LimitAmount` aggregated across periods in range
- [x] Tab + filter chrome pinned (no scroll-collapse on long pages)
- [x] `chart-format.ts` extracted (centralizes axis tick + tooltip formatting)
- [ ] SavedReport CRUD — deferred per [ADR-0055](decisions/ADR-0055-saved-report-configurations-deferred.md); reassess after launch
- [ ] Spending by Category Over Time — deferred per [ADR-0054](decisions/ADR-0054-nice-to-have-reports-priority.md)
- [ ] Year-over-Year Comparison — deferred per ADR-0054

### Review — verification checklist

- [x] Unified `/app/review` with `?tab=reconciliations` and `?tab=transfers` deep links
- [x] Razor controller redirects target the new tab anchors (commits `7ba0423`, `101d79b`)
- [x] Reconciliations: inline `Confirm match` row action
- [x] Reconciliations: `Dispute` row menu (AlertDialog)
- [x] Reconciliations: `Confirm all` (AlertDialog), backed by new `TryConfirmAllAsync`
- [x] Transfers: per-card `Link to existing` (shared dialog with same-currency-filtered account picker)
- [x] Transfers: per-card `Create transfer` (shared dialog)
- [x] Transfers: per-card `Dismiss` (fires immediately, no confirmation)
- [x] Sidebar `Review` badge driven by new `ReviewCountProvider` over the two existing `pending/count` endpoints
- [x] Server: throwing CRUD variants dropped from `ITransferReviewService` and `IImportStagedTransactionService`
- [x] Server: `StagedTransactionDto` and `StagedTransferDto` enriched with `AccountCurrencyCode` + `AccountCurrencySymbol`
- [x] Razor views, `Staged*ViewModel` classes, and `_Layout` pending-count badges deleted

### Import — verification checklist

- [x] Two SPA surfaces: `/app/import` (3-step wizard) and `/app/import/profiles` (list + nested `/new` and `/:id/edit`)
- [x] `FileDropzone` primitive (single-file drag-and-drop + click-to-browse + 10 MB guard)
- [x] `WizardStepper` primitive (numbered pills, completed pills clickable)
- [x] Header detection via `POST /api/import/headers`
- [x] Profile-driven mapping copies from `GET /api/import-profiles/:id`
- [x] Submit posts multipart to `POST /api/import`
- [x] Result tiles deep-link to Review (`?tab=reconciliations`, `?tab=transfers`) and Movements (`?needsReview=true`)
- [x] Save-as-profile prompt on result step gated on `selectedProfileId === null`
- [x] Format inferred from uploaded file's extension
- [x] Razor `ImportController` and `CsvImportProfilesController` slimmed to 302 redirects
- [x] Razor views and the four Razor-only ViewModels deleted
- [x] Locale-safe parsing for `comma_decimal` / `dot_decimal` users
- [x] LiabilityPayment / Transfer duplicate detection works against archive
- [ ] Dual debit/credit columns — deferred to follow-up plan
- [ ] Confidence-scored transfer detection — deferred (see [`docs/superpowers/specs/2026-04-29-import-transfer-detection-confidence-design.md`](superpowers/specs/2026-04-29-import-transfer-detection-confidence-design.md))
- [ ] Transfer-keyword settings — deferred to follow-up plan

---

## Stage 5 — Frontend polish + data-loading ease-in

**Status: ✅ Done (2026-05-09).** Data-loading ease-in fully rolled out (5.2/5.3/5.4). Tier 1 polish done (T1.1–T1.4 + T1.6 shipped; T1.5 deliberately not shipped). Tier 2 polish done (T2.7–T2.10). Tier 3 polish done (T3.11–T3.15 shipped 2026-05-08). Tier 4 testing-infrastructure done (T4.16–T4.18 shipped 2026-05-08). Tier 5 done (T5.20/T5.21 shipped 2026-05-09; T5.19 deliberately deferred — no candidate consumer). Plus a batch of ad-hoc UX fixes shipped on top of the planned tiers — see § Ad-hoc UX fixes below.

> **As of 2026-05-09, next up:** Stage 5 is closed out. The next launch-critical work is Stage 6 (Identity infrastructure). Stage 5 was explicitly *not* blocking Phase 3 launch.

> **Why this is its own stage:** these are app-wide UX fundamentals that touch every page. They can't be picked up under any single Stage 1–4 because they cross all of them. They're explicitly *not* blocking Phase 3 launch — they're the difference between "shipped" and "feels well done."

### Sub-stages

| # | Sub-stage | Status | Reference |
|---|---|---|---|
| 5.1 | Polish checklist audit doc — file-by-file inventory of frontend gaps | ✅ 2026-05-06 (commit `5a653d3`) | [`docs/ceres-polish-checklist-frontend.md`](ceres-polish-checklist-frontend.md) |
| 5.2 | Data-loading ease-in — primitives (`useDelayedLoading` + `<DataTransition>`) | ✅ 2026-05-04 | [`docs/superpowers/specs/2026-05-04-data-loading-ease-in-design.md`](superpowers/specs/2026-05-04-data-loading-ease-in-design.md) · [`docs/superpowers/plans/2026-05-04-data-loading-ease-in.md`](superpowers/plans/2026-05-04-data-loading-ease-in.md) |
| 5.3 | Data-loading ease-in — reference rollout on Accounts page | ✅ 2026-05-04 (commits `c6052d0` → `5b7d72e`) | (above) |
| 5.4 | Data-loading ease-in — full rollout to remaining pages | ✅ 2026-05-07 (commits `12fdc1e` → `40514b8`; plan revised in `abd912a`) | [`docs/superpowers/plans/2026-05-04-data-loading-ease-in-rollout.md`](superpowers/plans/2026-05-04-data-loading-ease-in-rollout.md) |
| 5.5 | Polish checklist Tier 1 — six small CSS / one library wire-up | ✅ 2026-05-07 (T1.1–T1.4 + T1.6 shipped; T1.5 deliberately not shipped) | [`docs/ceres-polish-checklist-frontend.md`](ceres-polish-checklist-frontend.md) § Tier-ordered action list |
| 5.6 | Polish checklist Tier 2 — extract shared primitives | ✅ 2026-05-08 (T2.7 `fa6c0d7`, T2.8 `496f1e3`, T2.9 `55d2030`, T2.10 superseded by 5.2) | (above) |
| 5.7 | Polish checklist Tier 3 — production-app polish | ✅ 2026-05-08 (T3.11 `c372b89`, T3.12 `8b4c731`, T3.13 `0c5c1b0`, T3.14 `1394db6`, T3.15 `d828161` + 17 follow-up commits during browser-pass) | (above) |
| 5.8 | Polish checklist Tier 4 — testing and observability | ✅ 2026-05-08 (T4.16 `9f61771`, T4.17 `e96d898`, T4.18 `c37f4eb`) | (above) |
| 5.9 | Polish checklist Tier 5 — discretionary | ✅ 2026-05-09 (T5.20 `17d6e93`, T5.21 `37f55fe`; T5.19 deferred — no candidate consumer) | (above) |
| 5.10 | Ad-hoc UX fixes layered on top of the planned tiers | ✅ 2026-05-08 (see § Ad-hoc UX fixes below) | (this doc) |

### Data-loading ease-in — verification checklist

Primitives — done:

- [x] `useDelayedLoading(loading, options?)` shipped at `src/app/lib/use-delayed-loading.ts` with 150 ms default delay
- [x] `<DataTransition>` component shipped at `src/app/components/DataTransition.tsx` with `skeleton` / `data` / `error` slots
- [x] Cross-fade uses `var(--motion-duration-base)` + `var(--motion-easing-standard)`
- [x] `prefers-reduced-motion: reduce` collapses to instant swap
- [x] Both primitives tested with Vitest (fake timers for delay, matchMedia mock for reduced-motion)

Accounts reference — done:

- [x] `AccountCurrencySubtotals` card wrapped in `<DataTransition>` driven by `useDelayedLoading(list.loading)`
- [x] `AccountsBody` consolidated into the canonical `<DataTransition>` shape (state derivation + `&& !list.data` stale-data guard)
- [x] Synchronous-skeleton test rewritten to async with `waitFor` + `{ timeout: 500 }`

Rollout to remaining pages — done (2026-05-07). The plan was revised mid-rollout (commit `abd912a`) when Accounts surfaced a "page-level vs per-section" decision: pages whose multiple sections share one fetch must share one DataTransition (atomic swap). Per-page outcomes:

- [x] Task 1: Categories list page (`CategoriesLayout.tsx`) — single fetch, section-level (commit `12fdc1e`)
- [x] Task 2: Movements list page (`MovementsLayout.tsx`) — single fetch, page-level wrap with currency-readiness gate (commit `57776c4`)
- [x] Task 3: Recurring list page (`RecurringLayout.tsx`) — section-level; the second fetch (`allList`) only feeds empty-state copy, so single-section is correct (commit `43654d8`)
- [x] Task 4: Reports — 8 sub-commits (`f077d30`, `dc9caca`, `a604d1e`, `05de819`, `862985c`, `1a757aa`, `605e457`, `e6eeb17`)
- [x] Task 5: Budgets list page (`BudgetsLayout.tsx`) — per-tab pattern (commit `e29bad9`)
- [x] Task 6: Settings page (`SettingsPage.tsx`) — page-level with combined `settings.loading || currencies.loading` (commit `318d6d3`)
- [x] Task 7: Review (`ReconciliationList.tsx`) — section-level (commit `0ad2f1f`)
- [x] Task 8: Dashboard cards — 9 sub-commits, per-card "filling-in" pattern (`a13e099`, `69ddf8e`, `c18083f`, `338fed3`, `1ecc595`, `ec2e23d`, `87916a1`, `4f0d6be`, `40514b8`)
- [x] Task 9: Final verification — full client test suite green (807/807), production build clean

### Polish checklist (`ceres-polish-checklist-frontend.md`) — verification checklist

Tier 1 — six items, ~80% of visible improvement:

- [x] T1.1 Wire `next-themes` into `ThemeToggle.tsx` (10.2, partial 10.1) — provider mounted, toggle in TopBar + mobile drawer (commit `1a14118`)
- [x] T1.2 Add `transition` rule for Switch thumb in `index.css` (3.10) — animates `translate` with motion tokens (commit `481e045`)
- [x] T1.3 Add `::view-transition-old/new(root)` defaults in `index.css` (1.2) — dormant until navigation opts into root view transitions (commit `a192c1d`)
- [x] T1.4 Add global `prefers-reduced-motion: reduce` override (4.1, 4.2) — collapses every animation/transition to ~instant (commit `b38e473`)
- [~] T1.5 Add global theme-flip `transition-colors` rule (10.1) — **deliberately not shipped.** A global `*` rule made every hover/focus feel laggy because it animated all color changes, not just theme flips. Reverted before commit. Theme flip snaps, matching Vercel/Linear/GitHub. If smooth theme flips become a priority, the cleaner approach is wrapping `setTheme()` in `document.startViewTransition()` so the T1.3 root rule activates browser-side.
- [x] T1.6 Restore `<main>` scroll on browser back via custom `useScrollRestoration` hook (commit `de5e919`). Wired in `AppLayout`, so it covers every `/app/*` route (dashboard, movements, accounts, categories, budgets, recurring, import, reports, review, settings, support, profile, security). React-router's `<ScrollRestoration>` doesn't fit — it's data-router-only and watches `window`, while this app uses `<BrowserRouter>` and scrolls via `<main>`.

Tier 2 — extract shared primitives:

- [x] T2.7 Extract `<Field>` to a shared component (8.7) — `src/app/components/Field.tsx`; QuickAddModal/MovementForm/RecurringForm migrated (commit `fa6c0d7`)
- [x] T2.8 Extract `<MoneyInput>` shared component; fixes the QuickAdd `type="number"` locale bug for `comma_decimal` users (8.6) — `src/app/components/MoneyInput.tsx` (commit `496f1e3`)
- [x] T2.9 Skeleton ARIA — DataTransition emits `role="status" aria-busy="true" aria-live="polite"` on the skeleton slot with a `loadingLabel` prop (2.8) (commit `55d2030`)
- [x] T2.10 `useDelayedLoading` hook (2.7, 7.5) — superseded; shipped under Stage 5.2 (`useDelayedLoading` at `src/app/lib/use-delayed-loading.ts`)

Tier 3 — production-app polish:

- [x] T3.11 Migrate `duration-200` literals to motion tokens (3.1, 3.4) — shipped 2026-05-08 (commit `c372b89`); Tabs primitive bound to `--motion-duration-base` in follow-up `014f7e3`
- [x] T3.12 Build `<SubmitButton>` with idle/loading/success/error + spinner (8.4) — shipped 2026-05-08 (commit `8b4c731`); QuickAddModal-only consumer, MovementForm form-aware variant deferred per spec; success-flash dwell tuned to 1500 ms in `d462eec` after research
- [x] T3.13 `useOptimistic` for the Status block toggle on table rows (5.1) — shipped 2026-05-08 (commit `0c5c1b0`); spec corrected with `pendingRef` requirement (`52d2c17`)
- [x] T3.14 Replace MovementForm budget `<select>` with Combobox, or document the rule — shipped 2026-05-08 (commit `1394db6`); clear-✕ structural fix in `eeb0c13`/`d3702bd`/`182dd08`
- [x] T3.15 Harden `index.html` — `theme-color` meta, description meta, per-route titles (1.7) — shipped 2026-05-08 (commit `d828161`); served-HTML correction (Razor shells, not Vite's app.html) in `ff362fc`/`de28b8a`; favicon wired in `54a8451`

Tier 4 — testing and observability: ✅ all shipped 2026-05-08

- [x] T4.16 Disable CSS animations in `test-setup.ts` (12.1) — shipped 2026-05-08 (commit `9f61771`); injects a global stylesheet zeroing animation/transition durations + scroll-behavior, removing JSDOM event-timer flakiness introduced after T3.11's motion-token migration
- [x] T4.17 Add `vitest-axe` (4.6, 12.5) — shipped 2026-05-08 (commit `e96d898`); coverage on MovementForm (3 modes), QuickAddModal (3 tabs), AppLayout shell; `expectNoA11yViolations` helper gates on `serious`/`critical` severities; surfaced `button-name` violations on AccountCombobox/CategoryCombobox/BudgetCombobox triggers and fixed via `aria-label={selected?.name ?? placeholder}`
- [x] T4.18 Add bundle visualizer (`rollup-plugin-visualizer`) + size budget on `dist/assets/*.js` (5.5) — shipped 2026-05-08 (commit `c37f4eb`); emits `dist/stats.html` treemap on every build; `scripts/check-bundle-size.mjs` enforces gzip budgets on the seven largest chunks at current+20% headroom; chained into `pnpm build`

Tier 5 — discretionary: ✅ T5.20/T5.21 shipped 2026-05-09; T5.19 deferred

- [ ] T5.19 `@formkit/auto-animate` for lists that reorder (0.3, 3.6) — **deferred 2026-05-09: no candidate consumer.** Auto-animate animates between two states of the same children (slide instead of snap when items insert/remove/reorder). The SPA's lists today refetch-on-filter-change — the whole list is replaced, not items reordered. No drag-to-reorder UI exists. Reopens when a real reordering surface emerges (e.g., custom dashboard widget order, manual category sort).
- [x] T5.20 OKLCH support in `parseRgb()` so showcase shows live contrast ratios — shipped 2026-05-09 (commit `17d6e93`); also drops the try/catch fallback in `SwatchGrid` that previously masked the missing parser
- [x] T5.21 Add motion rules to "Working rules" section in `design-system.md` — shipped 2026-05-09 (commit `37f55fe`); inserted as rule 7, renumbered Pre-commit audit + UX/UI verification checklist

### "Feels well done" gut-check — verification checklist

From the same audit doc (§ 15). With all five tiers shipped (T1–T5 done as of 2026-05-09), most boxes are now ticked. Remaining `[ ]` and `[~]` items are deliberately deferred — they tie to features that have no consumer today (Button primitive scale-on-press, opt-in `<Link viewTransition>` for cross-fade page transitions). Reopen if a consumer emerges.

- [x] No element appears or disappears instantly except in response to typing — skeleton/data cross-fade ships across the SPA via DataTransition
- [x] No content jumps when data loads — skeleton heights match reality
- [~] Hovering any button gives visible feedback within 150 ms (uses `duration-200`, fine; partial)
- [ ] Pressing any button gives a subtle scale/color change (verify Button primitive — Tier 3)
- [x] Tab key reveals a clear focus ring on every interactive element
- [~] Switching themes is smooth, not flashy (T1.1 shipped; T1.5 deliberately not shipped — flip is a clean snap, not flashy)
- [~] Navigating between pages cross-fades, doesn't snap (T1.3 root rule shipped, dormant until navigation opts in via `<Link viewTransition>` — Tier 3 follow-up)
- [x] Submitting a form shows immediate feedback (T3.12 SubmitButton shipped 2026-05-08)
- [x] No spinners flash for <200 ms (Stage 5.4 rollout shipped)
- [x] All icons sized identically in similar contexts
- [x] Border radii consistent
- [x] In Reduce Motion mode, the app still works and animations are subdued (T1.4 global override + T2.9 status region)
- [x] Switch toggle slides smoothly (T1.2 — `transition: translate` on `--motion-duration-base`)

### Ad-hoc UX fixes (sub-stage 5.10)

Polish work that surfaced from real-world use rather than the polish-checklist audit. Shipped 2026-05-08.

| Fix | Commit |
|---|---|
| `IsCleared` toggle silently dropped on movement Create across Transactions/Transfers/LiabilityPayments — request DTOs, create VMs, controller mappings, and service entity-construction all missed the field; added with service-level tests + `docs/models.md` sync | `ba2ce8b` |
| Recurring Edit form stole focus on every keystroke (focus-heading effect ran on every `values` change instead of once after data loaded) | `c5ebb9e` |
| `AccountCombobox`/`CategoryCombobox` had no clear affordance — added opt-in `onClear?: () => void` prop with an inline ✕ on the trigger; wired at every consumer site (forms set state to null, filter bars delete the URL param) | `5a5b254`, `c7ea718` (visibility tweak) |
| Movements filter-bar fields were crumped to the right; switched to proportional widths (Search `flex-[2]`, others `flex-1`) so Search/Account/Type/Date spread evenly | `8f067af` |
| Movements currency-tabs flicker — strip popped in once accounts loaded, pushing the page down; render an `h-8` Skeleton placeholder while loading so single- vs multi-currency uncertainty doesn't shift layout | `c0a3a89` |
| Movements date-range trigger didn't fill its column after the proportional-width change; pass `className="w-full"` from `MovementsDateRangePicker` | `47c1c95` |
| Movements pagination was `justify-center` and flush against the table; right-align + add `space-y-4` breathing room | `3b489e0` |
| Reports pagination (`<ReportTableCard>`) was `justify-center`; right-align to match — propagates across all 8 reports | `dadb667` |
| Two empty-state CTAs missed `nativeButton={false}` so base-ui logged a warning every render of the empty state; full SPA audit cleared every site | `3454cc8` |

Two of these (`IsCleared on Create` and the Recurring focus-loss) are bug fixes, not polish — they're tracked here so the next session knows the resolution context. The IsCleared bug was wide enough to warrant its own service-level test trio in `IsClearedTests.cs`, `TransactionServiceTests.cs`, and `LiabilityPaymentServiceTests.cs`.

#### Tier 3 follow-up fixes (2026-05-08, post browser-pass)

A second wave of fixes surfaced during the Tier 3 verification browser-pass on iPhone SE. Shipped same day.

| Fix | Commit |
|---|---|
| Budgets list showed stale data after Create/Edit — only Budgets pair lacked the `useOutletContext().refetch()` call before navigate-back; Accounts, Categories, Recurring, Import Profiles already had it | `5ee1021` |
| QuickAddModal `+` button was hidden on mobile (gated behind `isDesktop`); lifted out of the desktop-only group so the modal is reachable on phones | `b3af129` |
| Movements header overflowed at 375 px; collapsed bulk-action button labels to icon-only below `sm` and added `flex-wrap` as fallback | `f90fff3` |
| Combobox clear ✕ rendered as a `<button>` inside the Popover trigger's `<button>`, triggering React's nested-button hydration warning; hoisted ✕ as an absolute sibling outside the trigger | `eeb0c13` |
| Combobox icons stopped hugging the right edge after the hoist; restored chevron + ✕ inset by reorganising the relative wrapper | `d3702bd` |
| Movements filter bar's global Clear button wiped `?currency`, briefly losing the active currency tab and unfiltering the Account dropdown — preserve `currency` when clearing filters | `5b4d50a` |
| SubmitButton success-flash dwell felt too short at 800 ms; bumped default to 1500 ms after research (industry consensus 1500–3000 ms for post-success dwell) | `d462eec` |
| TopBar `+` and avatar were practically touching with `gap-1`; bumped to `gap-2` for proper breathing room | `0bcab20` |
| MovementsCardList — new mobile card layout below `md`, replacing the desktop table with stacked cards (Smashing/Pencil & Paper/UXmatters consensus pattern for mobile transaction lists); shared `amountColor` helper extracted to `movement-type-display.ts` | `42b05e3` |
| Mobile card collisions — kebab overlapped amount on Line 1; mobile gutters reduced to `p-4` below `md`; long text now truncates instead of running behind the status badge | `228a1f1` |
| Mobile card actions inlined as flex siblings (instead of absolute-positioned) for natural alignment; currency tabs span full width on mobile | `f0776d5` |
| Type pill text wasn't left-aligned with the description below it on the card; `-ml-[9px]` cancels the Badge's internal `px-2` + 1 px border so pill text shares the lines-below x-position | `a5a2093`, `182dd08` |
| Movements header buttons cluster sat left-aligned when wrapped on mobile; `ml-auto` right-aligns the cluster | `55807f8` |
| `+ New` dropdown trigger label collapsed to icon-only below `sm` so the cluster fits beside the h1 on the same row | `96c2cd5` |
| TabsTrigger transition was Tailwind's default 150 ms (not migrated by T3.11); bound to `--motion-duration-base` for consistency with the rest of the app | `014f7e3` |
| TabsList stretched full-width on desktop after the mobile fix; `md:w-fit` restores the chip-style strip at md+ | `5e84207` |
| Razor SPA shells (`Views/App/Index.cshtml`, `Views/Shared/_Layout.cshtml`) had no favicon link, causing browser fallback to `/favicon.ico` 404; copied SVG to `wwwroot/` and wired explicit `<link rel="icon">` tags | `54a8451` |

#### Tier 5 follow-up fixes (2026-05-09, post Stage-5 close-out)

A handful of report-page polish surfaced after Stage 5 was marked done. Shipped same day.

| Fix | Commit |
|---|---|
| Reports sticky-chrome's `-mt-6` overshoot the mobile gutter (`p-4` after the responsive change), leaving a transparent strip above the chrome on phones; matched negative margin to breakpoint (`-mt-4 md:-mt-6`) | `1a58892` |
| Six reports (BudgetVsActual, IncomeExpense, MonthlyCashFlow, ExpenseBreakdown, LargestExpenses, NetWorthOverTime) wrapped KPI tile grid + ReportTableCard in a React fragment inside `<DataTransition>` — fragment's siblings don't get the outer `space-y-6`, so cards touched the table; replaced with `<div className="space-y-6">` | `4e2244a` |
| Sticky chrome's `-mt-*` extension wasn't paired with a matching `pt-*`, so the chrome's bg only painted around the tabs/title/filters; added `pt-4 md:pt-6` so the bg fills the full sticky region | `dea45a9` |
| `top: 0` sticky pinned the chrome at `<main>`'s content-box top, leaving 24px of `<main>`'s padding region uncovered; scrolled rows leaked through that strip between the topbar and the report tabs. Switched to `top: -1rem` mobile / `-1.5rem` desktop so the chrome anchors at `<main>`'s outer edge | `2c59535` |

---

## Stage 6 — Identity infrastructure (Batch 3b)

**Status: ✅ Done (2026-05-11).** All sub-stages shipped: 6a (Identity + Argon2id + sessions + CSRF + global authz fallback), 6b.2 (lockout + rate limits + failed-login logging), 6b.3 (13 security hardening fixes), 6c.1 (password reset), 6c.2 (reauth middleware), 6.12 (email change), 6.14 (audit log), 6.10 (lockout self-service unlock), 6.15 (HMAC TokenLookup — closes Argon2id-amplification DoS), the Stage 6b.1 latent TOTP-counter bug fix, the Stage 6c.2 `AuthMfaByUser` rate-limit partition follow-up, and the Stage 6 close-out authentication flow diagrams in `security-model.md`. 303/303 Authentication integration tests green. Stage 7 (multi-tenancy cutover) can now build on a real `AspNetUsers` table.

> **Goal:** ASP.NET Core Identity is wired with hardened options, password hashing pinned to Argon2id at OWASP minimums, TOTP infrastructure in place (encrypted seed, persistent replay-prevention, hashed backup codes), `UserSession` table and CSRF middleware operational, global authorization fallback policy enforced. **No UI yet — integration tests only.**

### Sub-stages

| # | Sub-stage | Spec / Reference |
|---|---|---|
| 6.1 | ASP.NET Core Identity + EF stores | `security-model.md` § ASP.NET Core Identity Hardening |
| 6.2 | Argon2id password hasher with pinned parameters (m=19456, t=2, p=1) | `security-model.md` § Passwords + `planning-phase3.md` § Password policy |
| 6.3 | `UserSession` table + token rotation + revocation | [ADR-0019](decisions/ADR-0019-session-management-user-configurable-with-ip-controls.md) + `security-model.md` § Sessions |
| 6.4 | TOTP enrollment + verification + replay prevention + backup codes | `security-model.md` § TOTP Secrets + § TOTP Backup Codes + `planning-phase3.md` § MFA |
| 6.5 | CSRF middleware (XSRF-TOKEN double-submit pattern) | [ADR-0063](decisions/ADR-0063-cookie-samesite-lax-with-csrf-tokens.md) + `security-model.md` § CSRF |
| 6.6 | Global authorization fallback policy (`RequireAuthenticatedUser`) | `security-model.md` § Global Authorization Policy |
| 6.7 | Cookie configuration (`__Host-` prefix, `HttpOnly`, `Secure`, `SameSite=Lax`) | ADR-0063 + `security-model.md` § Cookie Configuration |
| 6.8 | Rate limiting on `/login`, `/register`, `/password-reset` | `security-model.md` § Login + `planning-phase3.md` § Rate limiting |
| 6.9 | Failed-login logging table | `security-model.md` § Login → Failed login logging |
| 6.10 | Account lockout policy + self-service unlock signed-token endpoint | `security-model.md` § Login → Account lockout + Self-service unlock |
| 6.11 | Password reset flow (256-bit token, Argon2id-hashed, 15-min expiry, single-use, MFA-gated, session revocation on success) | `security-model.md` § Password Reset |
| 6.12 | Email-address-change flow (dual-address verification, 7-day revoke link to old address) | `security-model.md` § Email Address Change |
| 6.13 | Re-authentication middleware for sensitive operations | `security-model.md` § Login → Reauthentication |
| 6.14 | Audit log table (`AuditLog` entity + writer service) | `planning-phase3.md` § Audit logging |
| 6.15 | O(1) token verify via HMAC `TokenLookup` column on `PasswordResetToken` + `EmailChangeToken` (closed the Argon2id-amplification DoS vector on `/confirm` + `/revoke`). Pre-6.15 the green test suite was misleading: tests passed because `AuthTestTokenCleanup.DeleteAllTestTokensAsync` emptied the token tables between test classes, while production had no equivalent cleanup and tokens accumulated naturally (up to 7 days for email-change RevokeOld). 6.15's ship-gate deleted the test-side cleanup and replaced it with regression tests that insert N=200 dummy rows; the full Authentication suite stayed green under accumulated load. See § 6.15 recap below and `docs/superpowers/specs/2026-05-11-stage-6-15-token-lookup-design.md` § 1 and § 8.4. | `planning-phase3.md` § Stage 6.15 — Argon2id-O(N) DoS vector on token verify (resolved) |

### Verification checklist

> **Stage 6a (2026-05-09):** Identity wiring + Argon2id + sessions + cookies + CSRF + global authorization fallback are shipped (24 integration tests in `ProjectCeres.Tests/Integration/Authentication/`). Items below are marked `[x]` for 6a; remaining items are scoped to 6b (TOTP, lockout, rate-limit, failed-login logging) and 6c (password reset, email change, reauth, audit log).

> **Stage 6b.2 (2026-05-10):** Lockout enforcement + sliding-window rate limits + failed-login logging + backup-code-during-lockout shipped (54 ship-gate tests in `ProjectCeres.Tests/Integration/Authentication/`). 6b.1 latent bug fixed: `TwoFactorAuthenticatorSignInAsync` replaced with `VerifyTwoFactorTokenAsync`, so wrong TOTP codes no longer increment the password lockout counter. All `Unauthorized(...)` returns aligned to api-contract envelope shape. Items below marked `[x]` for 6b.2; remaining lockout-email + password-reset rate limit + self-service unlock items are scoped to 6c.

> **Stage 6b.3 (2026-05-10):** Security hardening — 13 production fixes (1 Critical, 4 High, 4 Medium, 2 Low + 2 surfaced by edge-case tests) closing post-6b.2 audit gaps. Per-user semaphores on backup-code consume + persistent-cookie rotation; persistent cookie sessionId-prefix eliminates pre-auth DoS; backup-code regen requires current TOTP; MFA re-enroll returns 409; register no longer enumerates accounts; CSRF gets own rate-limit bucket; logout rate-limited; SessionRevocationValidator debounced; MfaAwareLengthValidator wired to TwoFactorEnabled; persistent-cookie rotation closes SecurityStamp window. 22 net-new edge-case tests added. Stage 6c continues with password-reset, email-change, reauth middleware, audit log.

> **Stage 6c.1 (2026-05-10):** Password-reset flow shipped — 2 endpoints (`POST /api/auth/password-reset/request` and `/confirm`), Argon2id-hashed single-use tokens (15-min expiry, base64url URL fragment), MFA-conditional gating per ADR-0069 (TOTP only — backup codes rejected at reset), all-sessions-revoked + `SecurityStamp` regen on success, lockout cleared on success, `EmailConfirmed` promoted on success, supersession-on-new-request via per-user `SemaphoreSlim`. Rate limits: per-IP `AuthLoginByIp` (10/min) + service-side per-email `MemoryCache` window (5/hour). New `IEmailService` abstraction with dev-only `LogOnlyEmailService`; production deliberately throws at startup until Stage 8 wires the real provider. Surfaced + fixed two real bugs in production code while writing tests: `MfaVerifiedAt` was loaded `AsNoTracking` and never persisted (now written via `ExecuteUpdateAsync` on the MFA branch); `PasswordResetConfirmRequest.TotpCode` `[StringLength(8)]` blocked backup-code-length submissions from reaching the service-side shape check (relaxed to 32 so the 401 `INVALID_MFA_CODE` rejection lands per spec). 46-test ship-gate green (request, confirm-no-mfa, confirm-mfa, concurrency, rate-limits, session-revocation, architecture). Stage 6c continues with email-change (6.12), reauth middleware (6.13), audit log (6.14), and lockout self-service unlock.

> **Stage 6c.2 (2026-05-10):** Reauth middleware shipped. New `LastReauthAt` claim stamped at login (no-MFA + MFA + backup-code paths), refreshed via `RefreshSignInAsync` after step-up. New `POST /api/auth/reauth` endpoint accepts `{password?}` or `{totpCode?}` based on `user.TwoFactorEnabled`. New `[RequireRecentAuth]` attribute backed by `RecentAuthRequirement` policy + custom `IAuthorizationMiddlewareResultHandler` emitting `401 REAUTH_REQUIRED`. 5-minute freshness window. Three existing MFA endpoints (`/enroll`, `/enroll/verify`, `/backup-codes/regenerate`) retroactively gated; in-body TOTP from `/backup-codes/regenerate` removed and `RegenerateBackupCodesRequest` DTO deleted. Surfaced + fixed two real bugs in production code while writing tests: (1) `ReauthController.RefreshSignInAsync` lost the `sid` claim because the claims factory had no `PendingSessionItemKey` to copy — fixed by reading the existing `sid` from the inbound principal and staging it back into `HttpContext.Items`; (2) `AuthReauthByUser` rate-limit policy didn't actually partition per user because the rate-limit middleware runs before `UseAuthentication` — fixed by explicitly calling `httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme).Wait()` (mirroring `TotpByUserPartitioner`). 32-test ship-gate green (endpoint happy/failure/defensive, gate behaviour, retroactive gating, rate limit, cross-feature, architecture). **Follow-up flagged:** `AuthMfaByUser` (Stage 6b.3) has the same rate-limit-partition bug, but its existing test asserts only "any 429 fires" so it doesn't catch it. Same `AuthenticateAsync(TwoFactorUserIdScheme)` fix applies; tracked separately. Stage 6c continues with email-change (6.12), audit log (6.14), and lockout self-service unlock.

> **Stage 6.12 (2026-05-10):** Email-address-change flow shipped — 3 endpoints (`POST /api/auth/email-change/request|confirm|revoke`). Dual-token model: one `EmailChangeTokens` table with `Purpose` discriminator (`VerifyNew=1` 30-min, `RevokeOld=2` 7-day), Argon2id-hashed, single-use, atomic supersession on a new `/request` via per-user `SemaphoreSlim`. `/request` reauth-gated via existing `[RequireRecentAuth]` (Stage 6c.2); `/confirm` and `/revoke` anonymous because the email-link token IS the auth (per-IP `AuthLoginByIp` 10/min). Per-new-email service-side rate gate (`MemoryCache`, 5/hour). On `/confirm`: `SetEmailAsync` + `SetUserNameAsync` (registration sets `UserName == Email`; only updating `Email` would leave `FindByNameAsync(oldEmail)` resolving the user), `EmailConfirmed = true`, sibling `RevokeOld` row consumed atomically, all `UserSession` rows revoked, `SecurityStamp` regenerated. **Explicit divergence from password-reset: `/confirm` does NOT clear lockout** — email proof is not equivalent to password recovery. `/revoke` consumes both sibling rows, leaves `user.Email` UNCHANGED (critical), notifies old address only, does NOT touch sessions or `SecurityStamp`. **Cross-feature:** a successful `/api/auth/password-reset/confirm` atomically cancels any pending email-change for the same user (`ExecuteUpdateAsync` inside the existing per-user semaphore + cancellation email to old address) — closes the window where an attacker-initiated change could survive a victim's password-reset. New error codes `INVALID_EMAIL_CHANGE_TOKEN` (401), `EMAIL_ALREADY_IN_USE` (422), `EMAIL_UNCHANGED` (422). 37-test ship-gate green (request, confirm, revoke, concurrency, rate-limits, cross-feature, architecture). One small architectural cleanup along the way: dropped redundant explicit `[Authorize]` on `RequestChange` because `[RequireRecentAuth]` already inherits `AuthorizeAttribute`; the duplicate would trip the singular-attribute lookup in `Every_controller_action_declares_authorization_intent`. Stage 6c continues with audit log (6.14), lockout self-service unlock, and the `AuthMfaByUser` rate-limit-partition follow-up tracked in 6c.2.

> **Stage 6.14 (2026-05-11):** Audit-log entity + writer shipped. New `AuditLog` table with seven columns (Id, UserId, Action, EntityType?, EntityId?, OccurredAt, IpAddress) + DB-level CHECK constraint pinning the EntityType/EntityId pair invariant + two indexes (UserId+OccurredAt DESC, OccurredAt). `IAuditLogWriter` + `AuditLogWriter` (scoped, fresh `DbContext` via `IServiceScopeFactory`, loud-failure — mirrors `FailedLoginRecorder`'s pattern). 12 call sites wired: `AuthController` (Register, Login no-MFA, LoginTotp TOTP branch, LoginTotp backup-code branch, Logout), `MfaController` (EnrollVerify, RegenerateBackupCodes), `PasswordResetService` (Request known-email branch only, Confirm), `EmailChangeService` (Request, Confirm, Revoke). 4 enum values reserved-not-wired (`MfaDisabled`, `LockoutSelfServiceUnlock`, `DataExportRequested`, `GdprErasureRequested`) with `// wired in Stage X` source-file comments. 24-test ship-gate green (5 writer-level, 13 happy-path call sites, 3 negative incl. the end-to-end loud-failure `Failed_audit_insert_during_login_returns_500_AND_does_NOT_issue_session_cookie`, 3 architecture). Out of scope: read endpoint (Stage 12), retention purge (Stage 7+), EF global query filter + FK (Stage 7), financial events (Stage 7), DB-level INSERT-only role grant (Stage 16). Stage 6 close-out flow diagrams in `security-model.md` (line 573) overlay audit writes once 6.10 + 6.15 also land.

> **Stage 6.10 (2026-05-11):** Lockout self-service unlock shipped. New `LockoutUnlockToken` table (6 columns mirroring `PasswordResetToken` minus `MfaVerifiedAt`) with two indexes (UserId+ConsumedAt, ExpiresAt). New `LockoutUnlockTokenGenerator` + `LockoutUnlockService` (IssueAsync, ConfirmAsync) + `LockoutUnlockOutcome` (Success / InvalidToken). New `LockoutUnlockController` exposing single `POST /api/auth/lockout-unlock` (anonymous, AuthLoginByIp rate limit, 422 on empty token, 401 INVALID_LOCKOUT_UNLOCK_TOKEN on rejection). `AuthController.Login` wired with transition detection inside the existing `_loginLocks` semaphore (`wasLockedBefore` + `isLockedAfter` from `userStub.LockoutEnd` re-reads around `PasswordSignInAsync`); `IssueAsync` fires only in the `signIn.IsLockedOut` branch when `lockoutTransitioned` is true — the email-DoS defence ensures at most one unlock email per lockout window. `LoginTotp` is deliberately NOT wired (Stage 6b.2 removed framework counter mutation from TOTP path; lockout can only transition in `PasswordSignInAsync`); test `LoginTotp_observing_locked_state_does_NOT_issue_token` pins this. Confirm clears `AccessFailedCount` + `LockoutEnd` only — does NOT revoke `UserSession` rows, does NOT regenerate `SecurityStamp`, does NOT log the user in (undo-only, like `EmailChangeService.RevokeAsync`). Wires the `LockoutSelfServiceUnlock` audit enum value reserved-not-wired in Stage 6.14. 18-test ship-gate green (5 issuance, 10 confirm — incl. concurrent-callers and constant-time, 3 architecture). Out of scope: 15-min cleanup sweep (Stage 7+), EF global query filter + FK (Stage 7), `/app/lockout-unlock` SPA page (Stage 9), ES email variant (Stage 8), DB-level INSERT-only role grant (Stage 16).

> **Stage 6.15 (2026-05-11):** HMAC token-lookup column shipped. New `TokenLookup byte[]` column (32 bytes, unique index) on `PasswordResetToken` and `EmailChangeToken`, derived via `HMAC-SHA256(Authentication:TokenLookupSecret, rawToken)`. New `TokenLookupHasher` singleton; secret bound from config (fail-loud on missing or `<32-byte` decoded). Single combined migration `AddTokenLookup` (EF Tools bundled both table changes into one migration after the snapshot already covered the second table — functionally equivalent to the spec's planned 2-migration split). Migration backfills existing rows via `decode(md5(TokenHash || Id::text), 'hex')` and stamps `ConsumedAt = NOW()` in the same statement so legacy rows can never match a real verify. `PasswordResetService.ConfirmAsync` + `EmailChangeService.ConfirmAsync` + `EmailChangeService.RevokeAsync` refactored from candidate-loops to single-row `FirstOrDefaultAsync` on `TokenLookup`. Constant-time Argon2id dummy hash preserved after lookup miss. **Ship-gate enforcement:** `AuthTestTokenCleanup.DeleteAllTestTokensAsync` + 12 `IAsyncLifetime.DisposeAsync` hooks DELETED. The full Authentication integration suite (298 tests) stays green under accumulated token load, proving the production fix is real and not masked by test cleanup. Two pre-existing tests (`EmailChangeConfirmTests.Confirm_with_expired_token_returns_401` and `EmailChangeRevokeTests.Revoke_with_expired_token_returns_401`) seeded EmailChange rows directly — updated to compute and persist `TokenLookup` via the new hasher. New regression coverage: 4 `TokenLookupHasher` unit tests, 3 source-text architecture tests pinning "no `ToListAsync` in verify methods", 3 DoS-amplification integration tests seeding 200 dummy rows and asserting wall-clock < 1s. Stage 6 continues with the `AuthMfaByUser` rate-limit-partition follow-up and the close-out flow diagrams.

ASP.NET Identity hardening:

- [x] `options.Lockout.MaxFailedAccessAttempts = 10`
- [x] `options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15)`
- [x] `options.Lockout.AllowedForNewUsers = true`
- [x] `options.User.RequireUniqueEmail = true`
- [x] `options.SignIn.RequireConfirmedEmail = true`
- [x] `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.FromMinutes(5)` configured (catches revoked sessions within 5 min)

Password handling:

- [x] Default `IPasswordHasher<TUser>` replaced with Argon2id implementation pinned to `m=19456, t=2, p=1`
- [x] Password policy: minimum 8 characters, no maximum below 64 (NIST SP 800-63B)
- [x] No mandatory complexity rules; breached-password check via Have I Been Pwned API or local top-N list
- [x] Password reset endpoints always run Argon2id hash (against dummy if user not found) — constant-time enumeration prevention (Stage 6c.1: `RunDummyHash` on the unknown-email branch in `PasswordResetService.RequestAsync` and on the zero-candidates branch in `ConfirmAsync`)
- [x] Login endpoint always runs Argon2id hash (same defence)

`UserSession` table + token rotation:

- [x] `UserSession` entity exists with: `Id`, `UserId`, `TokenHash`, `IpCreatedAt`, `UserAgent`, `CreatedAt`, `LastUsedAt`, `RevokedAt nullable`, `IsPersistent bool`
- [x] Session token regenerated immediately after login (session-fixation prevention)
- [x] Logout marks `RevokedAt`; the cookie is cleared; subsequent requests with the cookie are rejected
- [x] Persistent ("remember me") sessions: long-lived token in HttpOnly cookie, hash stored in DB, **rotated on each use** (issue new token, invalidate old)
- [x] Per-session IP enforcement honoured (each session anchored to its creation IP when toggle is on) *(server-side enforcement wired in 6a with default-off; UI to expose the per-session toggle is deferred to Stage 12 — Sessions SPA page. See `roadmap-phase-three.md` § Stage 12 verification)*
- [x] Per-user IP block list (`UserBlockedIp`) revokes all sessions from that IP on add

TOTP:

- [x] TOTP seed generated with cryptographically secure RNG
- [x] TOTP seed stored encrypted at rest via ASP.NET Core Data Protection (`IDataProtector`) — application-layer wiring shipped in 6b.1; *production key-storage hardening (key persistence at rest, rotation procedure) is Stage 16 (Hosting + ops) by design — see `roadmap-phase-three.md` § Stage 16*
- [x] Replay-prevention table is **persistent** (database or Redis), not in-memory — survives application restart
- [x] Replay records auto-purged after 2 minutes
- [x] Backup codes hashed with Argon2id (not plaintext) — single-use, regeneration invalidates all previous codes
- [x] TOTP enrollment is **opt-in** per [ADR-0069](decisions/ADR-0069-mfa-opt-in-for-personal-users.md). Users enable from Settings → Security; once enabled, MFA is enforced on every subsequent login. Login does not block on enrollment, no grace period, no enforcement deadline. Onboarding presents MFA as recommended-but-skippable.
- [x] Backup-code use during lockout is honoured (lockout protects against password guessing, not TOTP abuse) — depends on lockout, ships in 6b.2
- [x] No SMS option exposed (SIM-swap vulnerability)

CSRF:

- [x] `IAntiforgery` middleware registered globally
- [x] All `POST`/`PUT`/`PATCH`/`DELETE` API endpoints validate the XSRF-TOKEN
- [x] `XSRF-TOKEN` cookie issued: `Secure=true`, `SameSite=Lax`, `HttpOnly=false` (SPA must read it)
- [x] Authorization endpoints `[AllowAnonymous]`-marked are exempt from antiforgery only where they have no state side-effect (e.g., GET login page); login POST validates
- [x] CSRF token rotation on login/logout

Global authorization:

- [x] `AddAuthorization` configured with `FallbackPolicy = RequireAuthenticatedUser()`
- [x] `[AllowAnonymous]` applied **only** to: `/api/auth/register`, `/api/auth/login`, `/api/auth/csrf`, `/api/health`, the legacy Razor SPA-shell controllers (App, Home), and the legacy 302-redirect controllers slated for Batch-4 deletion. Stage 6c expands the whitelist with `/api/auth/password-reset/*`, `/api/auth/email-verify/*`, and `/api/auth/lockout-unlock`.
- [x] Architecture test: any controller without `[Authorize]` or `[AllowAnonymous]` attribute fails the build (catches forgotten attributes)

Cookie configuration:

- [x] Auth cookie name uses `__Host-` prefix (e.g., `__Host-Session`)
- [x] `HttpOnly = true`, `Secure = true`, `SameSite = Lax` *(Secure is `Always` in Production, `SameAsRequest` outside Production for WAF tests; the `__Host-` prefix browser-side enforces Secure regardless)*
- [x] No `Domain` attribute set (forced by `__Host-` prefix)
- [x] `Path = /`
- [x] Verified in browser DevTools that the cookie has all four attributes after a successful login *(manual browser verification is Stage 9 — first stage with a real login UI; the cookie configuration itself is wired and tested in 6a — see `CookieAttributesTests`)*

Rate limiting:

- [x] `/login` endpoint: 10 requests/min/IP minimum, fixed-window
- [x] `/register` endpoint: 10 requests/min/IP minimum
- [x] `/password-reset` endpoint: 10 requests/min/IP minimum, plus per-account rate limit (Stage 6c.1: per-IP via existing `AuthLoginByIp` policy; per-account is a service-side `MemoryCache`-backed sliding window keyed by lowercased email, 5/hour, returns 429 `RATE_LIMITED` with `Retry-After`)
- [x] Account lockout: lock for 15 min after 10 failed attempts; counter resets on successful login
- [x] Lockout email includes a time-limited signed unlock link separate from the password-reset flow *(Stage 6.10)*
- [x] `/login/totp` endpoint: rate-limited per user (after credentials valid, before TOTP)

Failed-login logging:

- [x] Every failed login logs: timestamp, IP, user-agent, whether the failure was credential-based or TOTP-based
- [x] Attempted password is NEVER logged
- [x] Logs queryable for distributed credential-stuffing detection (multiple accounts, same source IP)

Password reset (Stage 6c.1, shipped 2026-05-10):

- [x] Token: 256-bit cryptographically random, base64url-encoded (`PasswordResetTokenGenerator.Generate` via `RandomNumberGenerator.GetBytes(32)`)
- [x] Stored as Argon2id hash only (never the raw token) — `PasswordResetToken.TokenHash`, same hasher as passwords (`m=19456 t=2 p=1`)
- [x] 15-minute expiry from issuance — `PasswordResetService.TokenLifetime`
- [x] Single-use: invalidate on first successful submission — `ConsumedAt` stamped synchronously in same DB transaction as password write
- [x] On successful reset: revoke all `UserSession` rows for that user — bulk `ExecuteUpdateAsync` plus `UpdateSecurityStampAsync` to invalidate in-flight Identity cookies via `SecurityStampValidator`
- [x] Reset flow requires a valid TOTP code before accepting the new password — **only when** `user.TwoFactorEnabled = true`, per ADR-0069 (MFA opt-in). No-MFA users complete reset with the email-link token alone; email-channel ownership is the second factor
- [x] Backup codes are the recovery path if TOTP device is lost — NOT a TOTP bypass during reset (service-side `MfaConstants.TotpCodeShape` regex rejects backup-code shapes with 401 `INVALID_MFA_CODE`)
- [x] Email always sent on reset request (registered or not — same response, same wall-clock timing) — known and unknown branches both run `RunDummyHash`; unknown branch records `FailedLoginAttempt` with reason `PasswordResetUnknownEmail`. **Note:** the unknown branch does NOT actually send an email (suppressed to avoid being a spam relay); the constant-time defence is the dummy hash, not a phantom send. Notification-on-unknown is reconsidered for Stage 8 once the real provider lands.
- [x] Separate notification email sent on successful password change — `BuildChangedEmail`, queued non-blocking via `IEmailService.SendAsync`

Email-address change (Stage 6.12, shipped 2026-05-10):

- [x] Reauthentication required before initiating — `EmailChangeController.RequestChange` carries `[RequireRecentAuth]`; arch test `EmailChangeController_has_correct_attribute_matrix` pins it; integration tests `Request_without_recent_reauth_returns_401_REAUTH_REQUIRED_and_writes_no_rows` and `Request_does_NOT_bypass_reauth_with_valid_session_cookie` exercise stale + missing claims.
- [x] Verification link sent to new address (256-bit token, 30-min expiry, single-use) — `EmailChangeService.VerifyTokenLifetime`, hash via `EmailChangeTokenGenerator` (`Argon2idPasswordHasher`), single-use enforced by `ConsumedAt` set in same `ExecuteUpdateAsync` as the Identity update. Test #1 asserts row + email shape.
- [x] Notification + revoke link sent to old address (256-bit token, 7-day expiry) — `EmailChangeService.RevokeTokenLifetime`. Test #1 asserts the second email + distinct token.
- [x] Old address remains the address-of-record until new address verified — `Revoke_happy_path_leaves_user_Email_UNCHANGED` (critical assertion); `/request` does NOT mutate `AspNetUsers.Email`.
- [x] Notification email sent to old address on successful change — `Confirm_sends_change_confirmed_email_to_BOTH_new_and_old_addresses`. Implementation sends to BOTH old and new addresses on confirm (security-model.md mandates the old-address notification; the new-address notification confirms the new state for the legitimate user).

Reauthentication for sensitive operations (Stage 6c.2, shipped 2026-05-10):

- [x] Fresh password (or TOTP) entry required before: re-enrol TOTP (`/api/auth/mfa/enroll`, `/mfa/enroll/verify`), regenerate backup codes (`/mfa/backup-codes/regenerate`). Change-password and email-change land in 6.12; sessions list lands in Stage 12; GDPR erasure lands in Stage 13 — all will declare `[RequireRecentAuth]` when shipped. Architecture test #29 pins the three currently-gated endpoints.
- [x] Persistent sessions ("remember me") do NOT bypass reauthentication — the gate reads the `LastReauthAt` claim on the application cookie, which is independent of the `__Host-Persist` rotation. Test #31 (`RefreshSignInAsync_after_reauth_does_not_disrupt_persistent_cookie`) pins this.
- [x] Reauthentication grant is short-lived (5 minutes) and scoped to a single sensitive action — `RecentAuthRequirement.Window = 5 min`. Test #18 (stale at 301s) and #19 (boundary at 300s) pin the window.

Audit logging:

- [x] `AuditLog` entity: `Id`, `UserId`, `Action`, `EntityType`, `EntityId`, `OccurredAt`, `IpAddress` *(Stage 6.14)*
- [x] Writes for: login (no-MFA / MFA / backup-code), logout, registration, password reset (request known-email + confirm), email change (request / confirm / revoke), MFA enrolment, backup-code regeneration *(Stage 6.14)*. Data export request + GDPR erasure call sites land in Stage 13; MFA-disable when a disable endpoint ships; lockout self-service unlock in Stage 6.10. Financial events (Transaction/Transfer Created/Deleted) deferred to Stage 7 with the multi-tenancy cutover.
- [x] Financial amounts NEVER appear in audit entries *(architecture test `AuditLog_entity_contains_no_financial_amount_columns`)*
- [x] 6-month auto-purge job *(deferred to Stage 13 — `IUserJobRunner` cross-tenant background-job foundation from Stage 7 (ADR-0067) is the prerequisite; AuditLog table + writer ship in 6.14 and are ready to be consumed by the purge job. The cron registration is row 13.6 + the verification line at Stage 13 below)*

Tests required before Stage 7 begins:

- [x] Login happy-path integration test (credentials → TOTP → cookie issued) — `LoginEndpointTests.Login_with_valid_credentials_creates_UserSession_and_sets_session_cookie` (no-MFA branch); `Mfa/LoginWithoutMfaTests.Login_without_mfa_returns_204_and_session_cookie_regardless_of_account_age`; `Mfa/LoginWithTotpTests.LoginTotp_with_valid_totp_issues_session_cookie_and_inserts_user_session` (MFA branch). Audited 2026-05-11.
- [x] Login wrong-password test returns identical error message + timing as login with non-existent user — `LoginEndpointTests.Login_returns_401_on_unknown_email_with_same_shape_and_status_as_wrong_password` (same envelope shape); `LoginEndpointTests.Login_returns_401_on_wrong_password` (same status). Audited 2026-05-11. Sub-200ms constant-time wall-clock parity is enforced by the `RunDummyHash` defence that's separately tested in `PasswordResetRequestTests.Request_with_unknown_email_returns_204_with_same_timing`.
- [x] Login wrong-TOTP test returns generic error — `LockoutBehaviorTests.WrongTotp_DoesNotIncrementPasswordLockoutCounter` (returns 401 on every wrong TOTP submission); `Mfa/LoginWithTotpTests.LoginTotp_with_replayed_code_returns_401` (replay → 401). Audited 2026-05-11.
- [x] Account lockout test: 10 failed attempts locks; 11th returns lockout error
- [x] Self-service unlock token test: valid token unlocks; expired token returns error — `LockoutUnlockConfirmTests.Confirm_with_valid_token_clears_AccessFailedCount_and_LockoutEnd` (valid unlocks); `LockoutUnlockConfirmTests.Confirm_with_expired_token_returns_401` (expired errors); `LockoutUnlockConfirmTests.Confirm_with_unknown_token_returns_401_INVALID_LOCKOUT_UNLOCK_TOKEN` (unknown). Audited 2026-05-11.
- [x] TOTP replay test: same code used twice within window is rejected on second use
- [x] TOTP replay survives app restart (persistent store, not in-memory)
- [x] Password reset happy-path integration test (request → email → click link → enter TOTP → set new password → all sessions revoked) — Stage 6c.1: `Confirm_no_mfa_succeeds`, `Confirm_with_mfa_two_step_succeeds`, `Successful_reset_revokes_all_user_sessions`
- [x] Password reset enumeration test: same response + timing whether email exists or not — Stage 6c.1: `Request_with_unknown_email_returns_204_with_same_timing` (200ms threshold; constant-time defence is `RunDummyHash` parity), `Request_with_unknown_email_does_not_create_db_row`, `Request_with_unknown_email_does_not_send_email`, `Request_per_email_limit_does_not_leak_user_existence`
- [x] CSRF test: state-changing request without XSRF-TOKEN header returns 400/403 — `CsrfTests.State_changing_request_without_csrf_token_returns_400`; `CsrfTests.CsrfCookiePresent_HeaderMissing_Returns400`; `CsrfTests.CsrfTokenFromUserA_OnUserBSession_Rejected` (cross-user token rejection); `CsrfTests.LoginTotp_WithoutCsrfHeader_Returns400` (extends to TOTP step). Audited 2026-05-11.
- [x] Reauthentication test: a gated endpoint without a fresh `LastReauthAt` claim returns 401 `REAUTH_REQUIRED` even with a valid session — Stage 6c.2 (`Gated_endpoint_with_stale_claim_returns_401_REAUTH_REQUIRED`, `Gated_endpoint_with_no_claim_returns_401_REAUTH_REQUIRED`, `MfaRegenerateBackupCodes_now_requires_recent_auth_not_in_body_totp`). Change-password specifically lands with 6.12.
- [x] Stage 6.15 verify-path is O(1) under accumulated token load — `TokenLookupArchitectureTests` (3 source-text architecture pins on `PasswordResetService.ConfirmAsync`, `EmailChangeService.ConfirmAsync`, `EmailChangeService.RevokeAsync`); `PasswordResetVerifyDosAmplificationTests` + `EmailChangeVerifyDosAmplificationTests` (3 DoS regressions seeding 200 dummy rows, wall-clock < 1s); `TokenLookupTamperResistanceTests` (3 defence-in-depth Argon2id checks). Audited 2026-05-11.
- [x] Stage 6c.2 follow-up `AuthMfaByUser` partition isolation — `Mfa/MfaRegenerateRateLimitTests.MfaRegenerate_rate_limit_is_partitioned_by_user` (user A's exhaustion does not exhaust user B); `Mfa/MfaRegenerateRateLimitTests.MfaRegenerate_anonymous_request_returns_401_not_429` (anonymous-fallback branch unreachable via `[Authorize]` short-circuit). Audited 2026-05-11.

Stage 6 close-out documentation:

- [x] **Authentication flow diagrams in `security-model.md`** — shipped 2026-05-11 as `security-model.md § Authentication Flow Diagrams (Stage 6 close-out)`. 11 Mermaid diagrams in a single end-of-stage pass: request pipeline; registration; login no-MFA branch; login MFA TOTP branch; login backup-code branch; password reset (request + confirm); reauth step-up + `[RequireRecentAuth]` gate; email-address change (request + confirm + revoke); lockout self-service unlock; audit-log writes overlay; cross-flow authentication state machine. Each diagram is paired with explicit audit prompts pointing at the integration tests that pin the behaviour shown.

---

## Stage 7 — Multi-tenancy cutover (Batch 3c)

**Status: ✅ Done (2026-05-12).** All 19 tasks shipped across two commit-trains on `main`. Commit 1 delivered the reversible scaffolding (Tasks 1–13): `IUserScope` + `IUserJobRunner` primitives; the unified `ICurrentUserAccessor` resolving HTTP-context → AsyncLocal-scope → `Guid.Empty` safe default; `: IUserOwned` promotion of 8 auth-internal entities; `Category.IsReserved` column + policy rewrite; canonical `Categories.Defaults` list; `CategorySeedService` wired into registration; `OwnedOrShared` → `Owned` call-site flip with a transitional `Category` bridge; EF global query filters on 22 entities (Movement as TPC root + 11 finance + 8 auth-internal + Category) with `IgnoreQueryFilters()` opt-outs at every legitimate pre-auth code path; architecture test pinning the `IgnoreQueryFilters()` allow-list; 15-test IDOR cross-tenant isolation suite (including the negative-assertion test proving the global filter catches a leak independently of service-layer `.Owned()` discipline); empty-DB boot regression test. Commit 2 delivered the destructive cutover (Tasks 14–18): `FakeCurrentUserAccessor` test double; `RemapSentinelToFirstUser` one-shot migration (pre-checks, 14 explicit-table UPDATEs, post-check, empty `Down()`; applied as no-op on dev + test DB because neither has registered users yet); `MakeCategoryUserIdNonNullable` schema migration + bridge removal (`IOptionallyUserOwned`, `OwnedOrShared`, and the Category dual-interface bridge all deleted); `SingleUserAccessor` class + `SentinelUserId` constant + sentinel-stamped seed methods deleted; the 78 test sites swapped to `FakeCurrentUserAccessor(sentinel)`. Task 19 closed out planning + docs. 956/956 tests green at HEAD. Stage 7.5 (PostgreSQL RLS, ADR-0068) is the immediate next stage.

> **Note: HttpContextCurrentUserAccessor contract change vs ADR-0067.** Task 9 softened the "throw `InvalidOperationException` when neither resolves" rule from ADR-0067 to "return `Guid.Empty`". EF Core eagerly evaluates global query filter expressions at model creation time, before any HTTP context or `IUserScope` is established — a throw at that moment crashes the app on startup. The safe default (`Guid.Empty`) means filters then produce a `WHERE` clause matching zero rows. The "no leakage" invariant still holds via the Task 10 architecture test that allow-lists every legitimate `IgnoreQueryFilters()` call site. Documented inline at `HttpContextCurrentUserAccessor.cs:18–24` and propagated to `multi-tenancy-strategy.md`, `planning-resolved.md` (with supersession annotation per the routing-table rule).
>
> **Note: RemapSentinelToFirstUser ran as a no-op on dev DB.** Both `project_ceres` (0 users) and `project_ceres_test` (after a clean wipe + replay due to 6,412 accumulated test-user stragglers from months of integration runs that tripped the production-correct "exactly 1 user" precondition) applied the migration via the "0 users → clean no-op" branch. The Phase 1/2 sentinel-tagged data in dev DB stays sentinel-tagged until the first real user registers — at which point Task 15's `Up()` body fires and remaps the cohort onto that user. The migration is on disk and tested in both branches (the `expected exactly 1` abort branch correctly fired against the test DB before the wipe).

> **Goal:** the sentinel `SingleUserAccessor` is replaced with a real, HTTP-context-backed `ICurrentUserAccessor`; EF global query filters apply to every user-owned entity; the `IUserScope` + `IUserJobRunner` primitives ship; the sentinel-to-real-user data migration runs on first registration; every existing service is audited for `UserId` scoping; the IDOR integration test suite is green.

### Sub-stages

| # | Sub-stage | Spec / Reference |
|---|---|---|
| 7.1 | `IUserScope.EnterAs(userId)` + AsyncLocal storage | [ADR-0067](decisions/ADR-0067-background-job-user-scope-with-iuserscope-and-runner.md) |
| 7.2 | `IUserJobRunner.ForEachUserAsync(filter, work)` runner | ADR-0067 |
| 7.3 | New `ICurrentUserAccessor` impl: HTTP context → AsyncLocal scope → throw | ADR-0067 |
| 7.4 | EF global query filters on every user-owned entity | [ADR-0065](decisions/ADR-0065-ef-global-query-filters-with-explicit-redundancy.md) |
| 7.5 | Architecture test gating `IgnoreQueryFilters()` to `Admin/` namespace | ADR-0065 |
| 7.6 | Sentinel-to-real-user data migration | [ADR-0066](decisions/ADR-0066-sentinel-remap-to-first-registered-user.md) |
| 7.7 | Remove `SingleUserAccessor` and the sentinel UUID constant | ADR-0066 |
| 7.8 | Service audit (every existing service that performs a query by ID) | `multi-tenancy-strategy.md` § Services to audit for Phase 3 |
| 7.9 | Remove `ISettingsService.EnsureExistsAsync` from `Program.cs` startup | ADR-0067 |
| 7.10 | IDOR integration test suite | `multi-tenancy-strategy.md` § Required Integration Tests Before Phase 3 Launch |

### Verification checklist

`IUserScope` + AsyncLocal:

- [x] `IUserScope` interface has `EnterAs(Guid userId): IDisposable` (Task 1, `ProjectCeres/Common/IUserScope.cs`)
- [x] Internal storage is `AsyncLocal<Guid?>` (propagates across `await` boundaries within a single logical flow) — `UserScope.cs`; covered by `UserScopeTests.EnterAs_propagates_across_await_boundaries`
- [x] `Dispose()` clears the value; nested `EnterAs` calls work correctly via stack semantics — covered by `UserScopeTests.EnterAs_nests_with_stack_semantics`

`IUserJobRunner`:

- [x] `ForEachUserAsync(filter, work)` enumerates users with `IgnoreQueryFilters()` (intentionally cross-tenant query) — Task 2, `UserJobRunner.cs:23`
- [x] Per-user iteration enters scope, invokes work, exits scope — covered by `UserJobRunnerTests.ForEachUserAsync_enters_scope_per_user_in_turn`
- [x] Per-user exception isolation: one user's failure does not abort the batch — covered by `UserJobRunnerTests.ForEachUserAsync_continues_after_one_user_throws`; `OperationCanceledException` matching the runner's own token is intentionally re-thrown
- [x] Per-user logging: each iteration logs success/failure with the user's id — `UserJobRunner.cs:38–41` (`logger.LogError(ex, "Per-user job failed for {UserId}", userId)`)

`ICurrentUserAccessor`:

- [x] Resolves in this order: HTTP context → AsyncLocal scope → return `Guid.Empty` (Task 9 amendment to ADR-0067 — see status note above; original ADR said throw, EF model-creation eager evaluation forced the safe-default)
- [x] Resolution-order tests cover all four paths — `CurrentUserAccessorResolutionTests` (4 tests: HTTP wins, scope fallback, no-claim fallback, neither-resolves → `Guid.Empty`)

EF global query filters:

- [x] Filters applied to 22 entities: `Account`, `Budget`, `Category` (via temporary `IUserOwned`/`IOptionallyUserOwned` bridge), `CategoryBudget`, `ImportProfile`, `ImportStagedTransaction`, `ImportStagedTransfer`, `ImportTransferExclusion`, `RecurringTransaction`, `SavedReport`, `Settings`, `Movement` (TPC root — covers `Transaction`/`Transfer`/`LiabilityPayment`), `UserSession`, `UserBlockedIp`, `UserMfaBackupCode`, `TotpReplayEntry`, `PasswordResetToken`, `EmailChangeToken`, `LockoutUnlockToken`, `AuditLog`. See `AppDbContext.ConfigureGlobalQueryFilters`.
- [x] `TransactionAttachment` / `TransferAttachment` are NOT filtered directly — they have no `UserId` column; service code scopes them via parent (`a.Transaction.UserId == ...`). EF emits two `PendingModelChangesWarning`s about the parent-attachment FK pair being a "required end with a filtered parent" — acceptable; documented inline in `ConfigureGlobalQueryFilters`.
- [x] System tables (`AccountType`, `CategoryType`, `Currency`, `ReportType`) carry NO filter
- [x] `FailedLoginAttempt` carries NO filter (nullable `UserId`; cross-tenant retention sweep)
- [x] Service code continues to write explicit `.Where(t => t.UserId == _currentUser.UserId)` (belt-and-suspenders) — Tasks 1–8 did NOT touch existing service code beyond the `OwnedOrShared` → `Owned` flip
- [x] Test: a query against `Accounts` run via `IUserScope.EnterAs(userA)` returns only User A's rows, never User B's — covered by `GlobalQueryFilterTests.Account_query_filtered_to_current_user_via_IUserScope`

`IgnoreQueryFilters()` boundary:

- [x] Architecture test fails the build if `IgnoreQueryFilters()` appears in any file outside `ProjectCeres/Admin/` or the documented allow-list — `ArchitectureTests § IgnoreQueryFilters_only_appears_in_documented_exception_paths` (Task 10) scans every `.cs` file under `ProjectCeres/` (skipping `/Migrations/` and comment lines) and asserts every non-allow-listed hit is empty
- [x] Documented exception paths: `UserJobRunner.cs`, `CategorySeedService.cs`, `PasswordResetService.cs`, `EmailChangeService.cs`, `LockoutUnlockService.cs`, `MfaBackupCodeService.cs`, `SessionRevocationValidator.cs`, `PersistentCookieRotationMiddleware.cs`, `TotpReplayGuard.cs` — each carries an inline `// Cross-tenant by design: <reason>` comment, and the allow-list itself (in `ArchitectureTests.cs`) carries per-entry trailing justification comments
- [x] Manual boundary-fires-on-violation check (Task 10 Step 3): temporarily adding `IgnoreQueryFilters()` to `AccountService.cs` caused the architecture test to fail with a useful message naming the unexpected file. Reverted.

Sentinel-to-real-user migration:

- [x] Migration runs inside a single PostgreSQL transaction — `RemapSentinelToFirstUser.Up()` is wrapped in `DO $$ ... END $$`, which Postgres treats as a single anonymous code block under the transaction EF migrations open
- [x] Pre-check: exactly one row exists in `AspNetUsers` (the user who just registered) — `SELECT COUNT(*) INTO user_count FROM "AspNetUsers"` with `RAISE EXCEPTION` if >1 and `RAISE NOTICE; RETURN` if 0 (clean no-op for fresh installs)
- [x] Pre-check: at least one row tagged with the sentinel UUID exists in user-owned tables — `sentinel_row_count` computed across 14 tables; `RAISE NOTICE; RETURN` if 0 (clean no-op for already-remapped DBs)
- [x] If either pre-check fails, the transaction aborts cleanly (no partial state) — confirmed: the `>1 users` branch raised the exception against the test DB's 6,412 stragglers and aborted with no schema/data change
- [x] All user-owned tables remapped: `Accounts`, `Categories`, `Transactions`, `Transfers`, `LiabilityPayments`, `CategoryBudgets`, `Budgets`, `RecurringTransactions`, `SavedReports`, `ImportProfiles`, `ImportStagedTransactions`, `ImportStagedTransfers`, `ImportTransferExclusions`, `Settings` (14 tables — `TransactionAttachment`/`TransferAttachment` scope via parent and have no UserId column, so they're intentionally excluded)
- [x] Post-check: `SELECT 1 FROM <every table> WHERE "UserId" = sentinel` returns nothing across all 14 tables; if any row survives, `RAISE EXCEPTION` rolls back the transaction
- [x] If post-check fails, the transaction rolls back — the `RAISE EXCEPTION` is the rollback trigger; standard PG behaviour
- [x] Migration runs **once only** — EF's `__EFMigrationsHistory` insert at the end of the apply records the migration as applied; subsequent `dotnet ef database update` runs see it as already-applied and skip. Idempotency safeguard: the pre-checks would no-op a hypothetical re-run anyway because the sentinel rows would be gone.
- [x] Pre-deployment manual gate: snapshot procedure documented; the operator took `pg_dump` of `project_ceres` before applying. The Stage 16 operations runbook will codify this as a mandatory checklist item.

Sentinel removal:

- [x] `SingleUserAccessor` class deleted — `ProjectCeres/Common/ICurrentUserAccessor.cs` now contains only the `ICurrentUserAccessor` interface (Task 17)
- [x] Sentinel UUID constant (`00000000-0000-0000-0000-000000000001`) removed from production code — confirmed via `grep -rn "00000000-0000-0000-0000-000000000001" ProjectCeres --include='*.cs' | grep -v "/Migrations/"` (zero hits in non-migration production code; historical migration files retain inline comment references which are frozen artifacts)
- [x] Test fixtures updated to use real test user UUIDs — `WafCollection.TestWebApplicationFactory` rebinds `ICurrentUserAccessor` to `new FakeCurrentUserAccessor(sentinel-Guid)` via an instance-factory DI registration (Task 18); the existing test DB rows are still stamped with the sentinel Guid so test data resolves correctly
- [x] Seed scripts updated — `AppDbContext.SeedAccounts`, `SeedCategories`, `SeedSettings` deleted (Task 17). `CategorySeedService.CopyDefaultsForUserAsync` (Task 7) is the new per-user seed path. The system-reference seeds (`AccountType`, `CategoryType`, `Currency`, `ReportType`) intentionally remain.
- [x] No grep hit for `SingleUserAccessor` or the sentinel constant in production code — confirmed at Task 18 close-out; remaining mentions are doc-comment references in `FakeCurrentUserAccessor.cs` and `WafCollection.cs` explaining the swap

Service audit (per `multi-tenancy-strategy.md` § Services to audit for Phase 3):

- [x] All 22 services audited at Stage 7 spec time (commit `8ee7364`'s pre-spec parallel agent run): every method that queries a user-owned table chains `.Owned(user)` (the project's `ICurrentUserAccessor`-aware extension), every entity write sets `UserId = user.UserId`. Defence-in-depth via the EF global query filters (Task 9) catches any future regression; the IDOR suite (Task 11) and the negative-assertion test prove the safety net works without the service-layer chain.
- [x] `AccountService`, `TransactionService`, `TransferService`, `LiabilityPaymentService`, `CategoryService`, `CategoryBudgetService`, `BudgetService` (goal budgets via `GoalBudgetsApiController` + service), `RecurringTransactionService`, `SavedReportService` (verified via `SavedReports` query filter assertions in `Every_user_owned_entity_carries_a_global_query_filter`), `SettingsService` (per-user post-Stage-7), `DashboardService` — all scoped
- [x] All 8 report generators (`NetWorth`, `IncomeExpense`, `ExpenseBreakdown`, `TransactionHistory`, `BudgetVsActual`, `LargestExpenses`, `MonthlyCashFlow`, `NetWorthOverTime`) — scoped via `ReportService`'s `_currentUser.UserId` parameter passed into every generator
- [x] `ImportService`, `CsvImportProfileService` (named `ImportProfileService`), `TransferReviewService`, `ImportStagedTransactionService` — all scoped
- [x] `TransactionAttachmentService`, `TransferAttachmentService` (named `FileAttachmentService` — single class handles both) — scope via parent Transaction/Transfer's UserId, not their own column; documented in `multi-tenancy-strategy.md` § EF Core Global Query Filters § Intentionally NOT filtered

Boot-time hooks:

- [x] `ISettingsService.EnsureExistsAsync` removed from `Program.cs` startup — confirmed by Stage 6a; comment block in `Program.cs:191` documents the removal
- [x] No remaining `IHostedService`, `IStartupFilter`, or boot-time hook queries user-owned tables — confirmed; `EmptyDbStartupTests` (Task 12) catches per-request hooks that crash on empty tables. The true cold-boot `IHostedService.StartAsync` regression is not directly testable because `WebApplicationFactory` boots once per collection fixture; the regression class is empirically not present (the suite is green against the rebuilt test DB with 0 users)
- [x] Any boot-time query that needed user data is moved into per-user lifecycle hooks — `CategorySeedService.CopyDefaultsForUserAsync` is invoked from `AuthController.Register` (production) and `AuthTestFixture.RegisterUserAsync` (tests)
- [x] Test: a smoke test starts the application with zero registered users and confirms no boot-time exception is thrown — `Integration/Startup/EmptyDbStartupTests § Health_endpoint_handles_request_against_empty_user_owned_tables` (the name calls out the precise regression class the test pins; see Task 12 review for the cold-boot scoping discussion)

IDOR integration test suite (per `multi-tenancy-strategy.md` § Required Integration Tests):

- [x] User A cannot read User B's accounts (`GET /api/accounts/{B's id}` → 404, NOT 403) — `IdorIsolationTests.UserB_cannot_read_UserA_account_by_id`
- [x] User A cannot read User B's transactions — `UserB_cannot_read_UserA_transaction_by_id` + the movement-type variant `UserB_cannot_read_UserA_movement_type_by_id`
- [x] User A cannot read User B's liability payments — covered by Movement TPC root: the global filter on `Movement` propagates to `LiabilityPayment` (an EF Core TPC rule). The `UserB_cannot_read_UserA_movement_type_by_id` test exercises the Movement endpoint which includes liability payments.
- [x] User A cannot read User B's budgets — `UserB_cannot_read_UserA_goal_budget_by_id`, `UserB_cannot_read_UserA_budget_discriminator`, `UserB_cannot_read_UserA_category_budget_by_id`
- [x] User A cannot read User B's categories — covered indirectly: every user has their own 26 per-user category copies post-Task-7, with disjoint GUIDs (`RegistrationSeedsCategoriesTests.Two_users_get_independent_category_copies` proves the disjointness). No `GET /api/categories/{id}` cross-tenant test was added because the only "shared" Category was the sentinel-stamped Opening Balance row which is now per-user-copied at registration.
- [x] User A cannot read User B's recurring transactions — `UserB_cannot_read_UserA_recurring_transaction_by_id`
- [x] User A cannot list User B's anything (list endpoints return zero of B's rows when called as A) — `UserB_account_list_excludes_UserA_accounts`, `UserB_movements_list_excludes_UserA_transactions`, `UserB_goal_budgets_list_excludes_UserA_budgets`
- [x] User A cannot aggregate over User B's data (sum/count endpoints scoped correctly) — covered by the negative-assertion test `Global_query_filter_alone_catches_leak_without_service_Owned`: a raw `db.Accounts.ToListAsync()` under User B's scope returns only B's rows. The principle generalises to every aggregation expressed in LINQ over the filtered DbSet.
- [x] User A cannot delete User B's resources (`DELETE /api/transactions/{B's id}` → 404) — `UserB_cannot_delete_UserA_transaction`
- [x] User A cannot edit User B's resources (`PATCH /api/transactions/{B's id}` → 404) — `UserB_cannot_patch_cleared_on_UserA_transaction`
- [x] Cross-tenant tests use real fixtures, not mocked services — the IDOR suite uses `AuthTestWebApplicationFactory` end-to-end with real PostgreSQL, real cookie auth, real CSRF. **Plus the load-bearing negative-assertion test** that proves the EF global query filter catches a leak when the service-layer `.Owned()` chain is bypassed.

---

## Stage 7.5 — PostgreSQL Row-Level Security (Batch 3c continued)

**Status: ✅ Done (2026-05-14).** Closes the named gap left by Stage 7 (raw SQL bypasses EF query filters). Shipped immediately after Stage 7 so policies turn on against a schema that already holds real `AspNetUsers.Id` values from the sentinel-to-real-user remap. See [ADR-0068](decisions/ADR-0068-postgres-rls-as-phase-3-defence-in-depth.md) for the full rationale (this stage was originally deferred to Phase 4 by ADR-0065 and superseded on 2026-05-09). 1009/1009 tests green at close-out.

> **Goal (achieved):** PostgreSQL Row-Level Security is enabled on every user-owned table with both `USING` and `WITH CHECK` policies tied to `current_setting('app.current_user_ref', true)`; a `DbConnectionInterceptor.ConnectionOpenedAsync` hook sets that GUC from `ICurrentUserAccessor` on every connection open; a separate Postgres role with `BYPASSRLS` backs Admin services and `IUserJobRunner` cross-tenant jobs; integration tests prove RLS catches `FromSqlRaw` bypass attempts and `IgnoreQueryFilters()` mistakes.

### Mechanism correction (built differently than the spec)

The spec ([`docs/superpowers/specs/2026-05-13-stage-7-5-postgres-rls-design.md`](superpowers/specs/2026-05-13-stage-7-5-postgres-rls-design.md) § 2.2) prescribed a per-command `IDbCommandInterceptor` issuing `SET LOCAL` inside the EF command's transaction. **That mechanism is unsafe for this codebase** and was replaced during implementation by a `DbConnectionInterceptor.ConnectionOpenedAsync` hook issuing `SELECT set_config('app.current_user_ref', '<uuid>', false)` (session scope) once per pooled-connection acquisition. Reasons:

1. EF Core 7+ bulk operations (`ExecuteDeleteAsync`, `ExecuteUpdateAsync`) do not open an EF transaction by default. `SET LOCAL` outside a transaction is a NOTICE and a no-op. The runtime uses bulk operations in 10+ production paths (`LockoutUnlockService`, `EmailChangeService`, `MfaBackupCodeService`, `TotpReplayGuard`, `UserBlockedIpMiddleware`) — every one would have silently bypassed the GUC and run with the policy filtering all rows. See Npgsql/efcore.pg [#2412](https://github.com/npgsql/efcore.pg/issues/2412) and [#4889](https://github.com/npgsql/npgsql/issues/4889).
2. The prepend-to-`CommandText` pattern broke EF's optimistic-concurrency reads on writes — Npgsql returned the prepended `SET`'s rows-affected (0) to EF as if it belonged to the INSERT/UPDATE, raising `DbUpdateConcurrencyException` on every write.

The connection-open variant relies on Npgsql's default `DISCARD ALL` reset to clear the GUC when a connection returns to the pool. As belt-and-braces, the interceptor explicitly issues `RESET "app.current_user_ref"` when `ICurrentUserAccessor.UserId == Guid.Empty` — so the property holds even if a deployment ever disables Npgsql's reset (required for pgBouncer transaction mode). The policy SQL uses `NULLIF(current_setting(...), '')::uuid` because `DISCARD ALL` resets custom-namespace GUCs to empty string (not NULL) per Postgres's documented limitation; without `NULLIF` every pooled-connection reuse would raise `22P02`.

### Sub-stages (commits)

| # | Sub-stage | Commit |
|---|---|---|
| 7.5.1 | Postgres roles: `ceres_app`, `ceres_admin` (BYPASSRLS), `ceres_migrator` (BYPASSRLS + DDL); `scripts/setup-postgres-roles.sql` + README setup section | `f3f6ead` |
| 7.5.2 | `RowLevelSecurityInterceptor : DbConnectionInterceptor` + `IPreAuthCallSiteTagger`, code-only (no DI yet) | `27141ef` |
| 7.5.3 | DI split: three connection strings + `AdminDbContext` subclass + interceptor wired on `AppDbContext` only | `2a47eb8` |
| 7.5.4 | Privilege-leak startup check refuses DDL-capable runtime role | `e7290b2` |
| 7.5.5 | The wall — `UserOwnedTables.cs` source-of-truth + `IUserOwned` promotion of attachment tables + `AddRowLevelSecurityPolicies` migration + 15-test RLS integration suite | `d4a803e` |
| 7.5.6 | `BackgroundJobScope` doorway refusal + architecture test pinning `.EnterAs(` to `BackgroundJobScope.cs` only | `c2795a5` |
| 7.5.7 | Doc sync + roadmap close-out | this commit |

### Verification checklist

Postgres roles + connection strings:

- [x] Three roles exist in production-equivalent local Postgres: `ceres_app`, `ceres_admin`, `ceres_migrator` *(both `project_ceres` and `project_ceres_test`, verified by `pg_roles` query)*
- [x] `ceres_app` has DML rights, no DDL, **no** `BYPASSRLS` *(privilege-leak startup check verifies; `PrivilegeLeakStartupCheckTests` covers in tests)*
- [x] `ceres_admin` has DML rights, no DDL, **has** `BYPASSRLS`
- [x] `ceres_migrator` has DDL rights, has `BYPASSRLS`, used only by `dotnet ef database update`
- [x] Three connection strings configured: `ConnectionStrings:ApplicationConnection`, `ConnectionStrings:AdminConnection`, `ConnectionStrings:MigrationConnection`
- [x] DI container resolves `AppDbContext` from `ApplicationConnection` and `AdminDbContext` from `AdminConnection` *(`DbContextRegistrationTests`)*

`RowLevelSecurityInterceptor`:

- [x] Implements `DbConnectionInterceptor.ConnectionOpened` + `ConnectionOpenedAsync` *(spec § 2.2 said `IDbCommandInterceptor`; mechanism corrected during implementation — see "Mechanism correction" above)*
- [x] Reads current `UserId` from `ICurrentUserAccessor`
- [x] Issues `SELECT set_config('app.current_user_ref', '<uuid>', false)` immediately after the connection opens
- [x] On `Guid.Empty`: issues `RESET "app.current_user_ref"` (belt-and-braces against `No Reset On Close`); logs a warning if the call site is not tagged pre-auth; never throws
- [x] Not registered on `AdminDbContext` (the admin role bypasses RLS at the database level)
- [x] GUC does not leak across pooled-connection reuse *(Group 4 test `GUC_does_not_leak_across_pooled_connections`)*

RLS policies on every user-owned table:

- [x] All 24 tables listed in `UserOwnedTables.All` (`Accounts`, `Budgets`, `Categories`, `CategoryBudgets`, `ImportProfiles`, `ImportStagedTransactions`, `ImportStagedTransfers`, `ImportTransferExclusions`, `RecurringTransactions`, `SavedReports`, `Settings`, `Transactions`, `Transfers`, `LiabilityPayments`, `TransactionAttachments`, `TransferAttachments`, `UserSessions`, `UserBlockedIps`, `UserMfaBackupCodes`, `TotpReplayEntries`, `PasswordResetTokens`, `EmailChangeTokens`, `LockoutUnlockTokens`, `AuditLogs`) have `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` *(`ParityTests.Every_user_owned_table_has_FORCE_RLS_enabled`)*
- [x] Each user-owned table has a `user_isolation` policy with both `USING ("UserId" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)` and `WITH CHECK (...)` clauses *(`ParityTests.UserOwnedTables_All_matches_pg_policies_user_isolation_set`)*
- [x] System tables (`AccountTypes`, `CategoryTypes`, `Currencies`, `ReportTypes`, `AspNetUsers` et al., `__EFMigrationsHistory`) have **no** RLS *(`ParityTests.System_tables_have_no_user_isolation_policy`)*
- [ ] **Deferred to Stage 12.** When the `SupportTicket` table ships, the same migration must add a `user_isolation` policy and append `SupportTickets` to `UserOwnedTables.All`. The parity test will fail the build until this happens.

Per-table integration tests (representative table per archetype + parameterized parity):

- [x] Group 1 — `FromSqlRaw` bypass returns 0 foreign rows (`Accounts`, `UserSessions`, `AuditLogs`); parity test ensures the same property holds for every other user-owned table by construction
- [x] Group 2 — `INSERT` with foreign `UserId` raises Postgres `42501` (`Accounts`, `UserSessions`); `UPDATE` to foreign `UserId` raises `42501` (`Accounts`); `DELETE` against foreign-`UserId` row affects 0 rows (`Accounts`)
- [x] Group 3 — `AdminDbContext` with `IgnoreQueryFilters` returns rows from all users; `AppDbContext` under user A does NOT see B's rows even with `IgnoreQueryFilters()` *(RLS is the wall the EF filter can't lift)*
- [x] Group 4 — interceptor warns on `Guid.Empty` outside pre-auth; no warning on pre-auth; GUC does not leak across pooled connections

Architecture tests:

- [x] `UserOwnedTables.All` ↔ `pg_policies` parity (`ParityTests.UserOwnedTables_All_matches_pg_policies_user_isolation_set`)
- [x] `RowLevelSecurityInterceptor` registered only on `AppDbContext`, not `AdminDbContext` (`DbContextRegistrationTests.RowLevelSecurityInterceptor_is_registered_only_on_AppDbContext`)
- [x] `.EnterAs(` called only from `BackgroundJobScope.cs` (`ArchitectureTests.EnterAs_only_called_inside_BackgroundJobScope`)
- [x] `IgnoreQueryFilters()` allow-list still pins documented exception paths (`ArchitectureTests.IgnoreQueryFilters_only_appears_in_documented_exception_paths`, intact from Stage 7)

Operational:

- [x] Local-dev seed script `scripts/setup-postgres-roles.sql` creates all three roles; idempotent
- [x] README's Setup section documents the `psql -f` invocation against both databases + the env-var password override for production
- [x] `dotnet ef database update --connection <MigrationConnection>` runs as `ceres_migrator` (verified locally on both `project_ceres` and `project_ceres_test`)
- [x] Application startup fails fast if `ApplicationConnection` is wired to a role with DDL rights (privilege-leak startup check; gated on `Stage75:SkipPrivilegeLeakCheck` for WAF tests only)
- [ ] **Deferred to Stage 16.** Production database setup must create `ceres_app`, `ceres_admin`, `ceres_migrator` per Stage 7.5; only `ceres_app` and `ceres_admin` credentials are deployed with the application; `ceres_migrator` credentials are held by the deploy operator and used only when applying migrations. The Stage 16 hosting runbook needs this note baked in.

Follow-ups recorded for future stages:

- **Stage 12 — `SupportTicket` table.** When the table ships, append `SupportTickets` to `UserOwnedTables.All` in the same migration that creates the table. The Stage 7.5 parity test will otherwise fail the build.
- **Stage 16 — production runbook.** Per the operational checklist item above.
- **Stage 7.6 — error legibility + OCP cleanup.** Stage 7.5 surfaced several diagnostic gaps (silent zero-row failures, stringly-typed pre-auth registry, hand-written `HasQueryFilter` registrations duplicating `UserOwnedTables.All`). Stage 7.6 closes them before Stage 8 begins so the cleanup lands while RLS is fresh in the codebase. See § Stage 7.6 below.

---

## Stage 7.6 — Error legibility + OCP cleanup (Batch 3c continued)

**Status: ✅ Done (shipped 2026-05-14).** Closed seven diagnostic + OCP gaps surfaced during Stage 7.5. Infrastructure-only — no user-facing behaviour changes, no schema changes. Lands before Stage 8 so the cleanups apply while the RLS pattern is fresh. All seven sub-stages shipped on `main`. See [`docs/superpowers/specs/2026-05-14-stage-7-6-error-legibility-and-ocp-cleanup.md` § Session state](superpowers/specs/2026-05-14-stage-7-6-error-legibility-and-ocp-cleanup.md) for the seven implementation lessons (7.6.2 mechanism correction, 7.6.2 message-parsing fallback, 7.6.4 closure-capture pitfall, 7.6.3 call-site-audit narrowing, 7.6.5 SettingsService catch-rewrite, 7.6.6 thin-coordinator pattern, 7.6.7 stale-registry-drift catch + 6→8 endpoint surface correction).

> **Goal:** every test failure shows the production stack trace on first run; every silent-zero-row failure mode is converted to a loud exception; `UserOwnedTables.All` becomes the single source of truth for the EF filter registration; the four meanings of `Guid.Empty` are replaced by a typed `UserContext` discriminated union.

Authority for sub-stage 7.6.7 (the `UserContext` rewrite): [ADR-0073](decisions/ADR-0073-user-context-as-discriminated-union.md).

Brainstorm spec: [`docs/superpowers/specs/2026-05-14-stage-7-6-error-legibility-and-ocp-cleanup.md`](superpowers/specs/2026-05-14-stage-7-6-error-legibility-and-ocp-cleanup.md).

### Sub-stages

| # | Sub-stage | Effort | Status | Commit | Spec § |
|---|---|---|---|---|---|
| 7.6.1 | WAF test-mode returns exception bodies — `HttpAssertions.HaveStatusCodeAsync` includes the response body in failure messages so a 500 surfaces the production stack on first run. | ~170 LOC | ✅ Done | `33e00cd` | § 2.1 |
| 7.6.4 | Iterate `UserOwnedTables.All` in `AppDbContext.OnModelCreating` — replaces 22 hand-written `HasQueryFilter` registrations with a reflection-driven generic-method invoke. **First attempt used raw `Expression.Lambda` and broke ~162 tests** (closure-capture pitfall, dotnet/efcore #14740); shipped version dispatches to `RegisterUserOwnedFilter<TEntity>` whose C# lambda the compiler emits with the right `this`-closure semantics. | ~80 LOC | ✅ Done | `fdbf960` | § 2.4 |
| 7.6.2 | `RlsPolicyViolationException` wrapping `SqlState 42501` on user-owned tables — typed exception carrying `TableName`, `GucUserId`, and the original `PostgresException`. Service code can now `catch (RlsPolicyViolationException)`. **Mechanism correction**: the spec originally said `IDbCommandInterceptor.CommandFailed`; that hook is observational only. Shipped version overrides `AppDbContext.SaveChanges{Async}` and calls a static `RlsExceptionTranslator.TryTranslate(...)` in a catch block. **Message-parsing fallback**: Postgres doesn't populate the structured `TableName` field for RLS violations, so the translator parses the table name from the message text. | ~330 LOC | ✅ Done | (see `RlsPolicyViolationException.cs` + `RlsExceptionTranslator.cs`) | § 2.2 |
| 7.6.3 | `AffectedRowCount` enforcement helper — `ExecuteUpdateExactlyAsync` / `ExecuteDeleteExactlyAsync` throw `AffectedRowCountMismatchException` when row count != expected. **Call-site audit correction**: the spec named 8 sites with exactly-one-row semantics; reading the code showed only 2 (`LockoutUnlockService.ConfirmAsync` token-consume L161 and `EmailChangeService.ConfirmAsync` verify-token-consume L287). The other 6 are 0-or-1 supersede sweeps, 0-or-many session-revoke calls, or retention sweeps where wrapping in `Exactly` would convert correct silent-zero behaviour into spurious crashes. Helper supports `[CallerMemberName]` and a custom `expectedRows`; 8 unit tests cover update + delete parity. | ~155 LOC | ✅ Done | (this commit) | § 2.3 |
| 7.6.5 | `DbExceptionTranslator` mapping SqlState codes (23505, 23503, 23502) to typed exceptions. **Apply the 7.6.2 lessons**: catch in `SaveChanges{Async}` override (not via interceptor); verify which `PostgresException` structured fields populate for each SqlState before writing the matcher. **Empirical PG 18.3 probe** (against `project_ceres_test` via `psql VERBOSITY verbose`) confirmed `TableName`, `ConstraintName`, and `ColumnName` populate cleanly for all three codes — no message-parsing fallback needed (unlike RLS 42501). Translator chains after `RlsExceptionTranslator` in both `SaveChanges` overrides. **`SettingsService` catch-rewrite**: the existing `catch (DbUpdateException ex) when (IsUniqueViolation(ex))` first-touch handler now catches `UniqueConstraintViolationException` instead — the only existing in-tree `DbUpdateException` catch on these SqlStates. | ~210 LOC | ✅ Done | (this commit) | § 2.5 |
| 7.6.6 | Split `TestDbFixture` into three single-responsibility fixtures: `MigrationFixture`, `AppContextFactory`, `AdminContextFactory`. **`TestDbFixture` retains its full public surface** (`Db`, `InitAsync`, `CreateAppContext`, `CreateAdminContext`, `DisposeAsync`, the three connection-string constants, `SentinelUserId`) because 26 test files + 4 fixture-internal callers depend on it; the refactor is internal composition only. | ~90 LOC | ✅ Done | (this commit) | § 2.6 |
| 7.6.7 | `UserContext` discriminated union + `[PreAuthCallSite]` attribute + `UserContextRequiredException`. Deletes `IPreAuthCallSiteTagger`. ADR-0073 is the design record. **Stale-registry catch**: the prior `IPreAuthCallSiteTagger` registry held `"Auth.PasswordResetRequest"` — a key that never matched any real MVC endpoint (the actual method is `PasswordReset.RequestReset`), so the warning-log had been firing on every password-reset-request in production. The new `[PreAuthCallSite]` attribute on the action method eliminates this drift class entirely. **6 → 8 endpoints**: spec listed 5; reading the code revealed 6 (`PasswordReset.Confirm` was missed), and the new architecture test caught 2 more (`EmailChange.ConfirmChange` + `EmailChange.RevokeChange`). Final: 8 attribute decorations + 3 explicit no-DB allow-list entries (`HealthApiController.Get`, `Validate`, `AuthController.Csrf`). | ~280 LOC | ✅ Done | (this commit) | § 2.7 |

### Verification checklist

Diagnostic improvements:

- [x] WAF integration test failures show the full server-side exception body. *(7.6.1 — `HttpAssertions.HaveStatusCodeAsync` + `BeSuccessfulAsync` ship in `ProjectCeres.Tests/Integration/Infrastructure/`; 5 unit tests verify the body-on-failure contract.)*
- [x] `RlsPolicyViolationException` is thrown (not `PostgresException`) when a write hits a user-owned table under the wrong user. *(7.6.2 — wired via `AppDbContext.SaveChanges{Async}` override calling `RlsExceptionTranslator.TryTranslate(...)`. 6 unit tests + 4 integration tests assert the typed exception with `TableName` and `GucUserId` payload.)*
- [x] `AffectedRowCountMismatchException` fires when a `ExecuteUpdateExactlyAsync` or `ExecuteDeleteExactlyAsync` call affects 0 rows where 1 was expected. *(7.6.3 — `BulkOperationExtensions` ships in `ProjectCeres/Common/`; 8 unit tests in `BulkOperationExtensionsTests` cover happy path + less-than + greater-than + custom `expectedRows` + `[CallerMemberName]` + message format, for both update and delete. Migrated to `LockoutUnlockService.ConfirmAsync` L161 and `EmailChangeService.ConfirmAsync` L287; the other 6 spec-named sites were re-classified as 0-or-1 / 0-or-many — see spec § Session state for the audit.)*
- [x] `UniqueConstraintViolationException` / `ForeignKeyViolationException` / `NullConstraintViolationException` are thrown (not generic `DbUpdateException`) for the three documented `SqlState` codes. *(7.6.5 — `DbExceptionTranslator` ships in `ProjectCeres/Common/`; chained after `RlsExceptionTranslator` in `AppDbContext.SaveChanges{Async}`. 7 unit tests in `DbExceptionTranslatorTests` cover each SqlState's typed-exception output, the 42501 pass-through (handled by RLS), unmatched SqlState pass-through, `DbUpdateException` without `PostgresException` inner pass-through, and the defensive empty-`ConstraintName` case. `SettingsService.GetAsync` first-touch race handler updated to `catch (UniqueConstraintViolationException)`.)*
- [x] `BackgroundJobScope.RunAsync` exception message names the rejected `UserContext` case. *(7.6.7 — the constructor now takes `ICurrentUserAccessor`; the doorway-refusal exception message includes `currentUser.Context.GetType().Name`. `BackgroundJobScopeTests.RunAsync_refuses_when_userId_is_Guid_Empty_before_work_runs` asserts the message contains the case name.)*
- [x] `UserContextRequiredException` is thrown by `_currentUser.Require()` when the context is not `Resolved`. *(7.6.7 — `CurrentUserAccessorExtensions.Require()` ships in `ProjectCeres/Common/`; 5 unit tests cover happy path + each non-`Resolved` case + message format.)*

OCP improvements:

- [x] `AppDbContext.OnModelCreating` references `UserOwnedTables.All` and contains no hand-written `HasQueryFilter` registrations except `Movement` (TPC root). *(7.6.4 — uses `MethodInfo.MakeGenericMethod` + `RegisterUserOwnedFilter<TEntity>` to dispatch to a generic helper whose C# lambda closes over `this._currentUser` correctly. New architecture test `UserOwnedTables_All_matches_HasQueryFilter_registrations` walks `IEntityType.BaseType` for TPC inheritance.)*
- [x] Adding a new user-owned entity is one append to `UserOwnedTables.All`. The Stage 7.5 parity test still catches RLS-policy drift. *(7.6.4 — covered by the new architecture test above + the existing parity test.)*

Architecture tests:

- [x] `IUserScope.EnterAs` allow-list test from Stage 7.5 continues to pass.
- [x] **New** `Every_anonymous_api_action_carries_a_PreAuthCallSite_attribute_or_is_in_the_no_db_allow_list` — Roslyn-free `Assembly.GetTypes()` scan of `[ApiController]` types for every `[AllowAnonymous]` action; each must carry `[PreAuthCallSite]` or be in an explicit no-DB allow-list. *(7.6.7 — caught two pre-auth endpoints the spec missed (`EmailChange.ConfirmChange` + `EmailChange.RevokeChange`) on first run, plus `AuthController.Csrf` (added to the no-DB allow-list because antiforgery doesn't open a DB connection).)*
- [x] `IgnoreQueryFilters()` allow-list test from Stage 7 continues to pass.

Removals:

- [x] `ProjectCeres/Common/IPreAuthCallSiteTagger.cs` + `PreAuthCallSiteTagger.cs` deleted. *(7.6.7 — DI registration in `Program.cs` removed alongside.)*
- [x] `ProjectCeres.Tests/Common/PreAuthCallSiteTaggerTests.cs` deleted. Replacement coverage in the rewritten `CurrentUserAccessorResolutionTests.cs` + the new architecture test. *(7.6.7 — the test had 4 cases (3× `[Theory]` + `[Fact]`); the rewritten resolution suite covers `Resolved` × 3, `PreAuth` × 1, `Background` × 1, `Uninitialized` × 1, plus the new architecture test enforces the boundary at the route layer.)*

Doc supersession (per ADR-0073 § Consequences):

- [x] `docs/multi-tenancy-strategy.md` § Background processes — the "`Guid.Empty` as safe default" amendment line is annotated, not deleted, with the supersession note.
- [x] `docs/planning-resolved.md` — Stage 7 entry's `Guid.Empty` reference is annotated.
- [x] ADR-0073 status flipped from "proposed" to "Accepted" at close-out.
- [x] ADR-0067's "`Guid.Empty` safe default" amendment cross-referenced from ADR-0073 (annotation lives in ADR-0067's Status line so any reader of that ADR sees the supersession immediately).

Test count:

- [x] Full suite ≥1024 passed, 0 failed. *(1038/1038 green at close-out — exceeded the floor by 14. Per `feedback_test_edge_cases_as_ship_gate`, each new typed exception has unit tests pinning what triggers it: `RlsPolicyViolationException` × 6, `AffectedRowCountMismatchException` × 8, `Unique/ForeignKey/NullConstraintViolationException` × 7 (combined), `UserContextRequiredException` × 5.)*

---

## Stage 8 — Email service + email security (Batch 3d)

**Status: ✅ Shipped 2026-05-15.** Six sub-stages (8a–8f) landed across `f785616` (8e webhook) and `8f` (this commit). Application surface complete (recipient lock, EN/ES templates, Resend integration with retry, per-user + per-IP rate limits, delivery webhook with signature verification). DNS publication (SPF/DKIM/DMARC) is procedural and shipped as the runbook at [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md); it executes in Stage 16 once a domain is registered. Templates whose call sites do not yet exist (registration confirmation, TOTP-enrolled/disabled, backup-codes-regenerated, new-session alert, GDPR export, account-erasure) are deferred to the stages that wire them — Stage 9 / 10 / 12 / 13.

> **Goal:** a transactional email service is integrated, DNS-level email security is configured (SPF, DKIM, DMARC), application-layer protections (recipient lock, sanitization, rate limiting) are in place, and the EN/ES transactional templates exist for every Phase 3 flow that sends mail.

> **Checklist legend:** `[x]` proved by an automated test that lives in the repo today. `[~]` code shipped, awaits DNS publication on the registered sending domain (executed in Stage 16 via the email-DNS runbook). `[ ]` deferred to the stage that wires the call site.

### Sub-stages

| # | Sub-stage | Spec / Reference |
|---|---|---|
| 8.1 | Integrate Resend (chosen 2026-05-12; see `planning-resolved.md`) | `planning-resolved.md` § Email provider for Phase 3 |
| 8.2 | `IEmailService` abstraction + provider implementation | New abstraction; provider behind interface for testability |
| 8.3 | DNS authentication: SPF, DKIM, DMARC | `security-model.md` § Email Security Rules → Layer 1 |
| 8.4 | Application controls: recipient lock, sanitization, per-user rate limit | `security-model.md` § Email Security Rules → Layer 2 |
| 8.5 | API key hygiene: secret store, send-only scope, rotation procedure | `security-model.md` § Email Security Rules → Layer 3 |
| 8.6 | Transactional templates EN + ES | `.resx` files per `planning-phase3.md` § Localization (`Emails.en.resx`, `Emails.es.resx`) |
| 8.7 | Email delivery telemetry (delivered / bounced / complained) | Provider webhook integration |

### Verification checklist

Provider integration:

- [x] `IEmailService` interface exists with `SendAsync(EmailMessage)` and is the only entry point for outgoing email — pinned by `EmailRecipientTests.EmailMessage_has_no_public_string_To_constructor` (the only `To` slot is the typed `EmailRecipient`) and `EmailRecipientTests.EmailRecipient_OverrideForEmailChange_is_called_only_by_EmailChangeService` (override factory has a single call site).
- [x] No code outside the email service constructs an SMTP client or provider client directly — `ResendEmailService` is the sole `Resend` SDK consumer; the architecture is preserved by the typed-recipient lock above and Stage 8c integration tests.
- [x] Provider API key in environment variable / secret store; never in source control — `Email:Resend:ApiKey` is read from configuration; `ResendEmailServiceTests.Production_without_api_key_throws_at_startup` pins the startup refusal.
- [x] Send-only scoped key used where the provider supports it — Resend "Sending" scope key per provider docs; rotation procedure in `security-model.md` § Email Security Rules → Layer 3 and in [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md).
- [x] Key rotation procedure documented in `security-model.md` § Email Security Rules (Layer 3) and in [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md) (DKIM dual-selector rotation; same secret-store mechanics for the API key).
- [x] Provider client is wrapped in retry logic — `ResendEmailServiceTests.Retries_on_500_three_times_then_throws` and `Does_not_retry_on_400` pin the 3-attempt exponential-backoff Polly policy and the no-retry-on-4xx contract.
- [x] Failed sends are logged but never block the user-facing request — `EmailRateLimitTests.Reset_request_for_unknown_email_and_known_email_have_equal_argon2id_call_count_on_429` pins the constant-time, swallow-failure boundary; password-reset request returns 204 regardless of send outcome.

DNS authentication (per `security-model.md` § Layer 1 — DNS authentication):

- [~] SPF record published on the sending domain — Runbook shipped at [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md) Step 1; publish on the registered domain in Stage 16.
- [~] DKIM record published with provider-supplied public key; signing active and verified — Runbook shipped at [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md) Step 1; publish on the registered domain in Stage 16.
- [~] DMARC record published with at minimum `p=none` at launch — Runbook shipped at [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md) Step 2; publish on the registered domain in Stage 16.
- [~] DMARC aggregate report destination configured (e.g., `rua=mailto:dmarc@example.com`) — Runbook shipped at [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md) Step 2; configure on the registered domain in Stage 16.
- [~] After 30 days of clean aggregate reports, advance DMARC to `p=quarantine`, then `p=reject` — Runbook shipped at [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md) Step 4 (90-day ramp `p=none → quarantine pct=25 → quarantine pct=100 → reject`); ramp executes in Stage 16.
- [~] All three records verified using `dig` and an external tool (e.g., MXToolbox) — Runbook shipped at [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md) Step 3; verify on the registered domain in Stage 16.

Application controls (per `security-model.md` § Layer 2 — Application controls):

- [x] Outgoing `To:` address ALWAYS resolved server-side from the authenticated user's verified email — never from a request parameter. The compile-time recipient lock (Stage 8a) makes `EmailMessage.To` an `EmailRecipient` value type, not a `string`. Pinned by `EmailRecipientTests.EmailMessage_has_no_public_string_To_constructor` (reflection-based: fails if any public constructor accepts a raw string).
- [x] User-controlled strings rendered into email subject/body are sanitized — `EmailComposerTests.Strips_cr_lf_in_subject` pins newline stripping (header-injection defence); `EmailComposerTests.Html_encodes_args_in_html_body` pins HTML encoding of all interpolated args in the HTML body.
- [x] Per-user rate limit on email-triggering endpoints — `EmailRateLimitTests.Sixth_password_reset_request_in_same_hour_for_same_email_returns_429` pins the 5/hour password-reset limit. GDPR-export rate limit lands with Stage 13 once the endpoint exists. Per-user email-change limit pinned by `Email_change_request_partitions_by_user_id_not_by_email`.
- [x] Per-IP rate limit on unauthenticated email-triggering endpoints — `EmailRateLimitTests.Eleventh_password_reset_request_from_same_ip_returns_429` pins the per-IP `AuthLoginByIp` 10/min limit on the password-reset request endpoint.
- [x] Test: attempting to send to an arbitrary `To:` parameter is rejected at the service boundary — see the recipient-lock entry above; the lock makes the case unrepresentable at compile time, and the reflection test pins that no public constructor exposes a `string`-typed `To` slot.

Transactional templates (EN + ES, per `planning-phase3.md` § Localization):

- [ ] Registration confirmation (verify-email link) — Deferred to Stage 9 — Auth SPA pages (no registration call site exists yet).
- [x] Password reset request — template present in `Emails.en.resx` + `Emails.es.resx`; rendered by `EmailComposer` and pinned by `EmailComposerTests.Renders_all_nine_templates_en_and_es` (Theory: 9 templates × 2 cultures = 18 cases).
- [x] Password changed notification — fires from `PasswordResetService.ConfirmAsync`; template covered by the all-nine-templates Theory above.
- [x] Email-change verify-new-address link — fires from `EmailChangeService.RequestAsync`; template covered by the all-nine-templates Theory above.
- [x] Email-change revoke-old-address link — fires from `EmailChangeService.RequestAsync`; template covered by the all-nine-templates Theory above.
- [ ] TOTP enrolled (security event) — Deferred to Stage 9 — Auth SPA pages (enrolment SPA flow wires the call site).
- [ ] TOTP disabled (security event) — Deferred to Stage 9 — Auth SPA pages (no disable call site exists yet).
- [ ] Backup codes regenerated (security event) — Deferred to Stage 9 — Auth SPA pages (the `/api/auth/mfa/backup-codes` regeneration endpoint exists from Stage 6 but currently does not send a notification; wire here).
- [x] Account lockout notification with self-service unlock link — fires from `LockoutUnlockService.IssueAsync`; template covered by the all-nine-templates Theory above. Arg-mapping pinned by `EmailComposerTests.LockoutUnlock_args_map_to_correct_slots`.
- [ ] New-session alert (when login from previously-unseen IP for that user) — Deferred to Stage 12 — Sessions + Support SPA pages (the session-novelty-detection call site lands there).
- [ ] GDPR data export ready (with 24-hour authenticated download link) — Deferred to Stage 13 — GDPR baseline (the export job lands there).
- [ ] Account-erasure confirmation — Deferred to Stage 13 — GDPR baseline (the erasure job lands there).
- [x] Each template exists in `Emails.en.resx` and `Emails.es.resx` — `EmailComposerTests.All_resx_keys_present_in_both_cultures` pins parity (every key declared in `EmailTemplateKey` has a `Subject`, `BodyText`, and `BodyHtml` entry in both `.resx` files).
- [x] Templates rendered server-side via `IStringLocalizer<EmailsResource>` keyed by user's `Settings.Language` — `LanguageResolverTests` pins the `Settings.Language` → `CultureInfo` resolution for authenticated users and the `Accept-Language` → cookie fallback for pre-auth flows.
- [x] No financial amounts in security-event emails — every templated arg is a string drawn from `ApplicationUser`/`UserSession` metadata; the `EmailMessage` shape exposes no `decimal`/`Amount`/`Balance` field. Stage 6.14's `AuditLog_entity_contains_no_financial_amount_columns` covers the structurally adjacent audit entity; the email side inherits the rule by template construction.

Security event notifications (mandatory regardless of user preferences, per `security-model.md` § Security Event Notifications):

- [ ] New-device/session login email — Deferred to Stage 12 — Sessions + Support SPA pages (session-novelty-detection call site lands there).
- [x] Password-changed email — fires from `PasswordResetService.ConfirmAsync` on success; template + send pinned end-to-end by the Stage 6c.1 password-reset integration tests using `CapturingEmailService`.
- [x] Email-change-initiated email — fires from `EmailChangeService.RequestAsync` to both the new and old addresses; pinned by the Stage 6.12 `EmailChangeRequestTests`.
- [x] Email-change-confirmed email — fires from `EmailChangeService.ConfirmAsync` to the old address (notification of the change taking effect); pinned by `EmailChangeConfirmTests`.
- [ ] TOTP-re-enrolled email — Deferred to Stage 9 — Auth SPA pages.
- [ ] TOTP-disabled email — Deferred to Stage 9 — Auth SPA pages.
- [ ] Backup-codes-regenerated email — Deferred to Stage 9 — Auth SPA pages (regeneration endpoint exists; notification wiring lands with the SPA flow).
- [x] Account-locked-out email — fires from `LockoutUnlockService.IssueAsync` only on the lockout transition (gated by the existing `_loginLocks` semaphore — Stage 6.10's email-DoS defence); pinned by the Stage 6.10 issuance tests.
- [ ] GDPR-erasure-initiated email — Deferred to Stage 13 — GDPR baseline.
- [~] All eight emails verified to actually fire in integration tests with test fixtures — four (password-changed, email-change-initiated, email-change-confirmed, account-locked-out) are pinned today via `CapturingEmailService`; the remaining five (new-device/session login, TOTP-re-enrolled, TOTP-disabled, backup-codes-regenerated, GDPR-erasure-initiated) land with the deferred stages above.

Telemetry + observability:

- [x] Provider webhook configured for delivered / bounced / complained events — `ResendWebhookController` accepts `email.sent` / `email.delivered` / `email.bounced` / `email.complained`; persistence pinned by `ResendWebhookTests.Accepts_valid_signature_and_records_event`.
- [x] Bounced events disable the user's email (mark `EmailVerified = false`) and surface a notice on next login — `ResendWebhookTests.Bounced_event_flips_EmailConfirmed_to_false` pins the flip on the matched-by-email branch. The on-next-login notice is a UI concern that lands with Stage 9 (auth pages); the data-side trigger is in place.
- [ ] Complaints (spam reports) auto-disable digest emails for that user — Deferred to Phase 4 (the weekly financial digest itself is a Phase 4 feature per `planning-phase3.md` § Cross-cutting concerns; no opt-out flag to flip yet).
- [x] Webhook endpoint validates provider signature (no spoofed events) — `ResendWebhookTests.Rejects_bad_signature_with_401` pins HMAC-SHA256 verification of the `Svix-Signature` header; `Timestamp_more_than_5_minutes_old_returns_401` pins the replay window.

Tests required before Stage 9 begins:

- [x] Send-email happy-path test mocks the provider client and asserts subject/body/to render correctly in both EN and ES — `EmailComposerTests.Renders_all_nine_templates_en_and_es` (18 cases) and `ResendEmailServiceTests` cover render + dispatch.
- [x] Recipient-lock test: passing an arbitrary `To:` is rejected — `EmailRecipientTests.EmailMessage_has_no_public_string_To_constructor` (compile-time type lock; no API path can supply a raw string).
- [x] Header-injection test: a user "name" with embedded `\r\n` cannot inject email headers — `EmailComposerTests.Strips_cr_lf_in_subject`.
- [x] Rate-limit test: 6th password-reset request within an hour is rejected — `EmailRateLimitTests.Sixth_password_reset_request_in_same_hour_for_same_email_returns_429`.
- [~] DKIM signature presence test (smoke test against staging provider config) — requires DNS published; staging smoke runs in Stage 16 once the registered sending domain is verified in Resend per [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md) Step 1.

---

## Stage 9 — Auth SPA pages (Batch 3e)

**Status: ❌ Pending.** First user-visible Phase 3 work. Lands after Stage 8 because every flow depends on a working email service.

> **Goal:** every auth screen exists in the SPA, designed to a high bar (these are the first thing beta users see), with full localization (EN/ES), accessibility (focus management, `aria-live`), and clear error states.

### Sub-stages

| # | Sub-stage | Reference |
|---|---|---|
| 9.1 | `/login` page (email + password form) | `planning-phase3.md` § 10 Auth screen design |
| 9.2 | `/login/totp` page (6-digit TOTP step) | (above) |
| 9.3 | `/register` page + email-verification interstitial | `planning-phase3.md` § 10 Auth screen design. **Origin:** completes a 6a-deferred slice — `RequireConfirmedEmail = true` was set deliberately in 6a for security correctness with the email-send + `ConfirmEmail` handler scheduled for 6c; 6c.1/6c.2 shipped password-reset + reauth instead and email verification slid here. See [Stage 6a spec § Why no auto-sign-in?](superpowers/specs/2026-05-09-stage-6a-identity-foundation-design.md) lines 463–480 for the rationale + the test-fixture cleanup decision deferred to this stage. |
| 9.4 | `/password-reset` request page + `/password-reset/confirm` (with TOTP) | `security-model.md` § Password Reset |
| 9.5 | Lockout / self-service unlock screen | `security-model.md` § Login → Account lockout self-service unlock |
| 9.6 | TOTP setup flow (QR + manual-entry fallback + backup-codes download) | `planning-phase3.md` § 10 Auth screen design |
| 9.7 | Backup-codes recovery flow (lost TOTP device) | (above) |
| 9.8 | Reauthentication prompts on sensitive operations | `security-model.md` § Login → Reauthentication |
| 9.9 | Language toggle on every auth card (globe icon) | `planning-phase3.md` § Localization |
| 9.10 | RLS coverage audit for `[PreAuthCallSite]` write paths + test-infra `ceres_app` parity | `planning-phase3.md` § Stage 7.5 deferred items (2026-05-18) |
| 9.11 | Playwright E2E foundations — install dep, `playwright.config.ts`, first golden-path suites, local-run docs (CI wiring lands in Stage 16.16) | [ADR-0071](decisions/ADR-0071-e2e-testing-on-playwright.md), `planning-phase3.md` § Phase 3 resolved decisions |

### Verification checklist

Layout + brand:

- [x] Centered card layout, no app shell (sidebar, top bar absent) — verified at 375px on `/login`, `/login/totp`, `/password-reset`, `/password-reset/confirm` during the 2026-05-20 Section E walkthrough. `/app/security` correctly retains the app shell per ADR-0069.
- [x] Ceres logo / wordmark placement consistent across all auth pages — verified 2026-05-20 (Section E images #59, #60, #70, #71).
- [x] Globe icon language toggle at the bottom of every auth card; instant in-place swap via `i18n.changeLanguage()`, no reload
- [x] Pre-auth language detection writes the `lang` cookie (non-HttpOnly, `SameSite=Lax`) — verified 2026-05-20 (Section E step 39): `lang=es` cookie present in DevTools cookie panel and in the request `Cookie` header of `/api/auth/csrf`.

`/login`:

- [x] Email + password fields with explicit `<label>` (`htmlFor`) — no placeholder-only labels
- [x] Submit button has idle / loading / error states
- [x] On wrong credentials: generic error "Email or password is incorrect" (no enumeration leak)
- [x] On account locked: "Account locked. Check your email for an unlock link." — no other detail
- [x] On success: redirect to `/` (dashboard) regardless of TOTP-enrolment status. Per [ADR-0069](decisions/ADR-0069-mfa-opt-in-for-personal-users.md), MFA enrollment is reachable only from Settings → Security; there is no first-login redirect to TOTP setup.
- [x] On `200 { requiresTotp: true }` from the API (TOTP challenge step): redirect to `/login/totp`. Distinct from the first bullet — that one covers the `204` success path; this one covers the second-factor challenge handoff.
- [x] "Forgot password?" link routes to `/password-reset`
- [x] "Create account" link routes to `/register`

`/login/totp`:

- [x] 6-digit input with auto-focus, auto-advance, paste handling — shadcn `InputOTP` primitive with 6 `InputOTPSlot` cells; auto-submit fires on 6 digits typed/pasted. Test `LoginTotp.test.tsx > auto-submits when 6 digits typed` pins the contract.
- [x] On wrong code: generic error "Invalid code" — `auth.totp.errors.invalid` i18n key; tested in `LoginTotp.test.tsx > on 401 INVALID_MFA_CODE renders the inline error`.
- [x] On expired window: same generic error (no leak that "the code was right but expired") — server already collapses both into `401 INVALID_MFA_CODE` per `AuthController.cs:286`; SPA renders the same `auth.totp.errors.invalid` regardless.
- [x] On success: cookie set, redirect to `/` (dashboard) **on dashboard path only** — first-run onboarding redirect deferred to Stage 15.5 (onboarding wizard owns first-run UX). Tested in `LoginTotp.test.tsx > auto-submits when 6 digits typed; on 204 navigates to /`.
- [x] Backup-code link below input: "Lost your device? Use a backup code" — i18n `auth.totp.backupCodePrompt`; tested in `LoginTotp.test.tsx > backup-code mode: link toggles form`.
- [x] Backup-code path uses single-use code (server-side single-use enforcement in `MfaBackupCodeService.VerifyAndConsumeAsync` at `AuthController.cs:325`); **on success offers backup-codes regeneration** — server returns `204` regardless and does not currently signal "you just consumed your last code". Regeneration prompt is Stage 9.7's scope (backup-codes recovery flow). 9.2 ships the consume path; 9.7 ships the post-consume UX. Spec: `docs/superpowers/specs/2026-05-17-stage-9-2-login-totp-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-2-login-totp-impl.md`.

`/register`:

- [ ] Email + password fields; password meets policy (≥ 8 chars, no max < 64) per Stage 6
- [ ] Submit returns 202 Accepted with "Check your email to verify your address" — no enumeration leak (same response if email already registered)
- [ ] Verification email sent with single-use 256-bit token, hashed, 30-min expiry
- [ ] `/email-verify?token=...` page accepts the token, marks email verified, redirects to `/login/totp/setup`

`/password-reset` (request) + `/password-reset/confirm` (action):

- [x] Request page: email field; submit returns "If that email is registered, you'll receive a link" (constant response + timing) — server `PasswordResetService.RequestAsync` mirrors the Argon2id cost across known/unknown email branches per Stage 6.16. SPA renders the success block (`auth.passwordReset.request.successTitle` + `successBody`) on 204 regardless. Tested in `PasswordReset.test.tsx > request 204 replaces form with success block`.
- [x] Email contains link with 256-bit token (15-min expiry) — server-side; verified by `PasswordResetService` unit tests, not by 9.4's SPA work. End-to-end verified 2026-05-19 manual test (Section D steps 29–38, MFA-enabled path).
- [x] **Form requires: TOTP code + new password** — implemented as a two-step UX (new-password first; reveal TOTP cells if server returns `200 { requiresTotp: true }`; submit again with all three fields). Note: SPA URL uses fragment-based delivery (`/password-reset#token=...`) per the server's existing URL convention at `PasswordResetService.cs:164`, not the query-based shape implied by the original roadmap line. Tested in `PasswordReset.test.tsx > confirm 200 requiresTotp → reveals OTP cells; second submit posts all three fields`.
- [x] On success: token invalidated (server-side per `PasswordResetService.ConfirmAsync`), all sessions revoked (server-side per `PasswordResetService.cs:350-353`), redirect to `/login` — SPA navigates to `/login?reset=1`; Login fires a sonner toast on mount. Tested in `PasswordReset.test.tsx > confirm 204 navigates to /login?reset=1` + `Login.test.tsx > fires the password-reset toast when /login?reset=1`.
- [x] On expired token: clear error, link to request a new one — `auth.passwordReset.confirm.errors.invalidToken` rendered as a full block with `auth.passwordReset.confirm.requestNewLink` link to `/password-reset`. Tested in `PasswordReset.test.tsx > confirm 401 INVALID_RESET_TOKEN → invalid-token block with request-new-link`.
- [x] On wrong TOTP: generic error, does NOT consume the reset token — server contract at `PasswordResetService.cs:300-303` already pins this (the token is consumed only on success). SPA renders the inline error and preserves the password fields. Tested in `PasswordReset.test.tsx > confirm 401 INVALID_MFA_CODE → inline error on OTP, password fields preserved`. Spec: `docs/superpowers/specs/2026-05-18-stage-9-4-password-reset-design.md`.

Lockout / unlock:

- [ ] After 10 failed login attempts, account is locked + email sent with self-service unlock link
- [ ] `/account/unlock?token=...` accepts the signed token, unlocks the account, redirects to `/login` with success toast
- [ ] Token expires after a reasonable window (e.g., 1 hour)
- [ ] Lockout email also tells the user "valid TOTP codes are still accepted during lockout" (per `security-model.md` § Login)

TOTP setup (entry point: `/app/security` per ADR-0069 — NOT `/login/totp/setup`; the original roadmap URL predated the ADR):

- [x] QR code displayed (otpauth:// URI) — `qrcode.react` 4.2.0 rendering an `otpauth://totp/Ceres:<email>?secret=<key>&issuer=Ceres&algorithm=SHA1&digits=6&period=30` URI from the server's `POST /api/auth/mfa/enroll` response. Tested in `Security.test.tsx > clicking Enable fires POST /api/auth/mfa/enroll and advances to wizard step 1`.
- [x] Manual-entry secret displayed below QR (collapsed behind a `<details>` disclosure) for accessibility / desktop authenticators — space-grouped format from the server's `manualEntryKey` field. Copy-to-clipboard button writes the unspaced key. Tested in the same Security test (asserts the manual key text is in the DOM).
- [x] Verification step requires entering one valid code before enrolment is complete — 6-cell `InputOTP` auto-submits to `/api/auth/mfa/enroll/verify`; server returns 10 backup codes on success.
- [x] On successful enrolment: 10 backup codes generated (cryptographically random, ≥ 20 bits entropy each per NIST 800-63B per existing `MfaBackupCodeService.GenerateOne`), shown ONCE in a 2×5 grid, with "Copy all" and "Download as .txt" actions. Tested in `backup-codes-download.test.ts` (Blob MIME type, URL revoke, txt content).
- [x] Backup codes are hashed in DB after this step (server-side, already done in `MfaBackupCodeService.GenerateAndPersistAsync` since Stage 6.4); the page warns "These codes will not be shown again" via an inline alert block.
- [x] User must confirm "I've saved my backup codes" checkbox before proceeding — the Done button is `disabled={!confirmed}` in `TotpEnrollStep2BackupCodes.tsx`.
- [x] After enrolment: page renders Enabled state (NOT a redirect — single SPA page). First-run onboarding redirect is Stage 15.5's owner per `docs/roadmap-phase-three.md:1518`. Wizard step 3 calls `auth.refresh()` before unmounting so `twoFactorEnabled` flips in context.
- [x] **Disable two-factor sign-in** — new `POST /api/auth/mfa/disable` endpoint in `MfaController` (`[Authorize] + [RequireRecentAuth]`, sets `TwoFactorEnabled = false`, resets authenticator key, purges persisted backup codes via new `MfaBackupCodeService.PurgeAsync`, writes `AuditLogAction.MfaDisabled` row). SPA "Turn off two-factor sign-in" button on the Enabled state opens an `AlertDialog` confirming the loss of MFA protection, then POSTs and re-renders Disabled state via `auth.refresh()`. Pinned by 5 integration tests in `ProjectCeres.Tests/Integration/Authentication/Mfa/MfaDisableTests.cs` (status, flag flip, backup-code purge, authenticator-key reset, 409-on-not-enabled).

Backup-codes recovery flow:

- [ ] `/login/totp` accepts a backup code in the same input or via a "Use backup code" toggle
- [ ] Backup code single-use: marked consumed in DB after success
- [ ] After backup-code login: warning banner on dashboard suggests "Re-enroll TOTP soon. You have N backup codes remaining."
- [ ] Re-enrolment from settings invalidates ALL existing backup codes and generates a fresh set

Reauthentication prompts:

- [ ] Reusable `<ReauthenticationDialog>` component prompts for fresh password before sensitive actions
- [ ] Triggered on: change password, change email, re-enroll TOTP, view active sessions, GDPR erasure (matches `security-model.md` list exactly)
- [ ] Successful reauthentication grants a 5-minute window scoped to the originating action
- [ ] Window expires; re-prompt required for the next sensitive action

Error states:

- [x] Wrong password — generic message — verified 2026-05-20 (Section E): Spanish login surfaces "El correo electrónico o la contraseña no son correctos" on 401, no enumeration leak.
- [ ] Expired TOTP — generic message
- [ ] Locked account — clear message with "check email" instruction
- [ ] Network error — retry-able toast, form state preserved
- [ ] Server error (500) — clear message, no stack trace exposed

Accessibility:

- [ ] Every form has explicit `<label>` and `aria-describedby` for errors
- [ ] Focus moves to the first error field on submission failure
- [ ] Toast errors are also announced via `aria-live="polite"` for screen readers
- [ ] Tab order is logical (email → password → submit → forgot-password → create-account)
- [ ] Auth cards are keyboard-navigable; no mouse-only interactions
- [ ] vitest-axe runs against `/login`, `/login/totp`, `/register`, `/password-reset` with zero violations

Localization:

- [x] Every string in every auth page uses `useTranslation()` keyed strings
- [x] EN + ES translations complete in `en.json` and `es.json`
- [x] No untranslated copy visible when toggling to ES — verified 2026-05-20 (Section E): `/login` and `/login/totp` rendered fully in Spanish (Iniciar sesión, Verifica tu identidad, Correo electrónico, Contraseña, ¿Olvidaste tu contraseña?, Crear una cuenta, Recordarme en este dispositivo, Volver a iniciar sesión, ¿Perdiste el dispositivo? Usa un código de respaldo). No English leaks observed.
- [ ] Date/time strings (e.g., "Token expires in 15 minutes") respect the user's locale — N/A so far: no relative-time strings render on the auth surfaces walked in 2026-05-20 Section E. Leave open until a surface that renders one is exercised.

Responsive (per [`planning-phase3-responsive.md`](planning-phase3-responsive.md) § Surface Inventory — auth surfaces are single-column centered card on every tier):

- [x] Mobile (375px iPhone SE): centered card fills viewport with comfortable padding; no horizontal overflow; touch targets on every input/button ≥ 44×44px — verified 2026-05-20 (Section E step 41) across `/login`, `/login/totp`, `/password-reset`, `/password-reset/confirm`, `/app/security`. No horizontal overflow on any surface.
- [ ] Tablet (768px iPad): centered card constrained to a readable max-width; layout unchanged from mobile beyond the max-width clamp
- [ ] Desktop (≥ 1024px): centered card constrained to a narrow max-width; sidebar/app shell absent on every auth page
- [x] TOTP 6-digit input renders cleanly on mobile (no tiny touch targets, no zoom-on-focus) — verified 2026-05-20 (Section E images #60, #66, #70): cells fit inside the card at 375px on `/login/totp`, `/app/security` step 1, and `/password-reset/confirm`.
- [x] QR code in TOTP setup flow is large enough to scan on mobile when displayed at the user's screen — verified 2026-05-20 (Section E image #66): QR renders at a comfortably scannable size at 375px.
- [x] Backup codes download offers a `.txt` that copies cleanly on mobile (long press → save / share sheet) — verified 2026-05-20 (Section E images #67/#68 plus user-confirmed Copy all + Download .txt actions appear below the screenshot crop).
- [x] Language toggle (globe icon) is reachable without scrolling on mobile — verified 2026-05-20 (Section E images #59, #60, #70, #71): globe icon sits at the bottom of every auth card with no scroll required at 375px.

Stage 6 deferred items (carry-forward from the Stage 6 verification checklist):

- [x] **Manual browser DevTools verification of the auth cookie after a real login** — verified 2026-05-20 (Section E step 39 images #50, #52): `__Host-Session` cookie present after login, HttpOnly ✓, Secure ✓ (per `SecurePolicy = SameAsRequest` in dev → off over HTTP, on over HTTPS; `Always` in production), SameSite=Lax ✓, Path=/ ✓, no Domain attribute ✓. Same audit cleared `__Host-XSRF`, `__Host-Persist`, `Mfa.RememberMe` (the last with intentional `Path=/api/auth/login` scoping per `AuthController.cs:187`). *Anchor: Stage 6 § Cookie configuration carry-forward.*

Stage 8 deferred items (carry-forward from Stage 8 — security-event email call-sites):

- [ ] **Wire registration-confirmation email at `POST /api/auth/register`** — the SPA register flow must call `IEmailComposer.Compose(EmailTemplateKey.RegistrationConfirmation, ...)` with a single-use 256-bit Argon2id-hashed token (30-min expiry) and send via `IEmailService`. Resx key `RegistrationConfirmation.{Subject,BodyText,BodyHtml}` does NOT yet exist in `Emails.en.resx` / `Emails.es.resx` — add EN + ES copy alongside the call-site wiring. Add `RegistrationConfirmation` to the `EmailTemplateKey` enum. *Anchor: Stage 8 § Transactional templates — Registration confirmation.*
- [ ] **Wire TOTP-enrolled security-event email at `POST /api/auth/mfa/enroll/verify`** — on successful enrolment, send a confirmation email so the user is notified if an attacker enrols TOTP on their account. Add `TotpEnrolled` to `EmailTemplateKey` + EN/ES resx keys (`TotpEnrolled.Subject/BodyText/BodyHtml`). Body includes IP + timestamp + "this wasn't me" link per `security-model.md § Security Event Notifications`. *Anchor: Stage 8 § Transactional templates — TOTP enrolled.*
- [ ] **Wire TOTP-disabled security-event email** — when a TOTP-disable endpoint ships in Stage 9 (or whenever the user can turn MFA off), send a notification email. Add `TotpDisabled` to `EmailTemplateKey` + EN/ES resx keys. Includes "re-enable + revoke all sessions" link. The disable endpoint itself does not exist today; the email wires alongside it. *Anchor: Stage 8 § Transactional templates — TOTP disabled.*
- [ ] **Wire backup-codes-regenerated email at `POST /api/auth/mfa/backup-codes/regenerate`** — the regeneration endpoint already exists (Stage 6) but currently sends no notification. Add `BackupCodesRegenerated` to `EmailTemplateKey` + EN/ES resx keys, then wire the call into `MfaController.RegenerateBackupCodes`. *Anchor: Stage 8 § Transactional templates — Backup codes regenerated.*
- [ ] **Wire TOTP-re-enrolled email** — when a user re-enrols TOTP from settings (the existing `/api/auth/mfa/enroll/verify` endpoint, but distinct from first-time enrolment), send the re-enrolled variant. Whether this reuses `TotpEnrolled` or gets its own `TotpReEnrolled` key is a Stage 9 design call. *Anchor: Stage 8 § Transactional templates — TOTP re-enrolled.*

Stage 9.10 — RLS pre-auth-write audit + test-infrastructure parity:

- [x] **`PreAuthUserScope` made re-entrant** (`ProjectCeres/Common/Authentication/PreAuthRlsScope.cs`, 2026-05-19) — when an outer scope is already open for the same `userId`, nested `BeginPreAuthUserScopeAsync` calls return a no-op sentinel scope instead of opening a second transaction (Npgsql rejects nested transactions on a connection). Cross-user nesting throws to surface composition bugs. Shipped together with `PasswordResetService.ConfirmAsync`'s RLS fix because the outer scope wraps inner `TotpReplayGuard.TryAcceptAsync` + downstream services that also open their own scopes. Regression test: `ProjectCeres.Tests/Integration/Rls/PreAuthWritesUnderRlsTests.PasswordResetTokens_select_under_PreAuth_returns_null_until_PreAuthUserScope_opens` pins the three contracts (filtered-without-GUC, admin-bypass, scoped-visible).
- [x] **`PasswordResetService.ConfirmAsync` fixed** (2026-05-19) — initial token lookup routes through `AdminDbContext.PasswordResetTokens.IgnoreQueryFilters()` (ceres_admin, BYPASSRLS + EF-filter bypass) because `match.UserId` isn't known yet; all subsequent writes (token consume, session revoke, email-change cancel, security-stamp regen) wrap in `_db.BeginPreAuthUserScopeAsync(match.UserId, ct)`. End-to-end verified 2026-05-19 manual test.
- [ ] **Audit every other `[PreAuthCallSite]` endpoint for the same RLS write/read anti-pattern** that the four 2026-05-18 fixes covered (`TotpReplayGuard`, `MfaBackupCodeService.VerifyAndConsume`, `PasswordResetService.RequestAsync`, `LockoutUnlockService.IssueAsync`). Specific call sites to audit: `PasswordResetService.ConfirmAsync` (called from `PasswordResetController.Confirm`), `LockoutUnlockService.ConfirmAsync` (called from `LockoutUnlockController.Confirm`), `EmailChangeService.ConfirmAsync` + `RevokeAsync` (called from `EmailChangeController.Confirm` / `Revoke`), `AuthController.Register`'s call chain through `UserManager.CreateAsync` + `_categorySeedService.CopyDefaultsForUserAsync` + `_auditLog.RecordAsync`. For each: trace the read+write paths against user-owned tables, wrap with `PreAuthUserScope` where needed, add a regression test under `ProjectCeres.Tests/Integration/Rls/PreAuthWritesUnderRlsTests.cs`. *Anchor: `planning-phase3.md` § Stage 7.5 deferred items (2026-05-18).*
- [ ] **Decide test-infrastructure parity strategy** — `ProjectCeres.Tests/Integration/WafCollection.cs` lines 75–76 override `ApplicationConnection` to point at `ceres_admin` (BYPASSRLS), which means every existing auth integration test runs with the Stage 7.5 RLS policies silently inert. The 2026-05-18 bug shipped because no test could catch it. Pick one and ship: (a) flip the override back to `ceres_app` for the auth-tier test collection and patch the test data setup to satisfy the user_isolation policy, OR (b) add a parallel `AuthRlsParityTestCollection` that runs the same flows against `ceres_app`. Document the decision in `docs/testing.md` § Rules so the override doesn't silently regress.
- [ ] **Extend `PreAuthWritesUnderRlsTests.cs` to cover the audit's findings** — one new `[Fact]` per call site that the audit confirms needs `PreAuthUserScope`. Each test follows the existing pattern: bind directly to `TestDbFixture.AppConnectionString` (`ceres_app`), install a `RowLevelSecurityInterceptor` with `UserContext.PreAuth`, exercise the service method, assert no `RlsPolicyViolationException` and the write actually persists (re-read via admin context to confirm). Pre-fix the new tests fail with 42501; post-fix they pass. The file is already the tripwire for this class of regression.
- [ ] **Sweep `security-model.md` § Row-Level Security with a "pre-auth write pattern" section** documenting `PreAuthUserScope` as the standing rule: any pre-auth call site that knows its userId AND must read or write a user-owned table MUST wrap the operation in a `PreAuthUserScope`. Cross-reference from `docs/architecture.md` if it documents the RLS layer.

Stage 9.11 — Playwright E2E foundations:

- [ ] **Install Playwright dev dep** — `pnpm --dir ProjectCeres.Client add -D @playwright/test`. *Anchor: [ADR-0071](decisions/ADR-0071-e2e-testing-on-playwright.md) § Implementation gates.*
- [ ] **Author `ProjectCeres.Client/playwright.config.ts`** — `baseURL` pointing at the production-built SPA; `webServer` block that boots `dotnet run --project ProjectCeres` + Vite preview before tests; `projects` matrix for Chromium / Firefox / WebKit; `fullyParallel: true`; trace + screenshot + video on failure; output to `ProjectCeres.Client/playwright-report/`. *Anchor: ADR-0071 § Decision + § Implementation gates.*
- [ ] **Create `ProjectCeres.Client/e2e/` directory** (separate from `src/**/__tests__/`); `pnpm --dir ProjectCeres.Client e2e` command wired in `package.json` per `docs/testing.md` § E2E Tool: Playwright (TypeScript). *Anchor: `testing.md` lines 99–112.*
- [ ] **Write the five Stage-9 golden-path suites** under `e2e/auth/`:
  - [ ] `register-login.spec.ts` — register → email-verification interstitial → login → dashboard
  - [ ] `password-reset.spec.ts` — forgot-password → reset-link email → new-password → login
  - [ ] `totp-enrol-and-first-login.spec.ts` — TOTP enrolment from Settings → sign-out → login with TOTP → dashboard
  - [ ] `lockout-self-service.spec.ts` — N wrong passwords → lockout → unlock-email link → re-login
  - [ ] `backup-code-recovery.spec.ts` — "lost device" → backup-code consume → dashboard. *Anchor: ADR-0071 § Decision lines 35–38; `planning-phase3.md` line 427.*
- [ ] **Wire fixture for real PostgreSQL** — E2E suite must run against a real `dotnet run` + Postgres (mirrors xUnit integration-test fixture pattern, but at the browser layer). Decide: shared dev DB with a per-suite reset, or a `project_ceres_e2e` database with the existing migration runner. Document the choice in `testing.md` § E2E. *Anchor: ADR-0071 § Decision line 31.*
- [ ] **Document local-run instructions** in `docs/testing.md` § Running tests — `pnpm --dir ProjectCeres.Client e2e` (headless), `pnpm --dir ProjectCeres.Client e2e --ui` (Playwright UI Mode), prerequisite of `dotnet run` already going or letting `webServer` boot it. *Anchor: `testing.md` § E2E Tool lines 108–112.*

> **CI wiring is deferred to Stage 16.16** (already-scheduled — see `roadmap-phase-three.md` § Stage 16). The `.github/workflows/ci.yml` file does not exist until Stage 16 ships per [ADR-0070](decisions/ADR-0070-ci-cd-on-github-actions.md); GH-Actions wiring for the Playwright suite (`npx playwright install --with-deps` cached, sharded runners, trace + report artefacts on failure) is part of that CI bring-up rather than this stage. Stage 9.11 ships the suite + local-run docs; Stage 16.16 wires it into CI.

---

## Stage 9.1.5 — Phase 1 polish + bugfix batch

**Status: ✅ Done (2026-05-17).** Opened + closed 2026-05-17. Closed-batch container for bugs discovered during the Phase 1 UX walkthrough and a pre-existing suite-wide auth-tier test-contention issue surfaced by the build hook the same session. All 9 sub-stages (a–i) shipped, all verification commands green (`dotnet test`: 1098 Passed / 0 Failed; `pnpm test`: green; both builds: clean), browser verification confirmed across the user-facing changes. Phase 2 plan-writing can resume. The batch followed the project's `no-unjustified-deferrals` rule (every bug discovered during in-flight work either fixed in the current commit or queued into the open batch stage, never deferred without a tooling-gap or already-scheduled justification).

> **Goal:** close every Phase-1 discovered defect before Phase 2's auth surfaces (register, email-verify, password-reset confirm) get planned and built on top of the same primitives.

### Sub-stages

| # | Sub-stage | Source |
|---|---|---|
| 9.1.5.a | LockoutUnlockToken.TokenLookup retrofit — extends Stage 6.15 indexed-lookup pattern to the third token sibling | Test-isolation flake surfaced 2026-05-17. After two false-start hypotheses (rate-limit partition leak, then "TBD pending architectural diagnosis"), deep-fix-mode round 3 traced the root cause to LockoutUnlockService.ConfirmAsync running O(N) Argon2id verifies over unconsumed-and-unexpired token rows. Under integration-test load the scan saturates CPU, causing timing-sensitive sibling tests to flake. The pattern was already solved for PasswordResetToken and EmailChangeToken in Stage 6.15; this stage retrofits LockoutUnlockToken via the same HMAC-SHA256 TokenLookup column + unique index. ConfirmAsync becomes O(1) DB lookup + 1 Argon2 verify. Full diagnostic notes in task #43. |
| 9.1.5.b | Diagnose + fix: lockout doesn't trigger reliably at 6 attempts (rate-limit fires first) | Phase 1 UX walkthrough — security-pipeline ordering between `AuthLoginByIp` and `AccessFailedCount` increment. Documented threshold (6) currently drifts to 8–9 depending on timing. |
| 9.1.5.c | Homegrown SPA theme provider replacing next-themes — fixes the OS-toggle-not-honored bug | Phase 1 UX walkthrough surfaced "tokens collapse to identical values in light + dark modes." Original spec (e262b62) misdiagnosed as a token-contrast issue and shipped commit `014f2b5` (bg-muted/30 → bg-background on AuthLayout) — kept on independent merits but did not fix the user-visible symptom. deep-fix-mode round 4 + runtime DevTools confirmed actual root cause: next-themes is designed for Next.js; in our Vite SPA the matchMedia change listener never fires, so `<html class="dark">` is stuck regardless of OS preference. Replaced next-themes with a homegrown ~85-line provider using useSyncExternalStore + ported disableTransitionOnChange + pre-paint inline script in index.html. Three audit-validated frontend-orchestrator passes (vercel-react-best-practices, web-design-guidelines, design-system reconciliation) rolled into the revised spec. |
| 9.1.5.d | Add in-app light/dark mode toggle | Phase 1 UX walkthrough — `ThemeProvider defaultTheme="system"` means OS preference wins at load with no in-app override affordance. Pairs with 9.1.5.c. Shipped: rewrote `ThemeToggle` from binary `<Button>` (light↔dark) to tri-state `<DropdownMenu>` (System/Light/Dark) using base-ui's `DropdownMenuRadioGroup`. Mounted in AuthLayout footer (third mount site; TopBar + MobileDrawer already had it). Four new i18n keys added under `theme.*` namespace (en + es). Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-d-in-app-theme-toggle-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-d-in-app-theme-toggle-impl.md`. |
| 9.1.5.e | Add language code (EN/ES) next to globe icon in `LanguageToggle` | Phase 1 UX walkthrough — globe icon alone doesn't communicate the active language. Shipped: trigger now renders `<Globe>` + active two-letter code (EN/ES) side-by-side; aria-label interpolates the active language name ("Change language, currently English" / "Cambiar idioma, actualmente Español") via the new `auth.languageToggle.ariaLabelWithLanguage` key; dropdown switched from plain `DropdownMenuItem` rows to a `DropdownMenuRadioGroup` so the active language carries `aria-checked="true"` and a visible check — matching the ThemeToggle vocabulary from 9.1.5.d. Dead `auth.languageToggle.ariaLabel` key removed in the same atomic commit. Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-e-language-code-toggle-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-e-language-code-toggle-impl.md`. |
| 9.1.5.f | Wire logout into the SPA app shell | Phase 1 UX walkthrough — server `POST /api/auth/logout` exists; no SPA UI calls it. User had to clear cookies manually mid-walkthrough to test failure cases. Shipped: `AuthContext` gains `logout()` (POSTs `/api/auth/logout`, then unconditionally clears local user + status — even on server 4xx/5xx the user gets signed out client-side and `RequireAuth` redirects on next render). `AvatarMenu`'s Logout menu item now opens a confirmation dialog with destructive Sign-out button + Cancel; on confirm calls `logout()` and navigates to `/login`. 5 new i18n keys under `auth.logout.{title,body,cancel,submit,submitting}` (en + es). Cross-cutting test fix: `TopBar.test.tsx` + `AppLayout.a11y.test.tsx` gain `AuthProvider` wrap and their bare `{ok,json}` fetch stubs replaced with real `Response` objects (the previous stubs didn't satisfy `apiFetch`'s `response.headers.get()` — surfaced now that TopBar mounts AvatarMenu which calls `useAuth`). |
| 9.1.5.g | Write ADR-0076 — SPA CSRF token-source via `X-XSRF-TOKEN` response header | Architectural decision shipped in commit `ce2d9e9` (Phase 1 mid-stream fix) without a corresponding ADR. Phase 2/3/4 every state-changing SPA endpoint inherits this pattern; per `feedback_persist_deferred_decisions`, architectural decisions get persisted in durable docs before downstream consumers cite them. Shipped: `docs/decisions/ADR-0076-spa-csrf-token-source-via-response-header.md` documents the decision, four alternatives considered (cookie-as-header anti-pattern, custom IAntiforgery, meta-tag injection, no-CSRF-rely-on-SameSite), consequences for every state-changing SPA endpoint, and references the originating commit `ce2d9e9` plus the logout cache-clear regression follow-up `2d978d5`. `security-model.md` § CSRF rewritten to describe the correct double-submit pair mechanism (the previous text described the broken cookie-as-header pattern that caused the original bug). |
| 9.1.5.h | `ILookupNormalizer` swap broke pre-existing user logins; codify rule + add backfill migration | Commit `4b35911` swapped Identity's default `UpperInvariantLookupNormalizer` for a custom `LowercaseLookupNormalizer` with no data migration. `AspNetUsers.NormalizedEmail` + `NormalizedUserName` rows seeded before that commit still held uppercase strings; `FindByNameAsync` produced lowercase and matched zero rows; login returned "invalid login" without ever checking the password (`AccessFailedCount` stayed 0). Only one row existed (dev seed, fixed in-session via `UPDATE … SET = LOWER(…)`). In production with real users this would have broken every existing login at deploy time. Shipped: EF migration `BackfillIdentityNormalizedToLowercase` (3 idempotent UPDATE statements covering all three normalized Identity columns including the currently-empty `AspNetRoles.NormalizedName` so future role introduction inherits the protection); 9 xUnit tests in `ProjectCeres.Tests/Integration/BackfillIdentityNormalizedToLowercaseTests.cs` pin uppercase-lowering / idempotency / NULL-safety per column; `Down()` throws `NotSupportedException` (reverting restores the broken state). Standing rule added to `security-model.md` § ASP.NET Core Identity Hardening: any `ILookupNormalizer` registration change requires a same-commit data-migration. Cross-referenced from `models.md` § ApplicationUser and the developer guide. Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-h-lookup-normalizer-backfill-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-h-lookup-normalizer-backfill-impl.md`. |
| 9.1.5.i | Mount `LanguageToggle` in the signed-in app shell | Found during 9.1.5.f browser verification — signed-in users had no way to change UI language without signing out first. `LanguageToggle` was mounted only at `AuthLayout` footer. Shipped: component moved from `src/app/components/auth/` to `src/app/components/` (no longer auth-only); icon-only dropdown variant now mounts in `TopBar` (desktop, between bell and theme toggle); new `LanguageDrawerRow` in `MobileDrawer` mirrors `ThemeDrawerRow`'s segmented `[EN][ES]` shape (`role="radiogroup"`, per-button `aria-checked`); new `auth.languageToggle.label` i18n key (`"Language"` / `"Idioma"`). Plus a same-commit fix to `9.1.5.f`'s logout flow: `AuthContext.logout()` now clears the cached CSRF request token via `setCachedXsrfRequestToken(null)`, because the server rotates the cookie+token pair on logout and the stale cached token was causing the next login POST to be rejected with 400 (browser-found regression). |

### Verification checklist

- [x] 9.1.5.a — LockoutUnlockToken has TokenLookup column + unique index (Stage 6.15 pattern); LockoutUnlockService.ConfirmAsync queries by indexed lookup (O(1) + 1 Argon2 verify); IssueAsync stamps the lookup; tamper-resistance test pins the lookup+verify pair as a contract; three consecutive full `dotnet test` runs green with no auth-tier flake recurrence. Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-a-lockout-unlock-token-lookup-impl.md`. Migration: `AddLockoutUnlockTokenLookup` (mirrors Stage 6.15's `20260511155005_AddTokenLookup`).
- [x] 9.1.5.b — at attempt 11+ on `/api/auth/login` (after lockout has engaged at attempt 10), the response is `401 ACCOUNT_LOCKED_OUT`, NOT `429 RATE_LIMITED`. `MaxFailedAccessAttempts` stays at 10 (security-model.md §lockout invariant). `LockoutCache` hint mechanism: `AuthController.Login` seeds per-email + per-IP-pointer cache entries on lockout; `OnRejected` consults them (memory-only, no DB query) for `/api/auth/login` rejections and surfaces the locked envelope. Per-IP pointer expires at 60s; per-email entry's TTL derives from `LockoutEnd - UtcNow`; cache invalidated by `LockoutUnlockService.ConfirmAsync`. Tests `RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited`, `RateLimitRejection_OnNonLoginEndpoint_StillReturnsRateLimited`, `RateLimitRejection_WhenPerEmailEntryAbsent_FallsBackToRateLimitedEnvelope`, `RateLimitRejection_DifferentUserFromSameIp_OutsideWindow_ReturnsRateLimited`, `RateLimitRejection_OnLoginTotpEndpoint_StillReturnsRateLimited`, `Confirm_RemovesEmailFromLockoutCache` pin the contract. Email normalization across `LockoutCache`, `FailedLoginRecorder`, `ResendWebhookController`, `EmailChangeService`, `PasswordResetService` consolidated onto `ILookupNormalizer` (custom `LowercaseLookupNormalizer` registered in DI). Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-b-lockout-vs-rate-limit-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-b-lockout-banner-impl.md`. Manual browser verification: enter 10 wrong passwords; attempt 11+ shows the locked-account banner (i18n key `auth.errors.account_locked_out`), NOT the rate-limit toast.
- [x] 9.1.5.c — `src/app/theme/theme-context.tsx` (homegrown ~85-line ThemeProvider with useSyncExternalStore for matchMedia subscription) replaces `next-themes` at both mount sites (`src/app/main.tsx`, `src/design-system/main.tsx`). 5 vitest tests pin: sync first-mount, OS toggle propagation, explicit setTheme override, system→light→system round-trip, localStorage corruption fallback. Pre-paint inline script in `index.html` kills first-paint flash; `disableTransitionOnChange` ported to prevent toggle smear; `<meta name="color-scheme" content="light dark">` added for UA chrome. next-themes removed from package.json; zero live references survive. Manual browser verification confirmed: `htmlClass` flips between "light" and "dark" on DevTools color-scheme emulation (resolvedBgVar/resolvedCardVar resolve correctly per mode). Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-revised-theme-provider-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-c-revised-theme-provider-impl.md`. CSP nonce tripwire added to `security-model.md`.
- [x] 9.1.5.d — in-app theme selection is reachable from THREE contexts with two presentations tailored to each: (a) icon-only `ThemeToggle` dropdown in `TopBar` (desktop signed-in shell) and `AuthLayout` footer (unauthenticated pages) — toolbar slots forbid 3 inline buttons; (b) inline segmented radiogroup in `MobileDrawer`'s bottom section — 1-tap direct selection, no portal positioning bugs, vertical pixels respected. Tri-state cycle (System/Light/Dark) lets the user return to OS-following mode without clearing storage. Persists across reloads via the homegrown `ThemeProvider`'s localStorage key `ceres.theme:v1`. 7 vitest tests in `src/design-system/components/ThemeToggle.test.tsx` pin the dropdown contract (Sun/Moon icon swap by resolved theme, 3 menuitemradio items, `aria-checked` on active preference, `setTheme(value)` per option); 3 tests in `MobileDrawer.test.tsx` pin the segmented contract + drawer scrollability + theme-row visual shape (icon-left, label, segmented selector right-aligned). 4 new i18n keys (`theme.{label,system,light,dark,ariaLabel}`) in en + es. `AuthLayout.test.tsx` asserts both LanguageToggle + ThemeToggle triggers render side-by-side. Manual browser verification confirmed end-to-end at `/app/login` + `/app/dashboard` (desktop ≥640px + 375px viewport) including drawer scroll fix, segmented row icon parity with Settings/Support, and DevTools prefers-color-scheme emulation. Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-d-in-app-theme-toggle-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-d-in-app-theme-toggle-impl.md`.
- [x] 9.1.5.e — `LanguageToggle` button shows the active language code (`EN` / `ES`) next to the globe icon; updates immediately on selection. 5 vitest tests in `src/app/components/auth/LanguageToggle.test.tsx` pin: globe-trigger aria-label includes language, Español click writes lang cookie via `menuitemradio` role, "EN" text on trigger in English, aria-label interpolates and updates on `i18n.changeLanguage`, active language carries `aria-checked="true"` in the dropdown. Manual browser verification confirmed (auth-page footer + DevTools accessibility tree).
- [x] 9.1.5.f — Sign Out affordance reachable from the app shell; calls `POST /api/auth/logout` via `apiFetch`; clears local auth-context state; navigates to `/login`; manual UX check confirms a fresh visit to `/app/` after sign-out redirects to `/app/login`. 5 vitest tests pin the contract: `auth-context.test.tsx` covers `logout()` clearing local state on 204 success AND on 401 (defensive — server already lost session); `AvatarMenu.test.tsx` covers dialog opens with destructive Sign-out button + Cancel, confirm POSTs to `/api/auth/logout` + navigates to `/login`, cancel closes dialog without POST. Local-state-clear is unconditional by design — `RequireAuth` redirects on next render even if the server returned 4xx/5xx. Manual browser verification confirmed end-to-end. Browser-found regression on relogin (stale cached CSRF token → 400) fixed in commit `2d978d5` and pinned by an `auth-context.test.tsx` cache-clear test — covered under 9.1.5.i.
- [x] 9.1.5.g — `docs/decisions/ADR-0076-spa-csrf-token-source-via-response-header.md` exists; cites commit `ce2d9e9` (original fix) + `2d978d5` (logout cache-clear follow-up); documents four alternatives (cookie-as-header anti-pattern with Microsoft Learn citation, custom IAntiforgery, `<meta>` tag injection, no-CSRF rely on SameSite); notes the consequence that every state-changing SPA endpoint inherits this pattern via `apiFetch`; cross-references ADR-0063 (SameSite=Lax + CSRF tokens) and ADR-0019 (session management). `security-model.md` § CSRF rewritten in the same commit to replace the previous misdescription of the SPA pattern (the old text described the broken cookie-as-header behavior) with the correct double-submit cookie-pair mechanism, cross-linked to the ADR.
- [x] 9.1.5.h — EF migration `BackfillIdentityNormalizedToLowercase` exists and is idempotent (re-running on a fully-lowercase table is a no-op — pinned by `Migration_is_idempotent_on_already_lowercase_*` tests per column); migration applied to local dev DB (0 rows changed; manual login verified post-migration); `security-model.md` § ASP.NET Core Identity Hardening carries the standing rule "`ILookupNormalizer` registration changes require a same-commit data-migration that backfills `AspNetUsers.NormalizedEmail` + `NormalizedUserName` + `AspNetRoles.NormalizedName` to the new normalizer's output"; rule cross-referenced from `models.md` § ApplicationUser and from the developer guide `01-aspnetcore-identity.md`; manual login on the local dev DB still works after the migration runs (regression check — confirms the migration didn't re-introduce the case mismatch).
- [x] 9.1.5.i — `LanguageToggle` reachable from signed-in app shell: visible in `TopBar` desktop controls AND in the `MobileDrawer` bottom section as a segmented `[EN][ES]` radiogroup matching `ThemeDrawerRow`'s shape. Changing language while signed in updates page strings immediately and persists across reload via the existing `lang` cookie. Logout → relogin cycle works (no CSRF 400 on the relogin POST — `auth-context.test.tsx` pins the cache-clear contract). 1 new vitest test in `auth-context.test.tsx` covers the cache-clear; existing 5 `LanguageToggle.test.tsx` tests still pass post-move (only relative-import path changed). Manual browser verification confirmed end-to-end (TopBar dropdown, mobile drawer segmented row, sign-out → re-sign-in without 400).
- [x] **Batch close-out** ✅ — all sub-stages ticked. Verification commands run 2026-05-17 23:29 UTC: `dotnet test` → 1098 Passed / 0 Failed; `pnpm --dir ProjectCeres.Client test --run` → all green; `pnpm --dir ProjectCeres.Client build` → all chunks within budget; `dotnet build ProjectCeres/ProjectCeres.csproj` → 0 errors. Stage 9.1.5 marked ✅ Done. Phase 2 plan-writing can resume.

---

## Stage 11 — Razor + URL cleanup (Batch 4)

**Status: ❌ Pending.** Mechanical cleanup. Lands after Stage 9 because Auth is the last surface that needs the `/app/` prefix to coexist with Razor stubs. (Note: previously stated "after Stage 10" when onboarding occupied that slot; onboarding moved to Stage 15.5, so this stage now follows Stage 9 directly with no functional change.)

> **Goal:** `/app/` prefix dropped, MVC infrastructure stripped from `Program.cs`, all per-area 302 redirects deleted, one-shot `/app/*` → `/*` 301 in place for legacy bookmarks. The product becomes a pure Web API + SPA.

See [`planning-phase3-spa-migration.md` → Final cleanup plan](planning-phase3-spa-migration.md#final-cleanup-plan-after-every-razor-view-is-gone) for the original detailed sweep.

### Sub-stages

| # | Sub-stage |
|---|---|
| 11.1 | React Router `basename` changes from `/app` to `/` |
| 11.2 | Razor host view's catch-all route changes from `app/{*path}` to `{*path}` |
| 11.3 | One-shot `/app/*` → `/*` 301 redirects added |
| 11.4 | Every per-area 302 redirect deleted (Dashboard, Movements, Transactions, Transfers, Categories, Accounts, Recurring, Reports, Review, Import, Settings, Budgets, CsvImportProfiles) |
| 11.5 | MVC infrastructure stripped from `Program.cs` (controllers-only API surface) |
| 11.6 | Razor host views deleted (`Views/App/`, `Views/Home/`, `Views/Shared/_Layout.cshtml`, `Error.cshtml`, `_ViewStart.cshtml`, `_ViewImports.cshtml`, `_ValidationScriptsPartial.cshtml`) |
| 11.7 | Stage 6a architecture tests widened back to full scope (`No_api_controller_class_has_AllowAnonymous` → `No_controller_class_has_AllowAnonymous`; `Api_HttpGet_actions_must_not_have_write_verb_names` → `HttpGet_actions_must_not_have_write_verb_names`). Both rules are dropped to API-only in 6a because of legacy Razor controllers; Stage 11 deletes those, restoring the full-scope contract. |
| 11.8 | Frontend lint cleanup sweep — drive `pnpm --dir ProjectCeres.Client lint` to zero. Logged 2026-05-13 after the ESLint 10 / `typescript-eslint` 8.59.3 upgrade surfaced 34 pre-existing violations. Detail: 18× `react-hooks/set-state-in-effect`, 14× `react-refresh/only-export-components`, 2× `react-hooks/exhaustive-deps`. Full file-by-file remediation plan in [`planning-phase3-spa-migration.md` → Final cleanup plan, item 6](planning-phase3-spa-migration.md#final-cleanup-plan-after-every-razor-view-is-gone). |

### Verification checklist

URL surface:

- [ ] `/` serves the SPA (no `/app/` prefix anywhere in user-facing URLs)
- [ ] `/app/*` returns 301 to `/*` for every path that was previously a Batch 2 SPA route
- [ ] Old SPA bookmarks tested: `https://.../app/movements?needsReview=true` redirects to `https://.../movements?needsReview=true` with query string preserved
- [ ] Old Razor bookmarks tested: `https://.../Movements` (the per-area 302 from Batch 2) is now a 404 (redirect chain has been removed)
- [ ] No `/app/` references remain in code or docs (grep returns nothing)

`Program.cs`:

- [ ] `AddControllersWithViews()` replaced with `AddControllers()`
- [ ] `MapControllerRoute(...)` calls deleted
- [ ] `MapRazorPages()` (if present) deleted
- [ ] No remaining MVC-specific service registrations
- [ ] `app.UseStaticFiles()` retained (still serves `wwwroot/` SPA assets)
- [ ] Application boots cleanly with zero MVC infrastructure

File deletions:

- [ ] `ProjectCeres/Views/` directory deleted entirely
- [ ] No `.cshtml` files anywhere in `ProjectCeres/`
- [ ] No `.razor` files (Phase 3 never used Blazor; just confirm)
- [ ] `obj/` and `bin/` rebuilt cleanly with no MVC remnants

Razor controller stubs:

- [ ] Every Razor controller (`MovementsController`, `TransactionsController`, `TransfersController`, `CategoriesController`, `AccountsController`, `RecurringTransactionsController`, `ReportsController`, `ImportController`, `CsvImportProfilesController`, `BudgetsController`, `SettingsController`, `DashboardController`, `ReviewController`/`TransfersReviewController`) deleted
- [ ] Verify by grep: no `: Controller` in `ProjectCeres/Controllers/` outside `ProjectCeres/Controllers/Api/`
- [ ] All API controllers under `Controllers/Api/` retained and functional

Architecture tests (widening from Stage 6a's API-only narrowing):

- [ ] `ArchitectureTests.No_api_controller_class_has_AllowAnonymous` renamed to `No_controller_class_has_AllowAnonymous`; the `.Namespace?.Contains(".Api") == true` filter is removed
- [ ] `ArchitectureTests.Api_HttpGet_actions_must_not_have_write_verb_names` renamed to `HttpGet_actions_must_not_have_write_verb_names`; the `.Namespace?.Contains(".Api") == true` filter is removed
- [ ] Both tests pass after the rename + filter removal — confirms no remaining controllers carry class-level `[AllowAnonymous]` or have `[HttpGet]` actions whose names start with write verbs

Smoke tests:

- [ ] Application boots without exception
- [ ] Full SPA loads at `/` and every page renders
- [ ] Browser dev-tools network tab shows no 404s for legacy assets
- [ ] All API integration tests still pass (the API surface is unchanged by this batch)
- [ ] No regressions in the IDOR test suite from Stage 7

Frontend lint cleanup (sub-stage 11.8):

- [ ] `pnpm --dir ProjectCeres.Client lint` exits 0 with zero errors and zero warnings
- [ ] All 18 `react-hooks/set-state-in-effect` violations either fixed (derive-during-render or refactored form-reset logic) or disabled per-line with a `Why:` comment justifying the external-system-sync exception (see `planning-phase3-spa-migration.md` § 6 for the false-positive list)
- [ ] All 14 `react-refresh/only-export-components` violations resolved: shadcn-authored files (`badge.tsx`, `button.tsx`, `tabs.tsx`) carry a per-file disable comment with a `Why:` noting the shadcn convention; provider files are split into `provider.tsx` + `context.ts`; helpers/types are moved to sibling `*-types.ts` / `*-utils.ts`
- [ ] Both `react-hooks/exhaustive-deps` warnings (`ReviewCountProvider.tsx`, `ReminderCountProvider.tsx`) resolved — each one verified as either a real stale-closure bug fixed by adding the dep, or a deliberate capture-at-mount with a per-line disable + `Why:` comment
- [ ] Full Vitest suite still passes after the cleanup (no regressions in form-reset effects or matchMedia hooks)
- [ ] `pnpm --dir ProjectCeres.Client build` still passes with all bundle-size budgets clean

---

## Stage 11.5 — Import sandbox + admin tooling (Batch 4)

**Status: ❌ Pending.** Developer-facing test environment for the CSV/XLSX import pipeline. Sits after Stage 11 (Razor cleanup) so the admin SPA route lands into a fully SPA-only world. See [ADR-0072](decisions/ADR-0072-import-sandbox-as-separate-environment.md) for the architecture pattern (multi-environment separation, NOT runtime switch) and the sequencing rationale (post-SPA-migration).

> **Goal:** the developer can iterate on parser bugs against fake bank data with zero risk of contaminating real data, including per-import bulk wipe and row-level multi-select delete. Same code, separate database, separate launch profile, separate port. The boundary is physical — different process, different database, different URL.

### Sub-stages

| # | Sub-stage | Spec / Reference |
|---|---|---|
| 11.5.1 | New `ASPNETCORE_ENVIRONMENT=Sandbox` + `appsettings.Sandbox.json` + `Sandbox` launch profile in `Properties/launchSettings.json` | ADR-0072 § Decision 1 |
| 11.5.2 | `project_ceres_sandbox` database created locally via `createdb`; `scripts/migrate-all.sh` applies migrations to both DBs | ADR-0072 § Decision 1 |
| 11.5.3 | Fail-fast startup check: refuses to boot the `Sandbox` build if connection string `Database=` does not end with `_sandbox` | ADR-0072 § Decision 1 |
| 11.5.4 | `ImportBatch` entity + nullable `ImportBatchId` FK on `Transaction`, `Transfer`, `LiabilityPayment`; `ImportService` mints a batch row per import and stamps every produced row | ADR-0072 § Decision 2 |
| 11.5.5 | Admin SPA route `/admin/import-batches` (list view + detail view) in `ProjectCeres.Client/`; uses shadcn DataTable + TanStack row selection; bulk-wipe and multi-select-delete actions | ADR-0072 § Decision 2 |
| 11.5.6 | Admin API endpoints register conditionally (`Sandbox` + `Development` only); architecture test asserts production 404 | ADR-0072 § Decision 1 |
| 11.5.7 | `IHostedService` seed runner gated to `Sandbox` environment: creates one sandbox user + fixed test accounts (Test Checking EUR / Test Checking USD / Test Savings / Test Credit Card) + default category set on first boot; idempotent | ADR-0072 § Decision 1 |

### Verification checklist

Sandbox environment + database:

- [ ] `dotnet run --launch-profile Sandbox` boots the app against `project_ceres_sandbox` on a port distinct from `Development`
- [ ] `Development` and `Sandbox` builds can run simultaneously without port conflicts
- [ ] `scripts/migrate-all.sh` applies pending migrations to both databases and exits non-zero on any per-database failure
- [ ] Startup fail-fast: launching `Sandbox` with a connection string whose `Database=` value does not match `*_sandbox` throws before `app.Run()` (integration test against deliberate misconfiguration)
- [ ] Seed runner creates the fixed sandbox user + accounts + categories on first sandbox boot; idempotent on subsequent boots
- [ ] Seed runner is NOT registered when `EnvironmentName != "Sandbox"` (architecture test)

`ImportBatch` primitive:

- [ ] `ImportBatch` entity exists with `Id`, `UserId`, `StartedAt`, `SourceFileName`, `ParserVersion?`, `RowsTotal`, `RowsImported`, `Status` columns
- [ ] `Transaction`, `Transfer`, `LiabilityPayment` each carry nullable `ImportBatchId Guid?` FK with index
- [ ] EF query filter on `ImportBatch` registered alongside the other `IUserOwned` filters in `OnModelCreating`
- [ ] `ImportService.ImportAsync` creates the `ImportBatch` row first, sets its `Status = InProgress`, then stamps every produced row with `ImportBatchId`; final status set to `Succeeded` / `Failed` / `Aborted` before commit
- [ ] Existing rows from before Stage 11.5 have `ImportBatchId = null` and continue to read/write normally (back-compat test)
- [ ] Architecture test: every entity created by an `IImporter<T>` implementation has an `ImportBatchId` column

Admin SPA route:

- [ ] `/admin/import-batches` lists every batch in reverse-chronological order for the current user
- [ ] Per-batch "Delete batch" cascades to every produced row + the `ImportBatch` row itself, in a single transaction
- [ ] Detail view `/admin/import-batches/:id` lists the rows the batch produced with a checkbox column
- [ ] "Delete selected" deletes exactly the multi-selected rows in a single transaction; the `ImportBatch` row remains (only its `RowsImported` count is recalculated)
- [ ] Empty-state copy when a batch has no surviving rows (all were deleted individually)
- [ ] Admin route + API endpoints register only when `EnvironmentName` is `Sandbox` or `Development`
- [ ] Integration test: in `Production` environment, `GET /api/admin/import-batches` returns 404 (route does not exist in the route table)

Operational:

- [ ] README's "Setup" section documents creating the sandbox database + running `migrate-all.sh`
- [ ] `appsettings.Sandbox.json` is checked in; secrets (if any) live in User Secrets keyed to the Sandbox environment
- [ ] CI smoke test: spin up a throwaway Postgres, run `migrate-all.sh`, run the seed runner, import a checked-in fake CSV, assert the rows landed, bulk-wipe, assert the rows are gone

---

## Stage 12 — Sessions + Support SPA pages (Batch 5)

**Status: ❌ Pending.** Two SPA pages still pending from Batch 2 that depend on Auth being live.

> **Goal:** users can review and revoke their active sessions, block IPs, and submit support tickets. The pages exist in the SPA at `/settings/sessions` and `/support`.

### Sub-stages

| # | Sub-stage | Reference |
|---|---|---|
| 12.1 | `/settings/sessions` SPA page | `planning-phase3.md` § Sessions + ADR-0019 |
| 12.2 | Per-session revoke action | (above) |
| 12.3 | IP block toggle from session row | (above) |
| 12.4 | `/support` SPA page (ticket form + list) | `planning-phase3.md` § Support ticket system |
| 12.5 | `SupportTicket` entity + service + API endpoints (if not already present) | (above) |
| 12.6 | Admin email notification on new ticket | (above) |
| 12.7 | **Stage 7.5 follow-up.** When the `SupportTicket` table ships, append `SupportTickets` to `ProjectCeres/Common/UserOwnedTables.cs § All` AND add an `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` + `user_isolation` policy to the same migration. The Stage 7.5 parity test (`ProjectCeres.Tests/Integration/Rls/ParityTests.UserOwnedTables_All_matches_pg_policies_user_isolation_set`) will fail the build until both halves land. | Stage 7.5 / ADR-0068 |

### Verification checklist

`/settings/sessions`:

- [ ] Reauthentication-gated route (per `security-model.md` § Reauthentication)
- [ ] Lists all active `UserSession` rows: created-at, last-used, IP, user-agent summary, "this session" indicator on the current row
- [ ] Per-row "Revoke" action calls `DELETE /api/sessions/{id}`
- [ ] Revoking the current session logs the user out + redirects to `/login`
- [ ] Per-row "Block this IP" action: adds the IP to `UserBlockedIp`, revokes all sessions from that IP simultaneously
- [ ] List refreshes optimistically after revoke / block
- [ ] Empty state: "No other active sessions" when only the current row exists

`/support`:

- [ ] Ticket form: subject (required), message (required), priority (Low / Normal / High / Urgent)
- [ ] On submit: ticket created with `Status = Open`, email sent to admin address
- [ ] User's own tickets listed below the form: subject, status, last update, "view" link
- [ ] Status indicators with semantic colours (Open = sky, InProgress = amber, Resolved = emerald, Closed = zinc)
- [ ] No edit / delete (ticket history is immutable)
- [ ] Admin reply mechanism: out of scope for Phase 3; users see "We'll respond by email"

Server side:

- [ ] `SupportTicket` entity exists: `Id`, `UserId`, `Subject`, `Message`, `Status`, `Priority`, `CreatedAt`, `UpdatedAt`
- [ ] Global query filter applies (only owner sees own tickets)
- [ ] Admin can list all tickets via `Admin/` endpoints (per Stage 7 / ADR-0065)
- [ ] Email notification to admin uses `IEmailService` (Stage 8) and the EN/ES templates

Tests:

- [ ] Revoke own session, verify cookie no longer authenticates
- [ ] Block own IP, verify subsequent requests from same IP rejected
- [ ] Submit ticket, verify admin receives email
- [ ] User A cannot view User B's ticket (IDOR)
- [ ] Reauthentication required to access `/settings/sessions`

Stage 6 deferred items (carry-forward from the Stage 6 verification checklist):

- [ ] **Per-session IP enforcement UI** — per-row "anchor this session to its creation IP" toggle on `/settings/sessions`. Server-side enforcement is wired in 6a with `default-off` (each `UserSession` has the field; when enabled, requests from a different IP are rejected). This stage exposes the toggle so users can opt in per session. Confirm the entity field name (`IsIpAnchored` or similar) when wiring; pin with an integration test that flips the toggle on, simulates a request from a different IP, and asserts 401. *Anchor: Stage 6 § UserSession table + token rotation carry-forward.*

Stage 8 deferred items (carry-forward from Stage 8 — security-event email call-sites):

- [ ] **Wire new-session/new-device alert email** — when a new `UserSession` is created from an IP not previously seen for that user, send a notification with IP + UA summary + "this wasn't me" link revoking that session. The session-novelty-detection call site lands here (compare new session IP against the user's prior `UserSession.CreatedFromIp` history; first occurrence → fire email). Opt-out by default-enabled, disable from notification preferences in Settings per `planning-phase3.md`. Add `NewSessionAlert` to `EmailTemplateKey` + EN/ES resx keys (`NewSessionAlert.Subject/BodyText/BodyHtml`). *Anchor: Stage 8 § Transactional templates — New-session alert + Stage 8 § Security event notifications — New-device/session login.*

Responsive (per [`planning-phase3-responsive.md`](planning-phase3-responsive.md) § Surface Inventory):

- [ ] `/settings/sessions` mobile: session rows render as cards (not table); per-row revoke + IP-block actions reachable
- [ ] `/settings/sessions` tablet: cards remain (per the responsive doc — Active sessions list is card-view through tablet, table only on desktop)
- [ ] `/settings/sessions` desktop: standard table layout
- [ ] `/settings/sessions` touch targets: per-row revoke and IP-block buttons each ≥ 44×44px on mobile; session-row card meets the same minimum across its tappable region
- [ ] `/support` mobile: ticket form full-page; ticket list as cards; status badges legible
- [ ] `/support` desktop: form modal or full-page (decided per the form-presentation rule), ticket list as table
- [ ] `/support` touch targets: every form input, submit button, and ticket-row tap target ≥ 44×44px on mobile

---

## Stage 13 — GDPR baseline (Batch 5)

**Status: ❌ Pending.** Legal gate before opening to invited beta testers in the EU. Covers the privacy policy, cookie consent, retention enforcement, full data export, and right-to-erasure flow.

> **Goal:** Project Ceres meets the legal minimum to host EU users per GDPR + AEPD 2024 + EU Accessibility Act. See [`legal.md`](legal.md) for the underlying compliance requirements.

### Sub-stages

| # | Sub-stage | Reference |
|---|---|---|
| 13.1 | Privacy policy text + page | `legal.md` + `security-model.md` § Article 13/14 |
| 13.2 | Cookie consent banner (AEPD 2024 guidelines) | `security-model.md` § Cookie Consent |
| 13.3 | Records of Processing Activities (RoPA) document | `security-model.md` § Article 30 |
| 13.4 | Subprocessor inventory + DPA list | `security-model.md` § Article 28 |
| 13.5 | Data retention policy enforced | `security-model.md` § Data Retention and Deletion Policy |
| 13.6 | Audit-log auto-purge job (6 months) | `planning-phase3.md` § Audit logging + ADR-0067 |
| 13.7 | Failed-login retention purge job | (above) |
| 13.8 | Full ZIP data export — async background job | `planning-phase3.md` § Full data export |
| 13.9 | Right-to-erasure flow | `security-model.md` § Article 30 + § Data Retention |
| 13.10 | Breach notification runbook | `security-model.md` § Article 33/34 |
| 13.11 | DPIA (Data Protection Impact Assessment) document | `security-model.md` § Article 35 |

### Verification checklist

Privacy policy + cookie consent:

- [ ] `/privacy` and `/legal` routes render the policy in EN + ES
- [ ] Policy describes every data type collected, legal basis, retention period, sharing (subprocessors), user rights
- [ ] Cookie consent banner appears on first visit; choice persists in a non-tracking cookie
- [ ] Banner offers granular choice: necessary (always on), analytics (opt-in), preferences (opt-in) — per AEPD 2024
- [ ] Reject-all is as easy as accept-all (single click, equally prominent)
- [ ] No tracking / analytics scripts load before consent
- [ ] Consent is revocable from a footer link on every page
- [ ] AEPD compliance verified against the 2024 guidelines

RoPA + DPA:

- [ ] `legal.md` § RoPA fully populated for every data type
- [ ] Subprocessor list includes: hosting provider, email provider, any analytics, any monitoring (Sentry / similar)
- [ ] DPA signed with each subprocessor; copies stored in secrets/legal repo
- [ ] DPA terms reflect GDPR Art. 28 requirements (subprocessor controls, breach notification, data return on termination)

Retention policy:

- [ ] Audit log: 6-month auto-purge cron job registered with the background runtime; runs daily, deletes `AuditLog` rows older than 6 months
- [ ] Failed-login records: same auto-purge, e.g., 90 days
- [ ] Soft-deleted SavedReports: hard-deleted after 90 days
- [ ] Soft-deleted CsvImportProfiles: hard-deleted after 90 days (already implemented; verify)
- [ ] Inactive user records: archived after defined period per `security-model.md` § Data Retention
- [ ] Each retention rule documented in the policy + verifiable in code (test that runs the purge against fixture data)

Full data export:

- [ ] Endpoint: `POST /api/me/export` returns 202 Accepted with a job id
- [ ] Background job (registered with `IUserJobRunner.EnterAs`) generates the ZIP
- [ ] ZIP contents: one CSV per entity (accounts, transactions, transfers, liability_payments, budgets, category_budgets, categories, recurring_transactions, saved_reports, support_tickets, settings); one folder per attachment type with original files
- [ ] Each CSV uses UTF-8 BOM for Excel compatibility
- [ ] Generation logs an `AuditLog` entry
- [ ] When done, sends email with authenticated download link — adds `GdprExportReady` to `EmailTemplateKey` + EN/ES resx keys (`GdprExportReady.Subject/BodyText/BodyHtml`), then calls `IEmailComposer.Compose(...)` from the export-job completion handler. Resx keys do NOT yet exist (deferred from Stage 8 because the call site lands here).
- [ ] Download link 24-hour expiry; tied to a single signed token, single-use
- [ ] Rate limit: max 1 export request / 24 hours / user
- [ ] Synchronous fallback rejected (see `planning-phase3.md` warning about HTTP worker exhaustion)

Right-to-erasure:

- [ ] `/settings/account/erasure` page describes what will be deleted, when, what is retained (legal-basis-required records like audit log), and confirms intent
- [ ] Reauthentication-gated initiation
- [ ] Audit log entry created at request time
- [ ] Confirmation email sent — adds `GdprErasureInitiated` to `EmailTemplateKey` + EN/ES resx keys (`GdprErasureInitiated.Subject/BodyText/BodyHtml`), then calls `IEmailComposer.Compose(...)` from the erasure-request handler. Resx keys do NOT yet exist (deferred from Stage 8 because the call site lands here).
- [ ] Erasure runs as background job (`IUserScope.EnterAs`); deletes all user-owned data per the documented retention policy
- [ ] Audit-log record of the erasure ITSELF retained (per legal basis) but pseudonymized (user id hashed via `UserRef`, see Stage 15)
- [ ] After erasure: account row marked `ErasedAt`; no future logins possible; email address freed for re-registration after a documented cooling period
- [ ] Test: User A initiates erasure → background job completes → no User A data remains in `accounts`, `transactions`, etc.; only audit-log records persist with pseudonymized identifier

Breach notification + DPIA:

- [ ] `legal.md` or `security-model.md` § Breach Notification Runbook documents: who decides, who notifies (DPA + affected users), what content, what timeline (72 hours under GDPR)
- [ ] DPIA completed for Phase 3 launch — covers high-risk processing (financial data + identity data + EU residents)
- [ ] DPIA outcome filed with DPO if appointed

Localization:

- [ ] Privacy policy + cookie consent banner translated EN + ES
- [ ] Erasure confirmation email template EN + ES — see "Right-to-erasure → Confirmation email sent" above; the resx keys are added in this stage (deferred from Stage 8 because the call site is here).
- [ ] Data export ready email template EN + ES — see "Full data export → When done, sends email" above; the resx keys are added in this stage (deferred from Stage 8 because the call site is here).

Responsive (per [`planning-phase3-responsive.md`](planning-phase3-responsive.md)):

- [ ] Cookie consent banner mobile: "accept all" and "reject all" buttons equally prominent, side-by-side or stacked, each ≥ 44×44px on mobile per the AEPD-2024 equal-prominence requirement
- [ ] Cookie consent banner does not obscure critical content at any breakpoint; granular-choice controls reachable without horizontal scroll
- [ ] `/privacy` and `/legal` pages render readably on 375px (no horizontal overflow; line length comfortable on mobile)
- [ ] Erasure confirmation dialog touch targets: confirm + cancel buttons ≥ 44×44px on mobile; destructive button visually distinct without relying on color alone (per WCAG 1.4.1)
- [ ] Data export request flow on mobile: `/settings/account` export button ≥ 44×44px; the post-202 confirmation message wraps cleanly

---

## Stage 14 — HTTP security headers + CORS (Batch 5)

**Status: ❌ Pending.** Operational security; can land anytime after Stage 6.

> **Goal:** every response carries the standard hardening headers, CORS is configured to allow only the SPA origin, and reverse-proxy forwarding is hardened against IP spoofing.

### Sub-stages

| # | Sub-stage | Reference |
|---|---|---|
| 14.1 | Content Security Policy (CSP) | `security-model.md` § HTTP Security Headers |
| 14.2 | `X-Content-Type-Options: nosniff` | (above) |
| 14.3 | `X-Frame-Options: DENY` | (above) |
| 14.4 | `Referrer-Policy: strict-origin-when-cross-origin` | (above) |
| 14.5 | HSTS once HTTPS enforced | (above) |
| 14.6 | CORS whitelist for the SPA origin only | `planning-phase3.md` § CORS policy |
| 14.7 | Forwarded-headers middleware with `KnownProxies` | `security-model.md` § Reverse Proxy + `planning-phase3.md` § Forwarded headers |
| 14.8 | Authenticated-response cache headers | `security-model.md` § Authenticated Response Cache Headers |

### Verification checklist

CSP:

- [ ] `Content-Security-Policy` header present on every HTML response
- [ ] No inline scripts (`'unsafe-inline'`) — Phase 2 React build is bundled
- [ ] Sources whitelisted: `'self'`, the email provider domain (if it serves images in emails clicked through), the analytics provider (if any, after consent)
- [ ] `report-uri` or `report-to` directive configured to a logging endpoint (catches violations early)
- [ ] CSP tested in browser DevTools: violations log nothing on a clean page render

Other security headers:

- [ ] `X-Content-Type-Options: nosniff` on every response
- [ ] `X-Frame-Options: DENY` on every response (no iframe embedding)
- [ ] `Referrer-Policy: strict-origin-when-cross-origin`
- [ ] `Permissions-Policy` configured (e.g., `camera=(), microphone=(), geolocation=()`)
- [ ] HSTS: `Strict-Transport-Security: max-age=31536000; includeSubDomains; preload` (once HTTPS is enforced; not before)
- [ ] Verified with `securityheaders.com` or equivalent: A+ rating

CORS:

- [ ] In dev: allowed origin `http://localhost:5173`
- [ ] In prod: allowed origin is the production frontend origin only
- [ ] `AllowAnyOrigin()` NEVER combined with `AllowCredentials()` — verified
- [ ] CORS preflight (`OPTIONS`) handled correctly for all API routes
- [ ] Test: a request from a non-whitelisted origin is rejected at the CORS layer

Forwarded-headers:

- [ ] `app.UseForwardedHeaders()` registered BEFORE all other middleware
- [ ] `ForwardedHeadersOptions` configured: `XForwardedFor | XForwardedProto`
- [ ] `KnownProxies` populated with the actual reverse-proxy IP(s); empty list rejected (security-model warning: "restrict trusted proxy addresses to prevent IP spoofing")
- [ ] Test: a request with a spoofed `X-Forwarded-For` from a non-trusted source does NOT update `HttpContext.Connection.RemoteIpAddress`

Cache headers:

- [ ] Authenticated API responses: `Cache-Control: no-store` (per `security-model.md` § Authenticated Response Cache Headers)
- [ ] Static SPA assets: `Cache-Control: public, max-age=...` with content-hashed filenames for invalidation
- [ ] No authenticated endpoints leak into shared caches (test by hitting the same endpoint with two different sessions and confirming distinct responses)

---

## Stage 15 — Identity masking, HMAC `UserRef` (Batch 5)

**Status: ❌ Pending.** Pre-launch breach mitigation. Lands after Stage 7 (multi-tenancy cutover) so every user-owned table is in place to add the column to.

> **Goal:** every user-owned data row stores `UserRef = HMAC-SHA256(USER_REF_SECRET, userId)` instead of (or alongside) the raw `UserId`. A database dump cannot link financial records to real user identities without the server secret.

### Sub-stages

| # | Sub-stage | Reference |
|---|---|---|
| 15.1 | Add `UserRef varchar(64)` column to every user-owned entity | `security-model.md` § API Authentication and Identity Masking → Layer 2 |
| 15.2 | `IUserRefService` that computes `HMAC-SHA256(USER_REF_SECRET, userId)` | (above) |
| 15.3 | Backfill migration to populate `UserRef` for existing rows | (above) |
| 15.4 | Updated insert path: every row write computes and stores `UserRef` | (above) |
| 15.5 | `USER_REF_SECRET` in secret store with rotation procedure | `security-model.md` § Secrets Rotation Procedures |
| 15.6 | Decision on retaining or dropping raw `UserId` | (recommend retaining for query performance; document) |

### Verification checklist

- [ ] `USER_REF_SECRET` is a cryptographically random 256-bit value
- [ ] Stored in environment variable / secret store; never in source control
- [ ] `IUserRefService.Compute(userId)` returns a deterministic 64-char hex string (HMAC-SHA256)
- [ ] Every user-owned entity has a `UserRef varchar(64) NOT NULL` column with an index
- [ ] Backfill migration ran successfully on all existing rows post-cutover
- [ ] Insert path: `UserRef` populated automatically by EF Core save interceptor or service base class
- [ ] Test: a row inserted via service has matching `UserId` and `UserRef` (where `UserRef` = HMAC of `UserId` under the secret)
- [ ] Rotation procedure documented: how to add a new secret, dual-hash period, retire old secret
- [ ] Test rotation: with both old and new secrets active, both lookups (by old `UserRef` and new `UserRef`) succeed during transition
- [ ] `Per-tenant payload encryption` (Phase 4) explicitly NOT in scope for Phase 3 (per `security-model.md` § Layer 3)

---

## Stage 15.5 — Onboarding wizard (Batch 5)

**Status: ❌ Pending.** Was Stage 10 (Batch 3f); moved here 2026-05-17 because Phase 3 accumulated too many polish + wiring items ahead of it. Lands immediately before the hosting cut so onboarding ships against a fully-polished, security-headers-on, identity-masked surface.

> **Goal:** five-step wizard at `/onboarding` that takes a freshly-registered user from "I just created an account" to "I see my net worth on the dashboard." Full-screen stepper, distinct from the standard app shell. See [ADR-0053](decisions/ADR-0053-guided-onboarding-deferred.md).

> **Decision gate before Step 1 design:** the timezone strategy is owed at this stage. ADR-0009 deferred the Phase 3 TZ decision to "before launch"; the Preferences screen here is the natural place to either add a TZ field (option B — per-user IANA) or commit to not adding one (option C — client passes its `today` in requests; recommended). See `planning-phase3.md` § Open Questions → "Timezone handling" for the 28-site audit, three options, and rationale. New ADR (next free: 0071 after CI/CD = 0069 and Playwright = 0070) supersedes ADR-0009's launch-gate follow-up at decision time.

### Sub-stages

| # | Sub-stage | Reference |
|---|---|---|
| 15.5.1 | `/onboarding` route + full-screen stepper layout | `planning-phase3.md` § 11 Onboarding flow design |
| 15.5.2 | Step 1 — Preferences (language, country, default currency, number format, date format; **timezone field gated on TZ-strategy ADR**) | (above) + `planning-phase3.md` § Localization + § Open Questions (Timezone handling) |
| 15.5.3 | Step 2 — First asset account | (above) |
| 15.5.4 | Step 3 — First liability (optional, skippable) | (above) |
| 15.5.5 | Step 4 — Opening balance | (above) + ADR-0010 (opening balance as auto-created transaction) |
| 15.5.6 | Step 5 — Immediate net worth display + "Go to dashboard" CTA | (above) |

### Verification checklist

Layout + flow:

- [ ] `/onboarding` is a top-level route OUTSIDE `AppLayout` (no sidebar, no top bar)
- [ ] Full-screen stepper with progress indicator at top showing 5 numbered steps
- [ ] First-run users (no `OnboardingCompletedAt` on Settings) are redirected to `/onboarding` after login until completion
- [ ] Completion flag persisted to per-user Settings; subsequent logins go straight to dashboard
- [ ] User can navigate back to a previous step; data from later steps is preserved if revisited
- [ ] Browser back button triggers an "are you sure you want to leave?" guard if onboarding is partial

Step 1 — Preferences:

- [ ] **Timezone strategy ADR landed before this step ships** (`planning-phase3.md` § Open Questions → Timezone handling). Outcome is one of: (A) sweep 28 server-side `DateTime.Today` sites to UTC, no Preferences field; (B) add TimeZoneId column + IANA picker to this step + IUserTimeZone service; (C — recommended) endpoints accept `today` query parameter from the client, no Preferences field, no schema change. Choice locks the field set below.
- [ ] Five fields grouped: Language (EN/ES), Country (ES, US, GB, CO, AR, VE, Other), Default Currency (EUR, USD, GBP, COP, ARS, VED), Number format (`comma_decimal` / `dot_decimal`), Date format (`DD/MM/YYYY` / `MM/DD/YYYY` / `YYYY-MM-DD`)
- [ ] All fields pre-filled from `Accept-Language` detection (`security-model.md` § Localization, `planning-phase3.md`)
- [ ] All fields independently overridable — no cascade (changing country does NOT change currency)
- [ ] Live format preview displayed: e.g., `€1.234,56 · 28/04/2026`
- [ ] Saving step 1 applies the language IMMEDIATELY (subsequent steps render in chosen language)
- [ ] Saved values written to per-user `Settings` row

Step 2 — First asset account:

- [ ] Fields: name (required), `AccountType` (select from asset types: Checking, Savings, Cash, Investment), currency (defaults to step 1's `DefaultCurrency`), opening balance (defaults to 0)
- [ ] Validation matches the regular Accounts form (Stage 3.3)
- [ ] On save: account created via the same `AccountService` used by `/accounts/new`
- [ ] At least one asset account is required to proceed; "Add another" button repeatable

Step 3 — First liability (optional):

- [ ] Skippable via "I don't have any liabilities" option
- [ ] If proceeding: same form shape as Account Create with liability types (Credit Card, Loan, Mortgage, Other)
- [ ] Conditional Asset/Liability fields work per Stage 3.3 (interest rate normalization, repayment type)

Step 4 — Opening balance:

- [ ] For each account created in Steps 2 + 3, prompts for the opening balance and date
- [ ] Defaults the date to today (per ADR-0010 § Opening balance cutover UX)
- [ ] If user changes the date, shows the explanation about how moving the date affects balance + history
- [ ] Creates an `IsSystem` opening-balance Transaction for each account

Step 5 — Net worth display:

- [ ] Computed across all accounts created in steps 2–4
- [ ] Shows per-currency breakdown if multiple currencies (matching the dashboard rule)
- [ ] "Go to dashboard" CTA marks `OnboardingCompletedAt` and redirects to `/`

Accessibility (per `planning-phase3.md` § 8 Accessibility baseline):

- [ ] Focus moves to the new step's `<h2>` heading on transition (`tabindex="-1"` + `.focus()`)
- [ ] `aria-live="polite"` step announcer announces step transitions
- [ ] `aria-current="step"` on the current step indicator
- [ ] Form-error focus management on validation failures
- [ ] vitest-axe runs against every onboarding step with zero violations

Localization:

- [ ] Every string keyed; EN + ES complete
- [ ] "Account type" labels seeded with localized names (e.g., Spanish for "Checking" → "Cuenta corriente")
- [ ] System category names available in chosen language for any defaults

Tests required before Stage 16 begins:

- [ ] Happy-path integration test: register → verify email → enroll TOTP → onboarding 5 steps → land on dashboard with correct net worth
- [ ] Skipped-liability path test (Step 3 skipped)
- [ ] Multi-asset-account path test (two accounts created in Step 2)
- [ ] Language change in Step 1 propagates to subsequent steps
- [ ] Onboarding completion flag persists; second login skips onboarding

Responsive (per [`planning-phase3-responsive.md`](planning-phase3-responsive.md) § Surface Inventory — full-screen stepper on every tier):

- [ ] Mobile (375px): stepper progress indicator visible without scrolling; step content fills viewport; the live format preview in Step 1 (`€1.234,56 · 28/04/2026`) wraps cleanly
- [ ] Tablet + desktop: stepper centered with comfortable max-width; same step content, just constrained
- [ ] Step transitions don't trigger horizontal overflow at any width
- [ ] All form inputs meet 44×44px touch-target minimum on mobile
- [ ] Per-account opening-balance step on mobile lists each account vertically (no horizontal table on small screens)
- [ ] Step 5 net-worth display per-currency breakdown wraps cleanly on mobile when 2+ currencies present

---

## Stage 16 — Hosting + ops (Batch 5)

**Status: ❌ Pending.** Final stage before public beta. Operational, not application-code.

> **Goal:** Project Ceres runs on a real server, behind HTTPS, with database TLS, automated backups, monitoring, and a documented deployment + rollback procedure.

### Sub-stages

| # | Sub-stage | Reference |
|---|---|---|
| 16.1 | Pick host (small VPS / managed PaaS) | `planning.md` § Open Questions: Hosting platform |
| 16.2 | HTTPS termination + auto-renewal (Let's Encrypt or provider-managed) | `security-model.md` § HTTPS and TLS |
| 16.3 | PostgreSQL TLS connection enforced | `security-model.md` § Database Connection TLS |
| 16.4 | Reverse proxy with `KnownProxies` set | `security-model.md` § Reverse Proxy |
| 16.5 | Automated database backups + encryption + retention | `security-model.md` § Backup Security |
| 16.6 | Backup restoration testing (quarterly) | `security-model.md` § Restoration Testing |
| 16.7 | Backup encryption-key rotation procedure | `security-model.md` § Backup Encryption Key Rotation |
| 16.8 | CI dependency vulnerability scanning | `security-model.md` § Dependency Scanning |
| 16.9 | CI secrets scanning | `security-model.md` § Secrets Scanning |
| 16.10 | Production migration strategy (`dotnet ef database update` vs. CI step vs. reviewed SQL) | `planning.md` § Open Questions |
| 16.11 | CD pipeline (trigger, staging, migration step, rollback plan) | `planning.md` § Open Questions: CD strategy |
| 16.12 | Monitoring + alerting (uptime, error rate, certificate expiry) | (operational) |
| 16.13 | Container / runtime hardening | `security-model.md` § Container / Runtime Hardening |
| 16.14 | Data Protection key persistence + rotation | `security-model.md` § TOTP Secrets + § Secrets Rotation Procedures + Stage 6 carry-forward |
| 16.15 | **Stage 7.5 follow-up.** Production database setup creates `ceres_app`, `ceres_admin`, `ceres_migrator` per `scripts/setup-postgres-roles.sql`. Only `ceres_app` (NOBYPASSRLS) and `ceres_admin` (BYPASSRLS) credentials are deployed with the application; `ceres_migrator` (DDL + BYPASSRLS) credentials are held by the deploy operator and used only when applying migrations. The privilege-leak startup check in `Program.cs` refuses to start if the runtime `ApplicationConnection` is wired to a privileged role — confirm it fires correctly under the production deployment configuration. | Stage 7.5 / ADR-0068 |
| 16.16 | **Stage 9.11 follow-up.** Playwright E2E suite (shipped in Stage 9.11) wired into `.github/workflows/ci.yml`: `npx playwright install --with-deps` cached via `actions/cache`; sharded across runner instances; trace + HTML report uploaded as workflow artefact on failure. Suite runs on every PR + on `main`. | [ADR-0071](decisions/ADR-0071-e2e-testing-on-playwright.md) § Implementation gates / Stage 9.11 |

### Verification checklist

HTTPS + TLS:

- [ ] Production traffic on port 443 with valid TLS certificate
- [ ] Port 80 redirects to 443
- [ ] Certificate auto-renewal verified (renewal job runs and succeeds in dry-run)
- [ ] TLS version: 1.2 minimum, 1.3 preferred (per `security-model.md` § HTTPS and TLS)
- [ ] Cipher suites: only modern ones — no RC4, 3DES, MD5
- [ ] Server tested with ssllabs.com: A or A+ rating
- [ ] HSTS header present once HTTPS verified clean

Database TLS:

- [ ] PostgreSQL connection string uses `Sslmode=Require` minimum, `Sslmode=VerifyFull` ideally
- [ ] CA certificate pinned where possible
- [ ] Test: a non-TLS connection attempt is rejected by the server

Reverse proxy:

- [ ] Reverse proxy (nginx / Caddy / provider) terminates TLS
- [ ] Forwards to .NET app via loopback or unix socket
- [ ] Forwards `X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host`
- [ ] Application's `ForwardedHeadersOptions.KnownProxies` matches the proxy's actual IP

Backups:

- [ ] Automated nightly `pg_dump` to encrypted storage (cloud blob with encryption-at-rest)
- [ ] Backup retention: 30 daily + 12 monthly minimum (per `security-model.md` § Retention Policy)
- [ ] Backup encryption key separate from database credentials, stored in secrets store
- [ ] Quarterly restoration test: actually restore a backup to a staging instance and verify integrity (per `security-model.md` § Restoration Testing)
- [ ] Backup access controls: only deployment role + DBA can read; logged

Migrations:

- [ ] Migration strategy documented (one of the three options in `planning.md` § Open Questions)
- [ ] Migrations run as a deploy step, not at app boot (boot-time migrations risk schema drift between instances)
- [ ] Migration rollback procedure documented for non-trivial migrations
- [ ] Pre-migration backup snapshot is part of the deploy runbook (especially for Stage 7's sentinel migration)

CI/CD:

- [ ] CI provider chosen (likely GitHub Actions per `planning.md`)
- [ ] CI runs: build, all server tests, all client tests, vulnerability scan (`dotnet list package --vulnerable`), secret scan
- [ ] CI fails the build on any test failure or security finding
- [ ] **Playwright E2E suite (shipped in Stage 9.11) wired into CI** per Stage 16.16 — `npx playwright install --with-deps` cached via `actions/cache`; sharded across runner instances; trace + HTML report uploaded as workflow artefact on failure. *Anchor: [ADR-0071](decisions/ADR-0071-e2e-testing-on-playwright.md) § Implementation gates.*
- [ ] CD pipeline runs: build artifact, deploy to staging, run integration smoke tests, deploy to prod (manual approval gate)
- [ ] Rollback plan documented: how to revert the last deploy in under 10 minutes

Container / runtime hardening (per `security-model.md` § Container / Runtime Hardening):

- [ ] Application runs as non-root user
- [ ] Minimal base image (Alpine or distroless)
- [ ] Read-only file system except for the writable upload/cache paths
- [ ] No SSH access to the application host (deploys via CI)
- [ ] Resource limits (CPU, memory) configured to prevent runaway processes

Monitoring + observability:

- [ ] Uptime monitor (every 5 min) hitting `/api/health` (or equivalent unauthenticated health endpoint)
- [ ] Error monitoring (Sentry or equivalent) capturing unhandled exceptions
- [ ] Certificate expiry monitor (alerts 30 days before expiry)
- [ ] Database disk-space monitor (alerts at 80%)
- [ ] Logs centralized (provider-agnostic; avoid storing logs only on the app host)

Pre-launch dry run:

- [ ] Full integration test suite passes against staging
- [ ] Cross-tenant IDOR test suite green (Stage 7)
- [ ] Email delivery tested end-to-end against the production provider config
- [ ] Publish SPF / DKIM / DMARC records on the registered sending domain per [`docs/runbooks/email-dns-setup.md`](runbooks/email-dns-setup.md). Advance DMARC `p=none → p=quarantine → p=reject` over ~90 days.
- [ ] CSP violations log empty after a full SPA browse-through
- [ ] Penetration testing scheduled or completed (per `security-model.md` § Responsible Disclosure and Penetration Testing)

Data Protection key storage (Stage 6 carry-forward):

- [ ] **ASP.NET Core Data Protection keys persisted to a durable location**, not the default ephemeral filesystem. Without this, every container restart rotates the keys silently and every encrypted-at-rest payload tied to those keys (`TotpReplayEntry`-style ephemeral payloads, anything signed by the antiforgery system, anything wrapped by `IDataProtector`) becomes unreadable after a deploy. Options on a single-VPS deployment: bind-mounted host directory (`PersistKeysToFileSystem`) with restricted permissions; or PostgreSQL-backed key ring via `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`. Confirm choice in `planning.md` § Open Questions if not yet locked.
- [ ] **Key ring rotation procedure documented in `security-model.md` § Secrets Rotation Procedures** — Data Protection auto-rotates the active key every 90 days by default; the rotation procedure documents what to verify after a rotation lands (no decryption failures in logs for 24 h; antiforgery still working across all sessions).
- [ ] **TOTP seed encryption verified end-to-end**: enrol a user, restart the application, log in with the same TOTP — must still verify. Pins the "Data Protection wiring works AND keys persisted across restart" invariant the Stage 6 verification deferred here.

---

## Master pre-launch verification checklist

> Final gate before opening Project Ceres to invited beta testers. Every item must be `[x]` or have a documented exception. This is the consolidated view across stages — if a stage above is incomplete, the parallel item here is incomplete too.

### Authentication + identity

- [ ] All Stage 6 verification items green
- [ ] All Stage 9 verification items green
- [ ] All Stage 15.5 verification items green
- [ ] First-user registration tested end-to-end on staging: register → verify email → enroll TOTP + download backup codes → onboarding 5 steps → land on dashboard
- [ ] Lockout tested: 10 failed attempts → email arrives with unlock link → unlock works
- [ ] Password reset tested: request → email → click → enter TOTP → set new password → all sessions revoked
- [ ] Email-change tested: dual-address verification, revoke link works for 7 days
- [ ] Reauthentication tested for every sensitive operation in `security-model.md`'s list

### Multi-tenancy + data isolation

- [ ] All Stage 7 verification items green
- [ ] Sentinel-to-real-user migration tested in staging with a fresh database snapshot
- [ ] Architecture test gating `IgnoreQueryFilters()` to `Admin/` is in CI and green
- [ ] Full IDOR test suite green (15+ tests covering reads, lists, aggregates, mutations, attachments)
- [ ] Manual cross-tenant audit: dump a single user's rows from staging and verify zero foreign-user rows present
- [ ] **IDOR coverage backlog (moved from Stage 7 close-out, 2026-05-12):**
  - [ ] User A cannot read User B's transfers — Stage 7 Task 11 deferred this because the test requires a two-account-per-user setup (Transfer source + destination must share currency and both belong to the same user); the global query filter on `Movement` covers it at the data layer, but the API-level 404 assertion is missing.
  - [ ] User A cannot read User B's saved reports — endpoint not yet implemented (no `SavedReportsApiController` exists). Pin once the saved-reports SPA page lands. Data-layer scoping is already pinned via `ArchitectureTests.Every_user_owned_entity_carries_a_global_query_filter`.
  - [ ] User A cannot download User B's transaction attachments — service-layer parent scoping returns the parent's 404 first; the API-level assertion still needs to be written.
  - [ ] User A cannot download User B's transfer attachments — same.
  - [ ] User A cannot upload an attachment against User B's transaction — same.

### Email + notifications

- [ ] All Stage 8 verification items green
- [ ] All 12 EN + ES templates tested in browser staging by sending a real email and viewing it
- [ ] All 9 mandatory security-event emails fire correctly
- [ ] DKIM signature present and validates externally (mxtoolbox.com)
- [ ] DMARC at `p=none` minimum at launch; aggregate reports flowing to a real inbox

### GDPR + legal

- [ ] All Stage 13 verification items green
- [ ] Privacy policy in EN + ES, accessible without auth, linked from every footer
- [ ] Cookie consent banner appears on first visit; AEPD-2024 compliant; tested with reject-all
- [ ] DPIA completed and filed
- [ ] DPA signed with every subprocessor
- [ ] Breach notification runbook documented and rehearsed (table-top exercise)
- [ ] Right-to-erasure tested end-to-end on staging (non-real user)
- [ ] Full data export tested end-to-end on staging; ZIP opens cleanly in Excel + a CSV viewer

### Security headers + CORS + reverse proxy

- [ ] All Stage 14 verification items green
- [ ] securityheaders.com rating: A or A+
- [ ] ssllabs.com rating: A or A+
- [ ] CSP violations zero on a clean browse-through
- [ ] CORS rejects non-whitelisted origins
- [ ] Forwarded-headers spoofing test: spoofed `X-Forwarded-For` from non-trusted source does NOT update remote IP

### Identity masking

- [ ] All Stage 15 verification items green
- [ ] `UserRef` populated on every existing row + new inserts
- [ ] `USER_REF_SECRET` in production secret store
- [ ] Rotation procedure documented

### Hosting + ops

- [ ] All Stage 16 verification items green
- [ ] HTTPS auto-renewal verified
- [ ] Database backups encrypted, retained per policy, restoration tested
- [ ] CI runs on every push, gates merges to main
- [ ] CD pipeline runs cleanly on a non-launch deploy (smoke deploy)
- [ ] Rollback procedure tested
- [ ] Uptime + error monitoring configured with real alerting destinations
- [ ] Penetration test report reviewed (if completed pre-launch)

### Localization

- [ ] EN + ES coverage: 100% of UI strings, system-seeded category names, transactional emails, generated reports
- [ ] Manual smoke test in ES across every page
- [ ] No untranslated copy visible when toggling to ES

### Accessibility (EU Accessibility Act / EN 301 549 / WCAG 2.1 AA)

- [ ] All `planning-phase3.md § 8 Accessibility baseline` items green
- [ ] vitest-axe runs across every page in CI; zero violations
- [ ] Keyboard navigation full coverage on every page
- [ ] `prefers-reduced-motion` respected (covered by Stage 5.5 T1.4)
- [ ] Screen-reader smoke test on critical flows (login, onboarding, dashboard, transaction create)
- [ ] Accessibility statement at `/accesibilidad` (per `planning-phase3.md § 8`)
- [ ] Accessibility feedback inbox documented in the statement
- [ ] Optional: third-party audit completed (recommended budget EUR 3,000–8,000 per `planning-resolved.md`)

### Phase 2 carry-forward

- [ ] Recurring reminder email push delivers correctly (deferred from Phase 2 per ADR-0044, lands with Stage 8)
- [ ] Settings → Sessions + Support pages live (Stage 12)

### Responsive (per [`planning-phase3-responsive.md`](planning-phase3-responsive.md))

- [ ] Every shipped SPA surface verified at three tiers: `mobile` (< 640px), `tablet` (640–1023px), `desktop` (≥ 1024px)
- [ ] Touch-target audit: every interactive element ≥ 44×44px on mobile
- [ ] No horizontal overflow at or above 320px on any page
- [ ] Responsive open questions resolved or explicitly deferred:
  - [ ] Card field priority per table surface (Transactions, Movements, Transfers, Accounts, Categories, Recurring, Reports tables)
  - [ ] Mobile row-action pattern (inline icon button / long-press menu / swipe-to-reveal — pick one and apply consistently)
  - [ ] Tablet form presentation (kept full-page, switched to bottom sheet, or switched to modal — pick one)
  - [ ] Chart minimum height per chart type on a 375px screen
  - [ ] Dashboard tablet grid (which cards span full width vs. 2-col)
- [ ] Device matrix sweep on staging: iPhone SE (375px), iPhone 14 (390px), iPad (768px), 13" laptop (1280px), 27" monitor (≥ 1920px)

### Documentation hygiene

- [ ] Every shipped stage marked done in this roadmap
- [ ] `planning-phase3.md`, `planning-resolved.md`, `planning.md` all reflect post-launch state (no stale "pending" items)
- [ ] All ADRs cross-referenced from the relevant stages
- [ ] CHANGELOG.md updated for the launch release

---

> **When the master checklist is fully green, Phase 3 is shippable. Phase 4 begins with: per-tenant payload encryption, social login (one provider — likely Google), JWT issuance for mobile, and the deferred items from `docs/planning-future.md`.** (PostgreSQL Row-Level Security was originally on this list but moved into Phase 3 as Stage 7.5 by ADR-0068.)



