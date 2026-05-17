# Stage 9.1.5.e — Language code next to globe in `LanguageToggle`

**Status:** Spec — pending user review, then writing-plans skill.
**Phase:** Phase 3, Stage 9.1.5 (Phase-1-discovered bugfix batch).
**Originating bug:** Roadmap line 1100 — "Phase 1 UX walkthrough — globe icon alone doesn't communicate the active language."
**Date:** 2026-05-17.
**Parent commit:** `32c0dae` (HEAD at spec-write — Stage 9.1.5.d close-out).

---

## 1. The user-facing problem

In the auth-page footer, two icon-only ghost buttons sit side-by-side: the globe (`LanguageToggle`) and the sun/moon (`ThemeToggle`). For the theme toggle, the rendered icon IS the active-state cue — Sun means light, Moon means dark. For the language toggle, the globe is a generic "language" symbol that conveys nothing about which language is currently rendering. A first-time user landing on `/app/login` cannot tell at a glance whether the page is in English or Spanish without reading the surrounding copy. Worse, screen-reader users get a flat "Change language" hint with no signal of the current language at all.

This stage closes both gaps: the trigger button gets a two-letter code (`EN` / `ES`) next to the globe so sighted users see the active language at a glance, and the screen-reader hint is expanded to "Change language, currently English" so the same information reaches assistive tech. As a smaller-but-paired improvement, the dropdown menu adopts the same checked-active-option pattern that `ThemeToggle` shipped in 9.1.5.d, giving the two footer controls a unified vocabulary.

## 2. What ships

1. Rewrite `src/app/components/auth/LanguageToggle.tsx`:
   - Trigger renders `<Globe />` + two-letter code (`EN` / `ES`) side-by-side inside the same button.
   - Aria-label interpolates the active language name: `Change language, currently English` / `Cambiar idioma, actualmente Español`.
   - Dropdown switches from `DropdownMenuItem` rows to `DropdownMenuRadioGroup` + `DropdownMenuRadioItem`. Active language carries `aria-checked="true"` and a visible check.
2. New i18n keys under `auth.languageToggle`:
   - `code.en = "EN"` / `code.es = "ES"` (both locales — codes are the same string)
   - `ariaLabelWithLanguage = "Change language, currently {{language}}"` (English) / `"Cambiar idioma, actualmente {{language}}"` (Spanish)
3. Remove the now-dead `auth.languageToggle.ariaLabel` key from both `en.json` and `es.json` (consumer gone).
4. Update `LanguageToggle.test.tsx`: 2 existing tests updated to match new contract; 3 new tests added.
5. Roadmap close-out: flip `[ ]` to `[x]` on lines 1100 + 1111 of `docs/roadmap-phase-three.md`; mark task #40 completed.

## 3. Component design

**File:** `src/app/components/auth/LanguageToggle.tsx` (rewrite in place — same path, same default export).

**Implementation:**

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

**Why this shape:**

- **`i18n.resolvedLanguage`** is the i18next-recommended source for "which language is currently rendering" — it handles fallback chains (`es-MX → es → en`) where `i18n.language` alone might return a string the app doesn't actually serve. Fallback to `i18n.language` then to `'en'` covers the rare case neither is set during first render.
- **Code reads from i18n** (`code.en` / `code.es`), not hard-coded. Keeps the casing decision in the locale files — if we ever want `EN` → `Eng`, it's a one-line locale edit, not a component edit.
- **Aria-label reuses existing autonym keys.** `currentName` derives from `t('auth.languageToggle.english')` / `t('auth.languageToggle.spanish')` — the same strings the menu rows show. No new "name" keys; the autonym serves both the menu and the aria-label interpolation.
- **`<Globe>` + `<span>` inside one `<Button>`**. The button primitive handles its own internal flex defaults; `ml-1.5` (6px) on the span gives breathing room between icon and code. `text-xs font-medium` keeps the code visually subordinate to the icon (the icon is the affordance, the code is the active-state indicator).
- **`DropdownMenuRadioGroup` + `DropdownMenuRadioItem`** matches the ThemeToggle dropdown shipped in 9.1.5.d. Two controls in the same footer now use the same primitive vocabulary. `aria-checked` lands on the active radio automatically; visible checkmark on the right is built into the primitive (see `src/components/ui/dropdown-menu.tsx:191-220`).
- **`value={current}` + `onValueChange`** is the canonical base-ui radio-group pattern. Same shape as `ThemeToggle`'s `value={theme}`.

### Visual reference

```
Auth-page footer (after 9.1.5.e):
┌──────────────────────────────────────────┐
│                                          │
│            [🌐 EN]   [🌙]                  │
│                                          │
│  ← LanguageToggle (this stage)            │
│                ← ThemeToggle (9.1.5.d)    │
└──────────────────────────────────────────┘
```

Trigger renders Sun if light-mode active, Moon if dark-mode active (theme); globe is constant, code flips per language preference. Both buttons remain `variant="ghost" size="sm"` to keep the footer visually unified.

