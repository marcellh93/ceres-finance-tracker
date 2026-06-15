# Stage 9.1.7 — Deferred code-simplification sweeps (test-project + React) (design)

**Date:** 2026-06-15
**Status:** Draft — pending user review
**Roadmap:** `docs/roadmap-phase-three.md` § Stage 9.1.7 (`[ ]` 9.1.7.a + 9.1.7.b).
**Origin:** Split out of Stage 9.1.6.g (which swept `ProjectCeres/` production `.cs` only) per the user-locked production-only scope decision, 2026-06-15. See the 9.1.6 spec (`2026-06-15-stage-9-1-6-code-shape-cleanup-design.md`) §6 deferral entry.

**Locked decisions (user, 2026-06-15):**
1. **Both trees, full discovery-then-fix** — run the established 9.1.6.g procedure on `ProjectCeres.Tests/` (9.1.7.a) and `ProjectCeres.Client/src/` (9.1.7.b).
2. **Test sweep is conservative / mechanical-only** — apply only safe within-statement simplifications; exclude anything that touches assertions, test structure, or state-setup paths.

**ceres-researcher fact-find + verify-against-codebase pre-flight (2026-06-15) folded in** — cited inline.

---

## 1. What it is

The two trees deferred out of 9.1.6.g get the same discovery-then-fix treatment: `code-simplifier` discovery → findings table grouped by class → ship safe-mechanical fixes only (one commit per class, batched if small) → anything that changes behavior/perf-class/public-API is queued as a sibling `[ ]`, not rolled in. **IDE0001 name-simplification is already done solution-wide** (9.1.6.f swept all test + production files), so this stage covers the *other* classes only.

**Right-sized expectation (explicit):** 9.1.6.g found the production tree already idiomatic — 2 fixes in 302 files. The test tree was *additionally* IDE0001-swept, and the React surface is small (0 `memo()`, 42 `useMemo` across 18 files, 13 `useCallback` across 5 files, no React Compiler). The honest likely outcome is **a findings table dominated by "0 actionable / already idiomatic" rows with a small handful of safe wins**. A near-empty result is a valid close-out — the findings table is the deliverable; the stage is NOT pushed to manufacture churn.

## 2. Sub-stage 9.1.7.a — test-project sweep (`ProjectCeres.Tests/`)

**Tool:** the `code-simplifier:code-simplifier` plugin agent, discovery-first (findings table before any edit). **There is no `simplify` skill** (confirmed) — do not reference one.

**Scope:** 209 `.cs` files / ~36k LOC under `ProjectCeres.Tests/` (excl. `obj/bin`). 170 Integration, 22 Unit, 16 Common, 1 Filters.

**Apply (conservative — locked decision 2):** only safe within-statement simplifications where they do NOT reduce diagnostic clarity:
- target-typed `new()`, collection expressions on literals, expression-bodied members, `is null`/`is not null` in **plain** C# (not inside EF expression trees).

**Exclude (the conservative guard rails):**
- Collapsing per-test arrange/setup blocks into shared helpers/fixtures/builders — risks moving state-setup off the feature-under-test path, which `docs/testing.md` § Rules (line 35) explicitly forbids ("do not call `DbContext`, repositories, or services directly to set state that the feature under test was supposed to set").
- Terser assertion forms that weaken what's asserted (`docs/testing.md` line 14 requires specific observable-output assertions; no `NotBeNull()`-as-sole-assertion).
- Merging multiple one-reason-to-fail tests into one.
- Any `is null`-style or other rewrite inside an EF/LINQ expression tree a test builds (9.1.6.g found all 33 such sites change SQL translation).

**Out-of-scope files (untouched — they deliberately exercise raw SQL / RLS invariants, per 9.1.6 spec §5):** `Integration/BackfillIdentityNormalizedToLowercaseTests.cs`, `Integration/Rls/Group1_BypassCaseTests.cs`, `Integration/Rls/RlsTestFixture.cs` (all confirmed present).

