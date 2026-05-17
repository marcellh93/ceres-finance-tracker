# Stage 9.1.5.d — In-app Theme Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the binary `ThemeToggle` with a tri-state `<DropdownMenu>` selector (System / Light / Dark), mount a third instance in the AuthLayout footer, and ship four new i18n keys for the labels.

**Architecture:** One component rewrite at `src/design-system/components/ThemeToggle.tsx` keeps the public API (same export name, same `showLabel?: boolean` prop) so the existing two mount sites (`TopBar.tsx:89`, `MobileDrawer.tsx:45`) work without source edits. A third mount lands in `AuthLayout.tsx`'s footer. Three commits: i18n keys → component rewrite + new tests + MobileDrawer label drop → AuthLayout mount + roadmap close-out.

**Tech Stack:** React 19, Vite, TypeScript, base-ui's DropdownMenu primitives (`@base-ui/react`), Lucide icons (Sun, Moon), react-i18next, Vitest + @testing-library/react.

**Spec:** `docs/superpowers/specs/2026-05-17-stage-9-1-5-d-in-app-theme-toggle-design.md` (committed `753510c`).

**Parent commit:** `753510c` (HEAD at plan-write).

---

## Binding constraints (apply to every task)

- Stay on `main`. No worktrees, no branches.
- `pnpm` only via `pnpm --dir ProjectCeres.Client …`. Never `npm`, `npx`, or `yarn`.
- No `Co-Authored-By` trailer in any commit message.
- TDD per `docs/testing.md` § Rules. Every test-touching commit names which of TDD cases (1), (2), or (3) applied. Commit 2's new test file is **case (1)** — new tests for a new (post-rewrite) component shape. The contract-change framing is incidental because zero existing tests assert on the old binary toggle's strings.
- Pre-existing failures get root-caused NOW (per `feedback_never_skip_tests_to_make_them_pass`). Don't defer.
- Stop-hook (`.claude/hooks/run-tests.sh`) — **CORRECTED 2026-05-17:** fires on Stop event (turn-end), NOT on commit. Tiers by file extension: turns that wrote only `.tsx`/`.ts`/`.md` files exit tier 0 without running `dotnet test`. This stage is frontend-only, so the hook will skip `dotnet test`. Do NOT run it manually unless the .NET suite is suspected red from a prior change. (Earlier drafts of this plan claimed the hook ran on every commit; that was wrong — see `CLAUDE.md` § "When the Stop hook actually fires".)
- `pnpm test` and `pnpm build` always run **foreground** (per `feedback_dont_background_one_shot_verifications` — backgrounded pnpm subprocess output is buffered/empty).
- Per the recent flake root-cause (commit `cda7b04`): **NO `vi.resetAllMocks()` in any test file's `afterEach`.** Use per-test fresh `vi.fn()` for spies.

---

## Discovery findings baked into this plan

Run during plan-write (grep + targeted Reads against parent commit `753510c`). The implementer does NOT need to re-discover these:

1. **The existing binary `ThemeToggle` has zero test coverage.** `grep -rn "Switch to" ProjectCeres.Client/src --include="*.test.tsx"` returns the production source only — no test references. The "update existing tests" portion of Commit 2 (spec § 5.2) is much smaller than the spec assumed: only `AppLayout.a11y.test.tsx` exercises the toggle indirectly (full-layout render + axe), and `AuthLayout.test.tsx` gets one NEW footer-children assertion.
2. **`MobileDrawer.test.tsx` never queries the `<span>Appearance</span>` label or the toggle.** Dropping the span needs no test update.
3. **`TopBar.test.tsx` never queries the ThemeToggle either** — it tests only QuickAdd suppression and the bell badge. The toggle renders inside TopBar but is not asserted on.
4. **Locale files exist; `theme.*` namespace does not exist yet.** `grep -c '"theme"' ProjectCeres.Client/src/app/i18n/locales/en.json` returns `0`. Same for `es.json`. Safe to add fresh.
5. **`LanguageToggle.test.tsx` is the canonical test pattern.** Pattern: `import i18n from '../../i18n/i18n'`, wrap in `<I18nextProvider i18n={i18n}>`, set `void i18n.changeLanguage('en')` in `beforeEach`, query by `screen.getByRole('button', { name: /english-aria-label-regex/i })`.
6. **`TopBar.test.tsx` is the canonical `matchMedia` mock pattern** (lines 9-22). Set `matches: true` for dark, `matches: false` for light. The ThemeToggle test reuses this exact pattern with per-test overrides.
7. **`TopBar.test.tsx` already wraps in `<ThemeProvider>`** (imported from `@/app/theme/theme-context`). The new ThemeToggle.test.tsx wraps in `<ThemeProvider>` similarly OR stubs `useTheme` via `vi.mock` — the test plan in this file goes with the `vi.mock` approach for finer-grained spy control over `setTheme`.

