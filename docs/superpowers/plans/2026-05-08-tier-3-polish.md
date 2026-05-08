# Tier 3 Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the five Stage 5 Tier 3 polish items (T3.11–T3.15) for the Project Ceres SPA: motion-token migration, index.html hardening + per-route titles, BudgetCombobox, optimistic Status toggle refactor, and SubmitButton primitive.

**Architecture:** Five sequential commits, low-to-high risk. Each commit is independently revertable. No new third-party dependencies. All work lives inside `ProjectCeres.Client/`. The .NET server is untouched.

**Tech Stack:** React 19 (`useOptimistic`, `useTransition`), Vite, TypeScript, Tailwind CSS v4 (motion tokens already defined in `index.css`), Vitest + React Testing Library, lucide-react (already installed), shadcn primitives (Button, Popover, Command — already in repo).

**Spec reference:** `docs/superpowers/specs/2026-05-08-tier-3-polish-design.md` (commits `f9edbc5`, `cbc1514`).

---

## File structure

### Files created

| Path | Responsibility |
|---|---|
| `ProjectCeres.Client/src/app/lib/use-document-title.ts` | One-effect hook that sets `document.title` to `${page} — Project Ceres` on mount and restores the previous title on unmount. |
| `ProjectCeres.Client/src/app/lib/use-document-title.test.ts` | Vitest unit tests for the hook. |
| `ProjectCeres.Client/src/app/components/BudgetCombobox.tsx` | Popover + Command picker for Goal Budgets. Mirrors `AccountCombobox` API minus `filter`. |
| `ProjectCeres.Client/src/app/components/BudgetCombobox.test.tsx` | Mirror of `AccountCombobox.test.tsx`. |
| `ProjectCeres.Client/src/app/components/SubmitButton.tsx` | Stateful Button wrapper with `idle / loading / success / error` phases. |
| `ProjectCeres.Client/src/app/components/SubmitButton.test.tsx` | Vitest unit tests covering all four phases. |

### Files modified

| Path | Why |
|---|---|
| `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx` | T3.11 — replace `duration-200` literal at line 110. |
| `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx` | T3.11 — 4 literals at 348/357/371/379. T3.14 — replace budget `<select>` at 297–314 with `BudgetCombobox`. |
| `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.tsx` | T3.11 — replace `duration-200` literal at line 93. |
| `ProjectCeres.Client/src/design-system/pages/Patterns.tsx` | T3.11 — 4 literals at 363/372/382/390. |
| `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx` | T3.13 — refactor to `useOptimistic` + `useTransition`. |
| `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.test.tsx` | T3.13 — add rapid-click race test. |
| `ProjectCeres.Client/src/app/components/QuickAddModal.tsx` | T3.12 — swap footer Save Button for `SubmitButton`. Refactor `submit()` to return `Promise<boolean>`. |
| `ProjectCeres.Client/src/app/components/QuickAddModal.test.tsx` | T3.12 — adjust assertions for new submit affordance. |
| `ProjectCeres.Client/index.html` | T3.15 — Razor islands shell, harden meta. |
| `ProjectCeres.Client/app.html` | T3.15 — SPA entry, harden meta. |
| `ProjectCeres.Client/design-system.html` | T3.15 — showcase entry, harden meta. |
| `ProjectCeres.Client/src/app/pages/Dashboard.tsx` | T3.15 — `useDocumentTitle('Dashboard')`. |
| `ProjectCeres.Client/src/app/pages/Accounts.tsx` | T3.15 — `useDocumentTitle('Accounts')`. |
| `ProjectCeres.Client/src/app/pages/Budgets.tsx` | T3.15 — `useDocumentTitle('Budgets')`. |
| `ProjectCeres.Client/src/app/pages/Categories.tsx` | T3.15 — `useDocumentTitle('Categories')`. |
| `ProjectCeres.Client/src/app/pages/Recurring.tsx` | T3.15 — `useDocumentTitle('Recurring')`. |
| `ProjectCeres.Client/src/app/pages/Import.tsx` | T3.15 — `useDocumentTitle('Import')`. |
| `ProjectCeres.Client/src/app/pages/Review.tsx` | T3.15 — `useDocumentTitle('Review')`. |
| `ProjectCeres.Client/src/app/pages/Settings.tsx` | T3.15 — `useDocumentTitle('Settings')`. |
| `ProjectCeres.Client/src/app/pages/Profile.tsx` | T3.15 — `useDocumentTitle('Profile')`. |
| `ProjectCeres.Client/src/app/pages/Security.tsx` | T3.15 — `useDocumentTitle('Security')`. |
| `ProjectCeres.Client/src/app/pages/Support.tsx` | T3.15 — `useDocumentTitle('Support')`. |
| `ProjectCeres.Client/src/app/pages/NotFound.tsx` | T3.15 — `useDocumentTitle('Not Found')`. |
| `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx` | T3.15 — `useDocumentTitle('Movements')`. |
| `ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx` | T3.15 — `useDocumentTitle('New Movement')`. |
| `ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx` | T3.15 — `useDocumentTitle('Edit Movement')`. |
| `ProjectCeres.Client/src/app/features/accounts/AccountCreate.tsx` | T3.15 — `useDocumentTitle('New Account')`. |
| `ProjectCeres.Client/src/app/features/accounts/AccountEdit.tsx` | T3.15 — `useDocumentTitle('Edit Account')`. |
| `ProjectCeres.Client/src/app/features/accounts/AccountLedger.tsx` | T3.15 — `useDocumentTitle('Account Ledger')`. |
| `ProjectCeres.Client/src/app/features/budgets/BudgetCreate.tsx` | T3.15 — `useDocumentTitle('New Budget')`. |
| `ProjectCeres.Client/src/app/features/budgets/BudgetEdit.tsx` | T3.15 — `useDocumentTitle('Edit Budget')`. |
| `ProjectCeres.Client/src/app/features/categories/CategoryCreate.tsx` | T3.15 — `useDocumentTitle('New Category')`. |
| `ProjectCeres.Client/src/app/features/categories/CategoryEdit.tsx` | T3.15 — `useDocumentTitle('Edit Category')`. |
| `ProjectCeres.Client/src/app/features/recurring/RecurringCreate.tsx` | T3.15 — `useDocumentTitle('New Recurring')`. |
| `ProjectCeres.Client/src/app/features/recurring/RecurringEdit.tsx` | T3.15 — `useDocumentTitle('Edit Recurring')`. |
| `ProjectCeres.Client/src/app/features/import/ProfilesLayout.tsx` | T3.15 — `useDocumentTitle('Import Profiles')`. |
| `ProjectCeres.Client/src/app/features/import/ProfileCreate.tsx` | T3.15 — `useDocumentTitle('New Import Profile')`. |
| `ProjectCeres.Client/src/app/features/import/ProfileEdit.tsx` | T3.15 — `useDocumentTitle('Edit Import Profile')`. |
| `ProjectCeres.Client/src/app/features/reports/ReportsLayout.tsx` | T3.15 — `useDocumentTitle('Reports')`. |
| `ProjectCeres.Client/src/app/features/reports/NetWorth.tsx` | T3.15 — `useDocumentTitle('Net Worth')`. |
| `ProjectCeres.Client/src/app/features/reports/NetWorthOverTime.tsx` | T3.15 — `useDocumentTitle('Net Worth Over Time')`. |
| `ProjectCeres.Client/src/app/features/reports/IncomeExpense.tsx` | T3.15 — `useDocumentTitle('Income vs. Expense')`. |
| `ProjectCeres.Client/src/app/features/reports/MonthlyCashFlow.tsx` | T3.15 — `useDocumentTitle('Monthly Cash Flow')`. |
| `ProjectCeres.Client/src/app/features/reports/ExpenseBreakdown.tsx` | T3.15 — `useDocumentTitle('Expense Breakdown')`. |
| `ProjectCeres.Client/src/app/features/reports/BudgetVsActual.tsx` | T3.15 — `useDocumentTitle('Budget vs. Actual')`. |
| `ProjectCeres.Client/src/app/features/reports/LargestExpenses.tsx` | T3.15 — `useDocumentTitle('Largest Expenses')`. |
| `ProjectCeres.Client/src/app/features/reports/TransactionHistory.tsx` | T3.15 — `useDocumentTitle('Transaction History')`. |
| `docs/design-system.md` | T3.11 — flip the migration-pending notes to past tense. |
| `docs/roadmap-phase-three.md` | After all five commits land — mark T3.11–T3.15 as `[x]`. |

