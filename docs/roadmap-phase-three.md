# Phase 3 Build Roadmap

> **Diataxis type:** Reference + how-to — sequenced delivery plan for Phase 3 (Hosted Beta), with verification checklists per stage. Mirrors the Phase 1 and Phase 2 roadmaps; consumed by `superpowers:executing-plans` and the after-stage verification step in `CLAUDE.md`.

> **Principles:** Spec-Driven Development (spec before code) · Test-Driven Development (failing test before implementation) · SOLID architecture maintained throughout · loud-failure over silent-failure (every cross-tenant boundary throws or logs explicitly).
>
> **TDD rule:** Every new service method, business rule, security mechanism, and financial calculation gets a failing test written first. The test must fail because the implementation does not exist — not because it is misconfigured. Controllers are excluded (thin HTTP handlers, verified via `WebApplicationFactory` integration tests). Infrastructure plumbing (migrations, DI wiring, reverse proxy) is verified by smoke tests, not by unit tests against the plumbing itself.
>
> **UI/UX rule:** Phase 3 introduces auth screens, onboarding, sessions/support pages, and the GDPR consent banner — every new SPA page is built to the design system at first ship (no second-pass redesign). For frontend changes, follow `CLAUDE.md` § Frontend Work: invoke `frontend-design`, run the UX/UI verification checklist in the browser, run `web-design-guidelines` before commit.
>
> **Security gate rule:** Stages that introduce or modify security-sensitive code (auth, multi-tenancy, sessions, email, headers) cannot be marked done without their verification checklist green. The verification list for each stage is drawn from `security-model.md`, `multi-tenancy-strategy.md`, and the ADRs — items there are not optional.

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
- [x] Accounts: per-currency subtotal strip rendered when 2+ currencies present
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

**Status: ⚠️ In progress.** Two parallel polish tracks: the Tier 1–5 list in `docs/ceres-polish-checklist-frontend.md` (audit doc, commit `5a653d3`) and the data-loading ease-in pattern (`useDelayedLoading` + `<DataTransition>`) — proven on Accounts, rollout to remaining pages still pending.

> **Why this is its own stage:** these are app-wide UX fundamentals that touch every page. They can't be picked up under any single Stage 1–4 because they cross all of them. They're explicitly *not* blocking Phase 3 launch — they're the difference between "shipped" and "feels well done."

### Sub-stages

| # | Sub-stage | Status | Reference |
|---|---|---|---|
| 5.1 | Polish checklist audit doc — file-by-file inventory of frontend gaps | ✅ 2026-05-06 (commit `5a653d3`) | [`docs/ceres-polish-checklist-frontend.md`](ceres-polish-checklist-frontend.md) |
| 5.2 | Data-loading ease-in — primitives (`useDelayedLoading` + `<DataTransition>`) | ✅ 2026-05-04 | [`docs/superpowers/specs/2026-05-04-data-loading-ease-in-design.md`](superpowers/specs/2026-05-04-data-loading-ease-in-design.md) · [`docs/superpowers/plans/2026-05-04-data-loading-ease-in.md`](superpowers/plans/2026-05-04-data-loading-ease-in.md) |
| 5.3 | Data-loading ease-in — reference rollout on Accounts page | ✅ 2026-05-04 (commits `c6052d0` → `5b7d72e`) | (above) |
| 5.4 | Data-loading ease-in — full rollout to remaining pages | ❌ Pending (committed plan: `fa8b7ff`) | [`docs/superpowers/plans/2026-05-04-data-loading-ease-in-rollout.md`](superpowers/plans/2026-05-04-data-loading-ease-in-rollout.md) |
| 5.5 | Polish checklist Tier 1 — six small CSS / one library wire-up | ❌ Pending | [`docs/ceres-polish-checklist-frontend.md`](ceres-polish-checklist-frontend.md) § Tier-ordered action list |
| 5.6 | Polish checklist Tier 2 — extract shared primitives | ❌ Pending | (above) |
| 5.7 | Polish checklist Tier 3 — production-app polish | ❌ Pending | (above) |
| 5.8 | Polish checklist Tier 4 — testing and observability | ❌ Pending | (above) |
| 5.9 | Polish checklist Tier 5 — discretionary | ❌ Pending | (above) |

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

Rollout to remaining pages — pending. Tasks per `docs/superpowers/plans/2026-05-04-data-loading-ease-in-rollout.md`:

