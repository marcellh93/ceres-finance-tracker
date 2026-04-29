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
| `AccountsController` | Index, Create, Edit, Deactivate, Ledger | Ledger becomes a filtered Movements query |
| `TransactionsController` | Index, Create, Edit, Delete | Delete → `DELETE /api/transactions/{id}` |
| `TransfersController` | Index, Create, Edit, Delete | Same pattern as Transactions |
| `BudgetsController` | Index, Create, Edit, Deactivate (×2 for Category + Goal) | Partially covered by `DashboardApiController` already — audit for overlap |
| `CategoriesController` | Index, Create, Edit, Deactivate | — |
| `RecurringTransactionsController` | Index, Create, Edit, Deactivate, Confirm, Dismiss | Confirm and Dismiss are stateful actions — design endpoint contract carefully |
| `ReportsController` | Index + report views | Each report → `GET /api/reports/{type}` |
| `SettingsController` | Edit | → `GET /api/settings` + `PATCH /api/settings` |
| `DashboardController` | Index | Deleted — dashboard is fully React; data already served by `DashboardApiController` |
| `AttachmentsController` | Serve, Delete | File serving needs special handling: streaming response, `Content-Disposition: attachment` |
| `HomeController` | Index | Deleted — replaced by React Router's root route |

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
/transactions               → Transactions list
/transactions/new           → Create transaction
/transactions/:id/edit      → Edit transaction
/transfers                  → Transfers list
/transfers/new              → Create transfer
/transfers/:id/edit         → Edit transfer
/movements                  → Unified ledger
/budgets/categories         → Category budgets
/budgets/goals              → Goal budgets
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

**Feature area porting order** (mirrors the design system implementation order):

1. Auth screens (login, TOTP, register, password reset) — no Razor equivalent; built fresh
2. Onboarding flow — no Razor equivalent; built fresh
3. Dashboard — highest visibility; `DashboardApiController` already exists
4. Transactions + Transfers + Movements — highest daily usage
5. Budgets + Reports
6. Accounts + Categories + Recurring Transactions
7. Settings + Session management + Support tickets