---

## Task 1: T3.11 — Motion-token migration sweep

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx:110`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx:348,357,371,379`
- Modify: `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.tsx:93`
- Modify: `ProjectCeres.Client/src/design-system/pages/Patterns.tsx:363,372,382,390`
- Modify: `docs/design-system.md:190,879-906,928,1125`

### Step 1: Audit the current literal sites

- [ ] **Step 1: Run the grep audit and confirm 11 sites**

Run:
```bash
cd <repo>
grep -rn "duration-200" ProjectCeres.Client/src/ | grep -v sheet.tsx
```

Expected: 11 lines across 4 files (MovementsLayout × 1, MovementForm × 4, BudgetsLayout × 1, Patterns × 4 — plus possibly stragglers we missed). If the count differs, update this task's site list accordingly before proceeding.

### Step 2: Migrate `MovementsLayout.tsx:110`

- [ ] **Step 2: Edit the file**

Open `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx`. Find:

```tsx
<ChevronDown className="h-4 w-4 opacity-70 transition-transform duration-200 group-data-[popup-open]/button:rotate-180" />
```

Replace with:

```tsx
<ChevronDown className="h-4 w-4 opacity-70 transition-transform [transition-duration:var(--motion-duration-base)] group-data-[popup-open]/button:rotate-180" />
```

### Step 3: Migrate `BudgetsLayout.tsx:93`

- [ ] **Step 3: Edit the file**

Open `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.tsx`. Find:

```tsx
<ChevronDown className="h-4 w-4 opacity-70 transition-transform duration-200 group-data-[popup-open]/button:rotate-180" />
```

Replace with:

```tsx
<ChevronDown className="h-4 w-4 opacity-70 transition-transform [transition-duration:var(--motion-duration-base)] group-data-[popup-open]/button:rotate-180" />
```

### Step 4: Migrate four sites in `MovementForm.tsx`

- [ ] **Step 4: Replace all four**

Open `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx`. Use a single `Edit` with `replace_all: true` for the literal `transition-colors duration-200` → `transition-colors [transition-duration:var(--motion-duration-base)]`. (All four sites use the exact same substring.)

Run replace-all on:
- old: `transition-colors duration-200`
- new: `transition-colors [transition-duration:var(--motion-duration-base)]`

Verify the file has 4 successful replacements.

### Step 5: Migrate four sites in `Patterns.tsx`

- [ ] **Step 5: Replace all four**

Open `ProjectCeres.Client/src/design-system/pages/Patterns.tsx`. Same `replace_all`:
- old: `transition-colors duration-200`
- new: `transition-colors [transition-duration:var(--motion-duration-base)]`

Verify 4 replacements.

### Step 6: Re-run the audit

- [ ] **Step 6: Verify zero literals remain in app code**

Run:
```bash
cd <repo>
grep -rn "duration-200" ProjectCeres.Client/src/ | grep -v sheet.tsx
```

Expected: zero matches.

### Step 7: Update `docs/design-system.md`

- [ ] **Step 7: Flip the migration-pending notes**

Open `docs/design-system.md`.

**Edit at line 190** — find:
```markdown
> **Status of the rule (2026-05-02):** the rule is aspirational — most existing components still use Tailwind literal durations (`duration-200`). New components should reference the tokens directly, and the existing literals are tracked for migration in a future cleanup pass. See [Known limitations](#known-limitations).
```

Replace with:
```markdown
> **Status of the rule (2026-05-08):** enforced — every app and showcase component uses the motion tokens. The shadcn-vendored `sheet.tsx` retains its literal because it is a vendored primitive.
```

**Edit at line 928** — find the bullet:
```markdown
- **`transition-colors duration-200`** is currently a literal; this is one of the components flagged for [migration to motion tokens](#motion).
```

Replace with:
```markdown
- **`transition-colors [transition-duration:var(--motion-duration-base)]`** uses the motion-base token directly. New components should follow the same pattern.
```

**Edit at line 1125** — find:
```markdown
- **Motion tokens are not yet enforced in older components.** Files like `MovementForm.tsx` still use Tailwind literal durations (`duration-200`) instead of `var(--motion-duration-base)`. The rule in [Motion](#motion) applies to new code; existing literals are tracked for a migration sweep. Don't introduce new literals.
```

Replace with:
```markdown
- ✅ **Motion tokens are now enforced everywhere** (migrated 2026-05-08). All app and showcase components use `[transition-duration:var(--motion-duration-base)]`. The shadcn-vendored `sheet.tsx` retains its literal because it is a vendored primitive — do not migrate it. Don't introduce new literals in app code.
```

**Edit at lines 879–906** — the Patterns code-snippet inside the doc echoes the same `transition-colors duration-200` literals. Run `replace_all: true` on the file with:
- old: `transition-colors duration-200`
- new: `transition-colors [transition-duration:var(--motion-duration-base)]`

This catches any other prose mentions in the same file.

### Step 8: Run the full client test suite

- [ ] **Step 8: Run tests in the foreground**

Run (foreground per project memory — backgrounding produces empty output):
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run
```

Expected: full suite passes (current baseline 807/807). Motion tokens don't change visual behavior under jsdom (no real timing), so test counts are unchanged.

### Step 9: Run the production build

- [ ] **Step 9: Build and check for warnings**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm build
```

Expected: clean build, no Tailwind warnings about unknown utilities. (Tailwind v4 supports arbitrary `[transition-duration:var(--token)]` syntax natively.)

### Step 10: Commit

- [ ] **Step 10: Commit**

Run:
```bash
cd <repo>
git add ProjectCeres.Client/src docs/design-system.md
git commit -m "feat(motion): migrate duration-200 literals to motion tokens (T3.11)" -m "Replaces all 11 transition-duration literals across MovementsLayout, MovementForm, BudgetsLayout, and the Patterns showcase with [transition-duration:var(--motion-duration-base)]. The shadcn-vendored sheet.tsx is left intact because vendored primitives are not migrated. design-system.md notes flipped from 'aspirational' to 'enforced'."
```

