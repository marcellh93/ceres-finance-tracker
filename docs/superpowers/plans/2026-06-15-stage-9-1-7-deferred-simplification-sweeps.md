# Stage 9.1.7 — Deferred simplification sweeps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Apply the 9.1.6.g discovery-then-fix procedure to `ProjectCeres.Tests/` (9.1.7.a) and `ProjectCeres.Client/src/` (9.1.7.b), shipping only safe-mechanical simplifications and capturing a findings table for each — a near-empty result is a valid outcome.

**Architecture:** Discovery-driven, not pre-written edits. Each sub-stage: dispatch the `code-simplifier` agent (via `frontend-orchestrator` for React) in discovery mode → capture a findings table grouped by class → apply only the safe-mechanical rows → verify → commit. The specific edits are whatever discovery surfaces, governed by the explicit apply/exclude/queue rules below. Two isolated commits, different tiers.

**Tech Stack:** xUnit + Moq + FluentAssertions (tests); React 19 + Vite + Vitest + base-ui (client); `code-simplifier:code-simplifier` agent; `frontend-orchestrator` → `vercel-react-best-practices`.

**Spec:** `docs/superpowers/specs/2026-06-15-stage-9-1-7-deferred-simplification-sweeps-design.md` (commit `4bda59e`).
**Branch:** `stage-9.1.7-deferred-simplification-sweeps` (off `main`; spec already committed there).

## Why no per-edit code blocks
The edits are unknown until discovery runs (the trees are expected near-idiomatic — 9.1.6.g found 2 fixes in 302 files). This plan specifies the exact procedure, commands, verification gates, and the apply/exclude/queue decision rules verbatim. The "minimal implementation" of each task is "apply the safe-mechanical findings the discovery surfaced, per the rules" — not a fixed diff.

## File map
- Modify (9.1.7.a): discovery-selected `.cs` files under `ProjectCeres.Tests/` (excl. `obj/bin` + the 3 out-of-scope files below)
- Modify (9.1.7.b): discovery-selected `.ts/.tsx` files under `ProjectCeres.Client/src/` (excl. `*.test.*` only if discovery flags production files; test files themselves are not the target)
- Modify (close-out): `docs/roadmap-phase-three.md`, `CHANGELOG.md`

---

### Task 1: 9.1.7.a — test-project simplification sweep

**Tool:** `code-simplifier:code-simplifier` agent. **Tier:** M (full `dotnet test`).

