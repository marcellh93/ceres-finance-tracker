# Changelog

## [Unreleased]

### Phase 3

#### Added

**Authentication (Stage 9.4 — `/password-reset` SPA pages, 2026-05-18)**
- Single SPA page mounted at `/password-reset` that dispatches by `location.hash`: empty/missing hash renders the request form (one email field + "Send reset link" submit + back-to-sign-in); `#token=<raw>` renders the confirm form (new + confirm password, optional TOTP cells revealed if the server responds `200 { requiresTotp: true }`)
- Honors the server's pre-existing URL convention from `PasswordResetService.cs:164` — reset link embeds the raw token in the URL fragment, not query, so the token never reaches the server logs or Referer headers
- Failure paths wired end-to-end: `401 INVALID_RESET_TOKEN` replaces the form with an invalid-token block + "Request a new link" link to `/password-reset`; `401 INVALID_MFA_CODE` shows an inline error on the TOTP cells without losing the password fields; `422 VALIDATION_ERROR` maps `details[]` to react-hook-form field errors (PwnedPasswords / policy violations surface inline)
- On `204` success: navigate to `/login?reset=1`; Login's existing `useEffect` toast handler now also fires `auth.login.toasts.passwordResetSuccess` ("Password reset. Sign in with your new password.")
- 11 vitest unit tests pin the contract: hash parsing (no token / empty token / valid token), request 204 success block, request 429 no-countdown variant, confirm zod mismatch (no fetch), confirm 204 navigate, requiresTotp two-step flow, INVALID_RESET_TOKEN block, INVALID_MFA_CODE preserves passwords, VALIDATION_ERROR maps to field errors
- `PasswordResetPlaceholder.tsx` deleted; its dead i18n keys at `auth.placeholders.passwordReset.*` removed in the same commit
- Spec: `docs/superpowers/specs/2026-05-18-stage-9-4-password-reset-design.md`

**Authentication (Stage 9.2 — `/login/totp` SPA page, 2026-05-17)**
- New `/login/totp` route mounted under the public `AuthLayout` centered-card branch; six-cell `InputOTP` shadcn primitive as the default state with auto-submit when 6 digits are typed or pasted; manual "Verify" button for backup-code mode
- Backup-code state reached via "Lost your device?" link toggle; uses a plain text input (alphanumeric); submit posts to the same `/api/auth/login/totp` endpoint per the server's existing contract
- Failure paths: `401 INVALID_MFA_CODE` renders the inline `auth.totp.errors.invalid` error and clears the cells; `401 ACCOUNT_LOCKED_OUT` navigates to `/account/unlock` (matches Login page parity); `401 UNAUTHENTICATED` navigates to `/login?expired=1` with a new sonner toast (`auth.login.toasts.totpExpired`); `429` reads `Retry-After` header and surfaces the countdown via `aria-live="polite"`, falling back to the no-countdown variant if the header is missing
- `submitTotp` page-local helper bypasses `apiFetch` only for this endpoint so the SPA can read response headers (CSRF prime + raw `fetch`); isolated to 9 lines, called out in the spec as the trade-off
- 10 vitest unit tests + 2 vitest-axe a11y tests (serious/critical filter via `expectNoA11yViolations`); pre-existing `Login.test.tsx` gains a sonner-mocked test asserting the `?expired=1` toast fires
- Test-infrastructure: `document.elementFromPoint` no-op polyfill added to `src/test-setup.ts` so input-otp's background timer no longer throws an Uncaught Exception under jsdom (was poisoning unrelated test files via cross-file pollution)
- Spec: `docs/superpowers/specs/2026-05-17-stage-9-2-login-totp-design.md`; plan: `docs/superpowers/plans/2026-05-17-stage-9-2-login-totp-impl.md`

**Authentication (Stage 9.3 — spec only, implementation in follow-up session, 2026-05-18)**
- `docs/superpowers/specs/2026-05-18-stage-9-3-register-and-email-verify-design.md` — comprehensive design for the `/register` SPA page + `/email-verify` token-consumption page + the server-side `EmailConfirmationToken` pipeline (entity + EF migration with TokenLookup column matching the Stage 6.15 / 9.1.5.a indexed-lookup pattern + service mirroring `PasswordResetService` line-for-line + two new HTTP endpoints + `RegistrationConfirmation` resx EN+ES + `EmailTemplateKey` enum addition + register-flow wiring + DI + ~10 integration tests + ~10 SPA tests)
- Splits the duplicate-username anti-enumeration notice INTO 9.3 scope (rather than deferring it to a still-undefined "Stage 6c follow-up" with no roadmap `[ ]` line) — the verification email itself IS the notice when sent to the existing account holder; per `feedback_deferral_requires_receiving_stage_checkbox`, deferring without a receiving entry isn't allowed
- Receiving entry / tripwire: the existing `[ ]` line at `docs/roadmap-phase-three.md:954` (Stage 9 sub-stage 9.3) — Stage 9 close-out cannot complete with 9.3 unticked

**Authentication (Stage 6 close-out — flow diagrams, 2026-05-11)**
- `docs/security-model.md § Authentication Flow Diagrams (Stage 6 close-out)` — single end-of-stage Mermaid set: request pipeline; registration; login (no-MFA / MFA TOTP / backup-code branches); password reset (request + confirm); reauth step-up + `[RequireRecentAuth]` gate; email-address change (request + confirm + revoke); lockout self-service unlock; audit-log writes overlay; cross-flow authentication state machine
- Each diagram paired with explicit audit prompts pointing at the integration tests that pin the behaviour
- With this in place, Stage 6 (Identity infrastructure, Batch 3b) is ✅ Done — all 12 sub-stages + 2 follow-ups shipped; 303/303 Authentication integration tests green; the Stage 6 verification checklist in `roadmap-phase-three.md` carries no `[ ]` items

**Authentication (Stage 6.12 — Email-address-change flow, 2026-05-10)**
- `POST /api/auth/email-change/request` (authenticated, `[RequireRecentAuth]`) — issues a 30-min VerifyNew token to the new address and a 7-day RevokeOld token to the old address; supersedes any prior pending pair
- `POST /api/auth/email-change/confirm` (anonymous, token-gated) — rewrites `Email` + `NormalizedEmail` + `UserName` + `NormalizedUserName`, sets `EmailConfirmed = true`, consumes both sibling rows atomically, revokes all `UserSession` rows, regenerates `SecurityStamp`, sends notifications to both new and old addresses; does NOT clear lockout (explicit divergence from password-reset)
- `POST /api/auth/email-change/revoke` (anonymous, token-gated) — consumes both sibling rows, leaves `user.Email` UNCHANGED, notifies old address only; sessions and `SecurityStamp` untouched
- `EmailChangeToken` entity with `Purpose` discriminator + `AddEmailChangeTokens` migration (single table, index parity with `PasswordResetTokens`)
- `EmailChangeService` with per-user `SemaphoreSlim` concurrency, per-new-email `MemoryCache` rate gate (5/hour)
- Cross-feature: a successful `/api/auth/password-reset/confirm` now atomically cancels any pending email-change for the same user and notifies the old address — closes the window where an attacker-initiated change with a still-live verify token could survive a victim's password-reset
- Canonical error codes: `INVALID_EMAIL_CHANGE_TOKEN` (401), `EMAIL_ALREADY_IN_USE` (422), `EMAIL_UNCHANGED` (422)
- 37-test integration ship-gate under `ProjectCeres.Tests/Integration/Authentication/EmailChange*`