## 4. i18n keys

Added under the existing `auth.languageToggle` namespace.

**English (`src/app/i18n/locales/en.json`):**
```json
"auth": {
  "languageToggle": {
    "ariaLabelWithLanguage": "Change language, currently {{language}}",
    "code": {
      "en": "EN",
      "es": "ES"
    },
    "english": "English",
    "spanish": "Español"
  }
}
```

**Spanish (`src/app/i18n/locales/es.json`):**
```json
"auth": {
  "languageToggle": {
    "ariaLabelWithLanguage": "Cambiar idioma, actualmente {{language}}",
    "code": {
      "en": "EN",
      "es": "ES"
    },
    "english": "English",
    "spanish": "Español"
  }
}
```

The old `auth.languageToggle.ariaLabel` key (`"Change language"` / `"Cambiar idioma"`) is removed in Commit 2 alongside its consumer swap. The autonym keys `english` / `spanish` are kept verbatim — same strings as the menu rows, no duplication.

## 5. Tests

### 5.1 Updated existing tests (`LanguageToggle.test.tsx`)

Two tests survive the rewrite with small assertion edits per TDD case (3) (production contract changed):

| # | Test name | Edit |
|---|---|---|
| 1 | `renders the globe button with an accessible label` | Existing regex `/change language/i` still matches the new aria-label `"Change language, currently English"` because the regex is a substring match. Test passes without edit, but the name should change to `renders the globe trigger with an aria-label that includes the active language` for accuracy. |
| 2 | `switches the language and writes the lang cookie when an option is selected` | Selector switches from `getByRole('menuitem', { name: 'Español' })` to `getByRole('menuitemradio', { name: 'Español' })` because the dropdown now uses radio items. Behavior assertion (`i18n.language === 'es'` and `document.cookie.contains('lang=es')`) is unchanged. |

### 5.2 New tests — TDD case (1)

| # | Test name | Assertion |
|---|---|---|
| 3 | `renders the active language code (EN) on the trigger in English` | After mount with `i18n.changeLanguage('en')` in beforeEach, `within(trigger).getByText('EN')` succeeds. |
| 4 | `aria-label interpolates the active language name and updates on language change` | Initial render: trigger's `aria-label` matches `/change language, currently english/i`. Then `await i18n.changeLanguage('es')`, re-query, assert `aria-label` matches `/cambiar idioma, actualmente español/i`. Single test covers both the i18n interpolation contract AND reactivity. |
| 5 | `marks the currently-active language as checked in the dropdown` | Open menu, assert English `aria-checked="true"` and Spanish `aria-checked="false"`. Click Spanish, re-open menu (base-ui closes the menu on selection), assert checks have flipped. |

### 5.3 Test mechanics

- Existing `beforeEach`/`afterEach` patterns stay: clear `lang` cookie, `void i18n.changeLanguage('en')` to reset to English.
- Per the cda7b04 root-cause fix: **no `vi.resetAllMocks()` anywhere in the file's `afterEach`.** The existing file doesn't have it; keep it that way.
- No `vi.mock` of `useTranslation` — real i18n via `<I18nextProvider i18n={i18n}>`, same wrapper the file already uses.
- `userEvent.setup()` per test (existing pattern).
- Test 4's language switch uses `await i18n.changeLanguage('es')`; after that, the component re-renders via the `useTranslation` subscription. The test's `await` ensures the change propagates before re-query.

### 5.4 TDD case map (per docs/testing.md § Rules)

- Tests 1, 2: case (3) — production CONTRACT intentionally changed (aria-label expanded, menu items are now radio items).
- Tests 3, 4, 5: case (1) — new tests for new behavior (code on trigger, interpolated aria-label, dropdown radio check).

### 5.5 Manual UX verification (browser, gates close-out)