---

## Task 2: T3.15 — index.html hardening + per-route titles

**Files:**
- Modify: `ProjectCeres.Client/app.html` (full rewrite)
- Modify: `ProjectCeres.Client/index.html` (full rewrite)
- Modify: `ProjectCeres.Client/design-system.html` (meta additions)
- Create: `ProjectCeres.Client/src/app/lib/use-document-title.ts`
- Create: `ProjectCeres.Client/src/app/lib/use-document-title.test.ts`
- Modify: 35 page components (see file-structure table)

### Step 1: Write the failing hook test

- [ ] **Step 1: Create `use-document-title.test.ts`**

Create `ProjectCeres.Client/src/app/lib/use-document-title.test.ts`:

```ts
import { renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useDocumentTitle } from './use-document-title';

describe('useDocumentTitle', () => {
  let originalTitle: string;

  beforeEach(() => {
    originalTitle = document.title;
    document.title = 'Original';
  });

  afterEach(() => {
    document.title = originalTitle;
  });

  it('sets the document title to "<page> — Project Ceres" on mount', () => {
    renderHook(() => useDocumentTitle('Movements'));
    expect(document.title).toBe('Movements — Project Ceres');
  });

  it('restores the previous title on unmount', () => {
    document.title = 'Before';
    const { unmount } = renderHook(() => useDocumentTitle('Movements'));
    expect(document.title).toBe('Movements — Project Ceres');
    unmount();
    expect(document.title).toBe('Before');
  });

  it('updates the title when the page prop changes', () => {
    const { rerender } = renderHook(({ page }) => useDocumentTitle(page), {
      initialProps: { page: 'Movements' },
    });
    expect(document.title).toBe('Movements — Project Ceres');
    rerender({ page: 'Reports' });
    expect(document.title).toBe('Reports — Project Ceres');
  });
});
```

### Step 2: Run the test to verify it fails

- [ ] **Step 2: Confirm failure mode**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/lib/use-document-title.test.ts
```

Expected: FAIL with `Cannot find module './use-document-title'` or similar.

### Step 3: Implement the hook

- [ ] **Step 3: Create `use-document-title.ts`**

Create `ProjectCeres.Client/src/app/lib/use-document-title.ts`:

```ts
import { useEffect } from 'react';

/**
 * Sets `document.title` to `${page} — Project Ceres` while the calling
 * component is mounted, and restores the previous title on unmount.
 *
 * Use once per top-level page component. Nested layouts can also call it,
 * but the deepest active hook wins because effects run leaf-first.
 */
export function useDocumentTitle(page: string) {
  useEffect(() => {
    const previous = document.title;
    document.title = `${page} — Project Ceres`;
    return () => {
      document.title = previous;
    };
  }, [page]);
}
```

### Step 4: Run the test to verify it passes

- [ ] **Step 4: Confirm green**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/lib/use-document-title.test.ts
```

Expected: 3/3 passing.

### Step 5: Resolve OKLCH primary tokens to sRGB hex for `theme-color`

- [ ] **Step 5: Compute the two theme-color values**

The light primary is `oklch(0.520 0.110 195)` (line 98 of `index.css`). The dark background — which is what mobile browsers paint behind status bars in dark mode — is `oklch(0.155 0.005 285)` (line 143).

Use a one-liner to convert. Run in a Node REPL or a scratch file:

```bash
cd <repo>/ProjectCeres.Client
node -e "
function oklchToSrgb(L, C, h) {
  const a = C * Math.cos(h * Math.PI / 180);
  const b = C * Math.sin(h * Math.PI / 180);
  const l_ = L + 0.3963377774 * a + 0.2158037573 * b;
  const m_ = L - 0.1055613458 * a - 0.0638541728 * b;
  const s_ = L - 0.0894841775 * a - 1.2914855480 * b;
  const l = l_ ** 3, m = m_ ** 3, s = s_ ** 3;
  const r = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
  const g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
  const b2 = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
  function gamma(u) {
    return u >= 0.0031308 ? 1.055 * Math.pow(u, 1/2.4) - 0.055 : 12.92 * u;
  }
  const toHex = v => Math.max(0, Math.min(255, Math.round(gamma(v) * 255))).toString(16).padStart(2, '0');
  return '#' + toHex(r) + toHex(g) + toHex(b2);
}
console.log('light primary:', oklchToSrgb(0.520, 0.110, 195));
console.log('dark background:', oklchToSrgb(0.155, 0.005, 285));
"
```

Record the two output hex strings. Use them verbatim in the next step. (As a fallback if conversion fails, use approximate values `#1aa39e` / `#1a3838` from the spec — but prefer the computed values.)

### Step 6: Harden `app.html`

- [ ] **Step 6: Edit `app.html`**

Replace `ProjectCeres.Client/app.html` contents entirely. The file has a small inline script that pre-applies the `sidebar-collapsed` class — preserve it verbatim.

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="description" content="Personal finance tracker for individuals and freelancers. Track assets, liabilities, net worth, income, expenses, and goal budgets." />
    <meta name="theme-color" content="<LIGHT_HEX_FROM_STEP_5>" media="(prefers-color-scheme: light)" />
    <meta name="theme-color" content="<DARK_HEX_FROM_STEP_5>" media="(prefers-color-scheme: dark)" />
    <title>Project Ceres</title>
    <script>
      (function () {
        try {
          var c = localStorage.getItem('ceres.sidebar.collapsed');
          if (c === 'true') document.documentElement.classList.add('sidebar-collapsed');
        } catch (e) {}
      })();
    </script>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/app/main.tsx"></script>
  </body>
</html>
```

Substitute the two `<…_HEX_FROM_STEP_5>` placeholders with the actual hex values computed in Step 5.

### Step 7: Harden `index.html`

- [ ] **Step 7: Edit `index.html`**

Replace `ProjectCeres.Client/index.html` contents entirely:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="description" content="Personal finance tracker for individuals and freelancers. Track assets, liabilities, net worth, income, expenses, and goal budgets." />
    <meta name="theme-color" content="<LIGHT_HEX_FROM_STEP_5>" media="(prefers-color-scheme: light)" />
    <meta name="theme-color" content="<DARK_HEX_FROM_STEP_5>" media="(prefers-color-scheme: dark)" />
    <title>Project Ceres</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

### Step 8: Harden `design-system.html`

- [ ] **Step 8: Edit `design-system.html`**

Replace the file entirely (preserve the existing title, add the same description and theme-color metas):

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="description" content="Project Ceres design system showcase — tokens, primitives, and recipes." />
    <meta name="theme-color" content="<LIGHT_HEX_FROM_STEP_5>" media="(prefers-color-scheme: light)" />
    <meta name="theme-color" content="<DARK_HEX_FROM_STEP_5>" media="(prefers-color-scheme: dark)" />
    <title>Project Ceres — Design System</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/design-system/main.tsx"></script>
  </body>
</html>
```

### Step 9: Wire `useDocumentTitle` into the 12 simple pages

- [ ] **Step 9: Add the hook call to each `pages/*.tsx`**

