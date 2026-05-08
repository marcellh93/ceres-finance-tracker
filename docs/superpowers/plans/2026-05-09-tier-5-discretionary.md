# Tier 5 Discretionary + Combobox Ordering Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the two actionable Tier 5 items (T5.20 OKLCH parser, T5.21 motion rule index) plus a combobox ordering fix that surfaced during the brainstorm. T5.19 stays deferred with a recorded rationale.

**Architecture:** Four sequential commits, lowest-risk first. Combobox ordering fix is the bug surfaced during planning — ships first as the "catalyst" change. T5.20 extends the design-system showcase parser. T5.21 indexes the motion rule in the design-system Working rules. Final commit syncs the roadmap and polish checklist.

**Tech Stack:** React 19 + Vitest + cmdk + shadcn `<Command>` (combobox fix); plain TypeScript with the OKLab→sRGB matrix already used in T3.15 (T5.20); markdown only (T5.21 + roadmap).

**Spec reference:** `docs/superpowers/specs/2026-05-09-tier-5-discretionary-design.md` (commit `9fa580b`).

**Per project memory:** all `pnpm` commands use `pnpm --dir ProjectCeres.Client …` (no `cd`-then-pnpm). All `git` commands use `git -C <repo> …`. Stay on `main`. No `Co-Authored-By:` trailer. No push.

---

## File structure

### Files created

| Path | Responsibility |
|---|---|
| `ProjectCeres.Client/src/design-system/lib/contrast.test.ts` | New unit tests for `parseRgb` covering rgb, oklch, error paths, and `contrastRatio` integration. |

### Files modified

| Path | Why |
|---|---|
| `ProjectCeres.Client/src/app/components/AccountCombobox.tsx` | Combobox fix — `shouldFilter={false}` + manual filter preserving alphabetical input order. |
| `ProjectCeres.Client/src/app/components/CategoryCombobox.tsx` | Same. |
| `ProjectCeres.Client/src/app/components/BudgetCombobox.tsx` | Same. |
| `ProjectCeres.Client/src/app/components/AccountCombobox.test.tsx` | Add ordering-during-search test. |
| `ProjectCeres.Client/src/app/components/CategoryCombobox.test.tsx` | Same. |
| `ProjectCeres.Client/src/app/components/BudgetCombobox.test.tsx` | Same. |
| `ProjectCeres.Client/src/design-system/lib/contrast.ts` | T5.20 — extend `parseRgb` to dispatch on `rgb()` vs `oklch()`. |
| `ProjectCeres.Client/src/design-system/components/SwatchGrid.tsx` | T5.20 — remove the try/catch that swallowed OKLCH parse failures. |
| `docs/design-system.md` | T5.21 — add motion-token rule as Working rule #7; renumber existing rules 7→8 and 8→9. |
| `docs/roadmap-phase-three.md` | Roadmap sync — mark T5.20/T5.21 done, T5.19 deferred with rationale, bump Stage 5 status. |
| `docs/ceres-polish-checklist-frontend.md` | Same. |

---

## Task 1: Combobox ordering fix

**Files:**
- Modify: `ProjectCeres.Client/src/app/components/AccountCombobox.tsx`
- Modify: `ProjectCeres.Client/src/app/components/CategoryCombobox.tsx`
- Modify: `ProjectCeres.Client/src/app/components/BudgetCombobox.tsx`
- Modify: `ProjectCeres.Client/src/app/components/AccountCombobox.test.tsx`
- Modify: `ProjectCeres.Client/src/app/components/CategoryCombobox.test.tsx`
- Modify: `ProjectCeres.Client/src/app/components/BudgetCombobox.test.tsx`

### Step 1: Write the failing test for AccountCombobox

- [ ] **Step 1: Add ordering-during-search test**

Open `ProjectCeres.Client/src/app/components/AccountCombobox.test.tsx`. Read the existing top-of-file fixtures, then add a new test at the end of the `describe('AccountCombobox', () => { … })` block.

The fixture in this file already has three accounts (`Checking`, `Savings`, `Credit Card`). For the alphabetical-order test, override the prop with a deliberately ordered list:

```tsx
  it('preserves the input order during search instead of cmdk relevance reorder', () => {
    const sorted = [
      { id: 'a1', name: 'Apple Bank', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
      { id: 'a2', name: 'Acorn Account', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
      { id: 'a3', name: 'Banana Bank', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
    ];
    render(<AccountCombobox accounts={sorted} value={null} onChange={vi.fn()} placeholder="Select" />);

    fireEvent.click(screen.getByRole('combobox'));

    const searchInput = screen.getByPlaceholderText('Search accounts…');
    fireEvent.change(searchInput, { target: { value: 'a' } });

    // All three names contain 'a' (case-insensitive). The list must
    // preserve the input order: Apple Bank → Acorn Account → Banana Bank.
    const items = screen.getAllByRole('option');
    expect(items.map((el) => el.textContent)).toEqual(['Apple Bank', 'Acorn Account', 'Banana Bank']);
  });
```

