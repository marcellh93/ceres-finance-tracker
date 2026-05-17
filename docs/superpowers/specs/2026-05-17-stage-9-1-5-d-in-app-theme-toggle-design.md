# Stage 9.1.5.d — In-app light/dark/system theme toggle

**Status:** Spec — pending user review, then writing-plans skill.
**Phase:** Phase 3, Stage 9.1.5 (Phase-1-discovered bugfix batch).
**Originating bug:** Roadmap line 1099 — "Add in-app light/dark mode toggle. Phase 1 UX walkthrough — `ThemeProvider defaultTheme=\"system\"` means OS preference wins at load with no in-app override affordance. Pairs with 9.1.5.c."
**Date:** 2026-05-17.
**Parent commit:** `0930563` (HEAD at spec-write).

---

## 1. The user-facing problem

After Stage 9.1.5.c shipped, the SPA respects OS color-scheme preference at first paint and propagates OS changes at runtime. But two things are still broken from the user's point of view:

1. **The existing `ThemeToggle` is binary (light ↔ dark) and lives only inside the signed-in app shell.** It is not reachable from auth-only routes (`/app/login`, `/app/register`, `/app/password-reset`). A user signing in for the first time on a misconfigured machine has no way to flip the theme before they authenticate.
2. **Clicking the toggle once locks the user out of system-following mode.** The provider supports `theme = 'system'` as a first-class state, but the toggle skips it entirely. Once the user clicks, `localStorage` holds `'light'` or `'dark'` forever — the OS preference is permanently ignored unless the user manually clears storage.

This stage fixes both.

## 2. What ships

1. Rewrite `src/design-system/components/ThemeToggle.tsx` from a binary `<Button>` cycle to a tri-state `<DropdownMenu>` selector using `DropdownMenuRadioGroup` + `DropdownMenuRadioItem`. Items: **System** (first, default), **Light**, **Dark**.
2. The trigger icon shows the *resolved* theme (Sun for light, Moon for dark). The *preference* (System / Light / Dark) is visible only inside the open dropdown via the radio-item check indicator.
3. Public API of `ThemeToggle` is unchanged: same export name, same `showLabel?: boolean` prop, same import paths. All existing mount sites work without source edits.
4. Drop the `<span>Appearance</span>` outer label in `MobileDrawer.tsx:43-46`; the dropdown trigger's own icon + label is self-describing (matches the LanguageToggle pattern in the rest of the app shell).
5. Add the third mount site: `<ThemeToggle />` inside `AuthLayout.tsx`'s footer, inline with the existing `<LanguageToggle />`, centered with `gap-2`.
6. Add 4 new i18n keys (`theme.system`, `theme.light`, `theme.dark`, `theme.ariaLabel`) to `en.json` and `es.json`.
7. New test file `src/design-system/components/ThemeToggle.test.tsx` (7 tests).
8. Updates to existing tests whose assertions assumed the old binary toggle shape: `TopBar.test.tsx`, `MobileDrawer.test.tsx`, `AppLayout.a11y.test.tsx`, `AuthLayout.test.tsx`.
9. Roadmap close-out: flip `[ ]` to `[x]` on lines 1099 and 1110 of `docs/roadmap-phase-three.md`; update task #42 (in_progress → completed).

## 3. Component design

**File:** `src/design-system/components/ThemeToggle.tsx` (rewrite — same path, same exports).

**Public API (unchanged):**

```tsx
type ThemeToggleProps = { showLabel?: boolean };
export function ThemeToggle(props: ThemeToggleProps): JSX.Element;
```

**Implementation:**

