# Stage 9.1.5.e — Language Code in LanguageToggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite the icon-only `LanguageToggle` so the trigger shows the active language code (EN/ES) next to the globe, the aria-label includes the active language name, and the dropdown uses a radio group with a checked active option.

**Architecture:** One component rewrite at `src/app/components/auth/LanguageToggle.tsx` keeps the public API (default export, zero props) so the single mount site (`AuthLayout.tsx:28`) works unchanged. Two implementation commits (i18n keys → atomic rewrite+tests+dead-key cleanup) plus one close-out commit.

**Tech Stack:** React 19, Vite, TypeScript, base-ui's DropdownMenu primitives (`@base-ui/react`), Lucide `<Globe />` icon, react-i18next (with `t(key, { language })` interpolation), Vitest + @testing-library/react.

**Spec:** `docs/superpowers/specs/2026-05-17-stage-9-1-5-e-language-code-toggle-design.md` (committed `865c5b5`).

**Parent commit:** `0a299ec` (HEAD at plan-write — spec landed at `865c5b5`; intervening commit `0a299ec` is a hook-config fix that doesn't touch any file in this stage's scope).

---

## Binding constraints (apply to every task)

- Stay on `main`. No worktrees, no branches.
- `pnpm` only via `pnpm --dir ProjectCeres.Client …`. Never `npm`, `npx`, or `yarn`.
- NO `Co-Authored-By` trailer in any commit message.
- TDD per `docs/testing.md` § Rules. Every test-touching commit names which of TDD cases (1), (2), or (3) applied. Commit 2's test edits: **Tests 1+2 = case (3)** (contract change — aria-label expanded; items now radio items); **Tests 3+4+5 = case (1)** (new tests for new behavior).
- Pre-existing failures get root-caused NOW (per `feedback_never_skip_tests_to_make_them_pass`). Don't defer.
- Stop-hook (`.claude/hooks/run-tests.sh`) runs `dotnet test` on every commit. Frontend-only stage but it still gates — both implementation commits must keep the .NET suite green.
- `pnpm test` and `pnpm build` always run **foreground** (per `feedback_dont_background_one_shot_verifications` — backgrounded pnpm subprocess output is buffered/empty).
- Per the recent flake root-cause (commit `cda7b04`): **NO `vi.resetAllMocks()` in any test file's `afterEach`.** Use per-test fresh `vi.fn()` for spies (this stage has no spies — no fetch mocking needed).
- Per `feedback_clean_dead_code_immediately`: remove the dead `ariaLabel` key in the SAME commit as the consumer swap. Don't file it as follow-up.

---

## Discovery findings baked into this plan

Run during plan-write (Read + grep against parent commit `865c5b5`). The implementer does NOT need to re-discover these:

1. **Existing component** (`src/app/components/auth/LanguageToggle.tsx`, 40 lines) is icon-only ghost button + dropdown with 2 plain `DropdownMenuItem` rows. Public API is `export function LanguageToggle()` (zero props). The single mount site is `src/app/layout/AuthLayout.tsx:28`.

2. **Existing test file** (`src/app/components/auth/LanguageToggle.test.tsx`, 40 lines) wraps in `<I18nextProvider i18n={i18n}>` (real i18n module, not mocked). `beforeEach` clears the lang cookie + sets `i18n.changeLanguage('en')`. Two existing tests:
   - Line 18: `it('renders the globe button with an accessible label')` — queries by `/change language/i`. **Substring match — survives the aria-label expansion** ("Change language, currently English" still matches `/change language/i`). No assertion edit needed; rename for accuracy.
   - Line 27: `it('switches the language and writes the lang cookie when an option is selected')` — queries by `getByRole('menuitem', { name: 'Español' })`. **MUST update to `menuitemradio`** because the dropdown switches to a radio group.

3. **Single consumer of the old `auth.languageToggle.ariaLabel` key**: `LanguageToggle.tsx:24`. Confirmed via `grep -rn "languageToggle.ariaLabel" ProjectCeres.Client/src` (one hit). Removing the key in Commit 2 alongside the consumer swap is safe.