**Design System**
- OKLCH color palette (light + dark) covering background, foreground, card, popover, primary (deep teal), secondary, muted, accent (pale teal), destructive (rose), success (emerald), warning (amber), info (sky), border, input, ring; tokens defined in `src/index.css` and aliased via `@theme inline`
- Inter Variable + IBM Plex Mono fonts self-hosted via `@fontsource-variable/inter` and `@fontsource/ibm-plex-mono`
- 8-color chart palette (`--chart-1` through `--chart-8`), all WCAG AA against `--background` in both modes
- Motion tokens: `--motion-duration-fast/base/slow`, `--motion-easing-standard/emphasized`
- Shadow tokens (`--shadow-sm` through `--shadow-lg`) tuned for both light and dark surfaces
- `<Numeric>` component for tabular numerics (mono + tabular-nums); enforcement mechanism — never apply `font-mono` directly
- `/design-system.html` showcase route with live token rendering, dark/light toggle, and pages for Overview, Colors, Typography, Spacing, Motion, Charts, Components
- Layout primitives: `<StatTile>` (vertical), `<StatRow>` (inline justify-between), `<EquationRow>` (compact muted caption) — codified in `src/components/`

**App Shell**
- React SPA mounted at `/app/` via ASP.NET Core catch-all route + Vite middleware; shell components: `AppLayout`, `Sidebar` (with collapse toggle + persisted state), `TopBar` (brand mark, global search, quick-add `+`, notifications, avatar menu), `MobileDrawer` for narrow viewports
- React Router v7 (BrowserRouter, basename `/app`) with route map for Dashboard, Movements, Transactions, Transfers, Accounts, Categories, Budgets, Recurring, Reports, Import, Settings, Support, Profile, Security
- Cmd+K / Ctrl+K keyboard shortcut to open global search modal; `useKeyboardShortcut` hook
- shadcn/ui (`base-nova` style) primitives installed: Button, Card, Dialog, Dropdown menu, Input, Label, Popover, Sheet, Skeleton, Switch, Tabs, Tooltip, Avatar, Badge, Kbd, Separator, Progress, Navbar
- Vitest + React Testing Library scaffold

**Dashboard**
- React Dashboard at `/app/` replacing Razor view; cards for Financial Health, KPI strip (Net Worth + Month-to-Date + Reminders), Category Budgets, Goal Budgets
- 4-panel asymmetric Financial Health card with vertical dividers; equation rows for Spendable Balance breakdown (Liquid, Bills due, Available today, Budget reserved, Safe to spend)
- Health card panel captions beneath headlines (Burn Rate "€143 / €350 spent", Runway "at €1000/mo", Income vs Avg "€3000 vs €2700 avg")
- MTD card rendered as 3 KPI tiles (Income / Expenses / Savings Rate) with `bg-muted/40` surfaces
- `useApi<T>` hook with `AbortController` cancellation on unmount and URL change
- Loading skeletons, error states with retry, and empty states on every card

**Backend**
- New `AppController` + `Views/App/Index.cshtml` host the SPA; catch-all route `app/{*path}` for client routing
- Typed `HealthSnapshotData` record (15 fields including `AvailableToday`, `SafeToSpend`, `RunwayMonths`, `AvgMonthlyExpense`, `CurrentMonthIncome`, `RollingAverageIncome`, `IncomeDeltaPercent`, `BudgetBurnRate`, `BudgetSpentMtd`, `BudgetTotalLimit`)
- Typed `DashboardSummaryDto` and `MtdSummary` records
- New `/api/dashboard/health` and `/api/dashboard/summary` endpoints in `DashboardApiController`
- `DashboardService.GetRunwayAsync` now returns `(months, avgMonthlyExpense)` tuple; `GetBudgetBurnRateAsync` returns `(burnRate, spent, totalLimit)` tuple

**Components**
- `<CardError section onRetry>` cross-feature primitive for "couldn't load X" + retry UI

**Movements**
- React Movements page at `/app/movements` with text search (debounced 300ms), account filter, date range filter, and numbered pagination (50/page); URL-encoded filter state (`?q=&accountId=&from=&to=&page=`); `useApi` `AbortController` handles request concurrency
- `MovementsTable`, `MovementsFilterBar`, `MovementsPagination`, `MovementClearedToggle` components under `src/app/features/movements/`
- `IMovementService.GetRecentAsync` and `CountAsync` accept `string? q` parameter; matches description OR category name for transactions, description-only for transfers and liability payments (PostgreSQL `EF.Functions.ILike`)
- `GET /api/movements` returning paged `MovementsPageDto`
- `MovementsApiController.PatchCleared` switch extended to handle `liabilitypayment` type (`ILiabilityPaymentService.MarkClearedAsync` added)
- Full Movements CRUD on the SPA at `/app/movements*`: routed Create page (`/movements/new` with type picker), routed Edit page (`/movements/:id/edit`) with danger-zone delete, row-level ⋯ menu (Edit/Delete), type filter dropdown, attachment upload (two-phase save-first), bulk mark-cleared, CSV export
- 22 typed API endpoints under `/api/transactions`, `/api/transfers`, `/api/liability-payments`, and `/api/movements` (see `docs/api-contract.md`)
- View-transition CSS hooks on movement rows and the form (animation upgrade is a follow-up)

**Quick-Add**
- `<QuickAddModal>` with three tabs (Transaction / Transfer / Liability Payment) wired to TopBar `+` button and Movements page header "+ New" button
- Currency symbol auto-derived from selected account, displayed as amount input prefix
- Inline 422 validation errors mapped from `ValidationProblemDetails` (PascalCase → camelCase key conversion)
- `AccountCombobox` and `CategoryCombobox` searchable selectors using shadcn Command + Popover
- `useDebounced<T>(value, delayMs)` hook for search-input debouncing

**Toasts**
- Sonner adopted as SPA-wide toast system; `<Toaster />` mounted in `AppLayout`; conventions documented in `docs/design-system.md`
- Toaster configured with `position="top-right"`, `closeButton`, `duration={5000}`, neutral popover background with semantic-colored icon (avoids dark-mode contrast issues from `richColors`)

**Backend**
- `POST /api/transactions`, `POST /api/transfers`, `POST /api/liability-payments` create endpoints
- `GET /api/accounts/active` and `GET /api/categories/active` combobox helper endpoints (return minimal `AccountOptionDto` / `CategoryOptionDto`)

**Charts**
- 5 dashboard chart components migrated from Razor islands to SPA (NetWorth Over Time, Income vs Expense, Spending by Category, Account Balances, Cash Flow); moved from `src/components/` to `src/app/features/dashboard/`
- `chartColors` util — semantic tokens (`income`, `expense`, `netWorth`, `assets`, `liabilities`) + `slot(n)` for chart palette; replaces hex literals in chart components
- `formatMonth(yyyyMm)` helper for chart axis labels — uses `Intl.DateTimeFormat` with `{ month: 'short', year: 'numeric' }` (e.g. "Apr 2026")
- Dashboard layout: NetWorth chart full-width as headline trend, then 2x2 grid for IncomeExpense / SpendingByCategory / AccountBalances / CashFlow
- All 5 chart endpoints converted to typed wrapper DTOs with `currencyCode` and `currencySymbol`: `NetWorthTrendDto`, `IncomeExpenseDto`, `SpendingByCategoryDto` (adds `total` for % calculations), `AccountBalancesDto`, `CashFlowDto`
- Trend endpoints (`net-worth-trend`, `income-expense`, `cash-flow`) extended from 6-month to 12-month window

**Design System (v1.2)**
- shadcn `<Badge>` extended with `success`, `warning`, `info` semantic variants matching the existing soft-tinted `destructive` recipe
- `<Tile>` KPI surface wrapper extracted from `MtdCard` into `src/components/Tile.tsx`
- Showcase `Toasts` page (Sonner variants + 5-stack queueing demo)
- Showcase `Patterns` page covering layout primitives (StatTile, StatRow, EquationRow, Tile) and CardError; existing Stat components moved out of Components page