```tsx
import { Moon, Sun } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { useTheme, type Theme } from '@/app/theme/theme-context';

type ThemeToggleProps = { showLabel?: boolean };

export function ThemeToggle({ showLabel = false }: ThemeToggleProps) {
  const { theme, resolvedTheme, setTheme } = useTheme();
  const { t } = useTranslation();
  const isDark = resolvedTheme === 'dark';
  const Icon = isDark ? Moon : Sun;
  const resolvedLabel = isDark ? t('theme.dark') : t('theme.light');

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button
            variant={showLabel ? 'outline' : 'ghost'}
            size={showLabel ? 'sm' : 'icon'}
            type="button"
            aria-label={t('theme.ariaLabel')}
          >
            <Icon className={showLabel ? 'h-4 w-4' : 'h-5 w-5'} />
            {showLabel && <span className="ml-2">{resolvedLabel}</span>}
          </Button>
        }
      />
      <DropdownMenuContent align={showLabel ? 'end' : 'center'}>
        <DropdownMenuRadioGroup
          value={theme}
          onValueChange={(value) => setTheme(value as Theme)}
        >
          <DropdownMenuRadioItem value="system">{t('theme.system')}</DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="light">{t('theme.light')}</DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="dark">{t('theme.dark')}</DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

**Why this shape:**

- Trigger styling matches the two existing usages: icon-only `ghost` (TopBar, AuthLayout footer) vs labeled `outline sm` (MobileDrawer's preferences row). The branching mirrors what the old binary toggle already did, so `<ThemeToggle />` and `<ThemeToggle showLabel />` callsites need zero source edits beyond the MobileDrawer label-removal.
- Trigger icon = resolved icon (Sun/Moon) — the icon always represents what the page looks like right now. The "what's my preference" question (System / Light / Dark) is answered inside the open dropdown by the radio check indicator. No `Monitor` icon anywhere, per the design decision in § 6.
- `value={theme}` binds to the *preference*, so when `theme === 'system'` the System radio is checked even though the trigger shows Sun or Moon. `onValueChange` casts to the existing `Theme` union from `theme-context.tsx`.
- `align="end"` on the labeled variant keeps the dropdown from spilling off-screen at the right edge of the MobileDrawer; `align="center"` keeps the icon-only variant aligned under its trigger.
- i18n keys follow the LanguageToggle precedent (`auth.languageToggle.english` etc.). All four new strings get added in the same commit chain.

**The `DropdownMenu` primitive is `@base-ui/react`-backed** (not Radix). The relevant base-ui API surface used here: `<DropdownMenuTrigger render={<Button …>…</Button>}>` (base-ui's render-prop slot, not Radix's `asChild`); `DropdownMenuRadioGroup` accepts `value` + `onValueChange(value: string)`; `DropdownMenuRadioItem` carries `data-checked` + `aria-checked` when its value matches the group's value. Verified by reading `src/components/ui/dropdown-menu.tsx` at parent commit.

## 4. Mount sites

### 4.1 TopBar (existing — no JSX change)

`src/app/layout/TopBar.tsx:89` already renders `<ThemeToggle />` inside the desktop-gated controls cluster. The rewrite preserves the same import path and component API, so this mount works unchanged. The component shape change is invisible at the import site.

### 4.2 MobileDrawer (existing — drop the outer label)

`src/app/layout/MobileDrawer.tsx:43-46` currently renders:

```tsx
<div className="mt-2 flex items-center justify-between px-3 py-2">
  <span className="text-sm text-foreground">Appearance</span>
  <ThemeToggle showLabel />
</div>
```

After this stage:

```tsx
<div className="mt-2 flex items-center justify-end px-3 py-2">
  <ThemeToggle showLabel />
</div>
```

The outer `Appearance` label was a holdover from the binary toggle's narrow surface. The new dropdown trigger shows the resolved theme icon + label (e.g., Sun + "Light"), and opening it shows three labeled options. The outer label duplicates information that's now visible on the trigger itself. Drop it.

The wrapper `<div>` stays — it keeps the right-alignment + padding consistent with the surrounding nav rows. `justify-between` becomes `justify-end` since there's only one child.

### 4.3 AuthLayout (new mount)

`src/app/layout/AuthLayout.tsx:26-28` currently renders:

```tsx
<footer className="flex justify-center" data-slot="auth-footer">
  <LanguageToggle />
</footer>
```

After this stage:

```tsx
<footer className="flex justify-center gap-2" data-slot="auth-footer">
  <LanguageToggle />
  <ThemeToggle />
