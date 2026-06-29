# Stage 11 — Razor + URL cleanup (Batch 4) — Design

**Date:** 2026-06-29
**Stage:** 11 (roadmap-phase-three.md → `## Stage 11 — Razor + URL cleanup`)
**Status:** Design approved 2026-06-29 — pending spec review, then writing-plans.

## Goal

The product becomes a **pure Web API + SPA**. Drop the `/app/` URL prefix, strip MVC infrastructure from `Program.cs`, delete every Razor controller stub and `Views/`, widen two Stage-6a architecture tests back to full scope, drive `pnpm lint` to zero, and shelve the import module + Review page from the beta (recoverable, not deleted).

Delivered as **three dependency-ordered commits** (Approach A). The roadmap's linear 11.1→11.9 order is wrong in two places and is re-sequenced here.

## Decisions locked during brainstorm (2026-06-29)

1. **Shelve the entire Review page, not just Reconciliations.** ADR-0078 line 26 ("the Transfers tab is independent") is **factually wrong** — the `ceres-researcher` pre-design pass found that both `ReconciliationReviewApiController` (via `IImportStagedTransactionService`) and `TransferReviewApiController` (via `ITransferReviewService`) read exclusively from staging tables (`ImportStagedTransactions` / `ImportStagedTransfers`) that are written **only** by `ImportService`. With import shelved, both tabs go permanently empty and the sidebar badge stays at 0. Keeping a Transfers-only tab would ship a page that can never have content — a hostile empty-state. → Shelve all of Review. **ADR-0078's premise must be corrected in the same commit.**

2. **Re-home the SPA host as a static `wwwroot/index.html` + `MapFallbackToFile`, fully dropping Razor.** Deleting `Views/` removes `Views/App/Index.cshtml`, which is the **only** thing emitting the production stylesheet `<link>` (via the `Vite.AspNetCore` `vite-href` TagHelper, added in the Stage 9 close-out — commit `46f1253`). No `wwwroot/index.html` exists today. A naive "delete `Views/` and boot" re-introduces the exact Stage 9 unstyled-SPA bug in reverse. → Generate `index.html` at build time with hashed asset tags resolved from Vite's manifest; serve via `app.MapFallbackToFile("index.html")`. No Razor, no `AddControllersWithViews`.

3. **Drive all 39 lint problems to zero, not just the 34 from the May plan.** The count drifted (auth pages shipped). Re-baseline to 39; each former violation resolves to a **real fix** OR a **per-line suppression with a `Why:` comment** — never a bare `eslint-disable`. End goal: a clean baseline so `pnpm lint` becomes a tripwire that surfaces *new* problems instead of burying them in a 39-item baseline.

## Sequencing — three commits (Approach A)

Ordering constraints (from `ceres-researcher`):
- Architecture-test widening (11.7) must land **after** the Razor controllers are deleted (11.5/11.6), or the widened tests fail against still-present controllers.
- The routing flip (basename + catch-all) must land **together** with the new SPA host, or the app can't serve a page.
- Import + Review shelving is **independent** of the routing work.

### Commit 1 — Shelve import + Review (sub-stage 11.9, expanded)

Independent, lands first, fully reversible (code stays in tree).

**SPA removals:**
- `ProjectCeres.Client/src/app/layout/nav-items.ts` — remove the **Import** item (L58, TOOLS group) and the **Review** item (L43). **Also remove the now-dead `import { useReviewCount }` (L14) and the `useReviewBadge` hook (L36)** — leaving them dangling after `ReviewCountProvider` is unmounted breaks the build (verify-against-codebase Conflict 2).
- `ProjectCeres.Client/src/app/App.tsx` — remove routes `/import`, `/import/profiles`, `/review` and their lazy page imports. These paths resolve to NotFound.
- Remove `ReviewCountProvider` from the provider tree; the sidebar badge it fed goes with the Review nav item.
- Update the layout tests (`Sidebar`, `MobileDrawer`, `TopBar`) that assert on the Import/Review nav items.

**Server endpoint fencing (code retained, never deleted):**
- Fence the five import/Review API controllers so no shelved UI path reaches a live endpoint: `ImportApiController` (`api/import`), `ImportHeadersController` (`api/import/headers`), `ImportProfilesApiController` (`api/import-profiles`), `ReconciliationReviewApiController` (`api/reconciliation-review`), `TransferReviewApiController` (`api/transfer-review`).
- Mechanism: conditional registration / fenced to a non-beta environment, returning 404 in the beta — following the in-tree `builder.Environment.IsEnvironment(...)` idiom (`Program.cs` uses it 7×) and the ADR-0072 conditional-registration precedent. **Pinned by an architecture test** asserting these routes 404 in the beta environment.
- `ImportService`, `TransferReviewService`, `ImportStagedTransactions` / `ImportStagedTransfers` tables — **left in the tree, untouched.**

