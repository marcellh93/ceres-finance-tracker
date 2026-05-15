# Phase 3 — MVC → SPA Migration Plan

> **Status: Approach locked 2026-04-28.** Design-first, migrate feature by feature — each area is fully API-tested, then React-built, then Razor-deleted. Never a big-bang deletion. Hosting model: Option A (React served from ASP.NET Core `wwwroot/`). See full spec: [`docs/superpowers/specs/2026-04-28-spa-migration-ux-overhaul-design.md`](superpowers/specs/2026-04-28-spa-migration-ux-overhaul-design.md). Controller inventory, route map, and `Program.cs` changes still need a final audit at implementation kickoff.

**Prerequisite:** Phase 2 is complete (as of 2026-04-28). The architectural decision to go full SPA at Phase 3 is committed — see [architecture.md](architecture.md#phase-3--full-spa-evaluation-point). This document is the execution plan for that decision.

---

## Index

1. [What gets deleted vs. what gets kept](#1-what-gets-deleted-vs-what-gets-kept)
2. [Controller-by-controller porting map](#2-controller-by-controller-porting-map)
3. [ViewModel → DTO strategy](#3-viewmodel--dto-strategy)
4. [Auth integration point](#4-auth-integration-point)
5. [React Router route map](#5-react-router-route-map)
6. [Program.cs changes](#6-programcs-changes)
7. [Hosting model for the React SPA](#7-hosting-model-for-the-react-spa)
8. [Migration sequencing](#8-migration-sequencing)

---

## 1. What gets deleted vs. what gets kept

### Deleted

- All Razor views (`Views/**/*.cshtml`) including `_Layout.cshtml`, `_ViewImports.cshtml`, `_ViewStart.cshtml`, `_ValidationScriptsPartial.cshtml`
- All MVC controller actions that return `IActionResult` with a `View()`
- Tailwind v3 / Razor CSS pipeline (`ProjectCeres/Styles/`, `wwwroot/css/site.css`, the MSBuild pre-build target)
- jQuery unobtrusive validation scripts
- Anti-forgery token infrastructure — `[ValidateAntiForgeryToken]` attributes on all controllers (replaced by CORS + HttpOnly cookie auth)

### Kept and promoted

- All service interfaces and implementations — untouched; the migration is purely at the HTTP layer
- All EF Core models, migrations, `AppDbContext` — untouched
- ViewModels that map cleanly to JSON shapes — audited and converted to request/response DTOs
- `Controllers/Api/` — already exists from Phase 2; grows to cover all endpoints
- `ProjectCeres.Client/` — the Vite + React project becomes the entire frontend

---

## 2. Controller-by-controller porting map

> **Note:** Phase 2 will add more controllers (Movements, Import, CsvImportProfiles, etc.). Each one added in Phase 2 gets ported at Phase 3 using the same pattern. Audit the full controller list at kickoff and extend this table.

| MVC Controller | Actions to port to API | Notes |
|---|---|---|
| `AccountsController` | Index, Create, Edit, Deactivate, Ledger | **Migrated (2026-05-03).** SPA at `/app/accounts` with nested `/new` and `/:id/edit` routes plus a sibling top-level `/accounts/:id/ledger` route. Page actions 302-redirect to the SPA. Razor views deleted; throwing CRUD service methods removed; `LiabilityProjectionService` deleted (projection math moved to SPA-side `projection.ts`, single source of truth post-cutover). `AccountListItemDto` gained a `HasTransactions` field that powers adaptive archive AlertDialog copy. `AccountPolicies.ValidateLiabilityRepayment` extended to symmetrically reject `null repaymentType + non-null rate`. Spec: `docs/superpowers/specs/2026-05-03-accounts-spa-design.md`. Plan: `docs/superpowers/plans/2026-05-03-accounts-spa.md`. |
| `TransactionsController` | Index, Create, Edit, Delete | **Migrated (2026-05-01).** Page actions 302-redirect to `/app/movements*`. Razor views deleted. `BulkMarkCleared` and `ToggleCleared` POST actions remain `[Obsolete]` until final SPA cleanup. |
| `TransfersController` | Index, Create, Edit, Delete | **Migrated (2026-05-01).** Page actions 302-redirect to `/app/movements*`. Razor views deleted. `ToggleCleared` POST action remains `[Obsolete]` until final SPA cleanup. |
| `BudgetsController` | Index, Create, Edit, Deactivate (×2 for Category + Goal) | **Migrated (2026-05-01).** Page actions 302-redirect to `/app/budgets/*`. Razor views deleted. POST overloads removed entirely (the SPA POSTs JSON to the new `/api/category-budgets` and `/api/goal-budgets`). |
| `CategoriesController` | Index, Create, Edit, Deactivate | **Migrated (2026-05-02).** SPA at `/app/categories` with nested `/new` and `/:id/edit` routes. Page actions 302-redirect to the SPA. Razor views deleted; throwing `CreateAsync`/`UpdateAsync`/`DeactivateAsync` service methods removed (covered at the API level by `CategoriesCrudApiTests`). Spec: `docs/superpowers/specs/2026-05-02-categories-spa-design.md`. Plan: `docs/superpowers/plans/2026-05-02-categories-spa.md`. |
| `RecurringTransactionsController` | Index, Create, Edit, Deactivate, Confirm, Dismiss | **Migrated (2026-05-03).** SPA at `/app/recurring` with nested `/new` and `/:id/edit` routes. Confirm and Dismiss wired as stateful PATCH/POST actions. Dismiss accepts an optional `{ nextDueDate }` body for ManualDate reminders. `PATCH /api/recurring-transactions/:id/reactivate` added alongside archive. `EstimatedAmount` is `decimal?` (null = amount varies). TopBar bell wired to `ReminderCountProvider`. Weekly/Biweekly `SnapToCalendarDay` fixed. Razor views deleted; throwing Razor-era service methods removed. |
| `ReportsController` | Index + 8 report views + 8 CSV exports | **Migrated (2026-05-03).** All actions 302-redirect to `/app/reports/*`. SPA at `/app/reports` with `ReportsLayout` + sticky filter bar, 8 report pages, CSV export via `?format=csv`. Razor views deleted. SavedReport CRUD deferred (ADR-0055). |
| `SettingsController` | Edit | **Migrated (2026-05-02).** SPA at `/app/settings`; `GET /api/settings` + `PATCH /api/settings` already shipped in Phase 2. Page action 302-redirects to the SPA. Razor view and view models deleted. Spec: `docs/superpowers/specs/2026-05-02-spa-page-pattern-and-settings-design.md`. Plan: `docs/superpowers/plans/2026-05-02-spa-page-pattern-and-settings.md`. **This is the SPA-page template** — Categories and the four remaining management screens (Accounts, Recurring, Reports, Review, Import) follow the patterns it locked. |
| `DashboardController` | Index | **Migrated (2026-04-29).** Dashboard is fully React; data served by `DashboardApiController`. 302 redirect from `/Dashboard` → `/app/` is live; Razor dashboard view, partial, and controller deleted. |
| `MovementsController` | Index | **Migrated (2026-04-30).** Movements list is fully React at `/app/movements`. 302 redirect from `/Movements` → `/app/movements` is live; `Views/Movements/Index.cshtml` is deleted. |
| `AttachmentsController` | Serve, Delete | File serving needs special handling: streaming response, `Content-Disposition: attachment` |
| `HomeController` | Index | Deleted — replaced by React Router's root route |
| `CsvImportProfilesController` | Index, Create, Edit, Delete, Recover | **Migrated (2026-05-06).** Page actions 302-redirect to `/app/import/profiles*`. Razor views deleted; Razor-only `ImportProfileCreateViewModel` + `ImportProfileEditViewModel` deleted. CRUD lives on `/api/import-profiles`. Spec / plan: see Import row in §8 below. |
| `ImportController` | Index, Summary, SaveProfile | **Migrated (2026-05-06).** `GET /Import` and `GET /Import/Summary` 302-redirect to `/app/import`. Razor views and Razor-only `ImportUploadViewModel` + `ImportSummaryViewModel` deleted. The 3-step wizard, Save-as-profile prompt, and 5 result tiles live in `features/import/`. `POST /api/import` (`ImportApiController`) is unchanged from Phase 2. Spec / plan: see Import row in §8 below. |
| `TransferReviewController` | Index + Link/CreateAsTransfer/DismissAsTransaction | **Migrated (2026-05-06).** Page action 302-redirects to `/app/review?tab=transfers`. Razor view and `StagedTransferViewModel` deleted. Stateful actions consolidated into `TransferReviewApiController` (`link-to-existing` / `create-as-transfer` / `dismiss-as-transaction`); throwing service variants on `ITransferReviewService` removed. See `Review` row in §8 below. |
| `ReconciliationReviewController` | Index | **Migrated (2026-05-06).** Page action 302-redirects to `/app/review?tab=reconciliations`. Razor view and `StagedTransactionViewModel` deleted. Inline `Confirm match` + `Dispute` + `Confirm all` actions backed by `ReconciliationReviewApiController`; throwing service variants on `IImportStagedTransactionService` removed in favour of `Try*Async` (incl. new `TryConfirmAllAsync`). See `Review` row in §8 below. |

---

## 3. ViewModel → DTO strategy

Phase 2 ViewModels are already close to DTOs. At Phase 3:

- **Read ViewModels** (`*ListItemViewModel`, `*EditViewModel` used for GET) → become JSON response shapes
- **Write ViewModels** (`*CreateViewModel`, `*EditViewModel` used for POST) → become JSON request bodies with `[FromBody]`
- Validation stays on Data Annotations + `ModelState` — already works with API controllers, no change needed
- Error response shape is already defined in `api-contract.md` and wired via Phase 2's `InvalidModelStateResponseFactory` — no change needed

---

## 4. Auth integration point

The SPA migration and auth are tightly coupled. The migration plan must sequence these together:

- ASP.NET Core auth (Identity or custom) wired before any API endpoint requires `[Authorize]`
- `[Authorize]` is applied via a global fallback policy (`RequireAuthenticatedUser`) — not per-controller opt-in. `[AllowAnonymous]` is applied only to login, register, password reset, and the React SPA catch-all.
- HttpOnly cookie set on login; all subsequent API requests carry it automatically — no token management in JS
- CSRF protection: the XSRF-TOKEN double-submit pattern (non-HttpOnly `XSRF-TOKEN` cookie + `X-XSRF-TOKEN` request header) is required on all state-changing endpoints. **CORS does not prevent CSRF** — do not remove anti-forgery protection. See `security-model.md → CSRF` for the full pattern.
- CORS policy configured to allow the React origin (`localhost:5173` in dev, production origin in prod) — see `planning-phase3.md` for CORS rules

---

## 5. React Router route map

> **Note:** Routes marked with `*` are new in Phase 3 — no Razor equivalent exists. All others are ports of existing Razor pages. Verify this list is complete at kickoff.

```
/                           → Dashboard
/accounts                   → Accounts list
/accounts/new               → Create account
/accounts/:id/edit          → Edit account
/accounts/:id/ledger        → Account ledger (filtered Movements)
/movements                  → Unified ledger
/movements/new              → Create movement (transaction / transfer / liability payment)
/movements/:id/edit         → Edit movement
/budgets                    → Unified Budgets page (tabbed: Category / Goal)
/budgets/new                → Routed Create (?type=category|spending|savings)
/budgets/:id/edit           → Routed Edit (discriminator endpoint resolves CategoryBudget vs GoalBudget)
/categories                 → Categories list
/categories/new             → Create category
/categories/:id/edit        → Edit category
/recurring                  → Recurring transactions list
/recurring/new              → Create recurring transaction
/recurring/:id/edit         → Edit recurring transaction
/import                     → CSV import
/reports                    → Reports index
/reports/:type              → Specific report
/settings                   → User preferences
/settings/sessions          → Active sessions list *
/support                    → Support ticket form and list *
/login                      → Login (outside app shell) *
/login/totp                 → TOTP verification step *
/register                   → Registration *
/password-reset             → Password reset flow *
/onboarding                 → First-run wizard *
```

---

## 6. Program.cs changes

Specific changes required to the ASP.NET Core pipeline at Phase 3:

- `AddControllersWithViews()` → `AddControllers()` (API only, no view engine)
- Remove `UseStaticFiles()` for Razor/CSS assets; keep it only for file attachment serving
- Remove `MapControllerRoute()` (MVC attribute routing) → keep `MapControllers()` (API attribute routing only)
- Remove Vite.AspNetCore embedded dev-server middleware — React is now a standalone origin in dev, a static build served from `wwwroot/` in prod
- Add `UseCors()` with the whitelisted React origin
- Add `UseAuthentication()` + `UseAuthorization()` in the correct middleware order
- Add `app.MapFallbackToFile("index.html")` so React Router handles all non-API routes (Option A hosting only — see below)

---

## 7. Hosting model for the React SPA

**Decision (locked 2026-04-28): Option A.** React build output is copied to ASP.NET Core `wwwroot/` and served as static files with `app.MapFallbackToFile("index.html")` for React Router. One deployable unit, no separate web server. Simplest for the invite-only beta.

**Revisit at Phase 4** if a mobile client is added — that would warrant Option B (separate origins served by nginx or Caddy, ASP.NET Core as a pure API behind CORS).

Record the final decision in an ADR at implementation kickoff.

---

## 8. Migration sequencing

Do not delete Razor in a single "big bang" PR. Port feature area by feature area:

1. Build all new API endpoints and verify them with integration tests — Razor still exists as the running app
2. Build React pages against the live API endpoints — Razor pages remain as fallback
3. Once a feature area is fully ported and verified end-to-end in React, delete its Razor views and MVC actions
4. Repeat for each feature area, following the design system implementation order in `planning-phase3.md`
5. Remove MVC infrastructure from `Program.cs` last, once no Razor views remain

**Bilingual middleware (Stage 9):** `LanguagePreferenceMiddleware` (`ProjectCeres/Common/Localization/`) was added in Stage 9 and registered in `Program.cs` before `UseRouting`. It reads the `lang` cookie written by the SPA's language toggle and sets `CultureInfo.CurrentUICulture` so the remaining `Views/Account/*` Razor pages render in the user's chosen language. This middleware stays until Stage 11.8 deletes those last Razor views; at that point the middleware becomes a no-op and can be removed alongside the Razor infrastructure cleanup.

**Feature area porting order** (mirrors the design system implementation order):

1. Auth screens (login, TOTP, register, password reset) — no Razor equivalent; built fresh
2. Onboarding flow — no Razor equivalent; built fresh
3. Dashboard — highest visibility; `DashboardApiController` already exists
4. Transactions + Transfers + Movements — highest daily usage
5. Budgets + Reports
6. Accounts + Categories + Recurring Transactions
7. Settings + Session management + Support tickets

### Frontend execution batches — actual revised order (locked 2026-05-02)

The original feature-area order above describes the **architectural sequencing** — what depends on what at the API + auth + design-system layers. The actual frontend execution diverged for two reasons that became clear once Phase 2 wrapped:

1. **Most API endpoints already shipped in Phase 2.** Movements, Transfers, Budgets, Categories, Recurring, Reports, and CSV import all have working API controllers; the SPA migration is now mostly a frontend exercise. This means the frontend can run ahead of auth without being blocked.
2. **Sentinel-based pre-auth.** `SingleUserAccessor` (see `ProjectCeres/Common/ICurrentUserAccessor.cs`) stamps every user-owned row with `00000000-0000-0000-0000-000000000001` until real Identity-backed auth lands. This unblocks every SPA page from depending on the Auth/Onboarding work, which was originally sequenced first because it was assumed to be a hard prerequisite. It is not — at auth time, a one-shot data migration remaps the sentinel to the first registered user's real Id and the FK to `AspNetUsers` is added.

With those two facts established, the frontend was reorganised into batches:

**Batch 1 — Foundation (complete):**
- Design system tokens + `docs/design-system.md` (2026-04-29)
- App shell — sidebar, top bar, responsive behavior (2026-04-29)
- Dashboard SPA (2026-04-29)
- Movements + Transactions + Transfers SPA, including quick-add (2026-04-30 → 2026-05-01)
- Budgets SPA (2026-05-01)

**Batch 2 — Pattern-validation pages (in progress):** Settings was promoted to first because it is the smallest possible page (one form, one resource, no list, no nested routes) and its purpose was to **lock the SPA-page template** that every remaining management screen would copy. Categories followed as the second pilot, exercising the template against a more complex shape (list + nested Create/Edit routes + archive lifecycle + system-row treatment). The remaining five pages port that template area-by-area.

| # | Page | Status | Notes |
|---|------|--------|-------|
| 1 | Settings | ✅ Migrated 2026-05-02 (commit `e842250`) | First pilot — locked the `features/<area>/` + page+form split + Popover+Command picker idiom. |
| 2 | Categories | ✅ Migrated 2026-05-02 (commit `9578f9a` + follow-ups `132d5df`, `87f6709`) | Second pilot — exercised the template against list + nested CRUD + archive + system-row UX. |
| 3 | Accounts | ✅ Migrated 2026-05-03 (commit `03220b8`) | List + Create/Edit/Ledger; per-currency subtotal strip; type-driven balance colour; conditional Asset/Liability fields with two-layer interest-rate normalisation; SPA-side payoff projection; adaptive archive copy via `HasTransactions`. |
| 4 | Recurring | ✅ Migrated 2026-05-03 | Frequency rules + next-due-date computation; Confirm/Dismiss stateful actions; archive/reactivate lifecycle; TopBar bell wired via `ReminderCountProvider`; Weekly/Biweekly snap fix. |
| 5 | Reports | ✅ Migrated 2026-05-03 | 8 report pages + `ReportsLayout` + sticky filter bar + CSV export. `DateRangePicker` extracted as shared component. Razor views deleted; `ReportsController` actions → 302 redirects. |
| 6 | Review | ✅ Migrated 2026-05-06 (commit `<sha>`) | Unified `/app/review` with Reconciliations + Transfers tabs (deep-linked via `?tab=`). Reconciliations: inline `Confirm match` + `Dispute` row menu (AlertDialog) + `Confirm all` (AlertDialog, backed by new `TryConfirmAllAsync`). Transfers: per-card `Link to existing` / `Create transfer` / `Dismiss` (Link/Create open shared dialog with same-currency-filtered account picker; Dismiss fires immediately). Sidebar `Review` badge driven by new `ReviewCountProvider` over the two existing `pending/count` endpoints. Server-side: throwing CRUD variants dropped from `ITransferReviewService` and `IImportStagedTransactionService`; `StagedTransactionDto` and `StagedTransferDto` enriched with `AccountCurrencyCode` + `AccountCurrencySymbol`. Razor controllers slimmed to redirects (commits `7ba0423`, `101d79b`); views, Razor-only ViewModels, and `_Layout` pending-count badges deleted. Spec: `docs/superpowers/specs/2026-05-04-review-spa-design.md`. Plan: `docs/superpowers/plans/2026-05-04-review-spa.md`. |
| 7 | Import | ✅ Migrated 2026-05-06 | Two SPA surfaces: `/app/import` (3-step wizard — File + account → Mapping → Review → Result) and `/app/import/profiles` (list + nested `/new` and `/:id/edit`, archive/reactivate within the existing 90-day window). New SPA primitives: `FileDropzone` (single-file, drag-and-drop + click-to-browse + 10 MB guard) and `WizardStepper`. Header detection via `POST /api/import/headers`; submit posts multipart to `POST /api/import`; profile CRUD on `/api/import-profiles`. Save-as-profile prompt on the result step shows when no saved profile was used; format is inferred from the uploaded file's extension. Result tiles deep-link to `/review?tab=reconciliations`, `/review?tab=transfers`, and `/movements?needsReview=true`. Razor `ImportController` and `CsvImportProfilesController` slimmed to redirects (file-level cutover). Algorithmic upgrades (dual debit/credit columns, confidence-scored transfer detection, transfer-keyword settings) deferred to follow-up plans on top of this cutover. Plan: `docs/superpowers/plans/2026-05-06-import-spa-cutover.md` (transcribed from `~/.claude/plans/go-with-c-merry-perlis.md`). |

**Batches 3, 4, and 5 are predominantly server / security / launch-readiness work, not frontend Razor → React ports.** Their decomposition lives in [`planning-phase3.md` § Phase 3 execution batches](planning-phase3.md#phase-3-execution-batches):

- **Batch 3 — Auth + multi-tenancy.** Auth screens, TOTP, registration, password reset, onboarding wizard, plus the server-side identity infrastructure, multi-tenancy cutover, and email service they depend on. Sequenced after Batch 2 ships so the sentinel-to-real-user data migration is the only remaining identity concern, avoiding re-touching SPA pages to wire `useAuth` mid-flight.
- **Batch 4 — Razor + URL cleanup.** Drop the `/app/` prefix, add the one-shot `/app/*` → `/*` 301, delete every per-area 302 redirect added during migration, strip MVC infrastructure from `Program.cs`, delete the Razor host views. See [Final cleanup plan](#final-cleanup-plan-after-every-razor-view-is-gone) below for the detail.
- **Batch 5 — Launch readiness.** Sessions + Support SPA pages, GDPR baseline, HTTP security headers + CORS, identity masking, hosting/ops setup. The non-SPA items are the bulk of this batch.

### Per-area redirect rules (during migration)

When a feature area is fully ported and its Razor view is deleted, the corresponding Razor controller action stops returning a `View()` and starts returning a **302 (Found, temporary)** redirect to the SPA route under `/app/...`. Example pattern in the controller:

```csharp
public IActionResult Index() => Redirect("/app/");
```

ASP.NET Core's `Redirect()` defaults to 302. **Do not use 301 (`RedirectPermanent`) for these migration redirects** — 301s cache aggressively in browsers and search engines, which makes rollback during migration painful. The redirect itself is throwaway: it disappears entirely in the final cleanup (see below).

### Final cleanup plan (after every Razor view is gone)

Once no Razor views remain (the last feature is ported), a single dedicated cleanup plan does the following in one sweep:

1. **Drop the `/app/` prefix.** React Router `basename` changes from `/app` to `/`. The Razor host view's catch-all route changes from `app/{*path}` to `{*path}` (or equivalent fallback) so the SPA serves at `/` directly.
2. **Add one-shot 301 redirects** from `/app/*` to `/*` to catch external bookmarks and any cached deep links. These are 301 (permanent) because the move is genuinely permanent.
3. **Delete every per-area 302 redirect** (`/Dashboard`, `/Movements`, etc.) added during migration — they served their purpose and now point to URLs that no longer exist.
4. **Strip MVC infrastructure from `Program.cs`** as already noted in step 5 of the migration sequence above.
5. **Widen the Stage 6a architecture tests** that were narrowed to the API namespace because of legacy Razor controllers:
   - `No_api_controller_class_has_AllowAnonymous` → `No_controller_class_has_AllowAnonymous` (drop the `.Namespace?.Contains(".Api") == true` filter). Once `AppController` and `HomeController` are gone, no controller should have class-level `[AllowAnonymous]` anywhere.
   - `Api_HttpGet_actions_must_not_have_write_verb_names` → `HttpGet_actions_must_not_have_write_verb_names` (drop the same filter). With legacy 302-redirect controllers gone, no `[HttpGet]` action should start with write verbs (`Create`, `Update`, `Delete`, `Archive`, `Confirm`, `Dispute`, …) anywhere.
   - Both renames + filter removals are a single small commit. The Stage 6a spec at `docs/superpowers/specs/2026-05-09-stage-6a-identity-foundation-design.md` § 6 records the original full-scope intent.

6. **Frontend lint cleanup sweep** (logged 2026-05-13 after the `pnpm up --latest` upgrade to ESLint 10 / `typescript-eslint` 8.59.3 surfaced these). Run `pnpm --dir ProjectCeres.Client lint` in this branch as the baseline — at time of logging it returns **34 problems (32 errors, 2 warnings)**, all pre-existing, none Razor-related. Three categories to address in a dedicated cleanup PR:

   - **18× `react-hooks/set-state-in-effect`** (errors). New React 19 lint plugin rule flagging the "you might not need an effect" anti-pattern. Mix of false positives and real anti-patterns — each one needs a judgment call, do not bulk-fix:
     - **False positives — disable per-line with a `Why:` comment**: `src/app/lib/use-media-query.ts` (legitimate `matchMedia` subscription), `src/app/lib/use-delayed-loading.ts` (legitimate timer), `src/app/lib/use-api.ts` (legitimate fetch-on-URL-change). These are the canonical "syncing with external system" pattern the React docs explicitly endorse.
     - **Real anti-patterns — refactor to derive-during-render**: `src/hooks/usePagination.ts:32` (clamping `currentPage` against `totalPages`) is the textbook example from the React docs.
     - **Mid-risk form-reset logic — fix carefully with tests as safety net**: `src/app/features/movements/MovementForm.tsx:118`, `src/app/features/budgets/BudgetEdit.tsx:55`, `src/app/features/budgets/CategoryBudgetForm.tsx:77`, `src/app/features/budgets/GoalBudgetForm.tsx:71`, `src/app/features/import/StepMapping.tsx:65`, `src/app/features/recurring/RecurringConfirmDialog.tsx:31`, `src/app/features/recurring/RecurringDismissDialog.tsx:24`, `src/app/features/recurring/RecurringEdit.tsx`, `src/app/features/review/ReviewLayout.tsx`, `src/app/components/DataTransition.tsx:45`, `src/app/components/MoneyInput.tsx:64`, `src/app/features/movements/AttachmentDropzone.tsx:173`, `src/components/DateRangePicker.tsx`, `src/design-system/components/ThemeToggle.tsx:15`.

   - **14× `react-refresh/only-export-components`** (errors). Vite HMR rule — fires when a `.tsx` file exports something that isn't a component (type, context, helper, cva variants) alongside its component(s). Split by remediation path:
     - **shadcn-authored — disable per-file** (`/* eslint-disable react-refresh/only-export-components */` at top): `src/components/ui/badge.tsx`, `src/components/ui/button.tsx`, `src/components/ui/tabs.tsx`. These export `*Variants` cva objects alongside the component *by shadcn convention* — `pnpm dlx shadcn add` regenerates them in this shape, so splitting them creates non-standard files that will be clobbered on the next shadcn update.
     - **Provider files — split into `provider.tsx` + `context.ts`**: `src/app/features/review/ReviewCountProvider.tsx`, `src/app/layout/ReminderCountProvider.tsx`, `src/app/features/recurring/RecurringLayout.tsx`. Touches every consumer's import.
     - **Helpers/types — move to sibling `*-types.ts` or `*-utils.ts`**: `src/components/DateRangePicker.tsx` (4 hits), `src/app/features/movements/MovementsDateRangePicker.tsx` (3 hits), `src/app/features/movements/MovementsFilterBar.tsx` (1 hit).

   - **2× `react-hooks/exhaustive-deps`** (warnings) in `src/app/features/review/ReviewCountProvider.tsx` and `src/app/layout/ReminderCountProvider.tsx`. **Do not bulk-fix.** Each is either a real stale-closure bug *or* a deliberate capture-at-mount choice — needs case-by-case inspection.

   Why this is on the Razor-retirement list and not done piecemeal: the violations cluster in files that the SPA migration is already actively churning (provider files, form-reset effects, the `MovementsDateRangePicker` pair). Fixing them in-flight risks merge conflicts with the migration batches; doing them after the Razor cleanup avoids that and gives the cleanup PR a stable target.

Net result: a clean URL space (`/`, `/transactions`, etc.) with one one-shot `/app/*` → `/*` 301 catching legacy URLs. Both the migration 302s and the `/app/` prefix vanish, the architecture tests return to their full-scope form, and the frontend lint baseline drops to zero.