</footer>
```

`gap-2` separates the two icon-only triggers without a divider. The footer's `flex justify-center` keeps both controls centered as a unit. At the 420px-max-width card, both icons fit comfortably side-by-side on every viewport size (verified mentally; will be confirmed in browser at 375px).

## 5. Tests

### 5.1 New file — `src/design-system/components/ThemeToggle.test.tsx`

Seven tests, structured around a `renderWithTheme(ui, themeOverrides)` helper at the top of the file that wraps the UI in `<I18nextProvider>` (or whatever the project test harness uses) and a stub `<ThemeContext.Provider value={...}>` exposing a spy `setTheme`.

| # | Test name | Assertion |
|---|---|---|
| 1 | `renders Sun icon when resolved theme is light` | render with `resolvedTheme='light'`, query the trigger button by `aria-label`, assert it contains an SVG matching the Lucide Sun pattern (`.lucide-sun` class or `data-slot` if Lucide exposes one — confirm during implementation by reading a rendered Lucide icon) |
| 2 | `renders Moon icon when resolved theme is dark` | mirror of #1 with `resolvedTheme='dark'` |
| 3 | `opens dropdown with System / Light / Dark items` | render, click the trigger, assert `getAllByRole('menuitemradio')` returns 3 items with the expected labels |
| 4 | `marks the current theme as checked` | render with `theme='system'`, open the menu, assert the System item has `aria-checked="true"` and the other two have `aria-checked="false"` (or use the project's existing convention if it differs) |
| 5 | `calls setTheme with the selected value` | parameterized: for each of `'system'`, `'light'`, `'dark'`, render fresh, open menu, click the matching item, assert the spy was called with the expected value. Separate renders avoid menu-close state interference. |
| 6 | `showLabel variant adds resolved label text` | render `<ThemeToggle showLabel />` with `resolvedTheme='light'`, assert the button contains the text "Light" (i18n key `theme.light`) |
| 7 | `trigger carries the i18n aria-label` | sanity check that `t('theme.ariaLabel')` resolves to the expected string in the test's i18n init |

**Test mechanics:**
- Per `feedback_never_skip_tests_to_make_them_pass`: all 7 tests must exit 0 before the commit can land.
- Per the recent fetch-mock flake root-cause (commit `cda7b04`): **no `vi.resetAllMocks()` in `afterEach`.** Use per-test fresh `vi.fn()` for the `setTheme` spy.
- `matchMedia` mock comes from the existing `src/test-setup.ts` polyfill (verified during 9.1.5.c).

### 5.2 Updates to existing tests

These files contain assertions that assume the old binary-button shape of `ThemeToggle`. They need re-pinning to the new dropdown shape in the same commit as the component rewrite. Exact assertion targets enumerated during writing-plans via grep:

- `src/app/layout/TopBar.test.tsx` — any `aria-label` match against `/Switch to (light|dark) mode/` becomes `aria-label="Toggle theme"` (the English value of `theme.ariaLabel`; the project's test i18n init defaults to English, so test assertions stay on the English string). Any `getByRole('button', { name: ... })` lookups update accordingly.
- `src/app/layout/MobileDrawer.test.tsx` — same `aria-label` swap, plus any assertion on the "Appearance" span needs deletion (the span no longer exists per § 4.2).
- `src/app/layout/AppLayout.a11y.test.tsx` — flagged in the 9.1.5.c spec as having a stale "theme" comment; check for actual `aria-label` assertions and update.
- `src/app/layout/AuthLayout.test.tsx` — needs ONE new assertion: footer contains two `<Button>` triggers (one each for LanguageToggle and ThemeToggle). A simple `within(footer).getAllByRole('button')` length check is sufficient.

TDD case (per docs/testing.md § Rules) for each touched test: **case (3) — contract changed.** The aria-label string changed; the test rewrites assert against the new contract.

### 5.3 i18n

New keys added to BOTH `src/app/i18n/locales/en.json` AND `src/app/i18n/locales/es.json`:

```json
"theme": {
  "system": "System",     // es: "Sistema"
  "light": "Light",       // es: "Claro"
  "dark": "Dark",         // es: "Oscuro"
  "ariaLabel": "Toggle theme"  // es: "Cambiar tema"
}
```

(Note: the `theme.label` key from the brainstorm is dropped — § 4.2 removes the outer "Appearance" span and the new dropdown trigger is self-describing, so no parent label is needed.)

No new translation parity tests in this stage. If the project has an existing i18n key-parity check, it catches missing Spanish keys automatically. If it doesn't, that's a separate finding — out of scope for 9.1.5.d (not a deferral, an unrelated finding).

### 5.4 Manual UX verification (browser, gates close-out)

After Commit 3 lands, the implementer (or the user if browser access is unavailable) runs:

1. `pnpm --dir ProjectCeres.Client dev` and `dotnet run --project ProjectCeres`.
2. Visit `https://localhost:7081/app/login`:
   - Footer shows Globe icon + Sun/Moon icon side-by-side, centered, with a small gap.
   - At 375px viewport, no wrap, no overflow.
   - Click the theme icon → dropdown shows System / Light / Dark with System checked by default.
   - Click "Light" while OS is dark → page flips to light. Reload → page stays light.
   - Click "System" → page flips back to OS preference.