If the existing test file uses a different account-fixture shape (check the actual `AccountOptionDto` import), adapt the fixture object literally to that shape — every field shown is required.

### Step 2: Run the test to verify it fails

- [ ] **Step 2: Confirm fail mode**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/components/AccountCombobox.test.tsx
```

Expected: this new test fails. Today's `<Command>` uses cmdk's default fuzzy-match scoring; under search "a" the items reorder by relevance (often `Acorn` first because it starts with "A" and has tighter match). The assertion `Apple Bank → Acorn Account → Banana Bank` fails.

### Step 3: Apply the fix to AccountCombobox

- [ ] **Step 3: Edit `AccountCombobox.tsx`**

Open the file. The current shape (around lines 25–28):

```tsx
export function AccountCombobox({ accounts, value, onChange, placeholder, filter, disabled, className = 'w-full', onClear }: Props) {
  const [open, setOpen] = useState(false);
  const filtered = filter ? accounts.filter(filter) : accounts;
  const selected = accounts.find((a) => a.id === value) ?? null;
```

Add a `useState` for the search input alongside `open`:

```tsx
import { useState, type MouseEvent } from 'react';
// (existing import line — no change needed if useState already imported)

export function AccountCombobox({ accounts, value, onChange, placeholder, filter, disabled, className = 'w-full', onClear }: Props) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');

  const typeFiltered = filter ? accounts.filter(filter) : accounts;
  const trimmed = search.trim().toLowerCase();
  const filtered = trimmed === ''
    ? typeFiltered
    : typeFiltered.filter((a) => a.name.toLowerCase().includes(trimmed));

  const selected = accounts.find((a) => a.id === value) ?? null;
```

Then find the `<Command>` block (around line 65). Add `shouldFilter={false}` to it, and bind the `<CommandInput>` to the new state. The block becomes:

```tsx
      <PopoverContent className="p-0" align="start">
        <Command shouldFilter={false}>
          <CommandInput
            placeholder="Search accounts…"
            value={search}
            onValueChange={setSearch}
          />
          <CommandList>
            <CommandEmpty>No accounts found.</CommandEmpty>
            <CommandGroup>
              {filtered.map((account) => (
                <CommandItem /* …existing props… */ />
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
```

Don't change the `CommandItem` body — only the `<Command>` and `<CommandInput>` props change. The existing `filtered.map` already uses the variable that's now controlled by the manual filter.

If `<CommandInput>`'s API expects `onValueChange` (cmdk default) versus `onChange`, match what's in `ProjectCeres.Client/src/components/ui/command.tsx`. Both are the same controlled-input pattern.

### Step 4: Run the AccountCombobox tests to verify the fix

- [ ] **Step 4: Confirm green**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/components/AccountCombobox.test.tsx
```

Expected: all AccountCombobox tests pass, including the new ordering test (the items now stay in input order).

### Step 5: Apply the fix to CategoryCombobox + add ordering test

- [ ] **Step 5: Edit CategoryCombobox.tsx and CategoryCombobox.test.tsx**

Open `ProjectCeres.Client/src/app/components/CategoryCombobox.tsx`. The shape is the same as AccountCombobox minus the `filter` prop. Apply the same change pattern:

```tsx
export function CategoryCombobox({ categories, value, onChange, placeholder, disabled, className = 'w-full', onClear }: Props) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');

  const trimmed = search.trim().toLowerCase();
  const filtered = trimmed === ''
    ? categories
    : categories.filter((c) => c.name.toLowerCase().includes(trimmed));

  const selected = categories.find((c) => c.id === value) ?? null;
```

In the JSX, change:

```tsx
        <Command>
          <CommandInput placeholder="Search categories…" />
          <CommandList>
            <CommandEmpty>No categories found.</CommandEmpty>
            <CommandGroup>
              {categories.map((category) => (
```

to:

```tsx
        <Command shouldFilter={false}>
          <CommandInput
            placeholder="Search categories…"
            value={search}
            onValueChange={setSearch}
          />
          <CommandList>
            <CommandEmpty>No categories found.</CommandEmpty>
            <CommandGroup>
              {filtered.map((category) => (
```

Note: `categories.map` → `filtered.map` — the only change to the iteration source. The `<CommandItem>` body stays identical.

Now open `ProjectCeres.Client/src/app/components/CategoryCombobox.test.tsx`. After the existing tests, append:

```tsx
  it('preserves the input order during search instead of cmdk relevance reorder', () => {
    const sorted: CategoryOptionDto[] = [
      { id: 'c1', name: 'Apparel', categoryTypeName: 'Expense' },
      { id: 'c2', name: 'Auto', categoryTypeName: 'Expense' },
      { id: 'c3', name: 'Bills', categoryTypeName: 'Expense' },
    ];
    render(<CategoryCombobox categories={sorted} value={null} onChange={vi.fn()} placeholder="Select" />);

    fireEvent.click(screen.getByRole('combobox'));

    const searchInput = screen.getByPlaceholderText('Search categories…');
    fireEvent.change(searchInput, { target: { value: 'a' } });

    const items = screen.getAllByRole('option');
    // Both Apparel and Auto match 'a'; Bills filtered out. Order preserved.
    expect(items.map((el) => el.textContent?.replace(/Expense$/, '').trim())).toEqual(['Apparel', 'Auto']);
  });
```

The `.replace(/Expense$/, '').trim()` strips the `categoryTypeName` suffix that the component renders alongside the name (existing behavior — see line 81 of `CategoryCombobox.tsx`: `<span className="text-xs text-muted-foreground">{category.categoryTypeName}</span>`).

### Step 6: Run the CategoryCombobox tests

- [ ] **Step 6: Confirm green**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/components/CategoryCombobox.test.tsx
```

Expected: all tests pass (existing 3 + 1 new = 4).

### Step 7: Apply the fix to BudgetCombobox + add ordering test

- [ ] **Step 7: Edit BudgetCombobox.tsx and BudgetCombobox.test.tsx**

Open `ProjectCeres.Client/src/app/components/BudgetCombobox.tsx`. Same change pattern. The component takes `budgets`:

```tsx
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
  const [search, setSearch] = useState('');

  const trimmed = search.trim().toLowerCase();
  const filtered = trimmed === ''
    ? budgets
    : budgets.filter((b) => b.name.toLowerCase().includes(trimmed));

  const selected = budgets.find((b) => b.id === value) ?? null;
```

In the JSX, find the `<Command>` block and change:

```tsx
        <Command>
          <CommandInput placeholder="Search budgets…" />
          <CommandList>
            <CommandEmpty>No budgets found.</CommandEmpty>
            <CommandGroup>
              {budgets.map((b) => (
```

to:

```tsx
        <Command shouldFilter={false}>
          <CommandInput
            placeholder="Search budgets…"
            value={search}
            onValueChange={setSearch}
          />
          <CommandList>
            <CommandEmpty>No budgets found.</CommandEmpty>
            <CommandGroup>
              {filtered.map((b) => (
```

Now open `ProjectCeres.Client/src/app/components/BudgetCombobox.test.tsx`. After the existing tests, append:

```tsx
  it('preserves the input order during search instead of cmdk relevance reorder', () => {
    const sorted: GoalBudgetListItemDto[] = [
      { id: 'b1', name: 'Apartment Fund', goalType: 'Spending', currencyCode: 'EUR', currencySymbol: '€', targetAmount: 5000, startDate: '2026-01-01', endDate: null, description: null, isActive: true },
      { id: 'b2', name: 'Auto Repair', goalType: 'Spending', currencyCode: 'EUR', currencySymbol: '€', targetAmount: 1000, startDate: '2026-01-01', endDate: null, description: null, isActive: true },
      { id: 'b3', name: 'Birthday Gifts', goalType: 'Spending', currencyCode: 'EUR', currencySymbol: '€', targetAmount: 500, startDate: '2026-01-01', endDate: null, description: null, isActive: true },
    ];
    render(<BudgetCombobox budgets={sorted} value={null} onChange={vi.fn()} />);

    fireEvent.click(screen.getByRole('combobox'));

    const searchInput = screen.getByPlaceholderText('Search budgets…');
    fireEvent.change(searchInput, { target: { value: 'a' } });

    const items = screen.getAllByRole('option');
    expect(items.map((el) => el.textContent)).toEqual(['Apartment Fund', 'Auto Repair']);
  });
```

If `GoalBudgetListItemDto` requires more fields than shown (the test file's existing fixture is the source of truth — match it), copy missing fields from there.

### Step 8: Run the BudgetCombobox tests

- [ ] **Step 8: Confirm green**

```bash
pnpm --dir ProjectCeres.Client test --run src/app/components/BudgetCombobox.test.tsx
```

Expected: 8 prior + 1 new = 9 passing.

### Step 9: Run the full suite

- [ ] **Step 9: Verify no regression**

```bash
pnpm --dir ProjectCeres.Client test --run
```

Expected: 850 prior + 3 new ordering tests = 853 passing. Modulo the same intermittent flake (`BudgetEdit > discriminator` / `MovementForm Test 14`) — retry once if it hits.

### Step 10: Run the production build

- [ ] **Step 10: Build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: clean, all chunks within budget. The Combobox change adds ~50 bytes per file uncompressed, well within the existing 18–20% headroom on `app-*.js`.

### Step 11: Commit

- [ ] **Step 11: Commit**

```bash
git -C <repo> add ProjectCeres.Client/src/app/components/AccountCombobox.tsx ProjectCeres.Client/src/app/components/CategoryCombobox.tsx ProjectCeres.Client/src/app/components/BudgetCombobox.tsx ProjectCeres.Client/src/app/components/AccountCombobox.test.tsx ProjectCeres.Client/src/app/components/CategoryCombobox.test.tsx ProjectCeres.Client/src/app/components/BudgetCombobox.test.tsx
git -C <repo> commit -m "fix(combobox): preserve input order during search instead of cmdk's relevance reorder" -m "shadcn's <Command> wraps cmdk, which by default re-sorts items by fuzzy-match score as the user types in the search input. For our domain comboboxes (Account, Category, Budget) this scrambles the alphabetical order the consumers pass in. Pass shouldFilter={false} and implement a case-insensitive substring filter manually in each component. The filter preserves the input order, so search narrows results without reordering them. New ordering tests in each component's test file lock the behaviour."
```

NO `Co-Authored-By:` trailer.

---

## Task 2: T5.20 — OKLCH support in parseRgb

**Files:**
- Create: `ProjectCeres.Client/src/design-system/lib/contrast.test.ts`
- Modify: `ProjectCeres.Client/src/design-system/lib/contrast.ts`
- Modify: `ProjectCeres.Client/src/design-system/components/SwatchGrid.tsx`

### Step 1: Write the failing tests

- [ ] **Step 1: Create `contrast.test.ts`**

Create `ProjectCeres.Client/src/design-system/lib/contrast.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { contrastRatio, formatRatio } from './contrast';

// parseRgb is internal; we exercise it via contrastRatio (which calls it twice)
// and through observable RGB values where rgb() pass-through means the
// expected numbers are predictable.

describe('contrast', () => {
  it('parses rgb(r, g, b)', () => {
    // White vs black is the canonical 21:1 contrast ratio.
    expect(contrastRatio('rgb(255, 255, 255)', 'rgb(0, 0, 0)')).toBeCloseTo(21, 1);
  });

  it('drops alpha from rgba(r, g, b, a)', () => {
    expect(contrastRatio('rgba(255, 255, 255, 0.5)', 'rgba(0, 0, 0, 0.5)')).toBeCloseTo(21, 1);
  });

  it('parses oklch(L C h) — primary token', () => {
    // oklch(0.520 0.110 195) is the project's --primary token in light mode.
    // T3.15 computed the corresponding sRGB hex as #007c7c (rgb(0, 124, 124)).
    // Contrast against black should be ~5.05:1 (verified via formula on rgb(0,124,124) vs rgb(0,0,0)).
    const ratio = contrastRatio('oklch(0.520 0.110 195)', 'rgb(0, 0, 0)');
    expect(ratio).toBeGreaterThan(4.5);
    expect(ratio).toBeLessThan(6);
  });

  it('parses oklch(1 0 0) as white', () => {
    expect(contrastRatio('oklch(1 0 0)', 'rgb(0, 0, 0)')).toBeCloseTo(21, 1);
  });

  it('parses oklch(0 0 0) as black', () => {
    expect(contrastRatio('rgb(255, 255, 255)', 'oklch(0 0 0)')).toBeCloseTo(21, 1);
  });

  it('drops alpha from oklch(L C h / α)', () => {
    // Alpha must not affect the parsed RGB triple.
    const withAlpha = contrastRatio('oklch(1 0 0 / 0.5)', 'rgb(0, 0, 0)');
    const withoutAlpha = contrastRatio('oklch(1 0 0)', 'rgb(0, 0, 0)');
    expect(withAlpha).toBeCloseTo(withoutAlpha, 1);
  });

  it('throws on unsupported color spaces (e.g. hsl)', () => {
    expect(() => contrastRatio('hsl(0, 100%, 50%)', 'rgb(0, 0, 0)')).toThrow(/Cannot parse color/);
  });

  it('cross-format contrast (oklch white vs rgb black) returns ~21', () => {
    expect(contrastRatio('oklch(1 0 0)', 'rgb(0, 0, 0)')).toBeCloseTo(21, 1);
  });

  it('formatRatio produces the AAA/AA/Large/Fail label', () => {
    expect(formatRatio(21)).toContain('AAA');
    expect(formatRatio(5.5)).toContain('AA');
    expect(formatRatio(3.5)).toContain('Large only');
    expect(formatRatio(2)).toContain('Fail');
  });
});
```

### Step 2: Run the test to verify it fails

- [ ] **Step 2: Confirm fail mode**

```bash
pnpm --dir ProjectCeres.Client test --run src/design-system/lib/contrast.test.ts
```

Expected: the OKLCH-related tests fail because `parseRgb` currently rejects anything that isn't `rgb()`/`rgba()`. The pure-rgb tests should already pass.

### Step 3: Extend `parseRgb` with OKLCH support

- [ ] **Step 3: Edit `contrast.ts`**

Replace the contents of `ProjectCeres.Client/src/design-system/lib/contrast.ts` with:

```ts
/**
 * Parse a CSS color string into an RGB triple in the [0, 255] range.
 *
 * Accepts:
 * - `rgb(r, g, b)` / `rgba(r, g, b, a)` — alpha is dropped
 * - `oklch(L C h)` / `oklch(L C h / α)` — converted via the standard
 *   linear-RGB matrix; alpha is dropped
 *
 * Throws if the input matches neither format.
 */
function parseRgb(input: string): [number, number, number] {
  const trimmed = input.trim();
  const oklchMatch = trimmed.match(/^oklch\(([^)]+)\)$/i);
  if (oklchMatch) {
    return oklchToSrgb(oklchMatch[1]);
  }
  const rgbMatch = trimmed.match(/^rgba?\(([^)]+)\)$/i);
  if (rgbMatch) {
    const parts = rgbMatch[1]
      .split(/[\s,/]+/)
      .map((s) => parseFloat(s.trim()))
      .filter((n) => !Number.isNaN(n));
    return [parts[0], parts[1], parts[2]];
  }
  throw new Error(`Cannot parse color: ${input}`);
}

/**
 * Convert OKLCH components (as a single string like "0.520 0.110 195" or
 * "0.520 0.110 195 / 0.8") to an sRGB triple in [0, 255]. Alpha is dropped.
 *
 * Matrix and gamma constants come from Björn Ottosson's reference OKLab→sRGB
 * implementation. Same constants used in the T3.15 theme-color computation.
 */
function oklchToSrgb(args: string): [number, number, number] {
  const tokens = args.split('/')[0].trim().split(/\s+/);
  const L = parseFloat(tokens[0]);
  const C = parseFloat(tokens[1]);
  const h = parseFloat(tokens[2]);
  if (Number.isNaN(L) || Number.isNaN(C) || Number.isNaN(h)) {
    throw new Error(`Cannot parse OKLCH components: ${args}`);
  }
  const a = C * Math.cos((h * Math.PI) / 180);
  const b = C * Math.sin((h * Math.PI) / 180);
  // OKLab → linear LMS
  const l_ = L + 0.3963377774 * a + 0.2158037573 * b;
  const m_ = L - 0.1055613458 * a - 0.0638541728 * b;
  const s_ = L - 0.0894841775 * a - 1.2914855480 * b;
  const lLms = l_ ** 3;
  const mLms = m_ ** 3;
  const sLms = s_ ** 3;
  // Linear LMS → linear sRGB
  const lr =  4.0767416621 * lLms - 3.3077115913 * mLms + 0.2309699292 * sLms;
  const lg = -1.2684380046 * lLms + 2.6097574011 * mLms - 0.3413193965 * sLms;
  const lb = -0.0041960863 * lLms - 0.7034186147 * mLms + 1.7076147010 * sLms;
  // Linear sRGB → gamma-encoded sRGB → 0–255
  const gamma = (u: number) => (u >= 0.0031308 ? 1.055 * Math.pow(u, 1 / 2.4) - 0.055 : 12.92 * u);
  const clamp = (v: number) => Math.max(0, Math.min(255, Math.round(gamma(v) * 255)));
  return [clamp(lr), clamp(lg), clamp(lb)];
}

function relativeLuminance([r, g, b]: [number, number, number]): number {
  const channel = (c: number) => {
    const v = c / 255;
    return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/**
 * Compute the WCAG 2.1 contrast ratio between two color strings.
 * Use `getComputedStyle(el).backgroundColor` / `.color` to resolve
 * CSS custom properties to concrete color() values before passing in.
 */
export function contrastRatio(a: string, b: string): number {
  const la = relativeLuminance(parseRgb(a));
  const lb = relativeLuminance(parseRgb(b));
  const [light, dark] = la > lb ? [la, lb] : [lb, la];
  return (light + 0.05) / (dark + 0.05);
}

export function formatRatio(ratio: number): string {
  const r = ratio.toFixed(2);
  if (ratio >= 7) return `${r} : 1 (AAA)`;
  if (ratio >= 4.5) return `${r} : 1 (AA)`;
  if (ratio >= 3) return `${r} : 1 (Large only)`;
  return `${r} : 1 (Fail)`;
}
```

### Step 4: Run the contrast tests to verify they pass

- [ ] **Step 4: Confirm green**

```bash
pnpm --dir ProjectCeres.Client test --run src/design-system/lib/contrast.test.ts
```

Expected: 9/9 passing.

### Step 5: Drop the try/catch in SwatchGrid

- [ ] **Step 5: Edit `SwatchGrid.tsx`**

Open `ProjectCeres.Client/src/design-system/components/SwatchGrid.tsx`. Find the block (around lines 30–48):

```tsx
    const bg = getComputedStyle(ref.current).backgroundColor;
    let ratio: string | undefined;
    if (swatch.contrastAgainst) {
      try {
        const probe = document.createElement('div');
        probe.style.color = `var(--${swatch.contrastAgainst})`;
        ref.current.appendChild(probe);
        const fg = getComputedStyle(probe).color;
        ref.current.removeChild(probe);
        ratio = formatRatio(contrastRatio(bg, fg));
      } catch {
        // Browser returned a non-rgb() color (e.g. oklch()) that the
        // contrast helper can't parse yet. Skip the ratio rather than
        // crash the page; track in design-system.md known limitations.
        ratio = undefined;
      }
    }
    setInfo({ rgb: bg, ratio });
```

Replace with:

```tsx
    const bg = getComputedStyle(ref.current).backgroundColor;
    let ratio: string | undefined;
    if (swatch.contrastAgainst) {
      const probe = document.createElement('div');
      probe.style.color = `var(--${swatch.contrastAgainst})`;
      ref.current.appendChild(probe);
      const fg = getComputedStyle(probe).color;
      ref.current.removeChild(probe);
      ratio = formatRatio(contrastRatio(bg, fg));
    }
    setInfo({ rgb: bg, ratio });
```

The try/catch is gone. With `parseRgb` now understanding OKLCH, the catch was unreachable for valid token references; for unsupported color spaces (none today) the page should fail loud rather than silently skip — that's the right tradeoff for an internal showcase.

### Step 6: Run the design-system showcase tests

- [ ] **Step 6: Confirm SwatchGrid still renders**

```bash
pnpm --dir ProjectCeres.Client test --run src/design-system/
```

Expected: existing showcase tests still pass. If `SwatchGrid` has its own test, it should now compute non-undefined ratios for OKLCH tokens (a behaviour change but a correct one).

### Step 7: Run the full suite + build

- [ ] **Step 7: Sanity check**

```bash
pnpm --dir ProjectCeres.Client test --run
pnpm --dir ProjectCeres.Client build
```

Expected: 853 prior + 9 new contrast tests = 862 passing. Build clean.

### Step 8: Visual verification

- [ ] **Step 8: Open the design-system showcase**

Start the dev server and visit the design-system page:

```bash
dotnet run --project ProjectCeres
```

In another terminal:

```bash
pnpm --dir ProjectCeres.Client dev
```

Open `http://localhost:5173/design-system.html`. Navigate to the color palette section. **Every swatch with a `contrastAgainst` token should now show a contrast ratio label** (e.g. "5.12 : 1 (AA)") — previously these were missing for any OKLCH-defined pair.

This is a manual confirmation step; no automated assertion. If a swatch still shows no ratio, inspect the browser console for an unexpected color-space string and report.

### Step 9: Commit

- [ ] **Step 9: Commit**

```bash
git -C <repo> add ProjectCeres.Client/src/design-system/lib/contrast.ts ProjectCeres.Client/src/design-system/lib/contrast.test.ts ProjectCeres.Client/src/design-system/components/SwatchGrid.tsx
git -C <repo> commit -m "feat(showcase): OKLCH support in contrast parser, drop try/catch fallback (T5.20)" -m "parseRgb in design-system/lib/contrast.ts now dispatches on rgb()/rgba() vs oklch() input. The OKLCH branch uses the standard OKLab→sRGB matrix (same constants as the T3.15 theme-color computation, traced to Björn Ottosson's reference implementation). Alpha is dropped on both formats. The SwatchGrid try/catch that masked the missing parser is removed — the catch was a bandaid for a known limitation that's now fixed. Contrast ratios on OKLCH-defined tokens (most of the project's palette) are no longer silently skipped in the showcase."
```

NO `Co-Authored-By:` trailer.

---

## Task 3: T5.21 — Motion rule in Working rules section

**Files:**
- Modify: `docs/design-system.md`

### Step 1: Edit the Working rules section

- [ ] **Step 1: Add the motion rule and renumber**

Open `docs/design-system.md`. Find the Working rules block (lines 43–64 area). The current numbering ends at rule 8 (UX/UI verification checklist).

Insert a new rule 7 (Motion tokens) before the existing rule 7 (Pre-commit audit), and renumber 7→8 and 8→9.

Specifically:

- Find the existing line `7. **Pre-commit audit.** …`. Insert this line BEFORE it:

```markdown
7. **Motion tokens.** All transition and animation durations must reference the motion tokens (`--motion-duration-fast/base/slow`). Don't introduce raw `duration-200` literals or hardcoded ms values — every animated property in the SPA goes through one of those three tokens. See [Motion](#motion) for the token table and easing functions.
```

- Change the existing `7. **Pre-commit audit.**` to `8. **Pre-commit audit.**`.

- Change the existing `8. **UX/UI verification checklist — required after every implementation.**` to `9. **UX/UI verification checklist — required after every implementation.**`.

No other content changes in this section.

### Step 2: Verify no other doc references the old numbering

- [ ] **Step 2: Grep for hardcoded references**

```bash
grep -rn "rule 7\|rule 8\|Working rule [78]\|working rules.*[78]" <repo>/docs/ <repo>/CLAUDE.md 2>/dev/null
```

Expected: no matches. The Working rules numbering isn't cross-referenced elsewhere — they're accessed by name (e.g. "the UX/UI verification checklist"), not by number. If a match surfaces, update it to the new number.

### Step 3: Commit

- [ ] **Step 3: Commit**

```bash
git -C <repo> add docs/design-system.md
git -C <repo> commit -m "docs(design-system): index motion-token rule in Working rules section (T5.21)" -m "The Motion section is rich (token table, easing functions, view-transition naming, status of the rule) but the Working rules at the top of design-system.md don't yet point at it. Engineers reading the rules-first don't know the motion-token rule exists until they hit a code review. Adds a one-bullet rule 7 referencing the Motion section; renumbers Pre-commit audit and UX/UI verification checklist to 8 and 9 respectively. No content change in the Motion section itself."
```

NO `Co-Authored-By:` trailer.

---

## Task 4: Roadmap + checklist sync

**Files:**
- Modify: `docs/roadmap-phase-three.md`
- Modify: `docs/ceres-polish-checklist-frontend.md`

### Step 1: Update Tier 5 list in `roadmap-phase-three.md`

- [ ] **Step 1: Replace the Tier 5 block**

Open `docs/roadmap-phase-three.md`. Find the Tier 5 list (around lines 331–335):

```markdown
Tier 5 — discretionary:

- [ ] T5.19 `@formkit/auto-animate` for lists that reorder (0.3, 3.6)
- [ ] T5.20 OKLCH support in `parseRgb()` so showcase shows live contrast ratios
- [ ] T5.21 Add motion rules to "Working rules" section in `design-system.md`
```

Replace with (substituting the actual commit hashes after each commit lands):

```markdown
Tier 5 — discretionary: ✅ T5.20/T5.21 shipped 2026-05-09; T5.19 deferred

- [ ] T5.19 `@formkit/auto-animate` for lists that reorder (0.3, 3.6) — **deferred 2026-05-09: no candidate consumer.** Auto-animate animates between two states of the same children (slide instead of snap when items insert/remove/reorder). The SPA's lists today refetch-on-filter-change — the whole list is replaced, not items reordered. No drag-to-reorder UI exists. Reopens when a real reordering surface emerges (e.g., custom dashboard widget order, manual category sort).
- [x] T5.20 OKLCH support in `parseRgb()` so showcase shows live contrast ratios — shipped 2026-05-09 (commit `<task-2-sha>`); also drops the try/catch fallback in `SwatchGrid` that previously masked the missing parser
- [x] T5.21 Add motion rules to "Working rules" section in `design-system.md` — shipped 2026-05-09 (commit `<task-3-sha>`); inserted as rule 7, renumbered Pre-commit audit + UX/UI verification checklist
```

### Step 2: Bump Stage 5 status line

- [ ] **Step 2: Update the status banner**

In the same file, find line 250 (the Stage 5 status line):

```markdown
**Status: ⚠️ In progress.** Data-loading ease-in fully rolled out (5.2/5.3/5.4). Tier 1 polish done (T1.1–T1.4 + T1.6 shipped; T1.5 deliberately not shipped). Tier 2 polish done (T2.7–T2.10). Tier 3 polish done (T3.11–T3.15 shipped 2026-05-08). Tier 4 testing-infrastructure done (T4.16–T4.18 shipped 2026-05-08). Tier 5 pending. Plus a batch of ad-hoc UX fixes shipped on top of the planned tiers — see § Ad-hoc UX fixes below.
```

Replace with:

```markdown
**Status: ✅ Done (2026-05-09).** Data-loading ease-in fully rolled out (5.2/5.3/5.4). Tier 1 polish done (T1.1–T1.4 + T1.6 shipped; T1.5 deliberately not shipped). Tier 2 polish done (T2.7–T2.10). Tier 3 polish done (T3.11–T3.15 shipped 2026-05-08). Tier 4 testing-infrastructure done (T4.16–T4.18 shipped 2026-05-08). Tier 5 done (T5.20/T5.21 shipped 2026-05-09; T5.19 deliberately deferred — no candidate consumer). Plus a batch of ad-hoc UX fixes shipped on top of the planned tiers — see § Ad-hoc UX fixes below.
```

Also find the "next up" line at line 252:

```markdown
> **As of 2026-05-08, next up:** Tier 4 (testing infrastructure) and Tier 5 (discretionary polish) remain. Alternative: switch to launch-critical work (Stage 6 Identity infrastructure). Stage 5 is explicitly *not* blocking Phase 3 launch.
```

Replace with:

```markdown
> **As of 2026-05-09, next up:** Stage 5 is closed out. The next launch-critical work is Stage 6 (Identity infrastructure). Stage 5 was explicitly *not* blocking Phase 3 launch.
```

### Step 3: Update the sub-stages table

- [ ] **Step 3: Mark 5.9 done**

In the same file, find the sub-stages table around line 268:

```markdown
| 5.9 | Polish checklist Tier 5 — discretionary | ❌ Pending | (above) |
```

Replace with:

```markdown
| 5.9 | Polish checklist Tier 5 — discretionary | ✅ 2026-05-09 (T5.20 `<task-2-sha>`, T5.21 `<task-3-sha>`; T5.19 deferred — no candidate consumer) | (above) |
```

### Step 4: Update `ceres-polish-checklist-frontend.md`

- [ ] **Step 4: Replace the Tier 5 block**

Open `docs/ceres-polish-checklist-frontend.md`. Find the Tier 5 block (around line 230):

```markdown
**Tier 5 — discretionary:**

19. `@formkit/auto-animate` for lists that reorder (0.3, 3.6)
20. OKLCH support in `parseRgb()` so showcase shows live contrast ratios
21. Add motion rules to "Working rules" section in `design-system.md`
```

Replace with:

```markdown
**Tier 5 — discretionary:** ✅ T5.20/T5.21 shipped 2026-05-09; T5.19 deferred

19. ⏸ `@formkit/auto-animate` for lists that reorder — **deferred 2026-05-09: no candidate consumer.** Reopens when a real reordering surface emerges.
20. ✅ OKLCH support in `parseRgb()` (commit `<task-2-sha>`); SwatchGrid try/catch fallback removed.
21. ✅ Motion rule indexed in design-system.md Working rules as rule 7 (commit `<task-3-sha>`).
```

### Step 5: Commit

- [ ] **Step 5: Commit**

After substituting the actual commit hashes from Tasks 2 and 3:

```bash
git -C <repo> add docs/roadmap-phase-three.md docs/ceres-polish-checklist-frontend.md
git -C <repo> commit -m "docs(roadmap): close out Stage 5 Tier 5 (T5.20, T5.21 shipped; T5.19 deferred)" -m "Tier 5 closes Stage 5 Frontend polish + data-loading ease-in. T5.20 (OKLCH parser) and T5.21 (motion rule index) shipped; T5.19 (auto-animate) deliberately deferred with rationale recorded in both the roadmap and the polish checklist — no candidate consumer in the SPA today, reopens when a drag-to-reorder surface emerges. Stage 5 status banner updated to ✅ Done; next-up note redirects to Stage 6 (Identity infrastructure)."
```

NO `Co-Authored-By:` trailer.

---

## Self-review notes (for the engineer)

- **Spec coverage:**
  - § Combobox ordering fix → Task 1 (3 component edits + 3 test additions, single commit)
  - § T5.20 → Task 2 (parser extension + test file + SwatchGrid simplification, single commit)
  - § T5.21 → Task 3 (single doc edit, single commit)
  - § T5.19 deferred-with-rationale → Task 4 (recorded in both roadmap and checklist)
- **Order rationale:** Combobox fix first (the catalyst, observable user-visible improvement). T5.20 next (code, testable). T5.21 third (docs, no test surface). Roadmap sync last so its commit references the actual hashes.
- **Out-of-scope reminders:**
  - T5.19 is intentionally not implemented — recorded as deferred in Task 4 only.
  - Server-side sort verification for accounts/categories/budgets endpoints is assumed correct; flag any discovery during browser pass.
  - HSL or other color-space support in `parseRgb` is not added.
  - Renaming `parseRgb` to `parseColor` for accuracy is cosmetic; deferred.
- **Stay-on-main:** Per project memory, no branches or worktrees. Commit straight to `main`.
- **No `Co-Authored-By` trailer:** Per project memory.
- **No `git push`:** This repo has no remote.
- **`pnpm --dir ProjectCeres.Client`:** Per project memory, all pnpm commands use this flag. Same for `git -C <repo>`.
- **Foreground tests:** Per project memory.