**ADR + docs:**
- **Correct ADR-0078** — fix the "Transfers tab is independent" premise; record that the open question resolved to "shelve all of Review" (both tabs are import-fed).
- Update `api-contract.md`, `models.md` (staging entities), `testing.md` (import suites) to reflect the shelved-from-beta surface, same commit.

**Verification:** nav has no Import/Review item and no dead links; `/import*` and `/review` resolve to NotFound; the badge is gone without console error; layout tests green; the fencing architecture test passes (beta-env 404).

### Commit 2 — Go pure-SPA (sub-stages 11.1, 11.2, 11.3, 11.5, 11.6 + new host)

The load-bearing commit; everything lands together because the app can't serve a page in an intermediate state.

**New SPA host (the piece the roadmap missed):**
- Generate a static `wwwroot/index.html` carrying the hashed CSS/JS asset tags resolved from Vite's build manifest. **Open implementation detail for writing-plans:** whether to use Vite's own `index.html` entry point (cleanest — `vite.config.ts` already has `manifest: true`) or a build step that reads `manifest.json`. Resolve against the actual Vite config in the plan.
- Serve via `app.MapFallbackToFile("index.html")`. `UseStaticFiles()` (L595) is retained.
- Re-point the existing stylesheet-guard e2e test (`ProjectCeres.Client/e2e/auth/spa-stylesheet.spec.ts`) at the new host.
- **Verify this in isolation FIRST** — boot the app, confirm the production stylesheet `<link>` renders in manifest mode — before the controller deletions pile on top. This is the highest-risk item in the stage.

**Routing flip:**
- `ProjectCeres.Client/src/app/main.tsx` — React Router `basename` `/app` → `/`.
- Razor catch-all `app/{*path}` → the SPA fallback `{*path}` (now `MapFallbackToFile`).
- Sweep hard-coded `/app/` literals beyond `basename`: `src/app/pages/Security.tsx:35` (`window.location.href = '/app/login'`) and `src/components/ui/navbar.tsx:48` (`href="/app/reports"`). The "grep finds no `/app/`" gate fails without these.
- Rewrite the **Vite dev middleware** (`Program.cs` ~603–649) — its request-detection assumes controllers own `/` and `/app/`. Dev-mode SPA serving must survive the prefix drop (separate path from production; easy to leave half-migrated).