**Docs**
- `docs/design-system.md` — Status badges section, Layout primitives section, CardError section, Skeleton convention paragraph, Known browser console messages section (SES + WebSocket + Recharts width warnings)

**Budgets**
- Full Budgets CRUD on the SPA at `/app/budgets`: unified tabbed list (Category / Goal), `+New` dropdown (Category Budget / Spending Goal / Savings Goal), routed Create + Edit pages with discriminator-based routing, archive lifecycle with one-click reactivate, "Show archived" toggle, conflict-aware Create flow that offers to reactivate existing archived budgets
- `Settings.BudgetPeriodStartDay` (1–31, default 1) — all category-budget actual-spend math respects the configured cycle. Configurable via the existing Razor Settings page (the SPA Settings page migration is a follow-up)
- 16 typed API endpoints under `/api/category-budgets`, `/api/goal-budgets`, `/api/budgets`, and `/api/currencies` (see `docs/api-contract.md`)
- Movement form gains a conditional Spending-Goal picker so transactions can be tagged toward Spending goals — visible only when ≥1 active matching goal exists in the transaction's currency

#### Changed

**Settings / Dashboard cycle math**
- Renamed `Settings.BudgetPeriodStartDay` to `Settings.PeriodStartDay` to reflect that it now drives every monthly view (Cycle to Date, Spending by Category, Income vs. Avg, Budget periods, etc.), not just budgets. UI label is now "Monthly cycle start day."
- "Month to Date" dashboard card renamed to "Cycle to Date" — its window now follows the user's configured monthly cycle, not strict calendar months.
- "Spending by Category" dashboard card subtitle is now "This period" (was "This month") and respects the configured cycle.

**Dashboard**
- `CategoryBudgetBars` and `GoalBudgetBars` refactored to render bare body without owning Card chrome (parent owns chrome)
- `CardTitle` accessibility upgrade: `<div>` → `<h3>` with `font-semibold`

**Movements**
- `MovementsTable` Type column migrated to `<Badge>` — Transaction = info, Transfer = chart-6 violet, Liability Payment = chart-7 orange (chart palette opt-out via className for non-status colors)
- `MovementClearedToggle` migrated to `<Badge variant="success|warning">`

**Components**
- `<CardError>` moved from `src/app/features/dashboard/` to `src/app/components/` (cross-feature primitive); 10 import paths updated via `git mv`
- shadcn `<Button>` primitive: `cursor-pointer` added to base classes (every interactive button across the SPA)

**Frontend**
- `AppLayout` grid bounded with `h-screen` (was `min-h-screen`) so only `<main>` scrolls and sidebar footer (Settings/Support/Collapse) stays pinned
- `TopBar` Cmd+K hint shows `Ctrl+K` on non-Mac platforms via `isMac()` runtime check
- All 5 chart `<ResponsiveContainer>` instances given `minWidth={0}` to silence Recharts "width(-1)" warnings
- tsconfig: deprecated `baseUrl` removed (TypeScript bundler resolution handles `paths` without it)

**Razor**
- `/Dashboard` now 302-redirects to `/app/`; Razor dashboard view (`Index.cshtml`), partial (`_HealthSnapshot.cshtml`), and MVC `DashboardController` deleted
- `data-react` mounting blocks for the 5 chart selectors removed from the legacy Razor `main.tsx`
- `/Movements` now 302-redirects to `/app/movements`; Razor `Views/Movements/Index.cshtml` deleted; MVC `MovementsController` reduced to redirect-only stub
- Razor `TransactionsController` and `TransfersController` page actions (Index/Create/Edit/Delete) now 302-redirect to the SPA at `/app/movements*`
- TopBar quick-add button and keyboard shortcut suppressed on `/app/movements*` routes (the routed Create page replaces the modal there)
- Razor `BudgetsController` page actions (Index, Goals, Create, CreateGoal, Edit, EditGoal, Deactivate, DeactivateGoal) now 302-redirect to the SPA at `/app/budgets/*`

**Budgets**
- `ICategoryBudgetService.GetActualSpendAsync(id, year, month)` semantics shift: `(year, month)` now identifies the period whose end falls in that calendar month, computed via the `BudgetPeriod` helper. Behavior unchanged for the default `BudgetPeriodStartDay = 1`

#### Fixed

**Authentication (Stage 6c.2 follow-up — `AuthMfaByUser` rate-limit partition fix, 2026-05-11)**
- `AuthMfaByUser` rate-limit policy now correctly partitions per user. The prior implementation read `httpContext.User?.FindFirst(NameIdentifier)` before `UseAuthentication` ran, so every authenticated MFA request fell into the `"anonymous-mfa"` shared bucket and one hostile user could exhaust the budget for everyone. Fix mirrors `AuthReauthByUser` (shipped in 6c.2): explicit `httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme).Wait()` before reading the claim, applied to both `Program.cs` and the `RateLimitedAuthTestWebApplicationFactory` override.
- New `MfaRegenerate_rate_limit_is_partitioned_by_user` test pins per-user isolation: drains user A's budget then asserts user B's first call is NOT 429.

**Authentication (Stage 6.15 — Argon2id-amplification DoS on token verify, 2026-05-11)**
- Argon2id-amplification DoS vector on `/password-reset/confirm`, `/email-change/confirm`, and `/email-change/revoke` closed via indexed `TokenLookup` column. Verify path is now O(1) regardless of token table size; pre-6.15 each call ran one Argon2id verify per unconsumed unexpired row (~20s at N=200 under OWASP-minimum params).
- New `TokenLookupHasher` singleton computes `HMAC-SHA256(Authentication:TokenLookupSecret, rawToken)`; new column added to `PasswordResetToken` and `EmailChangeToken` with a unique index per table; existing rows backfilled and stamped `ConsumedAt = NOW()` so legacy tokens cannot match real verifies.
- Test-infrastructure crutch removed: `AuthTestTokenCleanup.DeleteAllTestTokensAsync` + 12 `IAsyncLifetime.DisposeAsync` hooks deleted. The full Authentication integration suite (298 tests) now stays green under accumulated token load, proving the production fix is real rather than masked by between-test cleanup.

**Quick-Add**
- Quick-add modal: per-tab fields (account, category, source/dest, asset/liability) reset on tab switch so a stale selection from another tab can't leak in (shared fields like date, amount, description still persist)
- Combobox labels rendered as `<div>` instead of `<label>` since there's no input element to associate (fixes a11y "label without for" warning)

#### Removed

**Razor**
- `ProjectCeres/Helpers/DashboardViewHelper.cs` (server-side runway color helper) — only consumer was the deleted `_HealthSnapshot.cshtml` partial
- `ProjectCeres.Tests/DashboardViewHelperTests.cs`
- Razor views for Transactions and Transfers (Index, Create, Edit, Delete) and `Views/Shared/_AttachmentWidget.cshtml` deleted
- Razor views for Budgets (8 files under `Views/Budgets/`) deleted
- `BudgetsController` POST overloads (Create, CreateGoal, Edit, EditGoal, Deactivate, DeactivateGoal) removed entirely — the SPA POSTs JSON to the new `/api/category-budgets` and `/api/goal-budgets`

---

## [0.3.0] — 2026-04-26

### Phase 2

#### Added

**Transactions**
- Goal Budget field on Transaction Create and Edit forms is now hidden when no active Spending-type goal budgets exist — avoids showing an empty, non-functional dropdown
- Goal Budget field on Transaction Edit hidden when no Spending budgets match the transaction's account currency — prevents tagging a transaction to a mismatched budget
- `NeedsReview bool` column on `Transaction` model — set to `true` by `ImportService` when an imported row is flagged as a duplicate candidate; defaults to `false` for all manually created transactions
- `ITransactionService.MarkNeedsReviewAsync` — sets or clears `NeedsReview` on a transaction by ID
- `TransactionService.MarkNeedsReviewAsync` — implementation of the above
- `NeedsReview` field added to `TransactionListItemViewModel` and `TransactionEditViewModel`; mapped through `GetRecentAsync` and `UpdateAsync`

