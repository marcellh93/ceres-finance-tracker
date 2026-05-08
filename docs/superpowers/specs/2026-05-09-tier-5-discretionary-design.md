# Tier 5 Discretionary + Combobox Ordering Fix — 2026-05-09

**Roadmap reference:** `docs/roadmap-phase-three.md` § Stage 5 → Tier 5 discretionary (T5.19, T5.20, T5.21).

**Status:** Approved 2026-05-09. Implementation pending.

## Summary

Two Tier 5 items shipped + one bug fix surfaced during the brainstorm. T5.19 deliberately not shipped, recorded as a future-tier note.

| ID | Item | Why now |
|---|---|---|
| T5.19 | `@formkit/auto-animate` for lists that reorder | **Deliberately not shipped.** No candidate consumer in the SPA today. Recorded as a future suggestion. |
| T5.20 | OKLCH support in `parseRgb()` | Design-system showcase silently skips contrast ratios on every OKLCH-defined token, which is most of the palette. |
| T5.21 | Motion rules in design-system.md "Working rules" section | The Motion section exists; the Working rules index doesn't yet point at it. |
| (bug) | Combobox dropdown reorders by relevance score during search | When the user types in the category/account/budget search, items re-order by cmdk's fuzzy-match score instead of staying alphabetical. Surfaced during T5.19 discussion; auto-animate doesn't fix it; the fix is a `<Command>` filter override. |

## Decisions captured during brainstorm

- **T5.19 deferred:** Auto-animate animates between two states of the same children (slide instead of snap when items insert/remove/reorder). The SPA's lists today are server-fetched-on-filter-change — the whole list is replaced, not items reordered. No real consumer. Recording as future-tier suggestion in the roadmap so it's not lost.
- **T5.20 approach:** Extend `parseRgb()` to also parse `oklch(L C h)` strings in addition to `rgb()/rgba()`. Convert OKLCH→sRGB inline via the standard linear-RGB matrix the project already used for the T3.15 theme-color computation. Remove the try/catch fallback in `SwatchGrid` — once `parseRgb` understands OKLCH, the catch is dead code.
- **T5.21 approach:** Add a single bullet to the "Working rules" section pointing at `## Motion` for the canonical token reference and the "no new literals" rule.
- **Combobox ordering fix approach:** Pass `shouldFilter={false}` to `<Command>` and implement filtering manually in each Combobox. The manual filter applies a case-insensitive substring match against the option name and preserves the original input order — which is alphabetical for accounts/categories/budgets.

---

## T5.20 — OKLCH support in parseRgb

### Surface

- `ProjectCeres.Client/src/design-system/lib/contrast.ts` — extend `parseRgb` to handle `oklch(L C h)` and `oklch(L C h / α)` syntax.
- `ProjectCeres.Client/src/design-system/components/SwatchGrid.tsx` — remove the try/catch fallback that masked the missing parser.
- `ProjectCeres.Client/src/design-system/lib/contrast.test.ts` — new test file. Vitest covers the parsers + integration with `contrastRatio`.

### Why this matters

`SwatchGrid` reads `getComputedStyle(el).backgroundColor` and `.color` to compute contrast ratios. Browsers return computed colors in their original color space — for tokens defined in OKLCH (which is most of the project's palette per `index.css`), they return `oklch(...)` strings that the current `parseRgb` rejects. The result: every OKLCH swatch silently skips its contrast ratio in the design-system showcase. The catch in `SwatchGrid` was a bandaid; the fix is to teach the parser OKLCH.

### Implementation

`parseRgb` becomes a dispatcher that detects format and delegates:

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
    const parts = rgbMatch[1].split(/[\s,/]+/).map((s) => parseFloat(s.trim())).filter((n) => !Number.isNaN(n));
    return [parts[0], parts[1], parts[2]];
  }
  throw new Error(`Cannot parse color: ${input}`);
}

