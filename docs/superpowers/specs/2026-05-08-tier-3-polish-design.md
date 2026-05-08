# Tier 3 Polish Design — 2026-05-08

**Roadmap reference:** `docs/roadmap-phase-three.md` § Stage 5 → Tier 3 production-app polish (T3.11–T3.15).

**Status:** Approved 2026-05-08. Implementation pending.

## Summary

Five polish items shipped together, each landing as its own commit:

| ID | Item | Surface |
|---|---|---|
| T3.11 | Migrate `duration-200` literals to motion tokens | 11 sites across 4 files |
| T3.12 | `<SubmitButton>` primitive with idle/loading/success/error states | New shared component; QuickAddModal consumer only |
| T3.13 | `useOptimistic` refactor on `MovementClearedToggle` | Single component, no UX change |
| T3.14 | Replace MovementForm budget `<select>` with Combobox | New `BudgetCombobox`, MovementForm consumer |
| T3.15 | Harden `index.html` + per-route titles via `useDocumentTitle` | index.html + ~27 page components |

## Decisions captured during brainstorm

- **SubmitButton state count:** 4 states (idle / loading / success / error) with an 800 ms success flash that the parent `await`s before navigating or closing.
- **Optimistic toggle visual:** Silent — `useOptimistic` is a pure correctness refactor, no in-flight visual signal added.
- **Budget Combobox unselect UX:** `onClear` ✕ on the trigger + `placeholder="No budget"`. Mirrors `AccountCombobox` / `CategoryCombobox` exactly.
- **Title mechanism:** Custom `useDocumentTitle` hook (no third-party dependency).
- **Title format:** `<Page> — Project Ceres`.
- **Motion-tokens migration scope:** All 11 sites including the `Patterns.tsx` showcase. The shadcn-vendored `sheet.tsx` literal is excluded.
- **MovementForm submit:** Stays on the existing `Button + 'Saving…'` pattern this round. SubmitButton ships at QuickAddModal only; revisit when we have a form-aware variant.
- **Tailwind theme extension for motion durations:** Not added. Use arbitrary class `[transition-duration:var(--motion-duration-base)]` directly.

---

## T3.11 — Motion-token migration

### Sites

| File | Lines |
|---|---|
| `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx` | 110 |
| `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx` | 348, 357, 371, 379 |
| `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.tsx` | 93 |
| `ProjectCeres.Client/src/design-system/pages/Patterns.tsx` | 363, 372, 382, 390 |

### Replacement rule

| Before | After |
|---|---|
| `transition-colors duration-200` | `transition-colors [transition-duration:var(--motion-duration-base)]` |
| `transition-transform duration-200` | `transition-transform [transition-duration:var(--motion-duration-base)]` |
| `transition duration-200` | `transition [transition-duration:var(--motion-duration-base)]` |

### Doc sync

- `docs/design-system.md:190` — flip "the rule is aspirational" wording to past tense
- `docs/design-system.md:879–906` — update Patterns code snippet to show the token form
- `docs/design-system.md:928` — strip the "currently a literal" callout
- `docs/design-system.md:1125` — flip Known Limitation to "✅ migrated 2026-05-08"

### Verification

```bash
grep -rn "duration-200" ProjectCeres.Client/src/ | grep -v sheet.tsx
```

Should return zero matches.

### Out of scope

- `ProjectCeres.Client/src/components/ui/sheet.tsx` — shadcn-vendored, retains its literal.

---

## T3.12 — SubmitButton primitive

### Location

- `ProjectCeres.Client/src/app/components/SubmitButton.tsx`
- `ProjectCeres.Client/src/app/components/SubmitButton.test.tsx`

### API

```tsx
import type { ButtonProps } from '@/components/ui/button';

type SubmitButtonProps = Omit<ButtonProps, 'onClick' | 'type'> & {
  onClick: () => Promise<void>;
  loadingLabel?: string;    // default 'Saving…'
  successLabel?: string;    // default 'Saved'
  errorLabel?: string;      // default 'Try again'
  successDuration?: number; // default 800 (ms)
  children: React.ReactNode; // idle label
};
```

### State machine

```
idle ── click ──▶ loading ── promise resolves ──▶ success ── after successDuration ──▶ idle
                          └─ promise rejects  ──▶ error  ── click again ──▶ loading
```

- The button's outer `handleClick` resolves only **after** the success-flash window completes, so callers awaiting that promise can navigate/close once the flash is visible.
- `loading` and `success` are non-interactive (disabled). `error` is clickable for retry.