For each of these files, add `import { useDocumentTitle } from '../lib/use-document-title';` at the top of the imports, and add `useDocumentTitle('<page name>');` as the first statement inside the page component's body (before any other hook calls so it runs first):

| File | Page name to pass |
|---|---|
| `src/app/pages/Dashboard.tsx` | `'Dashboard'` |
| `src/app/pages/Accounts.tsx` | `'Accounts'` |
| `src/app/pages/Budgets.tsx` | `'Budgets'` |
| `src/app/pages/Categories.tsx` | `'Categories'` |
| `src/app/pages/Recurring.tsx` | `'Recurring'` |
| `src/app/pages/Import.tsx` | `'Import'` |
| `src/app/pages/Review.tsx` | `'Review'` |
| `src/app/pages/Settings.tsx` | `'Settings'` |
| `src/app/pages/Profile.tsx` | `'Profile'` |
| `src/app/pages/Security.tsx` | `'Security'` |
| `src/app/pages/Support.tsx` | `'Support'` |
| `src/app/pages/NotFound.tsx` | `'Not Found'` |

Example shape — for `Dashboard.tsx`, the top of the component becomes:

```tsx
import { useDocumentTitle } from '../lib/use-document-title';
// …other imports

export function Dashboard() {
  useDocumentTitle('Dashboard');
  // …rest unchanged
}
```

### Step 10: Wire `useDocumentTitle` into the 23 feature-folder pages

- [ ] **Step 10: Add the hook call to each `features/*/*.tsx` page**

The import path here is `'../../lib/use-document-title'` (one level deeper). For each file:

| File | Page name |
|---|---|
| `src/app/features/movements/MovementsLayout.tsx` | `'Movements'` |
| `src/app/features/movements/MovementCreate.tsx` | `'New Movement'` |
| `src/app/features/movements/MovementEdit.tsx` | `'Edit Movement'` |
| `src/app/features/accounts/AccountCreate.tsx` | `'New Account'` |
| `src/app/features/accounts/AccountEdit.tsx` | `'Edit Account'` |
| `src/app/features/accounts/AccountLedger.tsx` | `'Account Ledger'` |
| `src/app/features/budgets/BudgetCreate.tsx` | `'New Budget'` |
| `src/app/features/budgets/BudgetEdit.tsx` | `'Edit Budget'` |
| `src/app/features/categories/CategoryCreate.tsx` | `'New Category'` |
| `src/app/features/categories/CategoryEdit.tsx` | `'Edit Category'` |
| `src/app/features/recurring/RecurringCreate.tsx` | `'New Recurring'` |
| `src/app/features/recurring/RecurringEdit.tsx` | `'Edit Recurring'` |
| `src/app/features/import/ProfilesLayout.tsx` | `'Import Profiles'` |
| `src/app/features/import/ProfileCreate.tsx` | `'New Import Profile'` |
| `src/app/features/import/ProfileEdit.tsx` | `'Edit Import Profile'` |
| `src/app/features/reports/ReportsLayout.tsx` | `'Reports'` |
| `src/app/features/reports/NetWorth.tsx` | `'Net Worth'` |
| `src/app/features/reports/NetWorthOverTime.tsx` | `'Net Worth Over Time'` |
| `src/app/features/reports/IncomeExpense.tsx` | `'Income vs. Expense'` |
| `src/app/features/reports/MonthlyCashFlow.tsx` | `'Monthly Cash Flow'` |
| `src/app/features/reports/ExpenseBreakdown.tsx` | `'Expense Breakdown'` |
| `src/app/features/reports/BudgetVsActual.tsx` | `'Budget vs. Actual'` |
| `src/app/features/reports/LargestExpenses.tsx` | `'Largest Expenses'` |
| `src/app/features/reports/TransactionHistory.tsx` | `'Transaction History'` |

Pattern (one example, `MovementsLayout.tsx`):

```tsx
import { useDocumentTitle } from '../../lib/use-document-title';
// …other imports

export function MovementsLayout() {
  useDocumentTitle('Movements');
  // …rest unchanged
}
```

### Step 11: Run the full test suite

- [ ] **Step 11: Verify everything still passes**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run
```

Expected: 810/810 (807 prior + 3 new for `useDocumentTitle`). If any layout tests break because they assert against `document.title`, update them to match the new format `<Page> — Project Ceres`.

### Step 12: Run the production build

- [ ] **Step 12: Build**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm build
```

Expected: clean build, all three HTML entries emitted in `dist/`.

### Step 13: Commit

- [ ] **Step 13: Commit**

Run:
```bash
cd <repo>
git add ProjectCeres.Client/app.html ProjectCeres.Client/index.html ProjectCeres.Client/design-system.html ProjectCeres.Client/src/app/lib/use-document-title.ts ProjectCeres.Client/src/app/lib/use-document-title.test.ts ProjectCeres.Client/src/app/pages ProjectCeres.Client/src/app/features
git commit -m "feat(spa): per-route document titles + harden HTML entries (T3.15)" -m "Adds useDocumentTitle hook (sets '<Page> — Project Ceres' on mount, restores on unmount) and wires it into all 35 SPA routes. Hardens all three Vite HTML entries (app.html, index.html, design-system.html) with description meta and light/dark theme-color metas resolved from the primary OKLCH token."
```

---

## Task 3: T3.14 — Budget Combobox

**Files:**
- Create: `ProjectCeres.Client/src/app/components/BudgetCombobox.tsx`
- Create: `ProjectCeres.Client/src/app/components/BudgetCombobox.test.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx:297-314`

### Step 1: Write the failing test

- [ ] **Step 1: Create `BudgetCombobox.test.tsx`**

Create `ProjectCeres.Client/src/app/components/BudgetCombobox.test.tsx`:

```tsx
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { BudgetCombobox } from './BudgetCombobox';
import type { GoalBudgetListItemDto } from '../features/budgets/budgets-api';

const budgets: GoalBudgetListItemDto[] = [
  { id: 'b1', name: 'Vacation 2026', goalType: 'Spending', currencyCode: 'EUR', currencySymbol: '€', targetAmount: 2000, startDate: '2026-01-01', endDate: '2026-12-31', description: null, isActive: true },
  { id: 'b2', name: 'Old Goal', goalType: 'Spending', currencyCode: 'EUR', currencySymbol: '€', targetAmount: 500, startDate: '2025-01-01', endDate: '2025-12-31', description: null, isActive: false },
];

describe('BudgetCombobox', () => {
  it('shows the placeholder when no budget is selected', () => {
    render(<BudgetCombobox budgets={budgets} value={null} onChange={vi.fn()} />);
    expect(screen.getByText('No budget')).toBeInTheDocument();
  });

  it('shows the selected budget name when value is set', () => {
    render(<BudgetCombobox budgets={budgets} value="b1" onChange={vi.fn()} />);
    expect(screen.getByText('Vacation 2026')).toBeInTheDocument();
  });

  it('appends "(archived)" suffix to inactive budgets in the list', () => {
    render(<BudgetCombobox budgets={budgets} value={null} onChange={vi.fn()} />);
    fireEvent.click(screen.getByRole('combobox'));
    expect(screen.getByText('Old Goal (archived)')).toBeInTheDocument();
  });

  it('calls onChange with the budget id when an option is picked', () => {
    const onChange = vi.fn();
    render(<BudgetCombobox budgets={budgets} value={null} onChange={onChange} />);
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(screen.getByText('Vacation 2026'));
    expect(onChange).toHaveBeenCalledWith('b1');
  });

  it('renders a clear button when onClear is set and a value is selected', () => {
    render(<BudgetCombobox budgets={budgets} value="b1" onChange={vi.fn()} onClear={vi.fn()} />);
    expect(screen.getByRole('button', { name: /clear selection/i })).toBeInTheDocument();
  });

  it('does not render a clear button when no value is selected', () => {
    render(<BudgetCombobox budgets={budgets} value={null} onChange={vi.fn()} onClear={vi.fn()} />);
    expect(screen.queryByRole('button', { name: /clear selection/i })).toBeNull();
  });

  it('clear button calls onClear and does not open the popover', () => {
    const onClear = vi.fn();
    render(<BudgetCombobox budgets={budgets} value="b1" onChange={vi.fn()} onClear={onClear} />);
    fireEvent.click(screen.getByRole('button', { name: /clear selection/i }));
    expect(onClear).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('Vacation 2026' /* in list */)).toBeInTheDocument();
    // Popover should NOT have opened — there is exactly one occurrence (the trigger), not two.
  });

  it('hides the clear button when disabled', () => {
    render(<BudgetCombobox budgets={budgets} value="b1" onChange={vi.fn()} onClear={vi.fn()} disabled />);
    expect(screen.queryByRole('button', { name: /clear selection/i })).toBeNull();
  });
});
```

### Step 2: Run the test to verify it fails

- [ ] **Step 2: Confirm failure mode**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/components/BudgetCombobox.test.tsx
```

Expected: FAIL with `Cannot find module './BudgetCombobox'`.

### Step 3: Implement `BudgetCombobox.tsx`

- [ ] **Step 3: Create the component**

Create `ProjectCeres.Client/src/app/components/BudgetCombobox.tsx`:

```tsx
import { Check, ChevronsUpDown, X } from 'lucide-react';
import { useState, type MouseEvent } from 'react';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import type { GoalBudgetListItemDto } from '../features/budgets/budgets-api';

type Props = {
  budgets: GoalBudgetListItemDto[];
  value: string | null;
  onChange: (budgetId: string) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /**
   * When provided AND a value is selected, render an inline ✕ on the trigger
   * that calls this callback. Click stops propagation so the popover stays
   * closed. Omit on surfaces where clearing is not allowed.
   */
  onClear?: () => void;
  id?: string;
};

export function BudgetCombobox({
  budgets,
  value,
  onChange,
  placeholder = 'No budget',
  disabled,
  className = 'w-full',
  onClear,
  id,
}: Props) {
  const [open, setOpen] = useState(false);
  const selected = budgets.find((b) => b.id === value) ?? null;
  const showClear = !!onClear && !!selected && !disabled;

  function handleClear(e: MouseEvent<HTMLButtonElement>) {
    e.preventDefault();
    e.stopPropagation();
    onClear?.();
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id}
            variant="outline"
            role="combobox"
            aria-expanded={open}
            disabled={disabled}
            className={cn('justify-between', className)}
          >
            {selected ? selected.name : <span className="text-muted-foreground">{placeholder}</span>}
            <span className="ml-2 flex shrink-0 items-center gap-1.5">
              {showClear && (
                <button
                  type="button"
                  onClick={handleClear}
                  aria-label="Clear selection"
                  className="rounded-sm p-0.5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                >
                  <X className="h-4 w-4" />
                </button>
              )}
              <ChevronsUpDown className="h-4 w-4 opacity-50" />
            </span>
          </Button>
        }
      />
      <PopoverContent className="w-[--radix-popover-trigger-width] p-0">
        <Command>
          <CommandInput placeholder="Search budgets…" />
          <CommandList>
            <CommandEmpty>No budgets found.</CommandEmpty>
            <CommandGroup>
              {budgets.map((b) => (
                <CommandItem
                  key={b.id}
                  value={b.name}
                  onSelect={() => {
                    onChange(b.id);
                    setOpen(false);
                  }}
                >
                  <Check className={cn('mr-2 h-4 w-4', b.id === value ? 'opacity-100' : 'opacity-0')} />
                  {b.name}
                  {!b.isActive && ' (archived)'}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
```

If the `PopoverContent` width binding line above causes a TypeScript or Tailwind warning, copy it verbatim from `AccountCombobox.tsx` (we should mirror that file's exact width syntax — they are siblings).

### Step 4: Run the test to verify it passes

- [ ] **Step 4: Confirm green**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/components/BudgetCombobox.test.tsx
```

Expected: 8/8 passing. If any failure relates to popover width syntax, copy the exact `className` from `AccountCombobox.tsx`'s `<PopoverContent>` line.

### Step 5: Replace the `<select>` in `MovementForm.tsx`

- [ ] **Step 5: Swap the picker**

Open `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx`. At line ~297–314, find:

```tsx
        {showBudgetPicker && (
          <Field label="Budget (optional)" htmlFor="mf-budget">
            <select
              id="mf-budget"
              value={values.budgetId ?? ''}
              onChange={(e) => set('budgetId', e.target.value || null)}
              className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
            >
              <option value="">None</option>
              {matchingGoals.map((g) => (
                <option key={g.id} value={g.id}>
                  {g.name}
                  {!g.isActive ? ' (archived)' : ''}
                </option>
              ))}
            </select>
          </Field>
        )}
```

Replace with:

```tsx
        {showBudgetPicker && (
          <Field label="Budget (optional)" htmlFor="mf-budget">
            <BudgetCombobox
              id="mf-budget"
              budgets={matchingGoals}
              value={values.budgetId}
              onChange={(id) => set('budgetId', id)}
              onClear={() => set('budgetId', null)}
              placeholder="No budget"
            />
          </Field>
        )}
```

At the top of the file, add the import:

```tsx
import { BudgetCombobox } from '../../components/BudgetCombobox';
```

Place it alphabetically among the existing component imports.

### Step 6: Run the MovementForm tests

- [ ] **Step 6: Verify MovementForm tests still pass**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/features/movements/MovementForm.test.tsx
```

Expected: green. If a test asserts against the `<select>` element, update it to use the Combobox semantics (`role="combobox"`, the placeholder text, etc.) — but only if a test fails. Don't pre-emptively rewrite passing tests.

### Step 7: Run the full suite + build

- [ ] **Step 7: Sanity check**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run && pnpm build
```

Expected: all green, clean build.

### Step 8: Commit

- [ ] **Step 8: Commit**

Run:
```bash
cd <repo>
git add ProjectCeres.Client/src/app/components/BudgetCombobox.tsx ProjectCeres.Client/src/app/components/BudgetCombobox.test.tsx ProjectCeres.Client/src/app/features/movements/MovementForm.tsx ProjectCeres.Client/src/app/features/movements/MovementForm.test.tsx
git commit -m "feat(movements): replace MovementForm budget select with Combobox (T3.14)" -m "Introduces BudgetCombobox (Popover + Command, mirroring AccountCombobox/CategoryCombobox) and swaps the raw <select> at MovementForm.tsx:297–314 for it. Unselect UX: onClear ✕ on the trigger + 'No budget' placeholder. Inactive budgets render with '(archived)' suffix in the list."
```

---

## Task 4: T3.13 — useOptimistic refactor on MovementClearedToggle

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.test.tsx`

### Step 1: Replace the component implementation

- [ ] **Step 1: Edit `MovementClearedToggle.tsx`**

Replace the full contents of `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx` with:

```tsx
import { CheckCircle, Clock } from 'lucide-react';
import { useOptimistic, useState, useTransition } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import type { MovementType } from './movements-api';
import { MOVEMENTS_CLEARED_URL } from './movements-api';

type Props = {
  id: string;
  type: MovementType;
  isCleared: boolean;
};

function typeForApi(type: MovementType): string {
  if (type === 'Transaction') return 'transaction';
  if (type === 'Transfer') return 'transfer';
  return 'liabilitypayment';
}

export function MovementClearedToggle({ id, type, isCleared: initial }: Props) {
  const [serverCleared, setServerCleared] = useState(initial);
  const [optimisticCleared, applyOptimistic] = useOptimistic(serverCleared);
  const [, startTransition] = useTransition();

  function toggle() {
    startTransition(async () => {
      const next = !serverCleared;
      applyOptimistic(next);
      try {
        const response = await fetch(MOVEMENTS_CLEARED_URL(id), {
          method: 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ type: typeForApi(type), cleared: next }),
        });
        if (response.ok) {
          setServerCleared(next);
        } else {
          toast.error("Couldn't update status.");
        }
      } catch {
        toast.error("Couldn't update status.");
      }
    });
  }

  return (
    <button
      type="button"
      onClick={toggle}
      aria-label={optimisticCleared ? 'Mark as pending' : 'Mark as cleared'}
      className="inline-flex cursor-pointer items-center rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      {optimisticCleared ? (
        <Badge variant="success">
          <CheckCircle size={12} aria-hidden="true" />
          Cleared
        </Badge>
      ) : (
        <Badge variant="warning">
          <Clock size={12} aria-hidden="true" />
          Pending
        </Badge>
      )}
    </button>
  );
}
```

### Step 2: Run the existing tests

- [ ] **Step 2: Verify the 5 existing tests still pass**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/features/movements/MovementClearedToggle.test.tsx
```

