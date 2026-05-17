# Stage 9.1.5.c — Auth-Page Background Contrast Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Change `AuthLayout.tsx`'s page background from `bg-muted/30` to `bg-background` so the card's `border + shadow` provides the canonical shadcn separation in light mode, and `card` (lighter) vs `background` (darker) provides natural figure/ground in dark mode.

**Architecture:** One line of JSX changes. One new vitest test pins the class as a regression guard (and explicitly forbids re-introducing `bg-muted/30` or `bg-muted`). Manual UX verification confirms the visual outcome at both themes + mobile breakpoint. Roadmap close-out + task tracker update finish the stage.

**Tech Stack:** React 19 · Tailwind CSS v4 · @testing-library/react · vitest · next-themes (for OS-level dark-mode toggle, already wired in `main.tsx`).

**Source spec:** `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md` (commit `e262b62`).

---

## Binding constraints

- **Stay on `main`.** No worktrees, no branches.
- **No `Co-Authored-By` trailer** in commit messages.
- **TDD per `docs/testing.md` § Rules** — write the failing test, run it to confirm it fails for the right reason, then make it pass. Every test-touching commit names which of cases (1), (2), or (3) applied. **Case (3)** applies here (contract change: AuthLayout's page-background token).
- **Never modify, skip, or weaken tests.** No `[Fact(Skip=…)]`, no commented-out assertions.
- **Pre-existing test failures encountered mid-task get root-caused now.**
- **Stop-hook (`.claude/hooks/run-tests.sh`) — CORRECTED 2026-05-17:** the hook fires on the Stop event (turn-end), NOT on `git commit`. It also tiers by file extension: a turn that wrote only `.tsx`/`.ts`/`.md` files exits at tier 0 without running `dotnet test`. For this stage (frontend-only, no `.cs`/`.csproj`/`.sln` writes), the hook will skip `dotnet test` entirely. **Do NOT run `dotnet test` preemptively** — it's wasted work. The frontend test (`pnpm --dir ProjectCeres.Client test --run`) is what the implementer runs before commits. (Earlier drafts of this plan claimed the hook ran on commit; that was wrong.)
- **`pnpm` only.** Every Vite/vitest invocation uses `pnpm --dir ProjectCeres.Client`. Never `npm`, never `npx`. Per `feedback_pnpm_only_never_npm`.
- **Do NOT touch `AuthLayout.a11y.test.tsx`** — that file covers a different concern (axe-core a11y violations). The new test lives in a sibling file `AuthLayout.test.tsx`.

## File map

| File | Action | Purpose |
|---|---|---|
| `ProjectCeres.Client/src/app/layout/AuthLayout.tsx` | **modify** | Line 18: replace `bg-muted/30` with `bg-background`. One-character category change. |
| `ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx` | **create** | New vitest file pinning `bg-background` (positive) and `bg-muted/30` + `bg-muted` (negative). |
| `docs/roadmap-phase-three.md` | **modify** | Line 1098 (sub-stage description) + line 1108 (verification checkbox `[ ]` → `[x]`). |

## Commit-by-commit overview

| Commit | Subject | What lands |
|---|---|---|
| 1 | `fix(stage-9.1.5.c): AuthLayout uses bg-background so card border + shadow separates in light mode` | Test file + AuthLayout.tsx edit. TDD: test red → fix → test green. |
| 2 | `docs(stage-9.1.5.c): close out — auth-page background contrast fix verified` | Roadmap line 1098 + 1108 updates. Gated on manual UX verification per spec §5. |

---

### Task 1: Fix + regression test

**Files:**
- Modify: `ProjectCeres.Client/src/app/layout/AuthLayout.tsx:18`
- Create: `ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx`

This commit lands the one-line code fix and the new vitest assertion. TDD-ordered: test added first, confirmed failing for the right reason, then production fix applied, then test confirmed green.

- [ ] **Step 1: Write the failing test**

Create `<repo>/ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx` with this exact content:

```tsx
import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AuthLayout } from './AuthLayout';

describe('AuthLayout', () => {
  it('uses bg-background for the page surface so the card has light-mode separation', () => {
    // Regression test for Stage 9.1.5.c. Previously used bg-muted/30, which
    // composited to ~oklch(0.989) in light mode against a card of oklch(1.000)
    // — card edge invisible. bg-background (1.000) lets the card's border + shadow
    // do the separation, which is the canonical shadcn pattern.
    const { container } = render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<AuthLayout />}>
            <Route index element={<div>test</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    const pageDiv = container.querySelector('.min-h-dvh');
    expect(pageDiv).not.toBeNull();
    expect(pageDiv).toHaveClass('bg-background');
    expect(pageDiv).not.toHaveClass('bg-muted/30');
    expect(pageDiv).not.toHaveClass('bg-muted');
  });
});
```

Note: the imports + `MemoryRouter` + `Route element={<AuthLayout />}` shape mirrors the existing `AuthLayout.a11y.test.tsx` exactly. The `<Route index element={...}>` is required because `AuthLayout` renders `<Outlet />` and would error without a child route.

- [ ] **Step 2: Run the new test, confirm it fails for the right reason**

Run: `pnpm --dir <repo>/ProjectCeres.Client test --run src/app/layout/AuthLayout.test.tsx`

Expected: **FAIL** at `expect(pageDiv).toHaveClass('bg-background')` — the actual page div has `bg-muted/30`, not `bg-background`. The error message will say something like: *"Expected element to have class: bg-background. Received: min-h-dvh w-full bg-muted/30 flex items-center justify-center p-4 sm:p-6"*.

If the test fails for a different reason (e.g. `pageDiv is null`, `Cannot find module`), STOP and fix the test setup before proceeding. The expected failure is the `bg-background` class assertion.

- [ ] **Step 3: Apply the one-line production fix**

Open `<repo>/ProjectCeres.Client/src/app/layout/AuthLayout.tsx`. The current line 18 reads:

```tsx
    <div className="min-h-dvh w-full bg-muted/30 flex items-center justify-center p-4 sm:p-6">
```

Replace with:

```tsx
    <div className="min-h-dvh w-full bg-background flex items-center justify-center p-4 sm:p-6">
```

Only `bg-muted/30` changes to `bg-background`. Every other class on the line stays exactly as-is. No other lines in the file change.

- [ ] **Step 4: Run the new test, confirm it passes**

Run: `pnpm --dir <repo>/ProjectCeres.Client test --run src/app/layout/AuthLayout.test.tsx`
Expected: PASS.

- [ ] **Step 5: Run the full frontend test suite to confirm zero regressions**

Run: `pnpm --dir <repo>/ProjectCeres.Client test --run`

Expected: all green. `AuthLayout.a11y.test.tsx` should still pass — it doesn't assert on the page-background class; it asserts axe a11y violations, which are unaffected by a token swap (both `--background` and `--muted/30` resolve to readable contrast against `--card-foreground`).

If any test fails for an unexpected reason, root-cause it now — do not log a follow-up.

- [ ] **Step 6: Run the frontend build to confirm no TypeScript or bundle regressions**

Run: `pnpm --dir <repo>/ProjectCeres.Client build`

Expected: build succeeds. The bundle-budget check (`scripts/check-bundle-size.mjs`) should pass — this change shouldn't affect bundle size (Tailwind v4 generates a class either way).

- [ ] **Step 7: Run the backend test suite (stop-hook gate)**

Run: `dotnet test <repo>/ProjectCeres.sln`

Expected: all green. No backend code is touched in this stage, but the stop-hook runs `dotnet test` on every commit so the pre-commit run must already be clean. Re-run any documented Stage 9.1.5.a flake in isolation if it recurs (it shouldn't post-9.1.5.a, but be prepared).

- [ ] **Step 8: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Client/src/app/layout/AuthLayout.tsx \
  ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx

git -C <repo> commit -m "$(cat <<'EOF'
fix(stage-9.1.5.c): AuthLayout uses bg-background so card border + shadow separates in light mode

Replaces bg-muted/30 with bg-background on AuthLayout's page div. In light
mode, --muted (oklch 0.965) at 30% opacity composites to ~oklch(0.989) over
the implicit background — within ~1% lightness of --card (1.000), so the
card edge was visually invisible. With bg-background (1.000), the card's
existing border + shadow-sm provides the canonical shadcn separation cue
in light mode. In dark mode, --background (0.155) is darker than --card
(0.205), giving natural figure/ground.

The shadcn Card primitive already ships with `border bg-card text-card-
foreground shadow-sm`. Other surfaces in the app shell already use this
pattern; the auth pages now align with them.

New AuthLayout.test.tsx pins the contract structurally: the page div MUST
have bg-background and MUST NOT have bg-muted/30 or bg-muted (the latter
catches the most likely regression where someone "fixes the opacity" by
dropping the /30 instead of changing the token). jsdom doesn't compute CSS
variables, so the visual outcome still requires manual browser verification
(see commit 2 of this stage for the close-out gate).

Audit at spec-write time (spec §7.3) confirms the bug exists exactly once
in the codebase — only AuthLayout.tsx used bg-muted/30 as a page background.

Case applies (testing.md § Rules): case (3) — contract intentionally
changed. AuthLayout's page-background token is now bg-background; the
test pins this contract and forbids the prior token.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md §3, §5
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-c-auth-page-background-impl.md Task 1
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

### Task 2: Manual UX verification + roadmap close-out

**Files:**
- Modify: `docs/roadmap-phase-three.md` (line 1098 sub-stage row + line 1108 verification line)

This commit is **gated on manual browser verification**. The structural test in Task 1 confirms the class is correct, but jsdom doesn't compute CSS variables — only a real browser can confirm the visual separation works. Per spec §5 and `docs/design-system.md` working rules.

- [ ] **Step 1: Start the dev server**

Run: `pnpm --dir <repo>/ProjectCeres.Client dev`

Expected: Vite reports the local URL (typically `http://localhost:5173` or whatever the project's port is — check the Vite proxy config if the URL differs). Note: `BrowserRouter basename="/app"` in `main.tsx` means routes are served at `/app/...`, NOT `/`.

If the dev server is already running from a prior session, that's fine — just confirm it's up before opening the browser.

**If a browser is unavailable in the implementer's environment**: STOP and report DONE_WITH_CONCERNS. Per the CLAUDE.md frontend rule: "If browser access is unavailable, say so and hand the checklist to the user with specific URLs to check." Hand the URLs in §Step 2 to the user; do NOT proceed to Step 3 (the roadmap close-out) without the verification.

- [ ] **Step 2: Browser verification checklist (light mode → dark mode → mobile)**

Open these URLs in the browser, one at a time:

1. **`/app/login`** — the real login page (Stage 9 Task 6).
2. **`/app/register`** — the placeholder page (Stage 9.1 Commit A).
3. **`/app/password-reset`** — the placeholder page (Stage 9.1 Commit A).

For each URL, verify in **light mode** (macOS: System Settings → Appearance → Light):
- The card is a white surface centered on a white page.
- The card edge is visible via a thin 1px border + soft drop shadow.
- The boundary between card and page is unambiguous — you can tell where the card ends and the page begins.

Then toggle to **dark mode** (macOS: System Settings → Appearance → Dark):
- The page background goes near-black (`oklch(0.155)`).
- The card surface goes dark gray (`oklch(0.205)`) — visibly **lighter** than the page.
- The figure/ground reads as "card stands forward over a dark surround."

Then test **mobile viewport** (DevTools → Toggle device toolbar → set width to 375px):
- The card still centers horizontally with `p-4` padding around it.
- The same light/dark contrast cues apply at this width.

If any of these checks fail (card boundary invisible in light mode, card not lighter than page in dark mode, layout breaks at 375px), STOP and report the failure mode. The fix didn't land correctly.

- [ ] **Step 3: Update the roadmap entry**

Open `<repo>/docs/roadmap-phase-three.md`. Locate line 1098 (the sub-stage row text starting with `| 9.1.5.c | Fix: auth-page Tailwind tokens collapse...`). Replace with:

```
| 9.1.5.c | Auth-page card separation in light mode — replace `bg-muted/30` with `bg-background` on `AuthLayout` so the card's existing `border + shadow-sm` provides canonical shadcn separation | Phase 1 UX walkthrough — root cause was layout-level token choice: `bg-muted/30` composited to ~oklch(0.989) in light mode against `--card` of oklch(1.000), invisible delta. `--background` (1.000) lets the card's existing border + shadow-sm do the work (canonical shadcn pattern). Dark mode unaffected — `--card` (0.205) is naturally lighter than `--background` (0.155). |
```

Then locate line 1108 (the verification line starting with `- [ ] 9.1.5.c — toggling OS light/dark on /app/login...`). Replace with:

```
- [x] 9.1.5.c — `AuthLayout.tsx:18` uses `bg-background` (not `bg-muted/30`); vitest regression test `AuthLayout.test.tsx` pins the class and forbids `bg-muted/30` + `bg-muted` reintroduction; manual browser verification at light + dark + 375px confirms card boundary visible on `/app/login`, `/app/register`, `/app/password-reset`. Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-c-auth-page-background-impl.md`.
```

The `[ ]` flips to `[x]`.

- [ ] **Step 4: Verify the roadmap edit**

Run: `grep -n "9.1.5.c" <repo>/docs/roadmap-phase-three.md | head -5`

Expected: both the sub-stage row and the verification line show the updated text, with the verification line starting `- [x] 9.1.5.c`.

- [ ] **Step 5: Update task #39 in the task tracker**

Use the `TaskUpdate` tool with these parameters:
- `taskId`: `39`
- `status`: `completed`
- `subject`: `Bug: auth-page tokens collapse to identical values in light + dark modes — RESOLVED via bg-background swap`
- `description`: append a closing paragraph: `Resolved 2026-05-17 by Stage 9.1.5.c (spec e262b62, commit TBD-task1-sha). Root cause was AuthLayout's bg-muted/30 page background compositing to ~oklch(0.989) in light mode against --card of oklch(1.000) — invisible delta. Fix: bg-muted/30 → bg-background. Card now separates via its existing border + shadow-sm in light mode (canonical shadcn pattern); dark mode unchanged. Vitest regression test pins the class.`

- [ ] **Step 6: Sanity-check the docs-only edit didn't break the test suite**

Run: `pnpm --dir <repo>/ProjectCeres.Client test --run`
Expected: all green. (Docs-only edit shouldn't affect anything, but verify before committing.)

Run: `dotnet test <repo>/ProjectCeres.sln`
Expected: all green. Stop-hook gate.

- [ ] **Step 7: Commit**

```bash
git -C <repo> add \
  docs/roadmap-phase-three.md

git -C <repo> commit -m "$(cat <<'EOF'
docs(stage-9.1.5.c): close out — auth-page background contrast fix verified

Manual browser verification at /app/login, /app/register, /app/password-reset
in both light and dark modes (plus 375px mobile viewport) confirms the
bg-background fix from Task 1 (commit TBD-task1-sha) produces the expected
visual outcome:
  - Light mode: white card on white page, separated by the card's existing
    1px border + shadow-sm (canonical shadcn pattern).
  - Dark mode: card (oklch 0.205) visibly lighter than page (oklch 0.155),
    natural figure/ground for dark UIs.
  - Mobile 375px: same separation cue, no layout break.

- docs/roadmap-phase-three.md:1098 — sub-stage row updated to reflect the
  shipped fix (replaces "Tailwind tokens collapse" framing with the actual
  layout-level token swap).
- docs/roadmap-phase-three.md:1108 — verification checkbox flipped to [x];
  text replaced with the post-fix description listing the regression test +
  manual verification result.

Stage 9.1.5 batch progress: 9.1.5.a [x], 9.1.5.b [x], 9.1.5.c [x]. Remaining:
9.1.5.d (in-app light/dark toggle — note that this stage's fix is now visible
once 9.1.5.d ships the toggle; until then, only OS-level dark-mode users see
the fix), 9.1.5.e (language code next to globe), 9.1.5.f (SPA logout),
9.1.5.g (ADR-0076 CSRF token-source).

Case applies (testing.md § Rules): no test files modified in this commit;
production behavior unaffected.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-c-auth-page-background-impl.md Task 2
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

## Self-review

**Spec coverage:**
- §1 (the bug) — covered by Task 1's TDD red→green cycle.
- §2 (root cause) — captured in Task 1's commit message + Task 2's roadmap update.
- §3 (the fix) — Task 1 Step 3 (one-line code change).
- §4 (alternatives rejected) — captured in spec; no implementation work.
- §5 (tests) — Task 1 Steps 1-4 (vitest); Task 2 Steps 1-2 (manual browser).
- §6 (edge cases) — covered by Task 2 Step 2's browser checklist (mobile, all 3 auth pages).
- §7.1 (in scope) — items 1-5 mapped to Tasks 1-2.
- §7.2 (out of scope) — preserved by scope discipline.
- §7.3 (audit) — performed at spec-write time; no audit task needed here.
- §7.4 (no deferrals) — none.
- §8 (verification checklist) — every item maps to a Task step.

**Placeholder scan:** searched for "TODO", "TBD", "implement later", "as appropriate". The Task 2 commit-message body contains `commit TBD-task1-sha` — this is a deliberate placeholder where the implementer fills in Task 1's actual commit SHA after Task 1 lands. Same for task #39's description update in Task 2 Step 5. These are NOT plan-failure placeholders; they're forward-references the implementer fills in. Flagged here for clarity.

**Type consistency:** no types to track — this is a pure CSS-class swap. The test asserts on class strings as strings; the production code uses the same class strings. Consistent.

**Stop-hook safety:** Task 1's commit lands the test green (TDD red→green completes before the commit). Task 2's commit is docs-only. The stop-hook should never see a red commit.

**Manual-verification escape hatch:** Task 2 Step 1 explicitly tells the implementer to report DONE_WITH_CONCERNS if browser access is unavailable, hand the URLs to the user, and NOT proceed to the roadmap close-out without the verification. This honors the CLAUDE.md frontend rule.