function oklchToSrgb(args: string): [number, number, number] {
  // Format: "L C h" or "L C h / α" — alpha dropped.
  const tokens = args.split('/')[0].trim().split(/\s+/);
  const [L, C, h] = tokens.map((t) => parseFloat(t));
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
  // Linear sRGB → gamma-encoded sRGB → 0-255
  const gamma = (u: number) => (u >= 0.0031308 ? 1.055 * Math.pow(u, 1 / 2.4) - 0.055 : 12.92 * u);
  const clamp = (v: number) => Math.max(0, Math.min(255, Math.round(gamma(v) * 255)));
  return [clamp(lr), clamp(lg), clamp(lb)];
}
```

Constants traced to standard OKLab→sRGB conversion (Björn Ottosson's reference implementation).

### SwatchGrid simplification

Remove the try/catch in `SwatchGrid.tsx:32-46`. After T5.20, both `rgb()` and `oklch()` parse; the only failure mode would be an unsupported color space (rare; not currently used).

```tsx
// Before
let ratio: string | undefined;
if (swatch.contrastAgainst) {
  try {
    // …
    ratio = formatRatio(contrastRatio(bg, fg));
  } catch {
    ratio = undefined;
  }
}

// After
let ratio: string | undefined;
if (swatch.contrastAgainst) {
  // …probe DOM, get fg via getComputedStyle…
  ratio = formatRatio(contrastRatio(bg, fg));
}
```

### Tests

New file `ProjectCeres.Client/src/design-system/lib/contrast.test.ts`:

1. `parseRgb` parses `rgb(255, 128, 0)` → `[255, 128, 0]`.
2. `parseRgb` parses `rgba(255, 128, 0, 0.5)` → `[255, 128, 0]` (alpha dropped).
3. `parseRgb` parses `oklch(0.520 0.110 195)` (the project's primary token) → an RGB triple within ±1 of `[0, 124, 124]` (matches `#007c7c` from the T3.15 theme-color computation; ±1 tolerance accounts for floating-point rounding).
4. `parseRgb` parses `oklch(1 0 0)` → `[255, 255, 255]` (white).
5. `parseRgb` parses `oklch(0 0 0)` → `[0, 0, 0]` (black).
6. `parseRgb` parses `oklch(0.5 0.1 30 / 0.8)` → an RGB triple (alpha dropped).
7. `parseRgb` throws on `hsl(0, 100%, 50%)` (unsupported format).
8. `contrastRatio('rgb(255, 255, 255)', 'rgb(0, 0, 0)')` returns 21 (perfect contrast — sanity check).
9. `contrastRatio('oklch(1 0 0)', 'oklch(0 0 0)')` also returns ~21 (cross-format works).

---

## T5.21 — Motion rules in Working rules

### Surface

`docs/design-system.md` § Working rules.

### Implementation

Add a single bullet after rule 6 (View transitions) and before rule 7 (Pre-commit audit):

```markdown
7. **Motion tokens.** All transition and animation durations must reference the motion tokens (`--motion-duration-fast/base/slow`). Don't introduce raw `duration-200` literals or hardcoded ms values — every animated property in the SPA goes through one of those three tokens. See [Motion](#motion) for the token table and easing functions.
```

Renumber the existing rules 7 (Pre-commit audit) and 8 (UX/UI verification checklist) to 8 and 9 respectively.

### Why this matters

The Motion section is rich but unindexed in the Working rules. Engineers reading the rules first don't know the motion-token rule exists until they hit a code review.

### Tests

No tests — docs change.

---

## Combobox ordering fix

### Surface

- `ProjectCeres.Client/src/app/components/AccountCombobox.tsx`
- `ProjectCeres.Client/src/app/components/CategoryCombobox.tsx`
- `ProjectCeres.Client/src/app/components/BudgetCombobox.tsx`

All three use shadcn's `<Command>` (cmdk wrapper). Default behavior: `<CommandInput>` typing reorders items by `cmdk`'s fuzzy-match score. The user expects results to stay in the original input order (alphabetical), narrowed to matches.

### Implementation

Pass `shouldFilter={false}` to `<Command>` and implement the filter manually:

```tsx
import { useState } from 'react';

export function CategoryCombobox(/* …existing props… */) {
  const [search, setSearch] = useState('');
  // …existing state…

  // Case-insensitive substring match; preserves the input order (alphabetical
  // for our consumers). Empty search returns the full list.
  const filtered = search.trim() === ''
    ? categories
    : categories.filter((c) => c.name.toLowerCase().includes(search.toLowerCase()));

  return (
    // …
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
            <CommandItem /* … */ />
          ))}
        </CommandGroup>
      </CommandList>
    </Command>
    // …
  );
}
```