Expected: 5/5 passing. The user-visible behavior is identical, so no test changes should be required. If a test fails because it asserts against internal state (e.g., `useState` call order), update the assertion to use the rendered output (badge text + icon) instead of internals.

### Step 3: Add a rapid-click race test

- [ ] **Step 3: Append the new test case**

Open `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.test.tsx`. After the existing `describe(...)` body (inside the same `describe`), add the following test. Adapt the surrounding test scaffolding (mock setup) to match the existing tests in this file — match their `vi.fn()` / `globalThis.fetch` style exactly.

```tsx
  it('two rapid clicks settle in the second click direction even if responses arrive out of order', async () => {
    // Arrange: fetch resolves after a controllable delay so we can interleave responses.
    let resolveFirst!: (value: Response) => void;
    let resolveSecond!: (value: Response) => void;
    const responses = [
      new Promise<Response>((r) => { resolveFirst = r; }),
      new Promise<Response>((r) => { resolveSecond = r; }),
    ];
    let call = 0;
    globalThis.fetch = vi.fn(() => responses[call++]) as unknown as typeof fetch;

    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);
    const btn = screen.getByRole('button');

    // Act: two rapid clicks (cleared → pending → cleared semantics: false → true → false).
    fireEvent.click(btn); // optimistic: true
    fireEvent.click(btn); // optimistic: false

    // Resolve out of order: second response lands first (success), then first (failure).
    resolveSecond(new Response(null, { status: 200 }));
    resolveFirst(new Response(null, { status: 500 }));

    // Wait for transitions to flush.
    await waitFor(() => {
      expect(screen.getByText(/Pending/)).toBeInTheDocument();
    });

    // Assert: final state matches the second click's intent (Pending), not the first.
  });
```

If the existing test file does not yet import `waitFor`, add it to the `@testing-library/react` imports. Also confirm `Response` is available in jsdom; if not, polyfill via `vi.stubGlobal` or fall back to a plain `{ ok: boolean }` shape — match the surrounding tests' approach.

### Step 4: Run the test to verify it passes

- [ ] **Step 4: Confirm green**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/features/movements/MovementClearedToggle.test.tsx
```

Expected: 6/6 passing.

### Step 5: Run the full suite

- [ ] **Step 5: Sanity check**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run
```

Expected: full green.

### Step 6: Commit

- [ ] **Step 6: Commit**

Run:
```bash
cd <repo>
git add ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.test.tsx
git commit -m "refactor(movements): useOptimistic on MovementClearedToggle (T3.13)" -m "Replaces hand-rolled set-then-rollback optimistic update with React 19's useOptimistic + useTransition. User-visible behavior is unchanged; the refactor fixes a race where rapid clicks could leave the badge out of sync with the server when responses arrived out of order. Adds a rapid-click test that reproduces the previous race."
```

---

## Task 5: T3.12 — SubmitButton primitive + QuickAddModal consumer

**Files:**
- Create: `ProjectCeres.Client/src/app/components/SubmitButton.tsx`
- Create: `ProjectCeres.Client/src/app/components/SubmitButton.test.tsx`
- Modify: `ProjectCeres.Client/src/app/components/QuickAddModal.tsx` (footer + `submit()` return type)
- Modify: `ProjectCeres.Client/src/app/components/QuickAddModal.test.tsx` (assertions)

### Step 1: Write the failing SubmitButton tests

- [ ] **Step 1: Create `SubmitButton.test.tsx`**

Create `ProjectCeres.Client/src/app/components/SubmitButton.test.tsx`:

```tsx
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SubmitButton } from './SubmitButton';

describe('SubmitButton', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders the idle label initially', () => {
    render(<SubmitButton onClick={async () => {}}>Save</SubmitButton>);
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
  });

  it('shows loading label and aria-busy while the promise is in flight', async () => {
    let resolve!: () => void;
    const onClick = vi.fn(() => new Promise<void>((r) => { resolve = r; }));
    render(<SubmitButton onClick={onClick}>Save</SubmitButton>);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(screen.getByText('Saving…')).toBeInTheDocument());
    expect(screen.getByRole('button')).toHaveAttribute('aria-busy', 'true');

    resolve();
  });

  it('transitions to success then back to idle after successDuration', async () => {
    const onClick = vi.fn(() => Promise.resolve());
    render(<SubmitButton onClick={onClick} successDuration={800}>Save</SubmitButton>);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(screen.getByText('Saved')).toBeInTheDocument());

    await act(async () => {
      vi.advanceTimersByTime(800);
    });

    await waitFor(() => expect(screen.getByText('Save')).toBeInTheDocument());
  });

  it('transitions to error and stays clickable on rejection', async () => {
    const onClick = vi.fn(() => Promise.reject(new Error('boom')));
    render(<SubmitButton onClick={onClick}>Save</SubmitButton>);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => expect(screen.getByText('Try again')).toBeInTheDocument());
    expect(screen.getByRole('button')).not.toBeDisabled();
  });

  it('clicking from error state retries', async () => {
    let attempt = 0;
    const onClick = vi.fn(() => {
      attempt += 1;
      return attempt === 1 ? Promise.reject(new Error('first fails')) : Promise.resolve();
    });
    render(<SubmitButton onClick={onClick}>Save</SubmitButton>);

    fireEvent.click(screen.getByRole('button'));
    await waitFor(() => expect(screen.getByText('Try again')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button'));
    await waitFor(() => expect(screen.getByText('Saved')).toBeInTheDocument());

    expect(onClick).toHaveBeenCalledTimes(2);
  });

  it('double-click during loading is a no-op', async () => {
    const onClick = vi.fn(() => new Promise<void>(() => {})); // never resolves
    render(<SubmitButton onClick={onClick}>Save</SubmitButton>);

    const btn = screen.getByRole('button');
    fireEvent.click(btn);
    fireEvent.click(btn);

    expect(onClick).toHaveBeenCalledTimes(1);
  });
});
```