- [ ] **Step 1: Capture the baseline.**
Run: `dotnet test 2>&1 | tail -4` — record the total passed count (the post-sweep run must match it) and the elapsed time. Run `dotnet test` ALONE (do NOT run `pnpm test` concurrently — the 9.1.6 close-out's 15min run + a Vitest flake were both CPU contention).

- [ ] **Step 2: Dispatch the discovery pass (no edits).** Dispatch `code-simplifier:code-simplifier` over `ProjectCeres.Tests/` (excl. `obj/bin`). Instruct it: DISCOVERY ONLY, no edits; IDE0001 name-simplification is already done (9.1.6.f) — do NOT report it; produce a findings table grouped by simplification class with `file:line` + before→after + risk label (SAFE-MECHANICAL vs JUDGMENT/behavior-changing). Tell it to APPLY-candidates are ONLY: target-typed `new()`, collection expressions on literals, expression-bodied members, `is null`/`is not null` in plain C#. And to flag-but-EXCLUDE: collapsing per-test arrange/setup blocks into shared helpers (violates `docs/testing.md` line 35 — never set state off the feature-under-test path), terser/weaker assertion forms, merging one-reason-to-fail tests, any rewrite inside an EF/LINQ expression tree. Out-of-scope files it must NOT touch: `Integration/BackfillIdentityNormalizedToLowercaseTests.cs`, `Integration/Rls/Group1_BypassCaseTests.cs`, `Integration/Rls/RlsTestFixture.cs`.

- [ ] **Step 3: Record the findings table.** Paste the discovery findings table into `docs/roadmap-phase-three.md` § Stage 9.1.7 as a `### 9.1.7.a findings` subsection (before applying fixes, so the count is captured). Reference the eventual commit hash after Step 6.

- [ ] **Step 4: Apply ONLY the SAFE-MECHANICAL rows.** Apply the simplifications the discovery labeled SAFE-MECHANICAL, per the apply-list in Step 2. Skip every JUDGMENT/behavior-changing row. If a JUDGMENT row is genuinely worth doing, it goes to Step 7 (queue), not into this commit. Keep comments short (no rationale blocks — `feedback_keep_code_comments_short`).

- [ ] **Step 5: Verify (the test-count invariant is the tripwire).**
Run: `dotnet build 2>&1 | grep -iE "error" | grep -v LogError || echo clean`
Run: `dotnet test 2>&1 | tail -4`
Expected: build clean; **passed count == Step 1 baseline** (no test added, modified, or skipped to make a simplification pass — `feedback_never_skip_tests_to_make_them_pass`). If runtime moved >30% off Step 1, re-run once before concluding (`feedback_dont_handwave_perf_variance`).

- [ ] **Step 6: Commit.**
```bash
git add ProjectCeres.Tests/ docs/roadmap-phase-three.md
git commit -m "refactor(9.1.7.a): safe-mechanical simplifications in test project

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
(If discovery found zero SAFE-MECHANICAL rows: commit only the findings table with message `docs(9.1.7.a): test tree already idiomatic — findings table, no code changes`.)

- [ ] **Step 7: Queue any JUDGMENT findings (only if any are worth doing).** For any behavior/perf/API-changing finding worth pursuing, run the no-unjustified-deferrals procedure and add a `[ ]` line to a receiving stage in the SAME commit as Step 6 (cited reason + checkbox + tripwire). If none, skip.

---

### Task 2: 9.1.7.b — React simplification sweep

**Route:** `frontend-orchestrator` skill → `vercel-react-best-practices`. **Tier:** F (frontend pair).

- [ ] **Step 1: Capture the baseline.**
Run: `pnpm --dir ProjectCeres.Client test --run 2>&1 | tail -4` (ALONE — not concurrent with `dotnet test`). Record total passed count. Run: `pnpm --dir ProjectCeres.Client build 2>&1 | tail -3` — record bundle-budget status.

- [ ] **Step 2: Enter through frontend-orchestrator; discovery pass (no edits).** Invoke `frontend-orchestrator` (it classifies this as Phase 3 strip/simplify → routes to `vercel-react-best-practices`). Audit the production `.ts/.tsx` (excl. `*.test.*`), focusing on the ~18 files with `useMemo` + ~5 with `useCallback` + any component-defined-inside-component. Produce a findings table: each candidate with `file:line`, the rule (§5.3 primitive-`useMemo` removal / §5.4 inner-component hoist / other safe mechanical), and a KEEP-vs-REMOVE call.
  - **REMOVE only when:** `useMemo`/`useCallback` wraps a *simple expression returning a primitive* (boolean/number/string). (React Compiler is NOT enabled, so hooks are not redundant-by-compiler.)
  - **KEEP (do not remove):** any `useMemo` returning an array/object/sorted-list that feeds a memoized child or an effect dependency (referential stability — removing causes re-render storms / effect re-fires).
  - **EXCLUDE entirely:** React Native rules, `server-*`/RSC rules, `<ViewTransition>` migration, and anything that inlines a design token (`var(--…)` → literal is a regression).

- [ ] **Step 3: Record the findings table** into `docs/roadmap-phase-three.md` § Stage 9.1.7 as `### 9.1.7.b findings`.

- [ ] **Step 4: Apply ONLY the REMOVE/hoist rows + other safe mechanical simplifications.** No visual/layout change expected (this is hygiene). Propagate any shared-primitive change everywhere in the same pass.

- [ ] **Step 5: Verify (TIER F).**
Run: `pnpm --dir ProjectCeres.Client build 2>&1 | tail -3` — no bundle-budget regression vs Step 1.
Run: `pnpm --dir ProjectCeres.Client test --run 2>&1 | tail -4` — passed count == Step 1; allow ONE retry for the known CPU-contention Vitest flake (`project_vitest_waitfor_flake`); a second failure is root-caused, not retried.
If any rendered output changed (it should not), show the result and get approval before commit (CLAUDE.md "show then approval").

- [ ] **Step 6: Commit.**
```bash
git add ProjectCeres.Client/src/ docs/roadmap-phase-three.md
git commit -m "refactor(9.1.7.b): safe React re-render-hygiene simplifications

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
(If discovery found zero safe removals: commit only the findings table with message `docs(9.1.7.b): React tree already idiomatic — findings table, no code changes`.)

- [ ] **Step 7: Queue any JUDGMENT findings** the same way as Task 1 Step 7, if any.

---

### Task 3: Close-out (Phase E)

- [ ] **Step 1: Final verification — all four gates.**
Run: `dotnet build 2>&1 | grep -iE "error" | grep -v LogError || echo clean`
Run: `dotnet test 2>&1 | tail -4`
Run: `pnpm --dir ProjectCeres.Client build 2>&1 | tail -3`
Run: `pnpm --dir ProjectCeres.Client test --run 2>&1 | tail -4`
Expected: all exit 0; counts match baselines.

- [ ] **Step 2: Tick the roadmap.** In `docs/roadmap-phase-three.md` § Stage 9.1.7: tick `[x]` 9.1.7.a and 9.1.7.b (each referencing its findings table + commit), and flip Status to ✅ Done. Confirm zero unchecked `[ ]` remain under the 9.1.7 heading (any queued JUDGMENT follow-ups live under their own receiving stage, not here).

- [ ] **Step 3: changelog-sync.** Add to `[Unreleased]` § Phase 3 → `#### Changed` a "Code quality (Stage 9.1.7 — deferred simplification sweeps, 2026-06-15)" line summarizing what was applied (or "test + React trees already idiomatic; findings tables captured, no functional change" if near-empty).

- [ ] **Step 4: sync-docs.** Review the diff; no app-behavior change is expected, so likely no living-doc edits beyond the roadmap. Confirm and note.

- [ ] **Step 5: Commit close-out.**
```bash
git add docs/roadmap-phase-three.md CHANGELOG.md
git commit -m "docs(9.1.7): close out — Stage 9.1.7 Done (test + React sweeps)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-review (against the spec)
- **Spec §2 (9.1.7.a test sweep):** Task 1 — discovery, conservative apply-list, the exact exclude-list, out-of-scope files, test-count invariant, TIER M. ✓
- **Spec §3 (9.1.7.b React sweep):** Task 2 — frontend-orchestrator route, §5.3/§5.4 rules, the referential-stability KEEP footgun, the caveats (RN/RSC/view-transition/token-inline), TIER F + bundle budget + one-retry. ✓
- **Spec §1 (near-empty is valid):** Tasks 1/2 Step 6 each have the explicit zero-findings commit path. ✓
- **Spec §4 (sequencing):** two isolated commits, .a then .b, findings captured before fixes, baselines run alone to avoid contention. ✓
- **Spec §5 (ship gates) + §7 (close-out):** Task 3 — four gates, roadmap tick, sync-docs + changelog. ✓
- **Spec §6 (out of scope / queue):** Tasks 1/2 Step 7 — JUDGMENT findings → no-defer-gate queue, not rolled in. ✓
- **No placeholders:** every step has an exact command + exact decision rule. The discovery-dependent edits are governed by verbatim apply/exclude/queue rules — there is no "TBD"; the unknown is the *data* discovery returns, not the procedure.
- **Consistency:** sub-stage IDs (9.1.7.a/.b), tiers (M/F), tool names (`code-simplifier:code-simplifier`, `frontend-orchestrator`, `vercel-react-best-practices`) match the spec and the verified codebase throughout.