**Typography**
- Inter variable font self-hosted under `wwwroot/fonts/inter/` — two files cover all weights and italic variants
- IBM Plex Mono self-hosted under `wwwroot/fonts/ibm-plex-mono/` — Regular, Italic, Medium, MediumItalic, SemiBold, SemiBoldItalic, Bold, BoldItalic weights
- Inter set as the global body font for all UI text (labels, headings, buttons, body copy)
- IBM Plex Mono applied to `.amount-income`, `.amount-expense`, and `.amount-neutral` CSS classes — numeric columns in transaction and movements tables now render in monospace for clean vertical digit alignment

**Migrations**
- `AddTransactionNeedsReview` — adds `NeedsReview boolean NOT NULL DEFAULT FALSE` to the `Transactions` table

**Tests**
- `ClearedBadge.test.tsx` — 2 new tests: `needsReview = true` renders "Needs review" badge; `isCleared = true` with `needsReview = true` still renders "Cleared" (cleared state takes priority)

#### Changed

**Transactions**
- Goal Budget label updated to "Goal Budget (must match account currency)" on both Create and Edit forms
- `PopulateViewBagAsync` refactored to accept an optional `currencyFilterAccountId` parameter; on Edit, filters Spending budgets to those matching the selected account's currency; sets `ViewBag.Budgets = null` (hiding the field) when no qualifying budgets exist

**CSV Import**
- `ImportService.ImportAsync` — flagged duplicate rows now call `MarkNeedsReviewAsync(true)` in addition to leaving `IsCleared = false`; the `NeedsReview` flag drives the badge in the Transactions Index

**Movements**
- `ClearedBadge` component updated with a third render state: when `isCleared = false` and `needsReview = true`, renders an amber `AlertTriangle` badge labelled "Needs review" instead of the `Clock` "Pending" badge
- `ClearedBadge` now accepts optional `needsReview` prop (defaults to `false`); existing callers (Movements, Transfers Index) are unaffected
- `main.tsx` — `ClearedBadge` mount now reads `data-needs-review` dataset attribute and passes it as the `needsReview` prop
- `Transactions/Index.cshtml` — cleared badge mount point now emits `data-needs-review` from `item.NeedsReview`

**CsvImportProfiles**
- `CsvImportProfiles/Index.cshtml` — deleted profile countdown wording corrected to "Recoverable for X more day(s)"; Recover button SVG updated to the correct Lucide `rotate-ccw` path

#### Fixed

**Tests**
- `TransactionServiceTests` — 4 new integration tests: budget currency mismatch on Create throws, budget currency match on Create succeeds, same two cases for Update
- `BudgetServiceTests` and `GoalBudgetServiceTests` — constructor call updated to pass `IAccountService` as the second argument; pre-existing compilation error surfaced when the test project compiled after the constructor signature change
- `GoalBudgetServiceTests.GetProgressAsync_SavingsGoal_BalanceGrowsWithTransactions` — test was seeding an Expense transaction to grow a Savings account balance; corrected to use the Salary (Income) category so the transaction correctly increases the account balance

---

#### Added

**Budgets**
- `CategoryBudgetBars` component upgraded to use shadcn `Card`, `CardHeader`, `CardTitle`, `CardContent`, `Progress`, and `Badge` — progress bars now colour-coded green/amber/red by percent used
- `GoalBudgetBars` component upgraded to use shadcn `Card`, `CardHeader`, `CardTitle`, `CardContent`, and `Progress` — goal type shown as a blue pill badge matching the Goals index table
- Dashboard layout updated with two side-by-side mount points (`data-react="category-budget-bars"` and `data-react="goal-budget-bars"`) wired in `main.tsx`

**Movements**
- Type column in Movements table now renders colour-coded pill badges — blue for Transaction, purple for Transfer, orange for Liability Payment
- `CategoryTypeName` field added to `MovementListItemViewModel`; projected from `t.Category.CategoryType.Name` in `MovementService.QueryTransactions` via `ThenInclude`
- Amount column in Movements table now colour-coded — green for Income, red for Expense (matching the Transactions table), neutral for Transfers and Liability Payments

**Reports**
- `BudgetVsActualReportGenerator` — compares active category budget limits against actual spend for a given currency and date range; returns per-category rows with limit, actual, variance, and percent used
- `LargestExpensesReportGenerator` — returns top N expense transactions ordered by amount descending for a given currency and date range; respects `Limit` parameter (default 50)
- `MonthlyCashFlowReportGenerator` — returns income, expenses, and net grouped by calendar month for a given currency and date range; defaults to last 6 months when no range supplied
- `NetWorthOverTimeReportGenerator` — returns cumulative asset, liability, and net worth snapshots at the end of each month for a given currency and date range; defaults to last 12 months
- `ReportsController` actions: `BudgetVsActual`, `LargestExpenses`, `MonthlyCashFlow`, `NetWorthOverTime` — each reads from the corresponding generator with currency/date defaults from Settings
- `Views/Reports/BudgetVsActual.cshtml` — filter bar + table with limit/actual/variance/% used columns; over-budget rows highlighted in red
- `Views/Reports/LargestExpenses.cshtml` — filter bar with Top N selector (10/25/50/100) + table with date, description, category, account, amount
- `Views/Reports/MonthlyCashFlow.cshtml` — filter bar + month-by-month table with income, expenses, net columns; period totals in tfoot
- `Views/Reports/NetWorthOverTime.cshtml` — filter bar + monthly snapshot table with assets, liabilities, net worth columns
- Reports Index updated with links to all four new reports
- `ReportTypeKey` enum extended with `BudgetVsActual = 5`, `LargestExpenses = 6`, `MonthlyCashFlow = 7`, `NetWorthOverTime = 8`
- `_ViewImports.cshtml` — `@using ProjectCeres.Services.Reports` added so report row record types are available in all views

**Migrations**
- `Stage7ReportTypeSeed` migration — inserts `ReportType` rows for the four new report types (IDs 5–8); `Up()` contains only `InsertData` operations, no schema changes

**Docs**
- Developer guide updated: `GroupBy` with anonymous object key and `GroupBy` + `Select` summary pattern in LINQ file; cumulative snapshot pattern (one-query-then-filter-in-memory) in EF Core querying file; extending the factory checklist and enum–DB alignment rule in factory pattern file; seeding lookup table rows pattern with four-step workflow in migrations file

**Tests**
- 16 integration tests for the four Stage 7 generators: `BudgetVsActual` (returns correct plan vs. actual, excludes out-of-range transactions, excludes inactive budgets, shows zero actual when no spend), `LargestExpenses` (orders by amount desc, respects limit, excludes income, excludes out-of-range), `MonthlyCashFlow` (groups by month, excludes system transactions, filters by currency, omits months with no activity), `NetWorthOverTime` (monthly snapshots, includes liabilities, filters by currency, snapshots are cumulative)
- 4 factory dispatch unit tests added to `ReportGeneratorFactoryTests` — one per new generator

#### Changed

**Reports**
- `ReportGeneratorFactory` constructor extended with four new generator parameters; switch extended with four new cases
- `ReportsController` — injects the four new generators directly as constructor parameters (not via factory) since each has a dedicated action
- `AppDbContext.HasData` — `ReportType` seed extended from 4 rows to 8 rows
- `Program.cs` — four new `AddScoped` registrations for Stage 7 generators