3. Visit `/app/register` and `/app/password-reset` → same AuthLayout footer behavior.
4. Sign in (any signed-in route, e.g., `/app/dashboard`):
   - TopBar shows ThemeToggle in the desktop controls cluster (≥ 640px viewport).
   - Open it → same three options, same behavior.
   - Resize to 375px → MobileDrawer opens; bottom section shows the labeled ThemeToggle (Sun + "Light" or Moon + "Dark"), no "Appearance" outer label. Open it → same three options.
5. With `theme = 'system'`, open DevTools → Rendering → toggle "Emulate CSS prefers-color-scheme" between light and dark. Page flips both directions.

## 6. Design decisions resolved during brainstorming

| Question | Decision | Why |
|---|---|---|
| Q1: Toggle shape | DropdownMenu selector | Makes all three states (System/Light/Dark) equally one-click reachable. Mirrors LanguageToggle's pattern in the auth footer. A binary toggle ships the same "locked out of system" UX bug the stage exists to fix. |
| Q2: Trigger icon for system state | Resolved Sun/Moon | The icon always represents what the page *looks like*, matching the prior binary-toggle affordance. The preference (system vs explicit) is visible inside the dropdown. |
| Q3: Propagation to AppLayout mounts | Upgrade all three mounts | CLAUDE.md consistency rule: a change to a shared primitive applies everywhere in the same pass. Two UI shapes for the same concept across the app is the anti-pattern the rule exists to prevent. |
| Q4: AuthLayout footer layout | Inline row, `gap-2`, centered | Smallest visual delta from the current single-control footer. Matches the icon-only ghost-button idiom both controls already use. |
| MobileDrawer outer label | Drop the `<span>Appearance</span>` | The new dropdown trigger shows the resolved theme icon + label; the outer span is redundant. Matches the rest of the app shell where icon controls stand alone. |

## 7. Commit sequence

Three commits, each leaves the suite green:

### Commit 1 — i18n keys

- Add `theme.system`, `theme.light`, `theme.dark`, `theme.ariaLabel` to `src/app/i18n/locales/en.json` and `src/app/i18n/locales/es.json`.
- No code changes. `pnpm test` stays green (no consumer yet).
- Rationale: lets the rewrite commit reference the keys without a parallel-file race.

### Commit 2 — ThemeToggle rewrite + new tests + existing-test updates (atomic)

- Rewrite `src/design-system/components/ThemeToggle.tsx` per § 3.
- Create `src/design-system/components/ThemeToggle.test.tsx` with the 7 tests per § 5.1.
- Update `TopBar.test.tsx`, `MobileDrawer.test.tsx`, `AppLayout.a11y.test.tsx`, `AuthLayout.test.tsx` per § 5.2 (exact assertions enumerated during writing-plans).
- Drop the `<span>Appearance</span>` outer label + change `justify-between` → `justify-end` in `MobileDrawer.tsx:43-46`.
- TDD inside this commit: write the new test file first against the unchanged binary impl, watch it fail (case (3) — contract change), rewrite the component, watch it pass.
- Rationale: this is one atomic behavior change. Splitting "rewrite component" from "fix tests that broke because of the rewrite" leaves a red commit between them, which violates the binding rule that every commit must be green (stop-hook + `feedback_never_skip_tests_to_make_them_pass`).