4. **Locale file structure**: `auth.languageToggle` already exists at line 32 of both `en.json` and `es.json` with three keys (`ariaLabel`, `english`, `spanish`). New keys (`ariaLabelWithLanguage`, `code.en`, `code.es`) get added inside this block.

5. **No `vi.resetAllMocks()` exists in the test file** — the cda7b04 sweep already covered it (or it was never there). Don't reintroduce it.

6. **No fetch mocking, no `vi.fn()` spies in the test file** — i18n state is real, lang cookie is set via real `document.cookie` mutation. No mock cleanup needed beyond cookie reset.

---

## File structure

### Files created

None. All edits modify existing files.

### Files modified

- `ProjectCeres.Client/src/app/i18n/locales/en.json` — add `auth.languageToggle.ariaLabelWithLanguage` + `auth.languageToggle.code.{en,es}` (Commit 1); remove `auth.languageToggle.ariaLabel` (Commit 2).
- `ProjectCeres.Client/src/app/i18n/locales/es.json` — same shape (Spanish translations).
- `ProjectCeres.Client/src/app/components/auth/LanguageToggle.tsx` — complete rewrite from icon-only `DropdownMenuItem` rows to globe+code trigger + radio-group dropdown. Same default export, same zero props.
- `ProjectCeres.Client/src/app/components/auth/LanguageToggle.test.tsx` — 2 existing tests edited (Test 1 rename, Test 2 selector), 3 new tests added.
- `docs/roadmap-phase-three.md` lines 1100 + 1111 — flip `[ ]` to `[x]` with implementation + verification summaries.

### Files NOT touched (verified)

- `ProjectCeres.Client/src/app/layout/AuthLayout.tsx` — `<LanguageToggle />` at line 28 calls the same public API; no source edit needed.
- `ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx` — asserts on `data-slot="auth-footer"` button count; the trigger remains one button; no edit needed.

---

## Commit 1 — i18n keys (no consumer yet)

### Task 1.1: Add `ariaLabelWithLanguage` + `code.{en,es}` to English locale

**Files:**
- Modify: `ProjectCeres.Client/src/app/i18n/locales/en.json:32-36` (existing `auth.languageToggle` block)

- [ ] **Step 1: Confirm the current shape**

Run: `grep -A 4 '"languageToggle"' ProjectCeres.Client/src/app/i18n/locales/en.json`
Expected output:
```json
"languageToggle": {
  "ariaLabel": "Change language",
  "english": "English",
  "spanish": "Español"
}
```

- [ ] **Step 2: Replace the `languageToggle` block**

Find the existing block in `ProjectCeres.Client/src/app/i18n/locales/en.json`:

```json
    "languageToggle": {
      "ariaLabel": "Change language",
      "english": "English",
      "spanish": "Español"
    }
```