### Step 2: Run the test to verify it fails

- [ ] **Step 2: Confirm failure mode**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/components/SubmitButton.test.tsx
```

Expected: FAIL with `Cannot find module './SubmitButton'`.

### Step 3: Implement `SubmitButton.tsx`

- [ ] **Step 3: Create the component**

Create `ProjectCeres.Client/src/app/components/SubmitButton.tsx`:

```tsx
import { AlertCircle, Check, Loader2 } from 'lucide-react';
import { useState, type ComponentProps } from 'react';
import { Button } from '@/components/ui/button';

type ButtonProps = ComponentProps<typeof Button>;

type Phase = 'idle' | 'loading' | 'success' | 'error';

type SubmitButtonProps = Omit<ButtonProps, 'onClick' | 'type'> & {
  onClick: () => Promise<void>;
  loadingLabel?: string;
  successLabel?: string;
  errorLabel?: string;
  successDuration?: number;
  children: React.ReactNode;
};

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

export function SubmitButton({
  onClick,
  loadingLabel = 'Saving…',
  successLabel = 'Saved',
  errorLabel = 'Try again',
  successDuration = 800,
  children,
  variant,
  disabled,
  ...rest
}: SubmitButtonProps) {
  const [phase, setPhase] = useState<Phase>('idle');

  async function handleClick() {
    if (phase === 'loading' || phase === 'success') return;
    setPhase('loading');
    try {
      await onClick();
      setPhase('success');
      await sleep(successDuration);
      setPhase('idle');
    } catch {
      setPhase('error');
    }
  }

  const isBusy = phase === 'loading' || phase === 'success';
  const resolvedVariant = phase === 'error' ? 'destructive' : variant;

  return (
    <Button
      {...rest}
      type="button"
      variant={resolvedVariant}
      disabled={isBusy || disabled}
      aria-busy={phase === 'loading' ? 'true' : undefined}
      aria-live="polite"
      onClick={handleClick}
    >
      {phase === 'loading' && (
        <>
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
          {loadingLabel}
        </>
      )}
      {phase === 'success' && (
        <>
          <Check className="h-4 w-4" aria-hidden="true" />
          {successLabel}
        </>
      )}
      {phase === 'error' && (
        <>
          <AlertCircle className="h-4 w-4" aria-hidden="true" />
          {errorLabel}
        </>
      )}
      {phase === 'idle' && children}
    </Button>
  );
}
```

### Step 4: Run the test to verify it passes

- [ ] **Step 4: Confirm green**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/components/SubmitButton.test.tsx
```

Expected: 6/6 passing.

### Step 5: Refactor `QuickAddModal.submit()` to return `Promise<boolean>`

- [ ] **Step 5: Edit `QuickAddModal.tsx`**

Open `ProjectCeres.Client/src/app/components/QuickAddModal.tsx`. Find the `submit` function declaration (around lines 100–180 — search for `async function submit(` or `const submit = async`). Today it likely returns `void` or sets `submitting` state.

Modify it so that:
- It still performs the existing fetch + validation work.
- It returns `true` on a successful save, `false` on validation failure, and **throws** on a server error (so SubmitButton transitions to its error phase).
- Validation errors (missing fields) should still set `errors` state and toast — but **also throw**, so the SubmitButton shows its error state. Reasoning: today the modal stays open + shows inline errors; we want the SubmitButton to mirror that with its `error` phase. Validation failure should `throw new Error('validation')`.

Concretely, replace any current `return;` after a validation failure with `throw new Error('validation');`, and any current `return;` after a server failure with `throw new Error('server');`. Replace the implicit success `return` (or `setSubmitting(false)`) with `return true;`.

Remove the local `submitting` state if it becomes unused after the migration — SubmitButton manages that now. If `submitting` is read elsewhere in the JSX (e.g., to disable other inputs), keep it and update it from inside `submit()` as before.

### Step 6: Replace the footer Save Button

- [ ] **Step 6: Swap the footer**

In the same file, find lines 264–267:

```tsx
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button onClick={submit} disabled={submitting}>{submitting ? 'Saving…' : 'Save'}</Button>
        </DialogFooter>
```

Replace with:

```tsx
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <SubmitButton
            onClick={async () => {
              await submit();
              onOpenChange(false);
            }}
          >
            Save
          </SubmitButton>
        </DialogFooter>
```

Add the import at the top:

```tsx
import { SubmitButton } from './SubmitButton';
```

(Place alphabetically.)

If `submitting` state was kept for other purposes in Step 5, leave it untouched here. The SubmitButton no longer reads it.

### Step 7: Update `QuickAddModal.test.tsx`

- [ ] **Step 7: Adjust the existing tests**

Open `ProjectCeres.Client/src/app/components/QuickAddModal.test.tsx`. Search for any test that asserts:
- `getByText('Saving…')` — should still work; SubmitButton shows that during loading.
- `getByRole('button', { name: 'Save' })` — should still work; that is the idle label.
- A test that fires the click on the Save button and expects the modal to close — should still work because we still call `onOpenChange(false)` after `submit()` resolves. Note that the close happens after the 800ms success flash, so any test that asserts close synchronously after click needs to advance fake timers by 800 ms.

Concretely: any failing test of the form

```tsx
fireEvent.click(saveButton);
expect(onOpenChange).toHaveBeenCalledWith(false); // ← this synchronous assertion fails now
```

becomes:

```tsx
fireEvent.click(saveButton);
await waitFor(() => expect(screen.getByText('Saved')).toBeInTheDocument());
await act(async () => { vi.advanceTimersByTime(800); });
await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
```

Use `vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })` in `beforeEach` if not already enabled.

If existing tests don't use fake timers and the success-flash delay is acceptable real-time, you can use `await waitFor(...)` with a longer default `{ timeout: 1500 }` instead. Pick whichever matches the file's existing style.

### Step 8: Run the QuickAddModal tests

- [ ] **Step 8: Verify**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/components/QuickAddModal.test.tsx
```

Expected: green. Iterate on Step 7's adjustments until all assertions pass.

### Step 9: Run the full suite + build

- [ ] **Step 9: Sanity check**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run && pnpm build
```