### Commit 3 — AuthLayout footer mount + roadmap close-out

- Edit `AuthLayout.tsx:26-29` to add `gap-2` + `<ThemeToggle />` next to `<LanguageToggle />`.
- Extend `AuthLayout.test.tsx` with the footer-children-count assertion.
- Flip `[ ]` to `[x]` on lines 1099 and 1110 of `docs/roadmap-phase-three.md` with the implementation summary.
- Update task #42 to completed.
- Rationale: keeps the user-visible "now reachable from auth pages" change isolated for clean cherry-pick / revert if browser verification surfaces a layout issue. The rewrite (Commit 2) stays even if Commit 3 has to roll back.

Browser verification per § 5.4 runs AFTER Commit 3 lands, but BEFORE the close-out is treated as final. If verification fails, the close-out commit gets amended (per the project's "never commit a stage as Done with unchecked items" rule).

## 8. Scope guard

### 8.1 In scope

1. `src/design-system/components/ThemeToggle.tsx` rewrite per § 3.
2. `src/design-system/components/ThemeToggle.test.tsx` new file with 7 tests per § 5.1.
3. Existing test updates per § 5.2.
4. `src/app/i18n/locales/en.json` + `es.json` new `theme.*` keys.
5. `MobileDrawer.tsx` outer-label removal per § 4.2.
6. `AuthLayout.tsx` ThemeToggle mount per § 4.3.
7. Roadmap close-out + task #42 update.

### 8.2 Out of scope (design choices, NOT deferrals)

- Per-user-account theme persistence — Phase 5+ when the per-user settings table lands.
- Keyboard shortcut for theme switching — belongs to a future global-shortcuts stage.
- "Theme preview" hover-state in the dropdown items (showing a swatch of the resolved background as you hover).
- Restyling the `DropdownMenu` primitive.
- Theme options beyond light/dark/system (high-contrast, sepia, etc.).
- Touching the Razor layer's theme story — Razor uses `ProjectCeres/Styles/app.css`, separate concern.
- Adding a `Monitor` icon to the trigger for the system state — explicitly rejected in Q2.

### 8.3 Cross-codebase audit

- Grep for `next-themes` references in `ProjectCeres.Client/`: zero hits expected (verified during 9.1.5.c-revised; commit `2c3b2a1` removed all of them).
- Grep for `aria-label="Switch to light mode"` / `aria-label="Switch to dark mode"`: hits become updated test assertions in Commit 2.
- Grep for `<ThemeToggle` usages: three hits (TopBar, MobileDrawer, AuthLayout new mount). All preserved through the rewrite via the unchanged public API.

### 8.4 Deferred work

None. The fix is self-contained and ships in three commits.

## 9. Verification checklist

- [ ] All 7 new tests in `ThemeToggle.test.tsx` exit 0.
- [ ] `pnpm --dir ProjectCeres.Client test` exits 0.
- [ ] `pnpm --dir ProjectCeres.Client build` exits 0 (no TS errors, no bundle-budget regressions).
- [ ] `dotnet test` exits 0 (sanity — no backend changes, stop-hook runs it anyway).
- [ ] Browser verification per § 5.4 passes on every URL + viewport listed.
- [ ] `docs/roadmap-phase-three.md` lines 1099 + 1110 flipped to `[x]` with implementation summary.
- [ ] Task #42 in the tracker marked completed.
- [ ] `grep -rn "Switch to (light|dark) mode" ProjectCeres.Client/src` returns zero hits (the old binary-toggle aria-label strings).

## 10. Open questions

None at spec-write time. All four design decisions resolved during brainstorming (Q1–Q4), MobileDrawer outer-label removed per follow-up clarification, no `[TBD]` or `[follow-up]` markers anywhere.