**Budgets**
- `BudgetService` constructor updated to accept `IAccountService`; `GetAccountBalanceAsync` now delegates to `AccountService.GetBalanceAsync` — fixes Savings goal progress showing incorrect balance (transfers were excluded)
- `Budgets/Index.cshtml` — Card + Table layout, status as coloured pill badge, Edit button has pencil icon, Deactivate button has power-off icon, New button has plus icon
- `Budgets/Goals.cshtml` — same Card + Table upgrade; GoalType shown as blue pill badge; Edit/Deactivate icons
- `Budgets/Create.cshtml` and `Edit.cshtml` — wrapped in `dashboard-card`, form labels styled, Save/Cancel buttons have check/x icons
- `Budgets/CreateGoal.cshtml` and `EditGoal.cshtml` — same form styling upgrade; conditional Linked Account field preserved
- `Budgets/Deactivate.cshtml` and `DeactivateGoal.cshtml` — descriptive confirmation card with power-off icon on the confirm button

**Transactions**
- `Transactions/Index.cshtml` — Edit/Delete row buttons upgraded to `btn-sm` with pencil/trash-2 icons; New Transaction button has plus icon
- `Transactions/Edit.cshtml` — wrapped in `dashboard-card`, form labels styled, attachment list has download/trash icons, Save/Cancel buttons have check/x icons

**Transfers**
- `Transfers/Index.cshtml` — Edit/Delete row buttons upgraded with pencil/trash-2 icons; New Transfer button has plus icon
- `Transfers/Edit.cshtml` — wrapped in `dashboard-card`, form labels styled, attachment list has download/trash icons, Save/Cancel buttons have check/x icons

**Movements**
- `Movements/Index.cshtml` — Edit/Delete row action buttons upgraded with pencil/trash-2 icons; static cleared/pending spans for Liability Payments converted to pill badge style

**Frontend**
- File input (`input[type="file"].form-control`) globally styled in `app.css` using Tailwind `file:` pseudo-element utilities — picker button now shows as a styled pill with a right-border divider, matching the rest of the form controls

**Docs**
- Developer guide updated: `file:` pseudo-element utilities for styling native file inputs (Tailwind guide); `Progress` + `Card` data-display panel pattern (shadcn/ui guide); `ThenInclude` formal definition for multi-level eager loading (EF Core querying guide)

---

#### Added

**Recurring Reminders**
- `ReminderBehaviour` dispatch in `RecurringTransactionService` — `ConfirmAsync` and `DismissAsync` now route date advancement through three strategies: `SnapToCalendarDay` (advances to `DayOfPeriod` in the next calendar month, skips an extra month if confirmed on or after that day), `RelativeToLastConfirmation` (advances from the actual confirm date rather than the scheduled due date), `ManualDate` (throws `InvalidOperationException` unless a `nextDueDate` is supplied)
- `IRecurringTransactionService.GetUpcomingAsync(int withinDays)` — returns active reminders with `NextDueDate` between today and today + N days, ordered by due date
- `RecurringTransactionsController.Upcoming` action — serves the Upcoming Payments view at `/RecurringTransactions/Upcoming`
- `Views/RecurringTransactions/Upcoming.cshtml` — table of reminders due within 30 days; rows due today highlighted with an amber "Due today" badge
- Navbar upcoming-payments badge — server-rendered count of reminders due within 30 days passed to the React `Navbar` component via a `data-upcoming-count` attribute on `#navbar-root`

**Docs**
- Developer guide updated: string-based switch expression dispatch, `@inject` in `_Layout.cshtml` for layout-level service calls, `data-*` attribute bridge for passing server values to React components, `DateOnly` range filter pattern in EF Core queries

**Tests**
- 5 integration tests for `ReminderBehaviour` advancement: `SnapToCalendarDay` on-time → correct next month snap, `SnapToCalendarDay` confirmed late → skips forward an extra month, `RelativeToLastConfirmation` monthly → advances from confirm date, `ManualDate` without `nextDueDate` → throws, `ManualDate` with `nextDueDate` → sets exact date
- 1 integration test for `GetUpcomingAsync` — reminders due today and in 5 days included; reminder due in 35 days excluded

#### Changed

**Recurring Reminders**
- `IRecurringTransactionService.ConfirmAsync` — signature extended with optional `DateOnly? nextDueDate` parameter (backward-compatible default `null`)
- `RecurringTransactionCreateViewModel` / `RecurringTransactionEditViewModel` — `ReminderBehaviour` field added (defaults to `"SnapToCalendarDay"`)
- `RecurringTransactionService.CreateAsync` / `UpdateAsync` — `ReminderBehaviour` now persisted from ViewModel
- `Views/RecurringTransactions/Create.cshtml` and `Edit.cshtml` — `ReminderBehaviour` selector added; inline JavaScript hides `DayOfPeriod` field when `ManualDate` is selected
- `Views/RecurringTransactions/Confirm.cshtml` — `nextDueDate` date picker rendered when reminder's behaviour is `ManualDate`
- `RecurringTransactionsController.Confirm` POST — accepts optional `nextDueDate` parameter and forwards it to `ConfirmAsync`
- `_Layout.cshtml` — injects `IRecurringTransactionService` to compute upcoming count server-side; count embedded as `data-upcoming-count` on `#navbar-root`
- `main.tsx` — reads `data-upcoming-count` from `#navbar-root` and passes it as `upcomingPaymentsCount` prop to `<Navbar>`

---

**Accounts**
- `ILiabilityProjectionService` / `LiabilityProjectionService` — pure calculation service for amortising loan payoff projection; computes months to payoff, payoff date, total interest, and total paid given balance, annual rate, and monthly payment; throws when payment does not cover first month's interest
- Payoff projection panel on the Account Ledger view for Amortising accounts — form accepts a monthly payment amount and returns projection stats (payoff date, months, total interest, total paid); only rendered when account is Amortising with a non-zero balance
- `LiabilityProjectionViewModel` — read model carrying `MonthsToPayoff`, `PayoffDate`, `TotalInterest`, `TotalPaid`, `MonthlyPayment`

**Docs**
- Developer guide updated: pure calculation service pattern (no `DbContext` dependency, directly unit-testable), service-layer business rule validation via `InvalidOperationException`, conditional field visibility via inline JavaScript in Razor views

**Tests**
- 5 integration tests for `AccountService` — Amortising with null interest rate rejected, FullMonthly with interest rate rejected, Amortising with valid rate succeeds, `UpdateAsync` variants for both failure cases
- 5 unit tests for `LiabilityProjectionService` — known inputs verify payoff and interest range, extra payment yields earlier payoff and less interest, zero interest rate pays off in balance ÷ payment months, very small balance pays off in 1 month, payment too small to cover interest throws

#### Changed

**Accounts**
- `AccountCreateViewModel` — added `LiabilityRepaymentType` and `InterestRate` fields
- `AccountEditViewModel` — added `LiabilityRepaymentType` and `InterestRate` fields
- `AccountService.CreateAsync` — validates repayment type / interest rate rules for Liability accounts before saving; maps `LiabilityRepaymentType` and `InterestRate` onto the new entity
- `AccountService.UpdateAsync` — same validation and mapping on edit; loads `AccountType` via `Include` to access the type name
- `AccountsController` — injects `ILiabilityProjectionService`; `Edit` GET passes `ViewBag.IsLiability`; `Ledger` GET and POST compute and pass projection via ViewBag when account is Amortising with a positive balance
- `Views/Accounts/Create.cshtml` — liability repayment type selector and interest rate field added; JavaScript show/hide driven by account type and repayment type selectors
- `Views/Accounts/Edit.cshtml` — liability repayment type selector and interest rate field added (server-side conditional on `ViewBag.IsLiability`); JavaScript toggles interest rate field based on repayment type
- `Views/Accounts/Ledger.cshtml` — payoff projection panel added; rendered only for Amortising accounts with a positive balance
- `Program.cs` — `ILiabilityProjectionService` registered as scoped