Expected: all green, clean build.

### Step 10: Commit

- [ ] **Step 10: Commit**

Run:
```bash
cd <repo>
git add ProjectCeres.Client/src/app/components/SubmitButton.tsx ProjectCeres.Client/src/app/components/SubmitButton.test.tsx ProjectCeres.Client/src/app/components/QuickAddModal.tsx ProjectCeres.Client/src/app/components/QuickAddModal.test.tsx
git commit -m "feat(ui): SubmitButton primitive + QuickAddModal consumer (T3.12)" -m "Adds SubmitButton with idle/loading/success/error phases and a 800ms success-flash window. Migrates QuickAddModal's footer Save button to SubmitButton; submit() now returns Promise<boolean> and throws on server error so the button transitions to its error phase. MovementForm continues to use the existing Button + 'Saving…' pattern this round (form-aware variant deferred per spec)."
```

---

## Task 6: Browser-based UX/UI verification

The five commits are landed. Per CLAUDE.md, frontend changes need a manual browser pass.

### Step 1: Start the dev server

- [ ] **Step 1: Start servers**

In one terminal:
```bash
cd <repo>
dotnet run --project ProjectCeres
```

In another terminal:
```bash
cd <repo>/ProjectCeres.Client
pnpm dev
```

Open `http://localhost:5173/app/`.

### Step 2: Tab title verification

- [ ] **Step 2: Verify per-route titles**

Navigate through the SPA and confirm the browser tab title updates. Spot-check at least:
- `/app/` → `Dashboard — Project Ceres`
- `/app/movements` → `Movements — Project Ceres`
- `/app/reports/net-worth` → `Net Worth — Project Ceres`
- `/app/settings` → `Settings — Project Ceres`

### Step 3: theme-color verification

- [ ] **Step 3: Verify theme-color in DevTools**

Open Chrome DevTools → Elements → `<head>`. Confirm both `<meta name="theme-color">` tags are present with the computed hex values. On a mobile emulator (iPhone profile), the URL bar should adopt the light/dark hex when scrolled.

### Step 4: Optimistic toggle verification

- [ ] **Step 4: Status-toggle race test**

On `/app/movements`, click a row's Status badge twice in rapid succession. The badge should settle on the second-click's intent. Throttle the network in DevTools to "Slow 3G" to make the in-flight window observable, then verify the badge does not flicker between states out of order.

### Step 5: Budget Combobox verification

- [ ] **Step 5: Open MovementForm with a budget-eligible category**

Create or edit a Transaction whose currency matches an existing Goal Budget. The budget picker should render as a Combobox with `No budget` placeholder. Select a budget — the trigger label updates. Click the ✕ — selection clears, placeholder returns. Open the popover — archived budgets show `(archived)` suffix.

### Step 6: SubmitButton verification

- [ ] **Step 6: QuickAddModal save**

Open the QuickAdd modal from the TopBar `+`. Fill a valid Transaction, click Save. Observe:
- Spinner + `Saving…` during the request.
- ✓ + `Saved` for ~800 ms.
- Modal closes.

Then trigger an error path (e.g., toggle network offline, or submit with an invalid amount). Observe:
- Button transitions to `Try again` with destructive variant.
- Button stays clickable.
- Re-clicking with corrected input returns to the success path.

### Step 7: Motion-tokens visual smoke

- [ ] **Step 7: Confirm dropdowns and color transitions feel right**

On `/app/movements`, open the type filter (uses the migrated `transition-transform`). The chevron should rotate at the same speed as before. On the MovementForm, hover a category radio (uses migrated `transition-colors`). Color change should feel identical to pre-migration.

### Step 8: Reduced-motion sanity check

- [ ] **Step 8: System Preferences → Accessibility → Reduce Motion**

Toggle macOS Reduce Motion ON. Open the app. Confirm:
- The 800 ms success flash on SubmitButton still occurs (the duration is data, not animation).
- The chevron rotation is instant.
- The optimistic toggle still works.

### Step 9: Mobile breakpoint check

- [ ] **Step 9: 375 px viewport**

In DevTools, switch to iPhone SE (375 px). Open QuickAdd → Save. Confirm SubmitButton fits in the dialog footer, the Combobox popover is the right width on `/app/movements/new`.

---

## Task 7: Roadmap update

After all six tasks pass and the browser pass is clean.

### Step 1: Mark T3.11–T3.15 as `[x]` in the roadmap

- [ ] **Step 1: Edit `docs/roadmap-phase-three.md` lines 319–323**

Change five lines:

```markdown
- [x] T3.11 Migrate `duration-200` literals to motion tokens (3.1, 3.4) — shipped 2026-05-08 (commit hash from Task 1)
- [x] T3.12 Build `<SubmitButton>` with idle/loading/success/error + spinner (8.4) — shipped 2026-05-08 (commit hash from Task 5); QuickAddModal-only consumer; MovementForm deferred until form-aware variant
- [x] T3.13 `useOptimistic` for the Status block toggle on table rows (5.1) — shipped 2026-05-08 (commit hash from Task 4)
- [x] T3.14 Replace MovementForm budget `<select>` with Combobox, or document the rule — shipped 2026-05-08 (commit hash from Task 3)
- [x] T3.15 Harden `index.html` — `theme-color` meta, description meta, per-route titles (1.7) — shipped 2026-05-08 (commit hash from Task 2)
```

Substitute the actual commit hashes after each commit lands.

Also update the Stage 5 status line at line 250:

```markdown
**Status: ⚠️ In progress.** Data-loading ease-in fully rolled out (5.2/5.3/5.4). Tier 1 polish done (T1.1–T1.4 + T1.6 shipped; T1.5 deliberately not shipped). Tier 2 polish done (T2.7–T2.10). Tier 3 polish done (T3.11–T3.15 shipped 2026-05-08). Tier 4–5 pending.
```

### Step 2: Commit

- [ ] **Step 2: Commit**

```bash
cd <repo>
git add docs/roadmap-phase-three.md
git commit -m "docs(roadmap): mark Tier 3 polish (T3.11–T3.15) as shipped"
```

---

## Self-review notes (for the engineer)

- **Spec coverage:** Task 1 covers T3.11; Task 2 covers T3.15; Task 3 covers T3.14; Task 4 covers T3.13; Task 5 covers T3.12. Each spec section maps 1:1 to a task.
- **Order rationale:** Lowest-risk first. Motion tokens (mechanical, no behavior change) before titles (additive, no behavior change) before Combobox (UX swap, identical data) before optimistic refactor (behavior change but covered by tests) before SubmitButton (most surface area + dependent test rewrites).
- **Out-of-scope reminders:** MovementForm submit migration to SubmitButton is deferred (per spec). Tailwind theme extension for motion shorthand is deferred. Per-page `<meta name="description">` overrides are deferred.
- **Stay-on-main rule:** Per project memory, do not branch or use worktrees. All commits land on `main` directly.
- **No `Co-Authored-By` trailer:** Per project memory, omit it from every commit message in this plan.
- **No `git push` suggestions:** This repo has no remote. After committing, do not suggest pushing.