### Implementation sketch

```tsx
import { Loader2, Check, AlertCircle } from 'lucide-react';
import { useState } from 'react';
import { Button, type ButtonProps } from '@/components/ui/button';

type Phase = 'idle' | 'loading' | 'success' | 'error';

const sleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));

export function SubmitButton({
  onClick,
  loadingLabel = 'Saving…',
  successLabel = 'Saved',
  errorLabel = 'Try again',
  successDuration = 800,
  children,
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

  const disabled = phase === 'loading' || phase === 'success' || rest.disabled;
  const variant = phase === 'error' ? 'destructive' : rest.variant;

  return (
    <Button
      {...rest}
      type="button"
      variant={variant}
      disabled={disabled}
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

### Caller wiring

Only QuickAddModal in this round — the modal-style consumer where `onClick` (not native form submit) drives the flow.

```tsx
// QuickAddModal.tsx — replaces lines 264–267 footer Buttons
<DialogFooter>
  <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
  <SubmitButton
    onClick={async () => {
      const ok = await submit();
      if (!ok) throw new Error('submit-failed');
      onOpenChange(false);
    }}
  >
    Save
  </SubmitButton>
</DialogFooter>
```

`submit()` today returns `void`. Refactor it to return `Promise<boolean>` (true on success, false on validation failure or server error), letting the SubmitButton differentiate success from error via thrown rejection.

### Accessibility

- `aria-busy="true"` during loading
- `aria-live="polite"` on the button surface so phase changes ("Saved", "Try again") are announced
- Disabled in `loading` and `success` (prevents double-submit during the flash)
- `prefers-reduced-motion: reduce` users: the success flash still runs but the icon cross-fade is instant via the global override at `index.css` — no extra work needed in this component

### Tests

`SubmitButton.test.tsx` covers:

1. Renders idle label initially.
2. Click triggers loading state with spinner + `aria-busy="true"`.
3. Resolved promise transitions to success, holds for `successDuration` (fake timers), then back to idle.
4. Rejected promise transitions to error and stays clickable.
5. Re-clicking from error retries.
6. Double-click during loading is a no-op.

---

## T3.13 — useOptimistic refactor on MovementClearedToggle

### Surface

`ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx` only. No prop or visual changes.

### Why refactor

Today's `useState` + manual `setCleared(!next)` rollback has a race: two rapid clicks fire two `fetch`es in flight. If the first response arrives last and fails, the rollback flips state to whatever the *first* fetch's `next` was — out of sync with the server. `useOptimistic` is React-managed: rollback is automatic when the surrounding transition ends without a corresponding server commit.

### New shape

```tsx
import { useOptimistic, useState, useTransition } from 'react';
import { CheckCircle, Clock } from 'lucide-react';
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

### Behavior parity

- Click → badge flips immediately (same as today)
- Server success → badge stays flipped (same as today)
- Server error → badge reverts + toast (same as today, but rollback is automatic via `useOptimistic`)
- Rapid clicks → badge always reflects the latest committed server state plus the latest pending optimistic flip (better than today, where rollbacks could land out of order)

### Tests

`MovementClearedToggle.test.tsx` exists with 5 cases — all should pass unchanged because user-visible behavior is identical. Add one new test:

6. Two rapid clicks while the first request is in flight → final settled state matches the second click's intent, regardless of which response lands first.

---

## T3.14 — Budget Combobox

### New file

- `ProjectCeres.Client/src/app/components/BudgetCombobox.tsx`
- `ProjectCeres.Client/src/app/components/BudgetCombobox.test.tsx`

Lives next to `AccountCombobox.tsx` and `CategoryCombobox.tsx` for symmetry.

### Shape

```tsx
import { Check, ChevronsUpDown, X } from 'lucide-react';
import { useState, type MouseEvent } from 'react';
import { Button } from '@/components/ui/button';
import {
  Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList,
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
  onClear?: () => void;
  id?: string;
};

export function BudgetCombobox({
  budgets, value, onChange, placeholder = 'No budget',
  disabled, className = 'w-full', onClear, id,
}: Props) {
  const [open, setOpen] = useState(false);
  const selected = budgets.find((b) => b.id === value) ?? null;
  const showClear = !!onClear && !!selected && !disabled;

  function handleClear(e: MouseEvent<HTMLButtonElement>) {
    e.preventDefault();
    e.stopPropagation();
    onClear?.();
  }

  // Popover + Command trigger and content — exact mirror of AccountCombobox.tsx,
  // with item label `${b.name}${!b.isActive ? ' (archived)' : ''}`.
}
```