- [ ] Task 1: Categories list page (`CategoriesLayout.tsx`)
- [ ] Task 2: Movements list page (`MovementsLayout.tsx`)
- [ ] Task 3: Recurring list page (`RecurringLayout.tsx`)
- [ ] Task 4: Reports — 8 sub-commits (`BudgetVsActual`, `ExpenseBreakdown`, `IncomeExpense`, `LargestExpenses`, `MonthlyCashFlow`, `NetWorth`, `NetWorthOverTime`, `TransactionHistory`)
- [ ] Task 5: Budgets list page (`BudgetsLayout.tsx`)
- [ ] Task 6: Settings page (`SettingsPage.tsx`)
- [ ] Task 7: Review (`ReconciliationList.tsx`)
- [ ] Task 8: Dashboard cards — 9 sub-commits (`NetWorthCard`, `MtdCard`, `FinancialHealthCard`, `RemindersCard`, `NetWorthChart`, `CashFlowChart`, `IncomeExpenseChart`, `AccountBalancesChart`, `SpendingByCategoryChart`)
- [ ] Task 9: Final verification — full client test suite green, production build succeeds, browser sweep on every rolled-out page (fast / slow / reduced-motion paths)

### Polish checklist (`ceres-polish-checklist-frontend.md`) — verification checklist

Tier 1 — six items, ~80% of visible improvement:

- [ ] T1.1 Wire `next-themes` into `ThemeToggle.tsx` (10.2, partial 10.1) — `next-themes` is installed but not used
- [ ] T1.2 Add `transition` rule for Switch thumb in `index.css` (3.10) — currently snaps with no transition
- [ ] T1.3 Add `::view-transition-old/new(root)` defaults in `index.css` (1.2) — currently using browser default ~250 ms instead of token-driven 180 ms
- [ ] T1.4 Add global `prefers-reduced-motion: reduce` override (4.1, 4.2) — no rule anywhere currently
- [ ] T1.5 Add global theme-flip `transition-colors` rule in `index.css` (10.1) — theme switch is currently a hard flip
- [ ] T1.6 Add `<ScrollRestoration getKey={l => l.pathname} />` to `AppLayout.tsx` (9.1) — back-navigation currently loses scroll position

Tier 2 — extract shared primitives:

- [ ] T2.7 Extract `<Field>` to a shared component (8.7) — currently inlined twice in `QuickAddModal.tsx` lines 248–271 and `MovementForm.tsx` lines 105–134
- [ ] T2.8 Extract `<MoneyInput>` shared component; consume from both forms (8.6) — `QuickAddModal.tsx` violates the locale-aware Money input recipe (uses `type="number" step="0.01"`); breaks `comma_decimal` users
- [ ] T2.9 Wrap `<Skeleton>` to add `role="status"` + `aria-busy="true"` (2.8) — accessibility gap
- [ ] T2.10 Add `useDelayedLoading(isLoading, 200)` hook (2.7, 7.5) — note: this is now superseded by Stage 5.2; mark Tier 2 item done

Tier 3 — production-app polish:

- [ ] T3.11 Migrate `duration-200` literals to motion tokens (3.1, 3.4) — already on roadmap (Known Limitation in `design-system.md` line 1154)
- [ ] T3.12 Build `<SubmitButton>` with idle/loading/success/error + spinner (8.4) — replaces `QuickAddModal.tsx` line 230 and `MovementForm.tsx` lines 548–550
- [ ] T3.13 `useOptimistic` for the Status block toggle on table rows (5.1)
- [ ] T3.14 Replace MovementForm budget `<select>` with Combobox, or document the rule
- [ ] T3.15 Harden `index.html` — `theme-color` meta, description meta, per-route titles (1.7)

Tier 4 — testing and observability:

- [ ] T4.16 Disable CSS animations in `test-setup.ts` (12.1) — animations not currently disabled in tests
- [ ] T4.17 Add `vitest-axe` (4.6, 12.5) — minimum coverage: `MovementForm`, `QuickAddModal`, `AppLayout`
- [ ] T4.18 Add bundle visualizer (`rollup-plugin-visualizer`) + size budget on `dist/assets/*.js` (5.5)

Tier 5 — discretionary:

- [ ] T5.19 `@formkit/auto-animate` for lists that reorder (0.3, 3.6)
- [ ] T5.20 OKLCH support in `parseRgb()` so showcase shows live contrast ratios
- [ ] T5.21 Add motion rules to "Working rules" section in `design-system.md`

### "Feels well done" gut-check — verification checklist

From the same audit doc (§ 15). Once Tier 1 + Stage 5.4 (data-loading rollout) are done, every line below should be `[x]`:

- [ ] No element appears or disappears instantly except in response to typing
- [x] No content jumps when data loads — skeleton heights match reality
- [~] Hovering any button gives visible feedback within 150 ms (uses `duration-200`, fine; partial)
- [ ] Pressing any button gives a subtle scale/color change (verify Button primitive)
- [x] Tab key reveals a clear focus ring on every interactive element
- [ ] Switching themes is smooth, not flashy (T1.5 + T1.1)
- [~] Navigating between pages cross-fades, doesn't snap (browser default until T1.3)
- [~] Submitting a form shows immediate feedback (T3.12)
- [ ] No spinners flash for <200 ms (covered by Stage 5.4 rollout)
- [x] All icons sized identically in similar contexts
- [x] Border radii consistent
- [ ] In Reduce Motion mode, the app still works and animations are subdued (T1.4)
- [ ] Switch toggle slides smoothly (T1.2)

---