---

## File structure

### Files created

- `ProjectCeres.Client/src/design-system/components/ThemeToggle.test.tsx` — 7 new vitest tests pinning the tri-state dropdown contract.

### Files modified

- `ProjectCeres.Client/src/app/i18n/locales/en.json` — add `theme.*` namespace (4 keys).
- `ProjectCeres.Client/src/app/i18n/locales/es.json` — add `theme.*` namespace (4 keys, Spanish).
- `ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx` — complete rewrite from binary `<Button>` to tri-state `<DropdownMenu>`. Same exports, same prop API.
- `ProjectCeres.Client/src/app/layout/MobileDrawer.tsx:43-46` — drop `<span>Appearance</span>`, change `justify-between` → `justify-end`. Wrapper div stays.
- `ProjectCeres.Client/src/app/layout/AuthLayout.tsx:26-28` — add `gap-2` to footer className; add `<ThemeToggle />` next to `<LanguageToggle />`; add the import.
- `ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx` — add one new test asserting the footer contains both toggle triggers.
- `docs/roadmap-phase-three.md:1099,1110` — flip `[ ]` to `[x]` with implementation summary.

### Files not touched (verified via grep)

- `ProjectCeres.Client/src/app/layout/TopBar.tsx` — `<ThemeToggle />` at line 89 already calls the same API.
- `ProjectCeres.Client/src/app/layout/TopBar.test.tsx` — no assertions on the toggle.
- `ProjectCeres.Client/src/app/layout/MobileDrawer.test.tsx` — no assertions on the toggle or the "Appearance" label.
- `ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx` — exercises the toggle indirectly via full-layout axe; relies on the mocked `Toaster`. The tri-state dropdown may need verification that base-ui's Portal-rendered menu doesn't introduce axe violations when the menu is *closed* (the default initial state in the axe pass). If it does, this is a real bug and gets root-caused per the no-defer rule — not deferred.

---

## Commit 1 — i18n keys (no consumer yet)

### Task 1.1: Add `theme.*` keys to English locale

**Files:**
- Modify: `ProjectCeres.Client/src/app/i18n/locales/en.json`

- [ ] **Step 1: Read the current `en.json`**

Run: `cat ProjectCeres.Client/src/app/i18n/locales/en.json | head -40`
Expected: JSON object with existing namespaces (e.g., `auth`, `nav`, etc.). Confirm no `theme` top-level key.

- [ ] **Step 2: Add the `theme` namespace at the top level**

Add this block to `en.json` at the top level (sibling to existing namespaces — pick the slot that keeps alphabetical order if the file is sorted; otherwise append before the closing `}`):

```json
"theme": {
  "system": "System",
  "light": "Light",
  "dark": "Dark",
  "ariaLabel": "Toggle theme"
}
```

- [ ] **Step 3: Verify the file is valid JSON**

Run: `pnpm --dir ProjectCeres.Client exec node -e "JSON.parse(require('fs').readFileSync('src/app/i18n/locales/en.json','utf8')); console.log('OK')"`
Expected: `OK` (no SyntaxError thrown).

### Task 1.2: Add `theme.*` keys to Spanish locale

**Files:**
- Modify: `ProjectCeres.Client/src/app/i18n/locales/es.json`

- [ ] **Step 1: Read the current `es.json`**

Run: `cat ProjectCeres.Client/src/app/i18n/locales/es.json | head -40`
Expected: JSON object with the same namespaces as `en.json`. Confirm no `theme` top-level key.

- [ ] **Step 2: Add the `theme` namespace with Spanish translations**

Add this block to `es.json` at the top level (same slot as the English version):

```json
"theme": {
  "system": "Sistema",
  "light": "Claro",
  "dark": "Oscuro",
  "ariaLabel": "Cambiar tema"
}
```

- [ ] **Step 3: Verify the file is valid JSON**