**Verification (TIER M — `ProjectCeres.Tests/` edits → full suite):**
- `dotnet build` clean.
- Full `dotnet test` green.
- **Test count before == after** (the mechanical tripwire — no test added, modified, or skipped to pass a simplification, per `feedback_never_skip_tests_to_make_them_pass`).
- Per `feedback_dont_handwave_perf_variance`: if suite runtime moves >30% off baseline (~3:30 full suite), re-run once before concluding. Avoid running `dotnet test` concurrently with `pnpm test` (the 9.1.6 close-out's 15min run + a Vitest flake were both CPU-contention from parallel execution).

## 3. Sub-stage 9.1.7.b — React sweep (`ProjectCeres.Client/src/`)

**Tool route:** `frontend-orchestrator` skill (the mandatory entry for any `ProjectCeres.Client/` work, per CLAUDE.md) → for re-render hygiene / simplification it routes to `vercel-react-best-practices`. `frontend-orchestrator` classifies this as Phase 3 (strip/simplify) / maintenance.

**Scope:** 256 production `.ts/.tsx` files (excl. 164 co-located `*.test.*` files), ~43k LOC. The actionable surface is small — audit the ~18 files with `useMemo` + ~5 with `useCallback`, plus any component-defined-inside-component, NOT a 256-file rewrite.

**Apply:**
- Remove a `useMemo`/`useCallback` only where the expression is **simple AND returns a primitive** (boolean/number/string) — `vercel-react-best-practices` §5.3. React Compiler is **not** enabled (confirmed: no `babel-plugin-react-compiler`/`reactCompiler` in vite config or package.json), so existing hooks are not redundant-by-compiler — most are load-bearing.
- Hoist any component defined inside another component to module scope (§5.4).
- Other safe mechanical TS simplifications (target-typed/`as const` where clearer, optional-chaining over null-guards) where they don't change behavior.

**The footgun (keep, do not remove — the React analog of 9.1.6.g's `File.Exists` guard):** a `useMemo` returning an array/object/sorted-list that feeds a memoized child or an effect dependency provides **referential stability**; removing it causes re-render storms or effect re-fires. These stay.

**Caveats (CLAUDE.md + design-system):**
- Web-only — skip `vercel-react-best-practices` React Native rules.
- The project uses **CSS-based view transitions, not React's `<ViewTransition>`** — `vercel-react-view-transitions` is a future-migration reference, not a sweep target.
- Skip the `server-*`/RSC rule family (SSR-via-Razor + SPA, no RSC); read `bundle-dynamic-imports` as `React.lazy`.
- **Never inline a design-token value** as a "simplification" — replacing a `var(--…)` token with its literal value is a regression (`docs/design-system.md`). A change to a shared primitive must propagate everywhere in the same pass.

**Verification (TIER F — `.tsx` under `ProjectCeres.Client/` → frontend pair):**
- `pnpm --dir ProjectCeres.Client build` — no bundle-budget regression.
- `pnpm --dir ProjectCeres.Client test --run` green (allow ONE Vitest retry for the known CPU-contention flake `project_vitest_waitfor_flake`; a second failure is root-caused, not retried).
- If any rendered output/layout changes (it should not — this is hygiene, not redesign), show the result and get approval before commit (CLAUDE.md "show then approval"). A pure hook-removal/hoist with no visual change does not need a browser pass.

## 4. Sequencing

Two **isolated commits**, different verification tiers and toolchains — independently reviewable:
1. **9.1.7.a** (test sweep, TIER M, `code-simplifier` agent direct).
2. **9.1.7.b** (React sweep, TIER F, via `frontend-orchestrator`).

Each sub-stage's findings table is captured in the roadmap stage body **before** fixes flatten it (with commit hash). Order: 9.1.7.a then 9.1.7.b (so the full `dotnet test` run is isolated from the frontend run — no CPU contention).

**Branch:** `stage-9.1.7-deferred-simplification-sweeps` off `main`.

## 5. Ship gates

- 9.1.7.a: `dotnet build` + full `dotnet test` exit 0; test-count invariant holds.
- 9.1.7.b: `pnpm build` (budget) + `pnpm test --run` exit 0.
- Both findings tables recorded in `roadmap-phase-three.md` § Stage 9.1.7.
- A near-zero applied-fix count is an acceptable, valid result (§1) — Phase E close-out is satisfied by ticking `[ ]` 9.1.7.a/.b with their findings tables regardless of fix count.
- sync-docs + changelog-sync fired at close-out.

## 6. Out of scope

- `ProjectCeres/` production `.cs` (done in 9.1.6.g) and `Migrations/` (generated).
- IDE0001 name-simplification (done solution-wide in 9.1.6.f).
- The 3 raw-SQL/RLS test files named in §2.
- The Razor `Views/` layer (Tailwind v3, server-rendered) — not a React simplification target.
- Any behavior/perf/public-API change surfaced by discovery → queued as a new sibling `[ ]`, not rolled into the mechanical pass (no-defer gate: cited reason + receiving checkbox + tripwire).

## 7. Close-out (Phase E)

- Tick `[ ]` 9.1.7.a and 9.1.7.b with their findings tables + commit hashes.
- Flip Stage 9.1.7 Status to ✅ Done (zero unchecked `[ ]` under the heading; any queued follow-ups live under their own receiving stage).
- sync-docs (no app-behavior change expected → likely no living-doc edits beyond the roadmap) + changelog-sync (a "Code quality (Stage 9.1.7)" line under Changed, or "already idiomatic, no functional change" if near-empty).