Same change in all three Combobox files. The filter logic is domain-specific (Account searches by `name`, Category by `name`, Budget by `name`) so duplication is ~5 lines per file; not worth extracting a helper.

### Sort assumption

The fix relies on the *consumer* passing items in the desired order. Today:

- `AccountCombobox` — accounts come from `/api/accounts/active`. The server returns them by `Name` ascending (verified empirically in earlier sessions; if not, that's a server-side fix, not a client-side one).
- `CategoryCombobox` — categories from `/api/categories/active`. Same assumption.
- `BudgetCombobox` — budgets from `matchingGoals` (filtered Goal list). Order is whatever the parent passes; today the Goal Budgets list is server-sorted.

If any of those endpoints return unsorted data, the fix would expose that. Out of scope for this round — record any discovery as a follow-up.

### Tests

Each Combobox already has a test suite. Add one new test per file:

10. *("alphabetical order preserved during search")* — render with three items `[Apple, Banana, Cherry]`, type "a" in the search input, assert the rendered list shows `Apple` and `Banana` in that order (Cherry filtered out, A before B preserved).

The existing tests assert filter behavior at a higher level; they should continue passing because the behavior change is "items now stay in input order" rather than "matches differ."

---

## T5.19 — recorded as future-tier note

In the roadmap (`docs/roadmap-phase-three.md`), update the Tier 5 list to mark T5.19 as deliberately deferred with a clear rationale:

```markdown
- [ ] T5.19 `@formkit/auto-animate` for lists that reorder (0.3, 3.6) — **deferred 2026-05-09: no candidate consumer.** Auto-animate animates between two states of the same children (slide instead of snap when items insert/remove/reorder). The SPA's lists today refetch-on-filter-change — the whole list is replaced, not items reordered. No drag-to-reorder UI exists. Reopens when a real reordering surface emerges (e.g., custom dashboard widget order, manual category sort).
```

Same wording in `docs/ceres-polish-checklist-frontend.md` Tier 5 list.

---

## File structure

| Path | Status |
|---|---|
| `ProjectCeres.Client/src/design-system/lib/contrast.ts` | Modified — add OKLCH branch + helper |
| `ProjectCeres.Client/src/design-system/lib/contrast.test.ts` | New — 9 tests |
| `ProjectCeres.Client/src/design-system/components/SwatchGrid.tsx` | Modified — remove try/catch |
| `ProjectCeres.Client/src/app/components/AccountCombobox.tsx` | Modified — `shouldFilter={false}` + manual filter |
| `ProjectCeres.Client/src/app/components/CategoryCombobox.tsx` | Same |
| `ProjectCeres.Client/src/app/components/BudgetCombobox.tsx` | Same |
| `ProjectCeres.Client/src/app/components/AccountCombobox.test.tsx` | Modified — add ordering test |
| `ProjectCeres.Client/src/app/components/CategoryCombobox.test.tsx` | Modified — add ordering test |
| `ProjectCeres.Client/src/app/components/BudgetCombobox.test.tsx` | Modified — add ordering test |
| `docs/design-system.md` | Modified — add motion rule to Working rules; renumber |
| `docs/roadmap-phase-three.md` | Modified — mark T5.20/T5.21 done; T5.19 deferred with rationale; bump Stage 5 status |
| `docs/ceres-polish-checklist-frontend.md` | Modified — same |

## Commit plan

Three commits plus the roadmap update, in order:

1. **Combobox ordering fix** — `fix(combobox): preserve input order during search instead of cmdk's relevance reorder`
2. **T5.20** — `feat(showcase): OKLCH support in contrast parser, drop try/catch fallback`
3. **T5.21** — `docs(design-system): index motion-token rule in Working rules section`
4. **Roadmap sync** — `docs(roadmap): close out Stage 5 Tier 5 (T5.20, T5.21 shipped; T5.19 deferred)`

Order rationale: bug fix first (it was the catalyst), then T5.20 (code), then T5.21 (docs), then the umbrella roadmap update marking Stage 5 complete.

## Out of scope

- T5.19 implementation (deferred, recorded as future-tier).
- Server-side sort verification for accounts/categories/budgets (assumed alphabetical; flag any discovery).
- Renaming `parseRgb` to a more accurate name (e.g. `parseColor`) — would require touching `contrastRatio`'s callers; cosmetic.
- HSL or other color-space support in `parseRgb` — not in use.