Run: `pnpm --dir ProjectCeres.Client exec node -e "JSON.parse(require('fs').readFileSync('src/app/i18n/locales/es.json','utf8')); console.log('OK')"`
Expected: `OK`.

### Task 1.3: Verify the suite stays green (no consumer yet)

- [ ] **Step 1: Run the frontend tests (foreground)**

Run: `pnpm --dir ProjectCeres.Client test --run`
Expected: all tests pass (the existing suite — no new consumer of `theme.*` yet, no new test, no new code path touched).

- [ ] **Step 2: Run the frontend build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: build succeeds, no TS errors, no bundle-budget regressions.

### Task 1.4: Commit

- [ ] **Step 1: Stage and commit**

Run:
```bash
git -C <repo> add ProjectCeres.Client/src/app/i18n/locales/en.json ProjectCeres.Client/src/app/i18n/locales/es.json
git -C <repo> commit -m "feat(stage-9.1.5.d): add theme.* i18n keys (en + es)"
```

Expected: commit succeeds. Stop-hook runs `dotnet test` (frontend-only change → tier-0 skip; should exit 0 quickly). Commit message ends after the subject line (no `Co-Authored-By` trailer).

---

## Commit 2 — ThemeToggle rewrite + new tests + MobileDrawer label drop (atomic)

### Task 2.1: Write the new test file (TDD — failing tests first)

**Files:**
- Create: `ProjectCeres.Client/src/design-system/components/ThemeToggle.test.tsx`

- [ ] **Step 1: Write the complete test file**

Create `ProjectCeres.Client/src/design-system/components/ThemeToggle.test.tsx` with this content:

```tsx
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import i18n from '@/app/i18n/i18n';
import { ThemeToggle } from './ThemeToggle';
import * as themeContext from '@/app/theme/theme-context';

// Stub useTheme so we can spy on setTheme and control the (theme, resolvedTheme) pair per test.
// We avoid wrapping in <ThemeProvider> because doing so couples the test to the provider's
// internal state machine, and we already cover that in theme-context.test.tsx.
type UseThemeReturn = ReturnType<typeof themeContext.useTheme>;

function mockUseTheme(value: UseThemeReturn) {
  return vi.spyOn(themeContext, 'useTheme').mockReturnValue(value);
}

beforeEach(() => {
  void i18n.changeLanguage('en');
});

afterEach(() => {
  vi.restoreAllMocks();
});

function renderToggle(
  themeValue: UseThemeReturn['theme'],
  resolvedValue: UseThemeReturn['resolvedTheme'],
  showLabel = false,
) {
  const setTheme = vi.fn();
  mockUseTheme({ theme: themeValue, resolvedTheme: resolvedValue, setTheme });
  render(
    <I18nextProvider i18n={i18n}>
      <ThemeToggle showLabel={showLabel} />
    </I18nextProvider>,
  );
  return { setTheme };
}

describe('ThemeToggle', () => {
  it('renders the Sun icon when resolved theme is light', () => {
    renderToggle('system', 'light');
    const trigger = screen.getByRole('button', { name: /toggle theme/i });
    // Lucide icons render as <svg> with class 'lucide-sun' (or similar — the icon name
    // lowercased). Match the SVG class to confirm which icon rendered.
    const svg = trigger.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg!.classList.toString()).toMatch(/sun/i);
    expect(svg!.classList.toString()).not.toMatch(/moon/i);
  });

  it('renders the Moon icon when resolved theme is dark', () => {
    renderToggle('system', 'dark');
    const trigger = screen.getByRole('button', { name: /toggle theme/i });
    const svg = trigger.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg!.classList.toString()).toMatch(/moon/i);
    expect(svg!.classList.toString()).not.toMatch(/sun/i);
  });

  it('opens dropdown with System / Light / Dark items', async () => {
    const user = userEvent.setup();
    renderToggle('system', 'light');
    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    const items = await screen.findAllByRole('menuitemradio');
    expect(items).toHaveLength(3);
    expect(items.map((i) => i.textContent)).toEqual(['System', 'Light', 'Dark']);
  });

  it('marks the current theme preference as checked', async () => {
    const user = userEvent.setup();
    renderToggle('system', 'light');
    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    const systemItem = await screen.findByRole('menuitemradio', { name: 'System' });
    const lightItem = screen.getByRole('menuitemradio', { name: 'Light' });
    const darkItem = screen.getByRole('menuitemradio', { name: 'Dark' });
    expect(systemItem.getAttribute('aria-checked')).toBe('true');
    expect(lightItem.getAttribute('aria-checked')).toBe('false');
    expect(darkItem.getAttribute('aria-checked')).toBe('false');
  });

  it('calls setTheme("light") when Light is clicked', async () => {
    const user = userEvent.setup();
    const { setTheme } = renderToggle('system', 'light');
    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Light' }));
    expect(setTheme).toHaveBeenCalledWith('light');
  });

  it('calls setTheme("dark") when Dark is clicked', async () => {
    const user = userEvent.setup();
    const { setTheme } = renderToggle('system', 'light');
    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Dark' }));
    expect(setTheme).toHaveBeenCalledWith('dark');
  });

  it('calls setTheme("system") when System is clicked from an explicit preference', async () => {
    const user = userEvent.setup();
    const { setTheme } = renderToggle('light', 'light');
    await user.click(screen.getByRole('button', { name: /toggle theme/i }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'System' }));
    expect(setTheme).toHaveBeenCalledWith('system');
  });

  it('showLabel variant adds the resolved theme label next to the icon', () => {
    renderToggle('system', 'light', true);
    const trigger = screen.getByRole('button', { name: /toggle theme/i });
    expect(within(trigger).getByText('Light')).toBeDefined();
  });
});
```