---

**CSV Import**
- `ICsvImportProfileService` / `CsvImportProfileService` — full CRUD for import profiles with soft-delete and 90-day recovery window; column mappings stored as `jsonb` and deserialized to `CsvColumnMappings`
- `CsvImportProfilesController` — Index, Create, Edit, Delete (soft-delete), Recover; Razor views with Lucide icons
- `IImportService` / `ImportService` — CSV parsing via CsvHelper with user-configured column mappings, optional debit sign flip, and SHA-256 fingerprinting for duplicate detection
- `ImportApiController` at `POST /api/import` — accepts multipart form with CSV file and column mapping parameters; returns `ImportResult` JSON
- `ImportController` — Razor upload form with profile selector and manual column mapping fields; Summary page showing imported/flagged/failed counts
- `Views/Import/Index.cshtml` and `Summary.cshtml` — upload form and per-category result summary
- `CsvColumnMappings` ViewModel — carries user-configured column names and flip-debit-sign flag
- `ImportResult` ViewModel — carries `RowsImported`, `RowsFlagged`, `RowsFailed`, and a list of row-level error messages
- `ParsedImportRow` ViewModel — intermediate row produced by `ParseAsync` before persistence
- `ImportRequestViewModel` — model-bound from the multipart form for `ImportApiController`
- `ImportUploadViewModel` / `ImportSummaryViewModel` — ViewModels for the Razor upload and summary pages
- Fixture files: `valid_import.csv`, `duplicate_candidates.csv`, `invalid_rows.csv`, `xlsx_attempt.xlsx` — used by unit and integration tests; registered with `CopyToOutputDirectory: PreserveNewest`

**Docs**
- Developer guide updated: CSV parsing with CsvHelper, SHA-256 fingerprinting, soft-delete with time-bounded recovery, `jsonb` column mapping in EF Core, `Mock<IFormFile>` with `CopyToAsync` setup, fixture files via `CopyToOutputDirectory`, optional constructor parameters for partial unit testability

**Tests**
- 5 integration tests for `CsvImportProfileService` — create/retrieve, soft-delete exclusion from active list, 90-day purge window, update mappings, recover within window
- 6 unit tests for `ImportService` — `ParseAsync` with valid CSV, debit sign flip, custom column mapping, XLSX rejection, `GenerateFingerprint` determinism, fingerprint sensitivity to amount change
- 3 integration tests for `ImportService.ImportAsync` — 10 rows all cleared, duplicate flagged as `IsCleared = false`, result count correctness
- 3 `WebApplicationFactory` tests for `ImportApiController` — shape test, missing `accountId` → 422 with `VALIDATION_ERROR`, `.xlsx` file → 400 with message

**Movements**
- `MovementsApiController` at `PATCH /api/movements/{id}/cleared` — routes to `ITransactionService` or `ITransferService` based on `type` field in request body; returns 404 for unknown id, 400 for unknown type
- React `ClearedBadge` component — clickable badge that calls `PATCH /api/movements/{id}/cleared`, flips state optimistically on click, and reverts to original state on API error or network failure
- `MovementsApiTests` — 5 `WebApplicationFactory` integration tests covering transaction clear, toggle back to false, transfer clear, unknown id → 404, and unknown type → 400
- `ClearedBadge.test.tsx` — 5 Vitest tests covering static rendering (Cleared/Pending), PATCH call correctness, optimistic flip, revert on HTTP error, and revert on network error
- `ClearedBadge` mount point in `main.tsx` — mounts from `[data-react="cleared-badge"]` elements; reads `data-id`, `data-movement-type`, and `data-cleared` dataset attributes
- `MovementsController` with `Index` action — unified ledger showing all three movement types (Transaction, Transfer, LiabilityPayment) interleaved, with account/date filters and pagination
- `Views/Movements/Index.cshtml` — unified table rendering all three row types with type-specific column display, cleared badge, and Edit/Delete links that pass `returnUrl=/Movements`
- `MovementsControllerTests` — 10 `WebApplicationFactory` tests covering: `GET /Movements` returns 200, Transaction/Transfer Edit and Delete redirect to `returnUrl` when present and local, fall back to own Index when absent, and open redirect safety (external URL rejected)
- `WafCollection.cs` — `[CollectionDefinition("IntegrationTests")]` grouping all 19 integration test classes into one xUnit collection to prevent parallel races on `project_ceres_test`

**Security**
- Open redirect rule added to `docs/security-model.md` under Input Validation Rules and the phase table — `Url.IsLocalUrl(returnUrl)` required on every action that accepts a `returnUrl` parameter

**Budgets**
- `ICategoryBudgetService` / `CategoryBudgetService` — monthly spend caps for expense categories; guards against non-expense categories and duplicate active budgets
- `CategoryBudgetService.GetActualSpendAsync(id, year, month)` — caller-specified period for current dashboard use and future Budget vs. Actual reports
- `BudgetsController` — single controller covering Category Budgets (Index, Create, Edit, Deactivate) and Goal Budgets (Goals, CreateGoal, EditGoal, DeactivateGoal)
- `BudgetProgressResult` model — computed `Remaining` and `PercentUsed` properties; never stored as columns
- Goal Budget `GoalType` and `LinkedAccountId` — two archetypes: Spending (sums tagged transactions) and Savings (reads linked account balance)
- `BudgetService.GetProgressAsync` — returns `BudgetProgressResult` for a goal budget; routes to transaction-sum or account-balance query based on `GoalType`
- `DashboardApiController` at `/api/dashboard/category-budgets` and `/api/dashboard/goal-budgets` — JSON endpoints for React components
- React `CategoryBudgetBars` component — fetches category budgets, renders progress bars colored green/amber/red by percent used
- React `GoalBudgetBars` component — fetches goal budgets, renders progress bars in blue/green

**Views**
- Category Budgets CRUD views: Index, Create (expense categories only), Edit, Deactivate confirmation
- Goal Budgets CRUD views: Goals index, CreateGoal, EditGoal (with inline JS show/hide for LinkedAccount field), DeactivateGoal confirmation

**Architecture Decision Records**
- ADR-0056 — `GetActualSpendAsync` caller-specified year/month signature
- ADR-0057 — single `BudgetsController` with documented refactor trigger conditions

**Tests**
- `TestWebApplicationFactory` — custom `WebApplicationFactory<Program>` subclass that overrides `ConnectionStrings:DefaultConnection` to `project_ceres_test` in `ConfigureWebHost`; replaces bare `WebApplicationFactory<Program>` as the shared collection fixture, making test database isolation structural rather than per-class
- 11 integration tests for `CategoryBudgetService` (all guards, actual spend calculation, deactivate, getAll)
- 8 integration tests for `GoalBudgetService` (GoalType validation, GetProgressAsync for both archetypes)
- 2 `WebApplicationFactory` tests for `DashboardApiController` verifying JSON shape
- 4 Vitest component tests for `CategoryBudgetBars` and `GoalBudgetBars` (mocked fetch, async DOM assertions)

**Transfer Attachments**
- `IFileAttachmentService` extended with three new methods: `UploadForTransferAsync`, `GetTransferAttachmentAsync`, `DeleteTransferAttachmentAsync` — same MIME whitelist and size limit as transaction attachments; transfer files stored under `uploads/transfers/{transferId}/`
- `AttachmentsController` — three new actions: `UploadForTransfer` (POST), `DownloadTransfer` (GET), `DeleteTransfer` (POST)
- Attachment section added to `Views/Transfers/Edit.cshtml` — file list with download links and per-attachment Remove button; file input with accepted type hint; out-of-form delete forms linked via HTML `form=` attribute