- `pnpm --dir ProjectCeres.Client dev` + `dotnet run --project ProjectCeres`, browse `https://localhost:7081/app/login`.
- Footer shows `[🌐 EN]` next to the theme icon. Both fit on one line at 375px.
- Click the language trigger → dropdown opens with two rows: "English" (checked) and "Español" (unchecked).
- Click "Español" → page strings flip to Spanish; trigger now shows `[🌐 ES]`.
- Open the trigger again → "Español" is now checked, "English" unchecked.
- Open DevTools → Accessibility tree → trigger button. Verify `Name: "Cambiar idioma, actualmente Español"` (after Spanish switch) and `Name: "Change language, currently English"` (after English switch).
- Reload → language preference persists (existing `writeLangCookie` behavior; cookie name `lang`, set by `src/app/i18n/i18n.ts`; this stage doesn't change persistence).
- Same flow on `/app/register` and `/app/password-reset` (both inherit `AuthLayout`'s footer).

## 6. Commit sequence

Two implementation commits + one close-out commit. Every commit is green.

### 6.1 Commit 1 — i18n keys (no consumer yet)

- Add `auth.languageToggle.ariaLabelWithLanguage` and `auth.languageToggle.code.{en,es}` to both `en.json` and `es.json`.
- KEEP the existing `auth.languageToggle.ariaLabel` key — the existing component still reads it; removing it now would break the suite.
- No code changes.
- `pnpm test --run` stays green.

**Commit message:** `feat(stage-9.1.5.e): add language-code + interpolated-aria-label i18n keys`

### 6.2 Commit 2 — Component rewrite + test updates + dead-key removal (atomic)

- Rewrite `src/app/components/auth/LanguageToggle.tsx` per § 3.
- Update `LanguageToggle.test.tsx` per § 5.1, add tests per § 5.2.
- REMOVE the now-dead `auth.languageToggle.ariaLabel` key from both `en.json` and `es.json` in the SAME commit (the rewrite is the last consumer; removing it here keeps the locale file clean per `feedback_clean_dead_code_immediately`).
- TDD ordering inside the commit: update test file FIRST (against the unchanged component), capture the expected failures (Test 3 — no "EN" text on trigger; Test 4 — old aria-label doesn't include language; Test 5 — no radio items). THEN rewrite the component. THEN remove the dead i18n key.

**Commit message:** `feat(stage-9.1.5.e): LanguageToggle shows active code + radio-checked dropdown + a11y aria-label`

### 6.3 Commit 3 — Roadmap close-out + browser verification

- Flip line 1100 `[ ]` to `[x]` with the implementation summary.
- Flip line 1111 `[ ]` to `[x]` initially with "Manual browser verification pending user run" text (matches the 9.1.5.d pattern); re-flip to "confirmed" after user signs off in the browser.
- Update task #40 in the task tracker (in_progress → completed).

**Commit message:** `docs(stage-9.1.5.e): close out — LanguageToggle code + a11y verified`

## 7. Scope guard

### 7.1 In scope

1. `src/app/components/auth/LanguageToggle.tsx` rewrite per § 3.
2. `src/app/components/auth/LanguageToggle.test.tsx` updates per § 5.
3. i18n key additions and one removal per § 4.
4. Roadmap close-out + task #40 update.

### 7.2 Out of scope (design choices, not deferrals)

- Adding a signed-in-shell mount of `LanguageToggle`. The component lives in `src/app/components/auth/`; a future stage that mounts it in the app shell can move it then. Pre-emptive relocation is YAGNI.
- Extracting a `useLanguage()` hook. Single caller; YAGNI.
- New language options beyond English / Spanish — separate feature.
- Changing the lang cookie persistence mechanism — separate concern.
- Flag emoji in the dropdown (`🇬🇧 English` etc.) — known a11y/i18n anti-pattern (flags represent nations not languages). Explicitly rejected during brainstorming Q3.
- Adding a chevron to the trigger to signal "this opens a menu". The neighboring `ThemeToggle` has no chevron; adding one here would make the two buttons look intentionally different. Rejected during Q2.

### 7.3 Cross-codebase audit

- `grep -rn "languageToggle.ariaLabel" ProjectCeres.Client/src` at parent commit `32c0dae` returns exactly one consumer: `LanguageToggle.tsx:24`. Removing the key in Commit 2 alongside the consumer swap is safe.
- `grep -rn "LanguageToggle" ProjectCeres.Client/src` returns one mount site (`AuthLayout.tsx:28`) and one test file (`LanguageToggle.test.tsx`). No other consumers; no other tests to update.
- The component's public API surface (default export, zero props) is unchanged. `AuthLayout.tsx:28` works without source edit.

### 7.4 Deferred work

None. Two implementation commits + one close-out commit, all atomic.

## 8. Verification checklist

- [ ] All 5 tests in `LanguageToggle.test.tsx` exit 0.
- [ ] `pnpm --dir ProjectCeres.Client test --run` exits 0 (full suite stays at ≥908 passing).
- [ ] `pnpm --dir ProjectCeres.Client build` exits 0 (no TS errors, no bundle-budget regressions).
- [ ] `dotnet test` exits 0 (sanity; stop-hook runs it on commit anyway).
- [ ] `grep -rn "languageToggle.ariaLabel\"" ProjectCeres.Client/src` returns zero hits (the old key is gone; the new `ariaLabelWithLanguage` is the only one in use).
- [ ] Manual browser verification per § 5.5 passes on every URL + viewport listed.
- [ ] `docs/roadmap-phase-three.md` lines 1100 + 1111 flipped to `[x]` with implementation + verification summaries.
- [ ] Task #40 in the task tracker marked completed.

## 9. Open questions

None at spec-write time. All five clarifying decisions resolved during brainstorming (Q1 uppercase EN/ES, Q2 globe + code side-by-side, Q3 keep autonym labels, Q4 radio-checked dropdown, Q5 interpolated aria-label). File-organization decision resolved (Approach A: rewrite in place). Component shape, test plan, commit sequence all settled. No `[TBD]` or `[follow-up]` markers anywhere.
