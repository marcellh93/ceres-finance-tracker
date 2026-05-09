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
9. [Stage 8 — Email service + email security (Batch 3d)](#stage-8--email-service--email-security-batch-3d)
10. [Stage 9 — Auth SPA pages (Batch 3e)](#stage-9--auth-spa-pages-batch-3e)
11. [Stage 10 — Onboarding wizard (Batch 3f)](#stage-10--onboarding-wizard-batch-3f)
12. [Stage 11 — Razor + URL cleanup (Batch 4)](#stage-11--razor--url-cleanup-batch-4)
13. [Stage 12 — Sessions + Support SPA pages (Batch 5)](#stage-12--sessions--support-spa-pages-batch-5)
14. [Stage 13 — GDPR baseline (Batch 5)](#stage-13--gdpr-baseline-batch-5)
15. [Stage 14 — HTTP security headers + CORS (Batch 5)](#stage-14--http-security-headers--cors-batch-5)
16. [Stage 15 — Identity masking, HMAC `UserRef` (Batch 5)](#stage-15--identity-masking-hmac-userref-batch-5)
17. [Stage 16 — Hosting + ops (Batch 5)](#stage-16--hosting--ops-batch-5)
18. [Master pre-launch verification checklist](#master-pre-launch-verification-checklist)

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

**Status: ❌ Pending.** Server-side plumbing for real authentication. Lands before Stage 7 (multi-tenancy cutover) so the cutover has a real `AspNetUsers` table to remap onto.

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

### Verification checklist

ASP.NET Identity hardening:

- [ ] `options.Lockout.MaxFailedAccessAttempts = 10`
- [ ] `options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15)`
- [ ] `options.Lockout.AllowedForNewUsers = true`
- [ ] `options.User.RequireUniqueEmail = true`
- [ ] `options.SignIn.RequireConfirmedEmail = true`
- [ ] `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.FromMinutes(5)` configured (catches revoked sessions within 5 min)

Password handling:

- [ ] Default `IPasswordHasher<TUser>` replaced with Argon2id implementation pinned to `m=19456, t=2, p=1`
- [ ] Password policy: minimum 8 characters, no maximum below 64 (NIST SP 800-63B)
- [ ] No mandatory complexity rules; breached-password check via Have I Been Pwned API or local top-N list
- [ ] Password reset endpoints always run Argon2id hash (against dummy if user not found) — constant-time enumeration prevention
- [ ] Login endpoint always runs Argon2id hash (same defence)

`UserSession` table + token rotation:

- [ ] `UserSession` entity exists with: `Id`, `UserId`, `TokenHash`, `IpCreatedAt`, `UserAgent`, `CreatedAt`, `LastUsedAt`, `RevokedAt nullable`, `IsPersistent bool`
- [ ] Session token regenerated immediately after login (session-fixation prevention)
- [ ] Logout marks `RevokedAt`; the cookie is cleared; subsequent requests with the cookie are rejected
- [ ] Persistent ("remember me") sessions: long-lived token in HttpOnly cookie, hash stored in DB, **rotated on each use** (issue new token, invalidate old)
- [ ] Per-session IP enforcement honoured (each session anchored to its creation IP when toggle is on)
- [ ] Per-user IP block list (`UserBlockedIp`) revokes all sessions from that IP on add

TOTP:

- [ ] TOTP seed generated with cryptographically secure RNG
- [ ] TOTP seed stored encrypted at rest via ASP.NET Core Data Protection (`IDataProtector`)
- [ ] Replay-prevention table is **persistent** (database or Redis), not in-memory — survives application restart
- [ ] Replay records auto-purged after 2 minutes
- [ ] Backup codes hashed with Argon2id (not plaintext) — single-use, regeneration invalidates all previous codes
- [ ] TOTP enrolment is mandatory; 24-hour grace period for first login; cannot be skipped
- [ ] Backup-code use during lockout is honoured (lockout protects against password guessing, not TOTP abuse)
- [ ] No SMS option exposed (SIM-swap vulnerability)

CSRF:

- [ ] `IAntiforgery` middleware registered globally
- [ ] All `POST`/`PUT`/`PATCH`/`DELETE` API endpoints validate the XSRF-TOKEN
- [ ] `XSRF-TOKEN` cookie issued: `Secure=true`, `SameSite=Lax`, `HttpOnly=false` (SPA must read it)
- [ ] Authorization endpoints `[AllowAnonymous]`-marked are exempt from antiforgery only where they have no state side-effect (e.g., GET login page); login POST validates
- [ ] CSRF token rotation on login/logout

Global authorization:

- [ ] `AddAuthorization` configured with `FallbackPolicy = RequireAuthenticatedUser()`
- [ ] `[AllowAnonymous]` applied **only** to: `/login`, `/login/totp`, `/register`, `/password-reset`, `/email-verify`, the React SPA static-file catch-all, and the lockout self-service unlock endpoint
- [ ] Architecture test: any controller without `[Authorize]` or `[AllowAnonymous]` attribute fails the build (catches forgotten attributes)

Cookie configuration:

- [ ] Auth cookie name uses `__Host-` prefix (e.g., `__Host-Session`)
- [ ] `HttpOnly = true`, `Secure = true`, `SameSite = Lax`
- [ ] No `Domain` attribute set (forced by `__Host-` prefix)
- [ ] `Path = /`
- [ ] Verified in browser DevTools that the cookie has all four attributes after a successful login

Rate limiting:

- [ ] `/login` endpoint: 10 requests/min/IP minimum, fixed-window
- [ ] `/register` endpoint: 10 requests/min/IP minimum
- [ ] `/password-reset` endpoint: 10 requests/min/IP minimum, plus per-account rate limit
- [ ] Account lockout: lock for 15 min after 10 failed attempts; counter resets on successful login
- [ ] Lockout email includes a time-limited signed unlock link separate from the password-reset flow
- [ ] `/login/totp` endpoint: rate-limited per user (after credentials valid, before TOTP)

Failed-login logging:

- [ ] Every failed login logs: timestamp, IP, user-agent, whether the failure was credential-based or TOTP-based
- [ ] Attempted password is NEVER logged
- [ ] Logs queryable for distributed credential-stuffing detection (multiple accounts, same source IP)

Password reset:

- [ ] Token: 256-bit cryptographically random, base64url-encoded
- [ ] Stored as Argon2id hash only (never the raw token)
- [ ] 15-minute expiry from issuance
- [ ] Single-use: invalidate on first successful submission
- [ ] On successful reset: revoke all `UserSession` rows for that user
- [ ] Reset flow requires a valid TOTP code before accepting the new password
- [ ] Backup codes are the recovery path if TOTP device is lost — NOT a TOTP bypass during reset
- [ ] Email always sent on reset request (registered or not — same response, same wall-clock timing)
- [ ] Separate notification email sent on successful password change

Email-address change:

- [ ] Reauthentication required before initiating
- [ ] Verification link sent to new address (256-bit token, 30-min expiry, single-use)
- [ ] Notification + revoke link sent to old address (256-bit token, 7-day expiry)
- [ ] Old address remains the address-of-record until new address verified
- [ ] Notification email sent to old address on successful change

Reauthentication for sensitive operations:

- [ ] Fresh password entry required before: change password, change email, re-enrol TOTP, view active sessions, GDPR erasure
- [ ] Persistent sessions ("remember me") do NOT bypass reauthentication
- [ ] Reauthentication grant is short-lived (e.g., 5 minutes) and scoped to a single sensitive action

Audit logging:

- [ ] `AuditLog` entity: `Id`, `UserId`, `Action`, `EntityType`, `EntityId`, `OccurredAt`, `IpAddress`
- [ ] Writes for: login, logout, registration, password reset, email change, TOTP enrol/disable, backup-code regeneration, data export request, GDPR erasure
- [ ] Financial amounts NEVER appear in audit entries
- [ ] 6-month auto-purge job (registered as `IUserJobRunner` cross-tenant background job — see Stage 7 dependency)

Tests required before Stage 7 begins:

- [ ] Login happy-path integration test (credentials → TOTP → cookie issued)
- [ ] Login wrong-password test returns identical error message + timing as login with non-existent user
- [ ] Login wrong-TOTP test returns generic error
- [ ] Account lockout test: 10 failed attempts locks; 11th returns lockout error
- [ ] Self-service unlock token test: valid token unlocks; expired token returns error
- [ ] TOTP replay test: same code used twice within window is rejected on second use
- [ ] TOTP replay survives app restart (persistent store, not in-memory)
- [ ] Password reset happy-path integration test (request → email → click link → enter TOTP → set new password → all sessions revoked)
- [ ] Password reset enumeration test: same response + timing whether email exists or not
- [ ] CSRF test: state-changing request without XSRF-TOKEN header returns 400/403
- [ ] Reauthentication test: changing password without fresh password entry returns 401, even with valid session

---

## Stage 7 — Multi-tenancy cutover (Batch 3c)

**Status: ❌ Pending.** Highest-risk change in Phase 3. Touches every existing service. Lands after Stage 6 so the `AspNetUsers` table exists to remap onto.

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

- [ ] `IUserScope` interface has `EnterAs(Guid userId): IDisposable`
- [ ] Internal storage is `AsyncLocal<Guid?>` (propagates across `await` boundaries within a single logical flow)
- [ ] `Dispose()` clears the value; nested `EnterAs` calls work correctly via stack semantics
- [ ] Architecture test: `IUserScope.EnterAs` callers always wrap the call in `using` (no leaked scopes)

`IUserJobRunner`:

- [ ] `ForEachUserAsync(filter, work)` enumerates users with `IgnoreQueryFilters()` (intentionally cross-tenant query)
- [ ] Per-user iteration enters scope, invokes work, exits scope
- [ ] Per-user exception isolation: one user's failure does not abort the batch
- [ ] Per-user logging: each iteration logs success/failure with the user's id

`ICurrentUserAccessor`:

- [ ] Resolves in this order: HTTP context → AsyncLocal scope → throw `InvalidOperationException`
- [ ] The throw message names both options: "HTTP requests resolve from cookie; background jobs must enter via IUserScope.EnterAs()"
- [ ] No silent fallback to `Guid.Empty` anywhere
- [ ] Test: calling `_currentUser.UserId` in a unit-test without setting either context throws

EF global query filters:

- [ ] Filters applied to: `Transaction`, `Transfer`, `LiabilityPayment`, `Account`, `Category`, `CategoryBudget`, `Budget`, `RecurringTransaction`, `TransactionAttachment`, `TransferAttachment`, `SavedReport`, `UserSession`, `UserBlockedIp`, `Settings`, `SupportTicket`, `AuditLog`, `CsvImportProfile` (+ any other user-owned entity at cutover time)
- [ ] System tables (`AccountType`, `CategoryType`, `Currency`, `ReportType`, `SystemCategory`) carry NO filter
- [ ] Service code continues to write explicit `.Where(t => t.UserId == _currentUser.UserId)` (belt-and-suspenders)
- [ ] Test: a query against a user-owned table without `IgnoreQueryFilters()`, run as User A, returns zero User B rows even when the explicit `.Where()` is intentionally omitted

`IgnoreQueryFilters()` boundary:

- [ ] Architecture test fails the build if `IgnoreQueryFilters()` appears in any file outside `ProjectCeres/Admin/` or other documented exception paths
- [ ] Documented exception paths: `IUserJobRunner.ForEachUserAsync` (intentionally cross-tenant), audit-log purge, retention sweeps — each with a comment explaining why
- [ ] Test: an attempt to add `IgnoreQueryFilters()` to a regular service file fails the architecture test

Sentinel-to-real-user migration:

- [ ] Migration runs inside a single PostgreSQL transaction
- [ ] Pre-check: exactly one row exists in `AspNetUsers` (the user who just registered)
- [ ] Pre-check: at least one row tagged with the sentinel UUID exists in user-owned tables
- [ ] If either pre-check fails, the transaction aborts cleanly (no partial state)
- [ ] All user-owned tables remapped: `accounts`, `transactions`, `transfers`, `liability_payments`, `categories`, `category_budgets`, `budgets`, `recurring_transactions`, `transaction_attachments`, `transfer_attachments`, `saved_reports`, `csv_import_profiles`, `settings` (+ any other user-owned tables existing at cutover time)
- [ ] Post-check: `SELECT COUNT(*) WHERE user_id = sentinel` is 0 across all tables
- [ ] If post-check fails, the transaction rolls back
- [ ] Migration runs **once only** — gated by a flag or schema-version check; subsequent registrations don't re-run it
- [ ] Pre-deployment manual gate: a database backup snapshot is taken before the deployment that includes this migration

Sentinel removal:

- [ ] `SingleUserAccessor` class deleted
- [ ] Sentinel UUID constant (`00000000-0000-0000-0000-000000000001`) removed from production code
- [ ] Test fixtures updated to use real test user UUIDs
- [ ] Seed scripts updated
- [ ] No grep hit for `SingleUserAccessor` or the sentinel constant in production code

Service audit (per `multi-tenancy-strategy.md` § Services to audit for Phase 3):

- [ ] `AccountService` — every method scoped by `UserId`
- [ ] `TransactionService` — every method scoped
- [ ] `TransferService` — every method scoped
- [ ] `LiabilityPaymentService` — every method scoped
- [ ] `CategoryService` — every method scoped
- [ ] `CategoryBudgetService` — every method scoped
- [ ] `BudgetService` (goal budgets) — every method scoped
- [ ] `RecurringTransactionService` — every method scoped
- [ ] `TransactionAttachmentService` — every method scoped
- [ ] `TransferAttachmentService` — every method scoped
- [ ] `SavedReportService` — every method scoped
- [ ] `SettingsService` — every method scoped (including the no-longer-singleton path)
- [ ] `DashboardService` — every method scoped
- [ ] All 8 report generators (`NetWorth`, `IncomeExpense`, `ExpenseBreakdown`, `TransactionHistory`, `BudgetVsActual`, `LargestExpenses`, `MonthlyCashFlow`, `NetWorthOverTime`) — scoped
- [ ] `ImportService` — every method scoped
- [ ] `CsvImportProfileService` — every method scoped
- [ ] `TransferReviewService` — every method scoped
- [ ] `ImportStagedTransactionService` — every method scoped
- [ ] `ReminderCountProvider` server-side counterpart — scoped
- [ ] `ReviewCountProvider` server-side counterpart — scoped

Boot-time hooks:

- [ ] `ISettingsService.EnsureExistsAsync` removed from `Program.cs` startup
- [ ] No remaining `IHostedService`, `IStartupFilter`, or boot-time hook queries user-owned tables
- [ ] Any boot-time query that needed user data is moved into per-user lifecycle hooks (registration, login)
- [ ] Test: a smoke test starts the application with zero registered users and confirms no boot-time exception is thrown

IDOR integration test suite (per `multi-tenancy-strategy.md` § Required Integration Tests):

- [ ] User A cannot read User B's accounts (`GET /api/accounts/{B's id}` → 404, NOT 403)
- [ ] User A cannot read User B's transactions
- [ ] User A cannot read User B's transfers
- [ ] User A cannot read User B's liability payments
- [ ] User A cannot read User B's budgets (CategoryBudget + GoalBudget)
- [ ] User A cannot read User B's categories
- [ ] User A cannot read User B's recurring transactions
- [ ] User A cannot read User B's saved reports
- [ ] User A cannot read User B's transaction attachments (file content download)
- [ ] User A cannot read User B's transfer attachments
- [ ] User A cannot list User B's anything (list endpoints return zero of B's rows when called as A)
- [ ] User A cannot aggregate over User B's data (sum/count endpoints scoped correctly)
- [ ] User A cannot delete User B's resources (`DELETE /api/transactions/{B's id}` → 404)
- [ ] User A cannot edit User B's resources (`PATCH /api/transactions/{B's id}` → 404)
- [ ] User A cannot upload an attachment against User B's transaction
- [ ] Cross-tenant tests use real fixtures, not mocked services — the test must touch the real DB to verify global filters apply

---

## Stage 8 — Email service + email security (Batch 3d)

**Status: ❌ Pending.** Lands before Stage 9 (auth UI) because auth flows depend on a working email service.

> **Goal:** a transactional email service is integrated, DNS-level email security is configured (SPF, DKIM, DMARC), application-layer protections (recipient lock, sanitization, rate limiting) are in place, and the EN/ES transactional templates exist for every Phase 3 flow that sends mail.

### Sub-stages

| # | Sub-stage | Spec / Reference |
|---|---|---|
| 8.1 | Pick provider (SendGrid / Postmark / AWS SES / Mailgun) | `planning.md` § Open Questions: Email service |
| 8.2 | `IEmailService` abstraction + provider implementation | New abstraction; provider behind interface for testability |
| 8.3 | DNS authentication: SPF, DKIM, DMARC | `security-model.md` § Email Security Rules → Layer 1 |
| 8.4 | Application controls: recipient lock, sanitization, per-user rate limit | `security-model.md` § Email Security Rules → Layer 2 |
| 8.5 | API key hygiene: secret store, send-only scope, rotation procedure | `security-model.md` § Email Security Rules → Layer 3 |
| 8.6 | Transactional templates EN + ES | `.resx` files per `planning-phase3.md` § Localization (`Emails.en.resx`, `Emails.es.resx`) |
| 8.7 | Email delivery telemetry (delivered / bounced / complained) | Provider webhook integration |

### Verification checklist

Provider integration:

- [ ] `IEmailService` interface exists with `SendAsync(EmailMessage)` and is the only entry point for outgoing email
- [ ] No code outside the email service constructs an SMTP client or provider client directly
- [ ] Provider API key in environment variable / secret store; never in source control
- [ ] Send-only scoped key used where the provider supports it
- [ ] Key rotation procedure documented in `security-model.md` § Secrets Rotation Procedures (or new entry)
- [ ] Provider client is wrapped in retry logic (provider transient errors retry 3 times with exponential backoff)
- [ ] Failed sends are logged but never block the user-facing request (queued via `IUserJobRunner`)

DNS authentication (per `security-model.md` § Layer 1 — DNS authentication):

- [ ] SPF record published on the sending domain
- [ ] DKIM record published with provider-supplied public key; signing active and verified
- [ ] DMARC record published with at minimum `p=none` at launch
- [ ] DMARC aggregate report destination configured (e.g., `rua=mailto:dmarc@example.com`)
- [ ] After 30 days of clean aggregate reports, advance DMARC to `p=quarantine`, then `p=reject`
- [ ] All three records verified using `dig` and an external tool (e.g., MXToolbox)

Application controls (per `security-model.md` § Layer 2 — Application controls):

- [ ] Outgoing `To:` address ALWAYS resolved server-side from the authenticated user's verified email — never from a request parameter
- [ ] User-controlled strings rendered into email subject/body are sanitized (HTML-escaped, newline-stripped to prevent header injection)
- [ ] Per-user rate limit on email-triggering endpoints (e.g., max 5 password-reset requests / hour / user, max 1 GDPR export / 24 hours / user)
- [ ] Per-IP rate limit on unauthenticated email-triggering endpoints (e.g., password reset request before user is identified)
- [ ] Test: attempting to send to an arbitrary `To:` parameter is rejected at the service boundary

Transactional templates (EN + ES, per `planning-phase3.md` § Localization):

- [ ] Registration confirmation (verify-email link)
- [ ] Password reset request
- [ ] Password changed notification
- [ ] Email-change verify-new-address link
- [ ] Email-change revoke-old-address link
- [ ] TOTP enrolled (security event)
- [ ] TOTP disabled (security event)
- [ ] Backup codes regenerated (security event)
- [ ] Account lockout notification with self-service unlock link
- [ ] New-session alert (when login from previously-unseen IP for that user)
- [ ] GDPR data export ready (with 24-hour authenticated download link)
- [ ] Account-erasure confirmation (per `security-model.md` § Security Event Notifications)
- [ ] Each template exists in `Emails.en.resx` and `Emails.es.resx`
- [ ] Templates rendered server-side via `IStringLocalizer<EmailsResource>` keyed by user's `Settings.Language`
- [ ] No financial amounts in security-event emails (per `security-model.md` § Logging and PII Redaction)

Security event notifications (mandatory regardless of user preferences, per `security-model.md` § Security Event Notifications):

- [ ] New-device/session login email — includes IP, geolocation summary, UA summary, "this wasn't me" link
- [ ] Password-changed email — timestamp, IP, "revoke all sessions" link
- [ ] Email-change-initiated email — old + new address, revoke link (7-day TTL)
- [ ] Email-change-confirmed email
- [ ] TOTP-re-enrolled email — timestamp, IP, "this wasn't me" link
- [ ] TOTP-disabled email — timestamp, IP, "re-enable + revoke all sessions" link
- [ ] Backup-codes-regenerated email
- [ ] Account-locked-out email — cause, IP, self-service unlock link
- [ ] GDPR-erasure-initiated email — confirmation of what will be deleted and when
- [ ] All eight emails verified to actually fire in integration tests with test fixtures

Telemetry + observability:

- [ ] Provider webhook configured for delivered / bounced / complained events
- [ ] Bounced events disable the user's email (mark `EmailVerified = false`) and surface a notice on next login
- [ ] Complaints (spam reports) auto-disable digest emails for that user
- [ ] Webhook endpoint validates provider signature (no spoofed events)

Tests required before Stage 9 begins:

- [ ] Send-email happy-path test mocks the provider client and asserts subject/body/to render correctly in both EN and ES
- [ ] Recipient-lock test: passing an arbitrary `To:` is rejected
- [ ] Header-injection test: a user "name" with embedded `\r\n` cannot inject email headers
- [ ] Rate-limit test: 6th password-reset request within an hour is rejected
- [ ] DKIM signature presence test (smoke test against staging provider config)

---

## Stage 9 — Auth SPA pages (Batch 3e)

**Status: ❌ Pending.** First user-visible Phase 3 work. Lands after Stage 8 because every flow depends on a working email service.

> **Goal:** every auth screen exists in the SPA, designed to a high bar (these are the first thing beta users see), with full localization (EN/ES), accessibility (focus management, `aria-live`), and clear error states.

### Sub-stages

| # | Sub-stage | Reference |
|---|---|---|
| 9.1 | `/login` page (email + password form) | `planning-phase3.md` § 10 Auth screen design |
| 9.2 | `/login/totp` page (6-digit TOTP step) | (above) |
| 9.3 | `/register` page + email-verification interstitial | (above) |
| 9.4 | `/password-reset` request page + `/password-reset/confirm` (with TOTP) | `security-model.md` § Password Reset |
| 9.5 | Lockout / self-service unlock screen | `security-model.md` § Login → Account lockout self-service unlock |
| 9.6 | TOTP setup flow (QR + manual-entry fallback + backup-codes download) | `planning-phase3.md` § 10 Auth screen design |
| 9.7 | Backup-codes recovery flow (lost TOTP device) | (above) |
| 9.8 | Reauthentication prompts on sensitive operations | `security-model.md` § Login → Reauthentication |
| 9.9 | Language toggle on every auth card (globe icon) | `planning-phase3.md` § Localization |

### Verification checklist

Layout + brand:

- [ ] Centered card layout, no app shell (sidebar, top bar absent)
- [ ] Ceres logo / wordmark placement consistent across all auth pages
- [ ] Globe icon language toggle at the bottom of every auth card; instant in-place swap via `i18n.changeLanguage()`, no reload
- [ ] Pre-auth language detection writes the `lang` cookie (non-HttpOnly, `SameSite=Lax`)

`/login`:

- [ ] Email + password fields with explicit `<label>` (`htmlFor`) — no placeholder-only labels
- [ ] Submit button has idle / loading / error states
- [ ] On wrong credentials: generic error "Email or password is incorrect" (no enumeration leak)
- [ ] On account locked: "Account locked. Check your email for an unlock link." — no other detail
- [ ] On success without TOTP enrolled: redirect to `/login/totp/setup` (first-login enrolment grace path)
- [ ] On success with TOTP enrolled: redirect to `/login/totp`
- [ ] "Forgot password?" link routes to `/password-reset`
- [ ] "Create account" link routes to `/register`

`/login/totp`:

- [ ] 6-digit input with auto-focus, auto-advance, paste handling
- [ ] On wrong code: generic error "Invalid code"
- [ ] On expired window: same generic error (no leak that "the code was right but expired")
- [ ] On success: cookie set, redirect to `/` (dashboard) or onboarding if first-run
- [ ] Backup-code link below input: "Lost your device? Use a backup code"
- [ ] Backup-code path uses single-use code; on success offers backup-codes regeneration

`/register`:

- [ ] Email + password fields; password meets policy (≥ 8 chars, no max < 64) per Stage 6
- [ ] Submit returns 202 Accepted with "Check your email to verify your address" — no enumeration leak (same response if email already registered)
- [ ] Verification email sent with single-use 256-bit token, hashed, 30-min expiry
- [ ] `/email-verify?token=...` page accepts the token, marks email verified, redirects to `/login/totp/setup`

`/password-reset` (request) + `/password-reset/confirm` (action):

- [ ] Request page: email field; submit returns "If that email is registered, you'll receive a link" (constant response + timing)
- [ ] Email contains link with 256-bit token (15-min expiry)
- [ ] `/password-reset/confirm?token=...` form requires: TOTP code + new password
- [ ] On success: token invalidated, all sessions revoked, redirect to `/login`
- [ ] On expired token: clear error, link to request a new one
- [ ] On wrong TOTP: generic error, does NOT consume the reset token (token still valid for retry until expiry)

Lockout / unlock:

- [ ] After 10 failed login attempts, account is locked + email sent with self-service unlock link
- [ ] `/account/unlock?token=...` accepts the signed token, unlocks the account, redirects to `/login` with success toast
- [ ] Token expires after a reasonable window (e.g., 1 hour)
- [ ] Lockout email also tells the user "valid TOTP codes are still accepted during lockout" (per `security-model.md` § Login)

TOTP setup (`/login/totp/setup`):

- [ ] QR code displayed (otpauth:// URI) — uses `qrcode` library or equivalent
- [ ] Manual-entry secret displayed below QR for accessibility / desktop authenticators
- [ ] Verification step requires entering one valid code before enrolment is complete
- [ ] On successful enrolment: 10 backup codes generated (cryptographically random, ≥ 20 bits entropy each per NIST 800-63B), shown ONCE, with download (.txt) + print options
- [ ] Backup codes are hashed in DB after this step; the page warns "These will not be shown again"
- [ ] User must confirm "I've saved my backup codes" checkbox before proceeding
- [ ] After enrolment: redirect to onboarding (Stage 10) for first-run, or dashboard for re-enrolment

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

- [ ] Wrong password — generic message
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

- [ ] Every string in every auth page uses `useTranslation()` keyed strings
- [ ] EN + ES translations complete in `en.json` and `es.json`
- [ ] No untranslated copy visible when toggling to ES
- [ ] Date/time strings (e.g., "Token expires in 15 minutes") respect the user's locale

Responsive (per [`planning-phase3-responsive.md`](planning-phase3-responsive.md) § Surface Inventory — auth surfaces are single-column centered card on every tier):

- [ ] Mobile (375px iPhone SE): centered card fills viewport with comfortable padding; no horizontal overflow; touch targets on every input/button ≥ 44×44px
- [ ] Tablet (768px iPad): centered card constrained to a readable max-width; layout unchanged from mobile beyond the max-width clamp
- [ ] Desktop (≥ 1024px): centered card constrained to a narrow max-width; sidebar/app shell absent on every auth page
- [ ] TOTP 6-digit input renders cleanly on mobile (no tiny touch targets, no zoom-on-focus)
- [ ] QR code in TOTP setup flow is large enough to scan on mobile when displayed at the user's screen
- [ ] Backup codes download offers a `.txt` that copies cleanly on mobile (long press → save / share sheet)
- [ ] Language toggle (globe icon) is reachable without scrolling on mobile

---

## Stage 10 — Onboarding wizard (Batch 3f)

**Status: ❌ Pending.** Lands after Stage 9 because the registration flow ends in onboarding.

> **Goal:** five-step wizard at `/onboarding` that takes a freshly-registered user from "I just created an account" to "I see my net worth on the dashboard." Full-screen stepper, distinct from the standard app shell. See [ADR-0053](decisions/ADR-0053-guided-onboarding-deferred.md).

### Sub-stages

| # | Sub-stage | Reference |
|---|---|---|
| 10.1 | `/onboarding` route + full-screen stepper layout | `planning-phase3.md` § 11 Onboarding flow design |
| 10.2 | Step 1 — Preferences (language, country, default currency, number format, date format) | (above) + `planning-phase3.md` § Localization |
| 10.3 | Step 2 — First asset account | (above) |
| 10.4 | Step 3 — First liability (optional, skippable) | (above) |
| 10.5 | Step 4 — Opening balance | (above) + ADR-0010 (opening balance as auto-created transaction) |
| 10.6 | Step 5 — Immediate net worth display + "Go to dashboard" CTA | (above) |

### Verification checklist

Layout + flow:

- [ ] `/onboarding` is a top-level route OUTSIDE `AppLayout` (no sidebar, no top bar)
- [ ] Full-screen stepper with progress indicator at top showing 5 numbered steps
- [ ] First-run users (no `OnboardingCompletedAt` on Settings) are redirected to `/onboarding` after login until completion
- [ ] Completion flag persisted to per-user Settings; subsequent logins go straight to dashboard
- [ ] User can navigate back to a previous step; data from later steps is preserved if revisited
- [ ] Browser back button triggers an "are you sure you want to leave?" guard if onboarding is partial

Step 1 — Preferences:

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

Tests required before Stage 11 begins:

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

## Stage 11 — Razor + URL cleanup (Batch 4)

**Status: ❌ Pending.** Mechanical cleanup. Lands after Stage 10 because Auth is the last surface that needs the `/app/` prefix to coexist with Razor stubs.

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

Smoke tests:

- [ ] Application boots without exception
- [ ] Full SPA loads at `/` and every page renders
- [ ] Browser dev-tools network tab shows no 404s for legacy assets
- [ ] All API integration tests still pass (the API surface is unchanged by this batch)
- [ ] No regressions in the IDOR test suite from Stage 7

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
- [ ] When done, sends email with authenticated download link (Stage 8 template)
- [ ] Download link 24-hour expiry; tied to a single signed token, single-use
- [ ] Rate limit: max 1 export request / 24 hours / user
- [ ] Synchronous fallback rejected (see `planning-phase3.md` warning about HTTP worker exhaustion)

Right-to-erasure:

- [ ] `/settings/account/erasure` page describes what will be deleted, when, what is retained (legal-basis-required records like audit log), and confirms intent
- [ ] Reauthentication-gated initiation
- [ ] Audit log entry created at request time
- [ ] Confirmation email sent (Stage 8 template)
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
- [ ] Erasure confirmation email template EN + ES (Stage 8)
- [ ] Data export ready email template EN + ES (Stage 8)

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
- [ ] CSP violations log empty after a full SPA browse-through
- [ ] Penetration testing scheduled or completed (per `security-model.md` § Responsible Disclosure and Penetration Testing)

---

## Master pre-launch verification checklist

> Final gate before opening Project Ceres to invited beta testers. Every item must be `[x]` or have a documented exception. This is the consolidated view across stages — if a stage above is incomplete, the parallel item here is incomplete too.

### Authentication + identity

- [ ] All Stage 6 verification items green
- [ ] All Stage 9 verification items green
- [ ] All Stage 10 verification items green
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

> **When the master checklist is fully green, Phase 3 is shippable. Phase 4 begins with: per-tenant payload encryption, social login (one provider — likely Google), JWT issuance for mobile, PostgreSQL Row-Level Security as defense in depth, and the deferred items from `docs/planning-future.md`.**