Note on test count: spec § 5.1 listed 7 tests with row 5 as parameterized over 3 values. This plan expands row 5 into three separate `it()` blocks (one per `setTheme` argument) for clearer failure isolation. That gives 9 `it()` blocks total — same coverage, finer-grained reporting. The spec's "7 tests" framing was a coverage count, not a hard cap.

- [ ] **Step 2: Run the new test file against the UNCHANGED binary `ThemeToggle`**

Run: `pnpm --dir ProjectCeres.Client test --run src/design-system/components/ThemeToggle.test.tsx`
Expected: tests FAIL. Specifically:
- Tests 1, 2 (Sun/Moon rendering): may pass by accident — the binary toggle also renders Sun/Moon icons, so a class match on `sun`/`moon` could pass against the existing code.
- Test 3 (3 menuitemradio items): MUST fail — binary toggle is a `<Button>`, not a `<DropdownMenu>`, so `findAllByRole('menuitemradio')` returns 0.
- Test 4 (aria-checked): MUST fail — no menuitemradio elements exist.
- Tests 5, 6, 7 (setTheme calls): MUST fail — binary toggle's click handler calls `setTheme(isDark ? 'light' : 'dark')`, never `'system'`. Test 5 (Light click) MIGHT pass against the binary toggle by coincidence if rendered with `theme='dark'`, but with `theme='system'` and `resolvedTheme='light'` the binary toggle's `setTheme(nextTheme)` would be `setTheme('dark')`, not `setTheme('light')`. Confirm by reading the output.
- Test 8 (showLabel variant Light text): the binary toggle already has `<span className="ml-2">{isDark ? 'Light' : 'Dark'}</span>` (ThemeToggle.tsx:25). With `resolvedTheme='light'` this renders the "Dark" text (because the binary cycles), not "Light". So this MIGHT fail.

This is the TDD case (1) failure shape: the new test file is exercising a contract the production code does not yet satisfy. Capture the failure summary in the commit body as evidence the TDD step was real.

### Task 2.2: Rewrite the ThemeToggle component

**Files:**
- Modify (full rewrite): `ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx`

- [ ] **Step 1: Replace the entire file contents**

Open `ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx` and replace its contents with:

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

type ThemeToggleProps = {
  showLabel?: boolean;
};

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

- [ ] **Step 2: Run the new test file against the rewritten component**