Replace with (note: KEEP `ariaLabel` for now — it's removed in Commit 2):

```json
    "languageToggle": {
      "ariaLabel": "Change language",
      "ariaLabelWithLanguage": "Change language, currently {{language}}",
      "code": {
        "en": "EN",
        "es": "ES"
      },
      "english": "English",
      "spanish": "Español"
    }
```

Insertion order: `ariaLabel`, then `ariaLabelWithLanguage` (alphabetical after `ariaLabel`), then `code` (alphabetical), then `english`, `spanish`. Matches the existing file's alphabetical-within-block convention (compare with `auth.login` which sorts its inner keys alphabetically).

- [ ] **Step 3: Verify the file is valid JSON**

Run: `pnpm --dir ProjectCeres.Client exec node -e "JSON.parse(require('fs').readFileSync('src/app/i18n/locales/en.json','utf8')); console.log('OK')"`
Expected: `OK`.

### Task 1.2: Add same keys to Spanish locale

**Files:**
- Modify: `ProjectCeres.Client/src/app/i18n/locales/es.json:32-36`

- [ ] **Step 1: Confirm the current shape**

Run: `grep -A 4 '"languageToggle"' ProjectCeres.Client/src/app/i18n/locales/es.json`
Expected output:
```json
"languageToggle": {
  "ariaLabel": "Cambiar idioma",
  "english": "English",
  "spanish": "Español"
}
```

- [ ] **Step 2: Replace the `languageToggle` block**

Find the existing block in `ProjectCeres.Client/src/app/i18n/locales/es.json`:

```json
    "languageToggle": {
      "ariaLabel": "Cambiar idioma",
      "english": "English",
      "spanish": "Español"
    }
```

Replace with:

```json
    "languageToggle": {
      "ariaLabel": "Cambiar idioma",
      "ariaLabelWithLanguage": "Cambiar idioma, actualmente {{language}}",
      "code": {
        "en": "EN",
        "es": "ES"
      },
      "english": "English",
      "spanish": "Español"
    }
```

Note: `code.en` and `code.es` are identical in both locales — language codes are language-neutral. Spanish autonyms `english` / `spanish` remain `"English"` / `"Español"` (already correct in the existing file).

- [ ] **Step 3: Verify the file is valid JSON**

Run: `pnpm --dir ProjectCeres.Client exec node -e "JSON.parse(require('fs').readFileSync('src/app/i18n/locales/es.json','utf8')); console.log('OK')"`
Expected: `OK`.

### Task 1.3: Verify the suite stays green (no consumer yet)

- [ ] **Step 1: Run all frontend tests**

Run: `pnpm --dir ProjectCeres.Client test --run`
Expected: every test passes (no consumer yet reads the new keys; existing `LanguageToggle.test.tsx` reads the still-present `ariaLabel` key and stays green).

- [ ] **Step 2: Run the frontend build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: build succeeds, no TS errors, all bundle chunks within budget.

### Task 1.4: Commit

- [ ] **Step 1: Stage and commit**

Run:
```bash
git -C <repo> add \
  ProjectCeres.Client/src/app/i18n/locales/en.json \
  ProjectCeres.Client/src/app/i18n/locales/es.json
git -C <repo> commit -m "feat(stage-9.1.5.e): add language-code + interpolated-aria-label i18n keys"
```

Expected: commit succeeds. Stop-hook runs `dotnet test` (i18n-only change → tier-0 skip; should exit 0 quickly). No `Co-Authored-By` trailer.

---

## Commit 2 — Component rewrite + test updates + dead-key removal (atomic)

### Task 2.1: Update the test file FIRST (TDD — failing tests first)

**Files:**
- Modify: `ProjectCeres.Client/src/app/components/auth/LanguageToggle.test.tsx` (full rewrite — 5 tests total)

- [ ] **Step 1: Replace the entire test file**

Replace the contents of `ProjectCeres.Client/src/app/components/auth/LanguageToggle.test.tsx` with:

```tsx
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { LanguageToggle } from './LanguageToggle';

function renderToggle() {
  return render(
    <I18nextProvider i18n={i18n}>
      <LanguageToggle />
    </I18nextProvider>,
  );
}

describe('LanguageToggle', () => {
  beforeEach(() => {
    document.cookie = 'lang=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
    void i18n.changeLanguage('en');
  });

  afterEach(() => {
    document.cookie = 'lang=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  });

  it('renders the globe trigger with an aria-label that includes the active language', () => {
    renderToggle();
    expect(screen.getByRole('button', { name: /change language/i })).toBeDefined();
  });

  it('switches the language and writes the lang cookie when an option is selected', async () => {
    const user = userEvent.setup();
    renderToggle();
    await user.click(screen.getByRole('button', { name: /change language/i }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Español' }));

    expect(i18n.language).toBe('es');
    expect(document.cookie).toContain('lang=es');
  });

  it('renders the active language code (EN) on the trigger in English', () => {
    renderToggle();
    const trigger = screen.getByRole('button', { name: /change language/i });
    expect(within(trigger).getByText('EN')).toBeDefined();
  });

  it('aria-label interpolates the active language name and updates on language change', async () => {
    renderToggle();
    // Initial render — English. Aria-label includes "currently English".
    expect(
      screen.getByRole('button', { name: /change language, currently english/i }),
    ).toBeDefined();

    // Switch to Spanish.
    await i18n.changeLanguage('es');

    // After re-render, aria-label is in Spanish and includes "actualmente Español".
    expect(
      screen.getByRole('button', { name: /cambiar idioma, actualmente español/i }),
    ).toBeDefined();
  });

  it('marks the currently-active language as checked in the dropdown', async () => {
    const user = userEvent.setup();
    renderToggle();

    // Open menu — English is the active language at mount.
    await user.click(screen.getByRole('button', { name: /change language/i }));
    let englishItem = await screen.findByRole('menuitemradio', { name: 'English' });
    let spanishItem = screen.getByRole('menuitemradio', { name: 'Español' });
    expect(englishItem.getAttribute('aria-checked')).toBe('true');
    expect(spanishItem.getAttribute('aria-checked')).toBe('false');

    // Select Spanish — menu closes after click in base-ui.
    await user.click(spanishItem);

    // Re-open menu.
    await user.click(screen.getByRole('button', { name: /change language/i }));
    englishItem = await screen.findByRole('menuitemradio', { name: 'English' });
    spanishItem = screen.getByRole('menuitemradio', { name: 'Español' });
    expect(englishItem.getAttribute('aria-checked')).toBe('false');
    expect(spanishItem.getAttribute('aria-checked')).toBe('true');
  });
});
```

Changes from the existing file (40 lines → ~75 lines):
- Added `within` to `@testing-library/react` imports (for `within(trigger).getByText('EN')` in Test 3).
- Extracted the `<I18nextProvider><LanguageToggle /></I18nextProvider>` wrap into a `renderToggle()` helper — reused by all 5 tests.
- Test 1 renamed from `renders the globe button with an accessible label` to `renders the globe trigger with an aria-label that includes the active language` (assertion unchanged — `/change language/i` is a substring match that still matches the expanded aria-label).
- Test 2 selector changed: `getByRole('menuitem', ...)` → `getByRole('menuitemradio', ...)`. Behavior assertions unchanged.
- Test 3, 4, 5 are new.
- No `vi.resetAllMocks()` (already absent — keep it that way).

- [ ] **Step 2: Run the new test file against the UNCHANGED binary component**

Run: `pnpm --dir ProjectCeres.Client test --run src/app/components/auth/LanguageToggle.test.tsx`
Expected: FAILURES per the TDD case map.

- **Test 1** (`renders the globe trigger with an aria-label that includes the active language`): may pass — the existing aria-label `"Change language"` matches the regex `/change language/i`.
- **Test 2** (`switches the language and writes the lang cookie when an option is selected`): MUST fail — the existing component renders `menuitem` not `menuitemradio`; `findByRole('menuitemradio', ...)` returns no element.
- **Test 3** (`renders the active language code (EN) on the trigger in English`): MUST fail — the existing component renders only the `<Globe>` icon, no "EN" text inside the trigger button.
- **Test 4** (`aria-label interpolates the active language name and updates on language change`): MUST fail — the existing aria-label is `"Change language"` (no language interpolation); `/change language, currently english/i` doesn't match.
- **Test 5** (`marks the currently-active language as checked in the dropdown`): MUST fail — no `menuitemradio` elements exist; `findByRole` returns no element.

Capture the failure summary; this is the TDD case (1)+(3) failure shape. The new test file describes a contract the existing component does not satisfy.

### Task 2.2: Rewrite the LanguageToggle component

**Files:**
- Modify (full rewrite): `ProjectCeres.Client/src/app/components/auth/LanguageToggle.tsx`

- [ ] **Step 1: Replace the entire file contents**

Open `ProjectCeres.Client/src/app/components/auth/LanguageToggle.tsx` and replace its contents with:

```tsx
import { Globe } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { writeLangCookie, type SupportedLanguage } from '../../i18n/i18n';

export function LanguageToggle() {
  const { t, i18n } = useTranslation();
  const current = (i18n.resolvedLanguage ?? i18n.language ?? 'en') as SupportedLanguage;
  const currentCode = t(`auth.languageToggle.code.${current}`);
  const currentName =
    current === 'es' ? t('auth.languageToggle.spanish') : t('auth.languageToggle.english');

  const choose = async (lang: SupportedLanguage) => {
    await i18n.changeLanguage(lang);
    writeLangCookie(lang);
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button
            variant="ghost"
            size="sm"
            type="button"
            aria-label={t('auth.languageToggle.ariaLabelWithLanguage', { language: currentName })}
          >
            <Globe className="h-4 w-4" />
            <span className="ml-1.5 text-xs font-medium">{currentCode}</span>
          </Button>
        }
      />
      <DropdownMenuContent align="center">
        <DropdownMenuRadioGroup
          value={current}
          onValueChange={(value) => void choose(value as SupportedLanguage)}
        >
          <DropdownMenuRadioItem value="en">{t('auth.languageToggle.english')}</DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="es">{t('auth.languageToggle.spanish')}</DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

This is the spec § 3 code, byte-for-byte. Notes:
- `i18n.resolvedLanguage` is i18next's canonical "what's actually rendering" accessor (handles fallback chains). The chain `?? i18n.language ?? 'en'` covers first-render edge cases.
- `currentCode` reads from i18n (`code.en` / `code.es`) — keeps casing decision in locale files.
- `currentName` reuses the existing autonym keys (`english` / `spanish`) — no duplication.
- `align="center"` matches the LanguageToggle's prior pattern (and is verified safe by `LanguageToggle.tsx`'s current code in the pre-rewrite file, plus its use elsewhere in the codebase).
- `ml-1.5 text-xs font-medium` on the code span — 6px gap from icon, subordinate weight to the icon.

- [ ] **Step 2: Re-run the test file**

Run: `pnpm --dir ProjectCeres.Client test --run src/app/components/auth/LanguageToggle.test.tsx`
Expected: all 5 tests PASS.

If any fail, root-cause now per the no-defer rule. Likely failure modes:
- `aria-label` regex case sensitivity: if the i18n value is `"Change language, currently English"` (capital E in English) but the regex matches case-insensitively (`/i` flag), this is fine. Tests use `/i` flags.
- base-ui `RadioItem` may emit `data-checked` in addition to or instead of `aria-checked`. The test checks `aria-checked`. Verified during 9.1.5.d that `aria-checked` IS emitted (ThemeToggle tests rely on it and pass). If something has changed since, update the test to also accept `data-checked`, but verify the primitive is still emitting an a11y attribute — DON'T weaken the assertion to "no a11y signal."
- `i18n.changeLanguage('es')` in Test 4 may need to be wrapped in `act(...)` or `waitFor(...)` if React's render lag is noticeable in jsdom. The plan's `await` should be sufficient because react-i18next subscribes to language changes synchronously, but if Test 4 flakes, wrap the assertion in `await waitFor(() => expect(...))` rather than introducing artificial delays.

### Task 2.3: Remove the now-dead `ariaLabel` key from both locale files

**Files:**
- Modify: `ProjectCeres.Client/src/app/i18n/locales/en.json` (delete one line)
- Modify: `ProjectCeres.Client/src/app/i18n/locales/es.json` (delete one line)

- [ ] **Step 1: Verify zero consumers of the old key**

Run: `grep -rn "languageToggle.ariaLabel" ProjectCeres.Client/src`
Expected: ONE hit only at `ProjectCeres.Client/src/app/i18n/locales/en.json` and `ProjectCeres.Client/src/app/i18n/locales/es.json` (the key definitions themselves). NO source code references — the consumer at `LanguageToggle.tsx:24` has been replaced with `ariaLabelWithLanguage` in Task 2.2.

If there's any third hit, STOP and investigate — there's a consumer the plan didn't account for.

- [ ] **Step 2: Remove the `ariaLabel` line from `en.json`**

In `ProjectCeres.Client/src/app/i18n/locales/en.json`, find:

```json
    "languageToggle": {
      "ariaLabel": "Change language",
      "ariaLabelWithLanguage": "Change language, currently {{language}}",
```

Delete just the `ariaLabel` line so it becomes:

```json
    "languageToggle": {
      "ariaLabelWithLanguage": "Change language, currently {{language}}",
```

- [ ] **Step 3: Remove the `ariaLabel` line from `es.json`**

In `ProjectCeres.Client/src/app/i18n/locales/es.json`, find:

```json
    "languageToggle": {
      "ariaLabel": "Cambiar idioma",
      "ariaLabelWithLanguage": "Cambiar idioma, actualmente {{language}}",
```

Delete just the `ariaLabel` line so it becomes:

```json
    "languageToggle": {
      "ariaLabelWithLanguage": "Cambiar idioma, actualmente {{language}}",
```

- [ ] **Step 4: Verify both files are still valid JSON**

Run:
```bash
pnpm --dir ProjectCeres.Client exec node -e "JSON.parse(require('fs').readFileSync('src/app/i18n/locales/en.json','utf8')); JSON.parse(require('fs').readFileSync('src/app/i18n/locales/es.json','utf8')); console.log('OK')"
```
Expected: `OK`.

### Task 2.4: Run the full frontend suite + build

- [ ] **Step 1: Run all frontend tests**

Run: `pnpm --dir ProjectCeres.Client test --run`
Expected: every test passes — the 5 new/updated LanguageToggle tests + the rest of the suite (should be 909 total: 908 from prior + 1 net new from this stage; Test 1 was renamed but still 1 test, Test 2 was edited but still 1 test, Tests 3+4+5 are 3 new tests = net +3 vs. the prior 2, but the spec says 5 total so net +3).

Verify the count: `pnpm --dir ProjectCeres.Client test --run 2>&1 | tail -5` should report `Tests N passed (N)` where N = 911 (908 + 3 net new). If it's lower, a regression slipped in — root-cause NOW.

- [ ] **Step 2: Run the frontend build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: build succeeds, no TS errors, all bundle chunks within budget.

- [ ] **Step 3: Run the .NET test suite (stop-hook will re-run on commit; do it now for clean state)**

Run: `dotnet test`
Expected: all backend tests pass. Frontend-only stage so this is a sanity check.

### Task 2.5: Commit

- [ ] **Step 1: Stage and commit**

Run:
```bash
git -C <repo> add \
  ProjectCeres.Client/src/app/components/auth/LanguageToggle.tsx \
  ProjectCeres.Client/src/app/components/auth/LanguageToggle.test.tsx \
  ProjectCeres.Client/src/app/i18n/locales/en.json \
  ProjectCeres.Client/src/app/i18n/locales/es.json
git -C <repo> commit -m "feat(stage-9.1.5.e): LanguageToggle shows active code + radio-checked dropdown + a11y aria-label"
```

Expected: commit succeeds. Stop-hook runs `dotnet test` (clean from Task 2.4 Step 3) and exits 0. No `Co-Authored-By` trailer.

---

## Commit 3 — Roadmap close-out (interim — re-amended after user browser-confirms)

### Task 3.1: Update roadmap line 1100 (sub-stage row)

**Files:**
- Modify: `docs/roadmap-phase-three.md:1100`

- [ ] **Step 1: Read the current line 1100**

Run: `sed -n '1100p' docs/roadmap-phase-three.md`
Expected: a line beginning `| 9.1.5.e | Add language code (EN/ES) next to globe icon in \`LanguageToggle\` | Phase 1 UX walkthrough — globe icon alone doesn't communicate the active language. |`.

- [ ] **Step 2: Append the implementation summary to the description column**

Find `docs/roadmap-phase-three.md:1100` and append to the third column (after the existing description text), mirroring how 9.1.5.d's row at line 1099 was updated:

```
| 9.1.5.e | Add language code (EN/ES) next to globe icon in `LanguageToggle` | Phase 1 UX walkthrough — globe icon alone doesn't communicate the active language. Shipped: trigger now renders `<Globe>` + active two-letter code (EN/ES) side-by-side; aria-label interpolates the active language name ("Change language, currently English" / "Cambiar idioma, actualmente Español") via the new `auth.languageToggle.ariaLabelWithLanguage` key; dropdown switched from plain `DropdownMenuItem` rows to a `DropdownMenuRadioGroup` so the active language carries `aria-checked="true"` and a visible check — matching the ThemeToggle vocabulary from 9.1.5.d. Dead `auth.languageToggle.ariaLabel` key removed in the same atomic commit. Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-e-language-code-toggle-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-e-language-code-toggle-impl.md`. |
```

### Task 3.2: Flip the verification checkbox (line 1111) — interim state

**Files:**
- Modify: `docs/roadmap-phase-three.md:1111`

- [ ] **Step 1: Read the current line 1111**

Run: `sed -n '1111p' docs/roadmap-phase-three.md`
Expected: `- [ ] 9.1.5.e — \`LanguageToggle\` button shows the active language code (\`EN\` / \`ES\`) next to the globe icon; updates immediately on selection`.

- [ ] **Step 2: Replace with the interim "pending user run" text**

Per the 9.1.5.d pattern, mark line 1111 as `[ ]` initially (NOT `[x]`) with explanatory text, because browser verification is the only outstanding item:

```
- [ ] 9.1.5.e — `LanguageToggle` button shows the active language code (`EN` / `ES`) next to the globe icon; updates immediately on selection. 5 vitest tests in `src/app/components/auth/LanguageToggle.test.tsx` pin: globe-trigger aria-label includes language, Español click writes lang cookie via `menuitemradio` role, "EN" text on trigger in English, aria-label interpolates and updates on `i18n.changeLanguage`, active language carries `aria-checked="true"` in the dropdown. **Manual browser verification pending user run** — checklist in `docs/superpowers/specs/2026-05-17-stage-9-1-5-e-language-code-toggle-design.md` § 5.5.
```

The `[ ]` checkbox is intentional — line 1111 is the user-facing verification line, not the implementation status. It flips to `[x]` after the user confirms in-browser per Task 3.5.

### Task 3.3: Update task #40 in the task tracker

**Subagent note:** You do not have access to the `TaskUpdate` tool. Report this step back to the controller. The controller will run:

```
TaskUpdate taskId=40 status=completed description="UX: add language code next to globe icon in LanguageToggle — IMPLEMENTED via Stage 9.1.5.e (globe + EN/ES code on trigger, interpolated aria-label, radio-checked dropdown). Browser verification pending user run."
```

### Task 3.4: Commit the roadmap close-out

- [ ] **Step 1: Stage and commit**

Run:
```bash
git -C <repo> add docs/roadmap-phase-three.md
git -C <repo> commit -m "docs(stage-9.1.5.e): roadmap entry + verification line (pending browser confirm)"
```

Expected: commit succeeds. Stop-hook runs `dotnet test` (clean) and exits 0.

### Task 3.5: Hand off browser verification to the user

Subagent surfaces this verbatim to the controller; controller surfaces to the user. Line 1111 is `[ ]` until the user signs off; on confirmation, the controller will run a separate `Edit` + commit to flip it.

```
Manual browser verification checklist (Stage 9.1.5.e):

Setup:
- [ ] `pnpm --dir ProjectCeres.Client dev` and `dotnet run --project ProjectCeres` both running
- [ ] Browser opens at https://localhost:7081

Auth pages:
- [ ] Visit /app/login → footer shows [🌐 EN] (globe icon + "EN" text inline) next to the theme icon
- [ ] At 375px viewport → both icons fit on one line, no wrap, no overflow
- [ ] Click the language icon → dropdown shows two rows: "English" (checked) and "Español" (unchecked)
- [ ] Click "Español" → page strings flip to Spanish; trigger now shows [🌐 ES]
- [ ] Open the trigger again → "Español" is now checked, "English" unchecked
- [ ] DevTools → Accessibility tree → trigger button → verify Name reads "Cambiar idioma, actualmente Español" (when in Spanish) and "Change language, currently English" (when in English)
- [ ] Reload page → language preference persists (existing cookie behavior; this stage doesn't change it but worth re-confirming)
- [ ] Visit /app/register and /app/password-reset → same footer behavior

Cross-feature regression:
- [ ] ThemeToggle still works alongside (shipped in 9.1.5.d) — both controls remain interactive
```

Until the user confirms all items, line 1111 stays `[ ]`. On confirmation, the controller flips it to `[x]` with text per the 9.1.5.d closure pattern (e.g., "Manual browser verification confirmed at /app/login — code flips immediately on selection, aria-label updates per DevTools accessibility tree, both placeholder pages inherit the change").

---

## Self-review against the spec

### Spec coverage check

Walking spec § 2 ("What ships") item by item:

1. ✓ Rewrite `LanguageToggle.tsx` — Task 2.2.
2. ✓ New i18n keys (`code.{en,es}`, `ariaLabelWithLanguage`) in both locales — Tasks 1.1, 1.2.
3. ✓ Remove dead `auth.languageToggle.ariaLabel` key from both locales — Task 2.3.
4. ✓ Update test file (2 edited + 3 new) — Task 2.1.
5. ✓ Roadmap close-out + task #40 update — Tasks 3.1, 3.2, 3.3.

Spec § 5.1 + § 5.2 (test plan): all 5 tests have code blocks in Task 2.1.

Spec § 6 (commit sequence): matches Tasks 1.* (Commit 1), 2.* (Commit 2), 3.* (Commit 3).

Spec § 8 (verification checklist):
- Test exit 0 — Task 2.4 Step 1.
- Build exit 0 — Task 2.4 Step 2.
- dotnet test exit 0 — Task 2.4 Step 3.
- Grep returns zero hits for old `languageToggle.ariaLabel` — Task 2.3 Step 1.
- Manual browser — Task 3.5.
- Roadmap lines flipped — Tasks 3.1, 3.2.
- Task #40 marked completed — Task 3.3.

No gaps.

### Placeholder scan

No "TBD", "TODO", "implement later", "Add appropriate error handling", "Similar to Task N", or steps without code blocks. Task 3.3 hands off to the controller — that's not a placeholder, it's an explicit subagent-vs-controller capability boundary. Task 3.5 (browser checklist) is an explicit gate that the spec also requires (§ 5.5).

### Type consistency

- `SupportedLanguage` type used consistently (Task 2.2 component + Task 2.1 test).
- `currentCode` reads from `t('auth.languageToggle.code.${current}')` (Task 2.2) — keys match the additions in Tasks 1.1 + 1.2.
- `currentName` reads from `t('auth.languageToggle.english')` / `t('auth.languageToggle.spanish')` (Task 2.2) — keys already exist; not added or removed.
- `ariaLabelWithLanguage` key added in Tasks 1.1 + 1.2, consumed in Task 2.2, asserted in Task 2.1 Test 4.
- `menuitemradio` role used in Tasks 2.1 Test 2, Test 5; matches `DropdownMenuRadioItem` rendered in Task 2.2.

Clean.

---

## Execution choice

**Plan complete and saved to `docs/superpowers/plans/2026-05-17-stage-9-1-5-e-language-code-toggle-impl.md`.** Two execution options:

**1. Subagent-Driven (recommended)** — Fresh implementer subagent per commit; two-stage review (spec compliance, then code quality) between commits. Recommended for this stage because Commit 2 is atomic (component rewrite + 5 test updates + 2 locale-file edits) and a fresh subagent reviewing the diff with no implementation-context bias is the standard quality gate.

**2. Inline Execution** — Execute tasks in this session using superpowers:executing-plans with batch checkpoints for user review.

Which approach?