**Docs**
- Developer guide updated: extending a service interface to support a second entity type (reuse vs. split decision), subdirectory isolation for multi-entity file storage

**Tests**
- `TransferAttachmentServiceTests` — 4 integration tests: upload persists DB record and writes file to disk, serve returns correct data and metadata, delete removes DB record and file from disk, spoofed file type rejected with "not allowed" message

#### Changed

**Transfer Attachments**
- `TransferEditViewModel` — added `IFormFile? Attachment` property
- `TransfersController` — injects `IFileAttachmentService`; Edit GET loads existing attachments into `ViewBag`; Edit POST handles optional file upload after record save; `enctype="multipart/form-data"` added to the Edit form

**CSV Import**
- `ProjectCeres.csproj` — CsvHelper 33.0.1 added

**Movements**
- `Movements/Index.cshtml` — static cleared/pending badge replaced with `ClearedBadge` React mount point for Transaction and Transfer rows; LiabilityPayment rows retain a static read-only badge
- `Transfers/Index.cshtml` — Status column added with `ClearedBadge` React mount point per row
- `Transactions/Index.cshtml` — Status column replaced with `ClearedBadge` React mount point; inline form toggle (ToggleCleared POST) removed from the Actions column
- Navbar updated: "Movements" added as a primary nav link between Dashboard and Accounts

**Transactions**
- `TransactionsController` Edit and Delete (GET + POST) accept optional `returnUrl` — redirects to it after success if local, falls back to `/Transactions` otherwise
- `Views/Transactions/Edit.cshtml` and `Delete.cshtml` — hidden `returnUrl` field added; Cancel link respects `returnUrl`
- Goal Budget dropdown on Transaction Create/Edit now filters to `GoalType == "Spending"` only — Savings goals track progress via account balance, not transaction tagging

**Transfers**
- `TransfersController` Edit and Delete (GET + POST) accept optional `returnUrl` — same pattern as Transactions
- `Views/Transfers/Edit.cshtml` and `Delete.cshtml` — hidden `returnUrl` field added; Cancel link respects `returnUrl`

**Navbar**
- Added Budgets and Import links between Categories and Reminders

**Data Models**
- `Budget` entity extended with `GoalType` (required) and `LinkedAccountId` (nullable FK to Account)

**Tests**
- `ApiInfrastructureTests`, `DashboardApiTests`, `MovementsApiTests`, `MovementsControllerTests` — updated to accept `TestWebApplicationFactory` instead of `WebApplicationFactory<Program>`; per-class `WithWebHostBuilder`/`UseSetting` overrides removed as redundant
- All 19 integration test classes annotated with `[Collection("IntegrationTests")]` — eliminates parallel races between `WebApplicationFactory` tests and `TestDbFixture` tests on `project_ceres_test`
- `MovementsControllerTests` WAF now overrides `ConnectionStrings:DefaultConnection` to target `project_ceres_test` instead of the dev database; seeded rows deleted via `ExecuteDeleteAsync` in `DisposeAsync`

#### Fixed

**Tests**
- WAF tests were seeding data into the dev database (`project_ceres`) because individual test classes forgot to call `WithWebHostBuilder`; structural fix via `TestWebApplicationFactory` subclass makes this impossible going forward; orphaned rows cleaned from dev database (6 transactions, 3 transfers, 12 accounts removed)
- `ReportServiceTests` and `MovementServiceTests` were flakily failing when run in parallel with WAF tests — WAF tests were writing to `project_ceres_test` concurrently with `TestDbFixture` rollback transactions; resolved by the `[Collection("IntegrationTests")]` grouping
- WAF tests were seeding data into the dev database (`project_ceres`) because no connection string override was in place; dev database cleaned (39 accounts, 6 transfers, 9 transactions removed)

**Movements**
- `ClearedBadge` was rendering with identical gray styling for both Cleared and Pending states because `badge-success` and `badge-warning` CSS classes were not defined; replaced with Tailwind utility classes (`bg-green-100 text-green-700` for Cleared, `bg-yellow-100 text-yellow-700` for Pending)

#### Removed

**Frontend**
- `HelloWorld` component and its test removed — React pipeline verification complete, component no longer needed

---

## [0.2.0] — 2026-04-21

### Phase 1

#### Added

**Project scaffold**
- ASP.NET Core MVC project scaffold (`ProjectCeres/`) with Controllers, Models, Views, Data, Services, ViewModels, Helpers, Filters, ModelBinders layers
- xUnit test project (`ProjectCeres.Tests/`) with solution file (`ProjectCeres.sln`)

**Data model**
- All 13 EF Core entity models: `Account`, `AccountType`, `Category`, `CategoryType`, `Currency`, `Transaction`, `TransactionAttachment`, `Transfer`, `Budget`, `CategoryBudget`, `RecurringTransaction`, `ReportType`, `SavedReport`, `Settings`
- `AppDbContext` with full Fluent API configuration, seed data for all system-defined lookup tables, and `Frequency` enum stored as string
- `LiabilityPayment` model — dedicated entity for recording debt payments from an asset account to a liability account (`Models/LiabilityPayment.cs`)

**Migrations**
- Initial EF Core migration (`InitialCreate`) applied to local PostgreSQL database
- Migration `RemoveDateSeparator` — dropped redundant `DateSeparator` column from `Settings`
- Migration `AddLiabilityPayment` — adds `LiabilityPayments` table with FK references to `Accounts`

**Features**
- Full CRUD for: Accounts, Categories, Transactions, Transfers, Recurring Transactions, Settings
- Deactivate (soft-delete) flows for Accounts and Categories
- Hard-delete with confirmation for Transactions and Transfers
- Four financial reports: Net Worth Statement, Income & Expense Summary, Expense Breakdown by Category, Transaction History
- Dashboard with month-to-date income/expense totals, savings rate, and pending recurring transaction reminders
- Opening balance management on Account Create and Edit — stored as a system-managed `Transaction` with `Category.IsSystem = true`, excluded from all reports
- File attachment upload, serve, and delete for transactions (`FileAttachmentService`) — magic-byte MIME validation via Mime-Detective, filesystem storage outside `wwwroot/`, system-generated storage paths, re-verification at serve time

**Accounts**
- Per-account balance ledger view at `/Accounts/{id}/Ledger` — shows every entry contributing to the account balance (opening balance, transactions, transfers, liability payments) in chronological order with a running balance column; linked from the Accounts index

**Transactions**
- File attachment field on the New Transaction form — optional, regular transactions only; validates magic bytes and file size before saving the transaction record
- File attachment upload on Edit Transaction — new attachment uploaded as part of the Save Changes submission; no separate Upload button required

**Liability payments**
- `ILiabilityPaymentService` / `LiabilityPaymentService` — full CRUD for liability payments with validation (currency match, account type guards, date-before-opening-balance guard)
- `TransactionListItemViewModel` — unified read model for the Transactions Index list that represents either a regular `Transaction` or a `LiabilityPayment` row via a `TransactionType` discriminator field
- `TransactionService.GetByIdForEditAsync` — returns a fully-populated `TransactionEditViewModel` covering both regular and liability-payment records
- `TransactionsController` — conditional server-side validation that removes `CategoryId` requirement for `LiabilityPayment` type and `LiabilityAccountId` requirement for regular transactions

**Number formatting**
- `NumberFormatHelper` — locale-aware decimal formatting for display (`FormatAmount`) and input pre-fill (`FormatInputValue`)
- `NumberFormatActionFilter` — global `IAsyncActionFilter` that injects `ViewData["NumberFormat"]` before every controller action
- `DecimalModelBinder` / `DecimalModelBinderProvider` — parses all `decimal` and `decimal?` form fields using the user's configured culture, with invariant-culture fallback for copy-pasted values