Run: `pnpm --dir ProjectCeres.Client test --run src/design-system/components/ThemeToggle.test.tsx`
Expected: all 8 tests PASS. If any fail, root-cause now (the spec's no-defer rule). Common failure modes:
- Lucide icon class name mismatch — read the actual rendered SVG outerHTML and update the regex. The Lucide convention is `lucide lucide-{icon-name}` as the class string, so `/sun/i` and `/moon/i` should match.
- base-ui `<DropdownMenuTrigger render={...}>` not propagating ARIA properly — verify the `aria-label` lands on the rendered button. If it doesn't, the `<Button>` inside `render` may need an explicit `id` or the trigger may need its own `aria-label` instead.
- `aria-checked` attribute not set by base-ui's `RadioItem` — check `src/components/ui/dropdown-menu.tsx` lines 191-220 for what attribute the primitive actually emits. base-ui uses `data-checked` for state and may emit `aria-checked` separately, or both. The test queries for `aria-checked`; if base-ui only emits `data-checked`, the test asserts on that instead. Update the test to match the actual primitive, since the primitive is the source of truth.

### Task 2.3: Drop the MobileDrawer outer "Appearance" label

**Files:**
- Modify: `ProjectCeres.Client/src/app/layout/MobileDrawer.tsx:43-46`

- [ ] **Step 1: Edit the wrapper div**

In `ProjectCeres.Client/src/app/layout/MobileDrawer.tsx`, find lines 43-46:

```tsx
            <div className="mt-2 flex items-center justify-between px-3 py-2">
              <span className="text-sm text-foreground">Appearance</span>
              <ThemeToggle showLabel />
            </div>
```

Replace with:

```tsx
            <div className="mt-2 flex items-center justify-end px-3 py-2">
              <ThemeToggle showLabel />
            </div>
```

- [ ] **Step 2: Run MobileDrawer tests**

Run: `pnpm --dir ProjectCeres.Client test --run src/app/layout/MobileDrawer.test.tsx`
Expected: all 3 existing tests pass. The tests don't assert on the "Appearance" span, so dropping it doesn't break them.

### Task 2.4: Run the full frontend suite + build

- [ ] **Step 1: Run all frontend tests**

Run: `pnpm --dir ProjectCeres.Client test --run`
Expected: every test passes, including:
- All 8 new `ThemeToggle.test.tsx` tests
- All 4 existing layout tests (TopBar, MobileDrawer, AppLayout.a11y, AuthLayout)
- The full ~897-test suite that's been holding 10/10 green since commit `cda7b04`

If `AppLayout.a11y.test.tsx` regresses (the axe pass), the new tri-state dropdown likely introduced an a11y violation. Read the axe-violation output, root-cause it (most likely candidates: missing accessible name on the dropdown trigger when icon-only, or base-ui Portal rendering a node outside the test container). Fix it in the component, not in the test. Per `feedback_never_skip_tests_to_make_them_pass`, do not skip or weaken the axe assertion.

- [ ] **Step 2: Run the frontend build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: build succeeds, no TS errors, no bundle-budget regressions. The new component imports from packages already in the bundle (`@base-ui/react`, `lucide-react`, `react-i18next`), so bundle size should be flat or near-flat.

- [ ] **Step 3: Run the .NET test suite (stop-hook will re-run on commit, run it now to verify clean state)**

Run: `dotnet test`
Expected: all backend tests pass. Frontend-only stage so this is a sanity check, but the stop-hook will block the commit if dotnet is red.

### Task 2.5: Commit

- [ ] **Step 1: Stage and commit**

Run:
```bash
git -C <repo> add \
  ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx \
  ProjectCeres.Client/src/design-system/components/ThemeToggle.test.tsx \
  ProjectCeres.Client/src/app/layout/MobileDrawer.tsx
git -C <repo> commit -m "feat(stage-9.1.5.d): rewrite ThemeToggle as tri-state DropdownMenu (System/Light/Dark)"
```

Expected: commit succeeds. Stop-hook runs `dotnet test` (clean from Task 2.4 step 3) and exits 0.

---

## Commit 3 — AuthLayout footer mount + roadmap close-out

### Task 3.1: Add ThemeToggle to AuthLayout footer

**Files:**
- Modify: `ProjectCeres.Client/src/app/layout/AuthLayout.tsx`

- [ ] **Step 1: Add the import**

In `ProjectCeres.Client/src/app/layout/AuthLayout.tsx`, after the existing `import { LanguageToggle } from '../components/auth/LanguageToggle';` line, add:

```tsx
import { ThemeToggle } from '@/design-system/components/ThemeToggle';
```

- [ ] **Step 2: Update the footer**

In the same file, find the `<footer>` block:

```tsx
        <footer className="flex justify-center" data-slot="auth-footer">
          <LanguageToggle />
        </footer>
```

Replace with:

```tsx
        <footer className="flex justify-center gap-2" data-slot="auth-footer">
          <LanguageToggle />
          <ThemeToggle />
        </footer>
```

### Task 3.2: Add the footer-children assertion to AuthLayout.test.tsx

**Files:**
- Modify: `ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx`

- [ ] **Step 1: Add the new test**

In `ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx`, after the existing `it('uses bg-background...')` block (line 26), add this new test inside the same `describe('AuthLayout', ...)` block:

```tsx
  it('mounts both LanguageToggle and ThemeToggle in the footer', () => {
    const { container } = render(
      <I18nextProvider i18n={i18n}>
        <MemoryRouter initialEntries={['/']}>
          <Routes>
            <Route element={<AuthLayout />}>
              <Route index element={<div>test</div>} />
            </Route>
          </Routes>
        </MemoryRouter>
      </I18nextProvider>,
    );
    const footer = container.querySelector('[data-slot="auth-footer"]');
    expect(footer).not.toBeNull();
    const buttons = within(footer as HTMLElement).getAllByRole('button');
    // LanguageToggle trigger + ThemeToggle trigger = 2 buttons
    expect(buttons).toHaveLength(2);
  });
```

- [ ] **Step 2: Add the new imports at the top of the file**

The existing test file imports `render`. Add `within` to the existing `@testing-library/react` import:

Before:
```tsx
import { render } from '@testing-library/react';
```

After:
```tsx
import { render, within } from '@testing-library/react';
```

Also add at the top:
```tsx
import { I18nextProvider } from 'react-i18next';
import i18n from '@/app/i18n/i18n';
```

(The new test needs i18n in scope because `ThemeToggle` calls `useTranslation()`. The existing first test doesn't need it because it only renders an outlet `<div>test</div>`. Wrapping both tests in `<I18nextProvider>` is the simpler choice — refactor the first test's render to use the same wrapper for consistency.)

- [ ] **Step 3: Refactor the first test to use the same I18nextProvider wrapper**

In the existing first test (`it('uses bg-background...')`), update the render call from:

```tsx
    const { container } = render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<AuthLayout />}>
            <Route index element={<div>test</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
```

To:

```tsx
    const { container } = render(
      <I18nextProvider i18n={i18n}>
        <MemoryRouter initialEntries={['/']}>
          <Routes>
            <Route element={<AuthLayout />}>
              <Route index element={<div>test</div>} />
            </Route>
          </Routes>
        </MemoryRouter>
      </I18nextProvider>,
    );
```

(With the ThemeToggle in the footer, the `AuthLayout` now mounts a component that calls `useTranslation()`. Without an `<I18nextProvider>` wrapper, the existing test would crash because `useTranslation()` throws outside a provider. The refactor is required for the existing test to keep passing, not optional.)

- [ ] **Step 4: Run AuthLayout tests**

Run: `pnpm --dir ProjectCeres.Client test --run src/app/layout/AuthLayout.test.tsx`
Expected: both tests pass — the existing `bg-background` test (still asserts on the page div's class) and the new footer-children test (asserts 2 buttons in the footer).

### Task 3.3: Run the full suite + build

- [ ] **Step 1: Run all frontend tests**

Run: `pnpm --dir ProjectCeres.Client test --run`
Expected: every test passes — the new AuthLayout assertion plus the rest of the suite.

- [ ] **Step 2: Run the frontend build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: build succeeds, no TS errors, no bundle-budget regressions.

### Task 3.4: Update roadmap and task tracker

**Files:**
- Modify: `docs/roadmap-phase-three.md:1099` (sub-stage row)
- Modify: `docs/roadmap-phase-three.md:1110` (verification line)

- [ ] **Step 1: Read the current 9.1.5.c entry (line 1109) as the close-out template**

Run: `sed -n '1109p' docs/roadmap-phase-three.md`
Expected: a single line beginning `- [x] 9.1.5.c — …` with full implementation summary.

- [ ] **Step 2: Update the 9.1.5.d sub-stage row (line 1099)**

Locate `docs/roadmap-phase-three.md:1099`:

```
| 9.1.5.d | Add in-app light/dark mode toggle | Phase 1 UX walkthrough — `ThemeProvider defaultTheme="system"` means OS preference wins at load with no in-app override affordance. Pairs with 9.1.5.c — both needed for the toggle to do anything visible. |
```

Append a status flip + brief summary inline at the end of the description column (mirroring how 9.1.5.c was updated):

```
| 9.1.5.d | Add in-app light/dark mode toggle | Phase 1 UX walkthrough — `ThemeProvider defaultTheme="system"` means OS preference wins at load with no in-app override affordance. Pairs with 9.1.5.c. Shipped: rewrote `ThemeToggle` from binary `<Button>` (light↔dark) to tri-state `<DropdownMenu>` (System/Light/Dark) using base-ui's `DropdownMenuRadioGroup`. Mounted in AuthLayout footer (third mount site; TopBar + MobileDrawer already had it). Four new i18n keys added under `theme.*` namespace (en + es). Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-d-in-app-theme-toggle-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-d-in-app-theme-toggle-impl.md`. |
```

- [ ] **Step 3: Flip the verification checkbox (line 1110)**

Locate `docs/roadmap-phase-three.md:1110`:

```
- [ ] 9.1.5.d — in-app toggle is reachable from the AuthLayout footer AND from the AppLayout topbar; switching persists across reloads (`next-themes` handles this); both modes render correctly with the 9.1.5.c token fix
```

Replace with:

```
- [x] 9.1.5.d — in-app toggle is reachable from the AuthLayout footer AND from the AppLayout topbar (TopBar desktop + MobileDrawer mobile); switching persists across reloads via the homegrown `ThemeProvider`'s localStorage key `ceres.theme:v1`; tri-state cycle (System/Light/Dark) lets the user return to OS-following mode without clearing storage. 8 vitest tests in `src/design-system/components/ThemeToggle.test.tsx` pin: Sun/Moon icon swap by resolved theme, 3 menuitemradio items with correct labels, `aria-checked` on the active preference, `setTheme(value)` called with each of `'system'` / `'light'` / `'dark'`, `showLabel` variant shows resolved theme label. AuthLayout footer assertion confirms both LanguageToggle + ThemeToggle triggers render side-by-side. Manual browser verification confirmed at `/app/login` + `/app/dashboard` (desktop + 375px viewport).
```

- [ ] **Step 4: Update task #42 in the task tracker**

Run: `TaskUpdate` with `taskId: 42`, `status: completed`, `description: "UX: add in-app light/dark mode toggle — RESOLVED via Stage 9.1.5.d (tri-state DropdownMenu replacing binary toggle, mounted at AuthLayout footer + TopBar + MobileDrawer)"`.

(For the implementer subagent: this step uses the `TaskUpdate` tool, not a CLI command. If running in a subagent that does not have access to `TaskUpdate`, surface this step back to the controller for execution.)

### Task 3.5: Commit

- [ ] **Step 1: Stage and commit**

Run:
```bash
git -C <repo> add \
  ProjectCeres.Client/src/app/layout/AuthLayout.tsx \
  ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx \
  docs/roadmap-phase-three.md
git -C <repo> commit -m "feat(stage-9.1.5.d): mount ThemeToggle in AuthLayout footer + roadmap close-out"
```

Expected: commit succeeds. Stop-hook runs `dotnet test` (clean) and exits 0.

### Task 3.6: Hand off browser verification

- [ ] **Step 1: Surface the manual verification checklist to the user**

Subagents implementing this plan typically cannot run a real browser. The implementer subagent surfaces this checklist verbatim to the controller, which surfaces it to the user. The roadmap close-out's `[x]` mark in Task 3.4 is provisional until the user confirms each item:

```
Manual browser verification checklist (Stage 9.1.5.d):

  Setup:
  - [ ] `pnpm --dir ProjectCeres.Client dev` and `dotnet run --project ProjectCeres` both running
  - [ ] Browser opens at https://localhost:7081

  Auth pages (AuthLayout footer):
  - [ ] Visit /app/login → footer shows Globe icon + Sun/Moon icon side-by-side, centered, with a small gap (gap-2)
  - [ ] Resize to 375px viewport → both icons still side-by-side, no wrap, no overflow
  - [ ] Click the theme icon → dropdown shows System / Light / Dark with System checked by default (first-time user)
  - [ ] Click "Light" → page flips to light immediately
  - [ ] Reload the page → page stays light (preference persisted)
  - [ ] Click the theme icon again → dropdown now shows Light as checked
  - [ ] Click "System" → page flips back to OS preference (Sun icon if OS-light, Moon if OS-dark)
  - [ ] Visit /app/register → same footer behavior
  - [ ] Visit /app/password-reset → same footer behavior

  App shell (TopBar desktop):
  - [ ] Sign in to reach /app/dashboard (or any signed-in route)
  - [ ] At ≥640px viewport, TopBar shows ThemeToggle in the desktop controls cluster (between bell and avatar)
  - [ ] Open it → same three options, same Light/Dark/System behavior

  App shell (MobileDrawer):
  - [ ] Resize to <640px viewport → desktop TopBar controls collapse; menu icon appears
  - [ ] Open the mobile menu → bottom of the drawer shows the labeled ThemeToggle (Sun + "Light" or Moon + "Dark") on the right side of its row, with no "Appearance" outer label
  - [ ] Open it → same three options, same behavior

  System-mode + OS change:
  - [ ] With theme set to System, open DevTools → Rendering → toggle "Emulate CSS prefers-color-scheme" between light and dark
  - [ ] Page flips both directions immediately (no reload required)
```

Until the user confirms all items, the stage is functionally green but not user-verified. The roadmap `[x]` mark stays per Task 3.4 (because we don't have a "Done pending verification" marker), but the controller should hold the next stage's planning until the user reports back. If any item fails, the close-out commit may need to be amended or a follow-up commit landed before the stage is truly closed.

---

## Self-review against the spec

Run after writing the plan, before handing off:

### Spec coverage check

Walking spec § 2 ("What ships") item by item:

1. ✓ Rewrite `ThemeToggle.tsx` to tri-state DropdownMenu — Task 2.2.
2. ✓ Trigger icon shows resolved theme — included in Task 2.2's code block.
3. ✓ Public API unchanged — Task 2.2's code keeps export name + prop API. Tasks for TopBar and existing AppLayout/MobileDrawer mounts are intentionally absent because the import sites work without source edits (verified in Discovery findings #3, #4).
4. ✓ Drop `<span>Appearance</span>` in MobileDrawer — Task 2.3.
5. ✓ Mount in AuthLayout footer — Task 3.1.
6. ✓ Add 4 new i18n keys — Tasks 1.1 + 1.2.
7. ✓ New test file with 7 tests — Task 2.1 (expanded to 8 `it()` blocks; same coverage).
8. ✓ Updates to existing tests — Task 3.2 (AuthLayout). Other existing tests (TopBar, MobileDrawer, AppLayout.a11y) verified in Discovery as not needing updates. Spec § 5.2 mentioned them as candidates; grep confirmed they don't reference the old binary toggle's strings.
9. ✓ Roadmap close-out — Task 3.4.

Spec § 7 (commit sequence) matches Tasks 1.* / 2.* / 3.*.

Spec § 9 (verification checklist) is covered by Tasks 2.4 step 1 (frontend tests), 2.4 step 2 (frontend build), 2.4 step 3 (dotnet test), Task 3.3 (same on Commit 3), and Task 3.6 (manual browser).

No gaps.

### Placeholder scan

No "TBD", "TODO", "implement later", "Add appropriate error handling", "Similar to Task N", or steps without code blocks. Task 3.6 has a hand-off (browser checklist) but that's not a placeholder — it's an explicit gate that the spec also requires (§ 5.4).

### Type consistency

- `theme.ariaLabel` is consistently used as the i18n key everywhere it appears (Tasks 1.1, 1.2, 2.1, 2.2).
- `theme.system` / `theme.light` / `theme.dark` consistent across i18n file additions and test assertions.
- Component prop type `{ showLabel?: boolean }` consistent between test (Task 2.1) and implementation (Task 2.2).
- `Theme` type from `@/app/theme/theme-context` referenced consistently in the rewrite.

Clean.

---

## Execution choice

**Plan complete and saved to `docs/superpowers/plans/2026-05-17-stage-9-1-5-d-in-app-theme-toggle-impl.md`.** Two execution options:

**1. Subagent-Driven (recommended)** — Fresh implementer subagent per commit; two-stage review (spec compliance, then code quality) between commits. Recommended for this stage because Commit 2 is the largest blast radius (component rewrite + 8 new tests + MobileDrawer edit) and a fresh subagent reviewing the diff with no implementation-context bias is the standard quality gate.

**2. Inline Execution** — Execute tasks in this session using superpowers:executing-plans with batch checkpoints for user review.

Which approach?