### Caller swap

```tsx
// MovementForm.tsx — replaces lines 297–314
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

### Tests

`BudgetCombobox.test.tsx` mirrors `AccountCombobox.test.tsx`:

1. Renders placeholder when value is null.
2. Renders selected budget name when value matches.
3. Appends `(archived)` suffix when `!isActive`.
4. Clicking an item calls `onChange` with that id.
5. ✕ button calls `onClear` and stops propagation (popover stays closed).
6. Disabled state hides the ✕.

---

## T3.15 — index.html hardening + per-route titles

### Static index.html changes

`ProjectCeres.Client/index.html` becomes:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="description" content="Personal finance tracker for individuals and freelancers. Track assets, liabilities, net worth, income, expenses, and goal budgets." />
    <meta name="theme-color" content="#134e4a" media="(prefers-color-scheme: light)" />
    <meta name="theme-color" content="#0a2625" media="(prefers-color-scheme: dark)" />
    <title>Project Ceres</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

The two `theme-color` hex values shown above are placeholders. During implementation, resolve them from the `--primary` token in `index.css` (deep-teal primary in light mode; the corresponding dark-mode background token).

### useDocumentTitle hook

`ProjectCeres.Client/src/app/lib/use-document-title.ts`:

```tsx
import { useEffect } from 'react';

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

### Page-level wiring

One `useDocumentTitle('Page Name')` call at the top of each top-level page component. ~27 sites:

| Route | Title page name |
|---|---|
| `/app/` | `Dashboard` |
| `/app/movements` | `Movements` |
| `/app/movements/new` | `New Movement` |
| `/app/movements/:id/edit` | `Edit Movement` |
| `/app/budgets` | `Budgets` |
| `/app/accounts` | `Accounts` |
| `/app/categories` | `Categories` |
| `/app/recurring` | `Recurring` |
| `/app/reports` | `Reports` |
| `/app/reports/net-worth` | `Net Worth` |
| `/app/reports/income-expense` | `Income vs. Expense` |
| `/app/reports/expense-breakdown` | `Expense Breakdown` |
| `/app/reports/transaction-history` | `Transaction History` |
| `/app/reports/budget-vs-actual` | `Budget vs. Actual` |
| `/app/reports/largest-expenses` | `Largest Expenses` |
| `/app/reports/monthly-cash-flow` | `Monthly Cash Flow` |
| `/app/reports/net-worth-over-time` | `Net Worth Over Time` |
| `/app/review` | `Review` |
| `/app/import` | `Import` |
| `/app/import/profiles` | `Import Profiles` |
| `/app/import/profiles/new` | `New Import Profile` |
| `/app/import/profiles/:id/edit` | `Edit Import Profile` |
| `/app/settings` | `Settings` |
| `/app/profile` | `Profile` |
| `/app/security` | `Security` |
| `/app/support` | `Support` |
| `/design-system` | `Design System` |

The list above is approximate — the exact route inventory will be confirmed against `src/app/AppRoutes.tsx` (or equivalent) during implementation.

### Tests

- `use-document-title.test.ts` — sets title on mount, restores on unmount, updates when `page` prop changes.
- Spot-check via one or two existing layout tests (e.g., `MovementsLayout.test.tsx`) to confirm the title is set when the page mounts. Not exhaustive — the hook itself is the unit under test.

---

## Commit plan

Each item ships as a separate commit, in this order (lowest-risk first):

1. **T3.11** — motion-token migration sweep + `design-system.md` doc sync
2. **T3.15** — index.html + `useDocumentTitle` hook + per-page wiring
3. **T3.14** — `BudgetCombobox` + MovementForm consumer
4. **T3.13** — `useOptimistic` refactor on `MovementClearedToggle`
5. **T3.12** — `SubmitButton` primitive + QuickAddModal consumer

After all five commits land, update `docs/roadmap-phase-three.md` Stage 5 Tier 3 checklist to mark T3.11–T3.15 as `[x]`.

## Out of scope

- MovementForm submit migration to SubmitButton (deferred until a form-aware variant exists).
- Tailwind theme extension that surfaces motion durations as `duration-base`/`duration-fast`/`duration-slow` shorthand (deferred — arbitrary class is sufficient).
- Per-page `<meta name="description">` overrides (the SPA is auth-gated and not search-indexable; the global description in `index.html` is sufficient).
- Tier 4 (testing infra) and Tier 5 (discretionary). Tracked separately.