**Services**
- `IFileAttachmentService.ValidateAsync` — pre-save file validation method that runs size and magic-byte checks without writing anything; used by the Create flow to fail fast before any DB write

**Tests**
- Unit tests: `BalanceCalculationTests` (6 tests), `SavingsRateTests` (7 tests), `CategoryBudgetGuardTests`
- Unit tests for `NumberFormatHelper` — `FormatAmount`, `FormatInputValue`, and `TryParseDecimal` including round-trip correctness (12 tests in `NumberFormatHelperTests.cs`)
- Unit tests for decimal parsing — both number format modes, invariant-input tolerance, thousands separators, and invalid input (12 tests in `DecimalParsingTests.cs`)
- Integration tests: `TransferValidationTests` (3 tests) — real PostgreSQL database with per-test transaction rollback isolation via `TestDbFixture`
- Integration tests for `AccountService`, `CategoryService`, `TransactionService`, `TransferService`
- Integration tests for `BudgetService` — `GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeactivateAsync`, and `GetActualSpendAsync`
- Integration tests for `RecurringTransactionService` — all 7 methods including `ConfirmAsync` and `DismissAsync`
- Integration tests for `ReportService` — all 4 report types with date range, account, category, and pagination filters
- Integration tests for `DashboardService` — MTD income/expense sums, savings rate, pending reminder count
- Integration tests for `SettingsService` — `GetAsync`, `UpdateAsync`, `EnsureExistsAsync` including create-from-scratch paths
- Integration tests for `FileAttachmentService` — `UploadAsync` (happy path + all validation guards), `GetAsync`, `DeleteAsync` using a per-test temp directory and `Mock<IWebHostEnvironment>`

**Frontend build**
- Tailwind CSS v3 build pipeline — pnpm + Tailwind CLI, input at `ProjectCeres/Styles/app.css`, output to `ProjectCeres/wwwroot/css/site.css`, wired into MSBuild pre-build target so `dotnet build` / `dotnet run` automatically regenerates CSS

**Documentation**
- Architecture Decision Records 0012–0031
- `docs/architecture.md` — layer model, request flow, phase evolution, frontend build pipeline section
- `docs/security-model.md` — threat model, data protection, access control
- `docs/api-contract.md` — API conventions, response shapes, versioning strategy
- `docs/multi-tenancy-strategy.md` — Phase 3 migration plan
- `docs/decisions/ADR-0004` — implementation note added documenting the tolerant decimal parsing fix and its implication for Phase 2 CSV/OFX import
- `docs/testing.md` — unit test priority list updated; Phase 1 unit and integration test coverage tables added
- `docs/guide/` — structured developer guide organized by stack topic; 23+ topic files across 7 modules
- `docs/roadmap-phase-one.md` — Phase 1 feature roadmap and verification checklist (fully checked)
- `docs/guide/07-testing/mocking-with-moq.md` — new guide file covering `Mock<T>`, `.Setup()`, `.Returns()`, and MIME detection test patterns
- `docs/guide/02-dotnet-platform/logging-with-ilogger.md` — new guide file covering `ILogger<T>` injection, log levels, structured placeholders
- `docs/guide/03-aspnetcore-mvc/model-binding-and-validation.md` — `DecimalModelBinder` section updated with tolerant parsing explanation and dry run of the corruption case
- `docs/guide/07-testing/test-structure-and-patterns.md` — new pattern added: extracting logic out of framework types for unit testability
- `dev-teacher` and `sync-docs` Claude Code skills added

#### Changed

**Transactions**
- `ITransactionService.CreateAsync` now returns `Guid` (the new record's ID) instead of `void`, enabling post-save operations like attaching a file
- Transaction form field order unified: Attachment field moved to after Description on both Create and Edit views
- Transaction Edit view: attachment upload merged into main form via `enctype="multipart/form-data"`; separate Upload form and button removed
- Transaction Edit view: per-attachment Remove forms moved outside the main `<form>` element and linked via HTML `form=` attribute — nested forms are silently ignored by browsers
- `TransactionService.DeleteAsync` now loads attachments and calls `FileAttachmentService.DeleteAsync` for each before removing the transaction row, ensuring disk cleanup on transaction delete
- `TransactionEditViewModel` — added `IFormFile? Attachment` property to support upload-on-save on the Edit flow
- `TransactionsController.Index` — now returns `IEnumerable<TransactionListItemViewModel>` (merged regular transactions + liability payments) instead of raw `Transaction` entities
- `TransactionsController.Create` POST — branches on `vm.TransactionType`; routes to `LiabilityPaymentService.CreateAsync` for `LiabilityPayment`, `TransactionService.CreateAsync` otherwise
- `TransactionCreateViewModel` / `TransactionEditViewModel` — added `TransactionType`, `LiabilityAccountId` fields to support the unified form

**Number formatting**
- `DecimalModelBinder` parsing logic extracted into `NumberFormatHelper.TryParseDecimal` — a pure static method with no framework dependencies, making it independently unit-testable
- All decimal display views updated to use `NumberFormatHelper.FormatAmount(...)` instead of `.ToString("N2")`
- All decimal input views updated to `type="text"` with explicit `value` pre-fill using `NumberFormatHelper.FormatInputValue(...)`

#### Fixed

**Number formatting**
- `DecimalModelBinder` silently corrupted amounts entered in invariant format (`100.00`) when the number format was set to `comma_decimal` — the period was interpreted as a thousands separator, producing `10000.00`; parsing order is now adjusted to detect and handle this case correctly
- `[Range(typeof(decimal), ...)]` attributes on ViewModels now use `ParseLimitsInInvariantCulture = true` — previously threw `FormatException` when the system locale used comma as decimal separator

**Recurring Transactions**
- Confirming a recurring reminder always reloaded the form without recording — `AccountId`, `CategoryId`, and `TransactionType` were missing from the Confirm view as hidden inputs, causing `ModelState.IsValid` to silently fail on every POST

**Transactions**
- Attachment upload section was absent from the New Transaction (Create) form — attachments could only be added by editing an existing transaction
- Remove button on Edit Transaction did not delete the file from disk or the DB record — the Remove `<form>` was nested inside the main edit `<form>`, causing browsers to silently discard it
- Deleting a transaction did not clean up its attached files from disk — `TransactionService.DeleteAsync` now iterates attachments and calls `FileAttachmentService.DeleteAsync` before removing the transaction row

**Settings**
- `SettingsService.UpdateAsync` — fixed dead guard (`if (settings.Id == 0)`) that prevented creating a settings row when none existed; replaced with an explicit `isNew` boolean

**Accounts**
- `AccountService.GetBalanceAsync` — transfer amounts are now correctly added/subtracted from account balances (`transfersIn` increases balance, `transfersOut` decreases it)
- `AccountService.GetBalanceAsync` — system (opening balance) transactions now always add to balance regardless of account type; regular transactions on liability accounts correctly apply inverted polarity

**Liability account balance**
- `ReportService` — liability account balances in Net Worth and Income & Expense reports now use the same polarity logic as `AccountService`, ensuring consistent figures across views
- Opening balance transactions on liability accounts were incorrectly being subtracted from the balance instead of added

**Reports**
- `GetTransactionHistoryAsync` was missing `!t.Category.IsSystem` filter — opening balance transactions were appearing in the Transaction History report

**Categories**
- Category Edit GET action was missing `PopulateViewBagAsync()` call — Lifestyle Tag dropdown rendered empty on the edit page

---

## [0.1.0] — 2026-01-01

### Added
- Initial project setup
- Full documentation: planning, models, legal, business model
- Architecture Decision Records 0001–0011
- Claude Code configuration (CLAUDE.md, sync-docs, hooks)
- `.env.example` with required environment variables