**One-shot `/app/*` → `/*` 301 (11.3):**
- Placed in this commit (pairs with the routing flip; additive, can't break page-serving).
- Mechanism: `app.UseRewriter(new RewriteOptions().AddRedirect("^app/(.*)", "$1", 301))` in the `Program.cs` pipeline, placed **before** `MapFallbackToFile`. Query string is auto-preserved. **NOT** a controller-based `RedirectPermanent` — that would resurrect the MVC surface being deleted (verify-against-codebase Conflict 1). Reference: Microsoft Learn, "URL Rewriting Middleware in ASP.NET Core."

**MVC teardown:**
- Delete all Razor (non-Api) controllers: the SPA shell (`AppController`), `HomeController`, and the 15 redirect stubs. (Deleting the 15 stubs **is** sub-stage 11.4 — the stubs *are* the per-area 302 redirects; no separate deletion step.)
- Delete `Views/` entirely (all 8 `.cshtml`).
- `Program.cs`: remove the `AddControllersWithViews(...)` block (L29–32, incl. the Razor-form-only `NumberFormatActionFilter` + `DecimalModelBinderProvider`) and both `MapControllerRoute` calls (L669, L673). **Keep** `AddControllers().ConfigureApiBehaviorOptions(...)` (L35–38, the 422-envelope API surface) and `UseStaticFiles()`.
- **Feasibility check before removing `DecimalModelBinderProvider`:** confirm no API controller depends on the decimal model binder (Razor-form concern). If any does, retain it on the `AddControllers()` block.

**Verification:** SPA-host stylesheet renders in manifest mode (checked in isolation first); app boots; `/` serves the SPA; `/app/movements?needsReview=true` 301s to `/movements?needsReview=true` with query preserved; old Razor bookmark `/Movements` is now 404; grep finds no `/app/` in code.

### Commit 3 — Re-tighten contracts + lint sweep (sub-stages 11.7, 11.8 + final grep)

Nothing here serves pages; it tightens the contracts that prove Commit 2 was complete and clears the lint baseline.

**Architecture-test widening (11.7):**
- `ArchitectureTests.cs:34` — `No_api_controller_class_has_AllowAnonymous` → `No_controller_class_has_AllowAnonymous`; remove `.Where(t => t.Namespace?.Contains(".Api") == true)`.
- `ArchitectureTests.cs:79` — `Api_HttpGet_actions_must_not_have_write_verb_names` → `HttpGet_actions_must_not_have_write_verb_names`; remove the same filter.
- Both pass only because Commit 2 deleted the Razor controllers that carried class-level `[AllowAnonymous]` (App/Home) and write-verb-named implicit-GET actions (review/recurring/export stubs). **That passing IS the proof the Razor teardown was complete** — which is why the widening lands last.

**Lint sweep (11.8) — re-baselined to 39:**
- Reuse the May file-by-file dispositions (`planning-phase3-spa-migration.md` § 6) for the 34 planned items: 18× `react-hooks/set-state-in-effect`, 14× `react-refresh/only-export-components`, 2× `react-hooks/exhaustive-deps`.
- Disposition the **5 new items** (no plan entry), each → real fix OR per-line suppression with `Why:`:
  - 2× new `only-export-components` context files (`auth/auth-context.tsx`, `theme/theme-context.tsx`) — likely the split-into-`provider.tsx`+`context.ts` treatment used elsewhere in the plan.
  - 3× new `react-hooks/incompatible-library` (`LoginTotp.tsx:181`, `PasswordReset.tsx:204`, `TotpEnrollStep1ScanVerify.tsx:70`) — investigate; genuine concurrent-safety issue → fix, false-positive → per-line disable with reason.
- Gate: `pnpm --dir ProjectCeres.Client lint` exits 0 (0 errors, 0 warnings). No bare suppressions.

**Final grep + doc sweep:**
- Confirm zero `/app/` references in code AND docs (~10 doc files under `docs/` still reference `/app/`).
- `sync-docs` runs against the full stage diff at close-out.

## Components & boundaries

| Unit | Purpose | Depends on |
|---|---|---|
| `wwwroot/index.html` (build-generated) | Production SPA host; emits hashed CSS/JS `<link>`/`<script>` | Vite build manifest |
| `app.MapFallbackToFile` + `UseRewriter` (Program.cs) | Serve SPA at `{*path}`; 301 legacy `/app/*` | static `index.html`, `UseStaticFiles` |
| Endpoint fencing (Program.cs conditional) | 404 the import/Review API in beta | `IWebHostEnvironment` |
| Architecture tests (full scope) | Prove no Razor controller survives | reflection over controller assembly |

## Testing

- **Server:** Razor teardown + test widening touch `.cs` → full `dotnet test` suite (Tier 2). Architecture tests are the completeness proof. Fencing architecture test (new) pins beta-env 404.
- **Client:** `pnpm test` (layout tests updated for nav removal), `pnpm build` (bundle budgets clean), `pnpm lint` exits 0. `spa-stylesheet.spec.ts` re-pointed at the new host.
- **Ship gate (after Commit 3):** `dotnet build`, `dotnet test`, `pnpm build`, `pnpm test`, `pnpm lint` all exit 0; frontend checklist — app boots, SPA loads at `/`, every page renders, no 404s for legacy assets, IDOR suite still green.

## Error handling / risks

- **SPA-host re-homing (Commit 2)** — highest risk; verify the stylesheet `<link>` in isolation before controller deletions. The reverse of the Stage 9 close-out bug.
- **Vite dev middleware** — `/app/`-coupled; rewrite or dev-mode serving breaks silently (production path is unaffected, so the break hides until someone runs `dotnet run`).
- **Dangling `useReviewCount` import** — Commit 1 must remove the import + hook, not just the nav entries.
- **`DecimalModelBinderProvider` removal** — feasibility-check API dependence first.

## Out of scope

- Deleting any import code/services/tables (recoverable per ADR-0078).
- Stage 11.5 (import sandbox) — on hold per ADR-0078.
- Any new feature surface.

## Cross-references

- Roadmap: `docs/roadmap-phase-three.md` → `## Stage 11`.
- ADR-0078 (import shelving; premise to correct).
- `docs/planning-phase3-spa-migration.md` § Final cleanup plan + § 6 (lint remediation).
- ADR-0072 (conditional-registration / environment-gating precedent).
- Stage 9 close-out commit `46f1253` (the stylesheet-injection mechanism being replaced).
