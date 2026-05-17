# Stage 9.1.5.c — Auth-page background contrast fix

**Status:** Spec — pending user review, then writing-plans skill.
**Phase:** Phase 3, Stage 9.1.5 (Phase-1-discovered bugfix batch).
**Originating bug:** Roadmap line 1098 (task #39) — "auth-page tokens collapse to identical values in light + dark modes."
**Date:** 2026-05-17.
**Parent commit:** `e6407bf` (HEAD at spec-write).

---

## 1. The bug, in plain English

On the SPA auth surfaces (`/app/login`, `/app/register` placeholder, `/app/password-reset` placeholder), toggling OS light/dark mode produces a card that looks visually identical to its page background in light mode. The user reports it as "the tokens collapse" — they don't, but the effect is the same: the card edge vanishes and the theme toggle appears to do nothing.

## 2. Root cause

`AuthLayout.tsx:18` sets the page background via `bg-muted/30`. Under Tailwind v4's opacity model:

| Mode | `--muted` raw | `bg-muted/30` composited over `--background` | `--card` | Card-vs-page lightness delta |
|---|---|---|---|---|
| Light | `oklch(0.965)` | `≈ oklch(0.989)` (over 1.000 background) | `oklch(1.000)` | **~1.1%** — below visual threshold; card edge disappears |
| Dark | `oklch(0.270)` | `≈ oklch(0.234)` (over 0.155 background) | `oklch(0.205)` | **~2.9%** — card is darker than page, stands forward |

The dark-mode math accidentally works (card is darker than the composited page); the light-mode math fails because `muted` (0.965) is too close to `card` (1.000), and the 30% opacity compresses the delta further. This was verified by inspecting `ProjectCeres.Client/src/index.css` (lines 91-184, which define every token) and `docs/design-system.md` lines 80-92 (which document the same values).

The tokens themselves are not broken — every token has distinct light/dark values. The bug is a layout-level choice: `AuthLayout` picked the wrong token for the page background. Light mode's card is `pure white` (1.000), and any page-background token whose composited lightness lands within ~5% of 1.000 will collapse the figure/ground relationship.

## 3. The fix

One line in `ProjectCeres.Client/src/app/layout/AuthLayout.tsx`:

```diff
-    <div className="min-h-dvh w-full bg-muted/30 flex items-center justify-center p-4 sm:p-6">
+    <div className="min-h-dvh w-full bg-background flex items-center justify-center p-4 sm:p-6">
```

After the change:

| Mode | Page bg (`--background`) | Card bg (`--card`) | Separation mechanism |
|---|---|---|---|
| Light | `oklch(1.000)` (pure white) | `oklch(1.000)` (pure white) | Card's existing `border` + `shadow-sm` (canonical shadcn Card pattern) |
| Dark | `oklch(0.155)` | `oklch(0.205)` | Card is **lighter** than the page (natural figure/ground for dark UIs) + border + shadow |

The shadcn `Card` primitive already ships with `border bg-card text-card-foreground shadow-sm` by default. In light mode the visual cue becomes the 1px border edge + the soft drop shadow — which is exactly how cards are separated from white backgrounds elsewhere in the project (the existing app shell already does this on dashboard cards against `bg-background`). In dark mode, the existing card-darker-than-muted/30 cue is replaced by card-lighter-than-background, which is the more conventional dark-UI figure/ground direction anyway.

## 4. Why we are not introducing a new `--auth-page-background` token

The brainstorm considered three approaches:

| Alternative | Why rejected |
|---|---|
| Use `bg-secondary` or `bg-muted` (no `/30`) | `--secondary` and `--muted` are documented as "surface" tokens (design-system.md:88, 90), not "page" tokens. Using them as a page background inverts the figure/ground in dark mode (the card becomes darker than the page, which is unusual). Also: `--muted` is meant for muted UI elements like skeletons and ghost rows, not a page chrome. |
| Introduce `--auth-page-background` | Adds a new token for one usage site. Violates the project's preference to reuse existing tokens unless multiple sites need the new abstraction (see `docs/design-system.md` § Tokens). Would also require documentation updates and a contrast-table entry. YAGNI — the existing `--background` already does the job once the layout uses it correctly. |
| Add `dark:bg-muted/30` and keep `bg-muted/30` for light + a different token for light | More moving parts. The bug is layer-specific (light only); fixing it with two classes when one class works is overengineering. |

`bg-background` is the minimal correct fix — it uses an existing semantic token, requires zero design-system changes, and unifies the auth-page chrome with the rest of the app shell.

## 5. Tests

One new vitest test pins the structural contract.

**File:** `ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx` (NEW — sibling to the existing `AuthLayout.a11y.test.tsx`).

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

The negative assertions (`not.toHaveClass('bg-muted/30')` and `not.toHaveClass('bg-muted')`) prevent the most likely regression shape: someone fixes the opacity but reintroduces a `muted` family class because "it looked muted-ish."

### Existing tests

- `AuthLayout.a11y.test.tsx` already covers landmark roles, focus order, contrast assertions (where applicable in jsdom). No change.
- Login, RegisterPlaceholder, PasswordResetPlaceholder render inside `AuthLayout`'s `<Outlet />` — they don't read the parent's classes, so they're unaffected.

### Manual UX verification

The structural test confirms the class is present. **Visual rendering still requires a browser check** because vitest's jsdom doesn't compute CSS variables. The implementer must:

1. Start the dev server: `pnpm --dir ProjectCeres.Client dev`.
2. Open `http://localhost:5173/app/login` (or the project's local URL) in a browser.
3. Toggle OS color scheme (System Preferences → Appearance → Light / Dark on macOS).
4. Verify in **light mode**: card visibly stands out from the page via a 1px border edge + drop shadow; the card surface and the page surface are both white but the boundary is clear.
5. Verify in **dark mode**: the card is visibly *lighter* than the page (figure/ground inverts to dark-UI convention).
6. Verify at **375px viewport** (mobile): same separation cue, no layout reflow issues.
7. Visit `/app/register` and `/app/password-reset` (placeholder pages) — same AuthLayout, same fix, no per-page edit needed.

## 6. Edge cases

### 6.1 Mobile viewport (375px)

The page layout uses `min-h-dvh w-full p-4 sm:p-6`. Viewport size doesn't change which background token applies. The card's `max-w-[420px]` keeps it from going edge-to-edge on mobile; the page-background fix is unchanged at every breakpoint.

### 6.2 `prefers-reduced-motion`

Orthogonal. `index.css` lines 194-203 already handle reduced-motion globally; this fix touches only the page background, no animation surfaces.

### 6.3 Future auth surfaces (`/email-verify`, `/password-reset/confirm`)

Not yet built but will inherit `AuthLayout` per the SPA migration spec. They get the fix for free — no per-page edit needed.

### 6.4 OS without color-scheme preference

`next-themes` `defaultTheme="system"` (per `main.tsx:17`) resolves to whatever the OS prefers. If a user has no preference, browsers default to light, so the fix matters for the most common case.

### 6.5 The shadcn Card primitive itself

The Card primitive at `ProjectCeres.Client/src/components/ui/card.tsx` is not modified. Its default `border bg-card text-card-foreground shadow-sm` styling is exactly what the new design relies on for light-mode separation.

## 7. Scope guard

### 7.1 In scope

1. One-line edit to `ProjectCeres.Client/src/app/layout/AuthLayout.tsx` (replace `bg-muted/30` with `bg-background`).
2. One new vitest test file `ProjectCeres.Client/src/app/layout/AuthLayout.test.tsx` (sibling to the existing `.a11y.test.tsx`).
3. Manual UX verification per § 5.
4. Roadmap close-out: flip 9.1.5.c `[ ]` to `[x]` in `docs/roadmap-phase-three.md`; replace the existing verification text with the post-fix description.
5. Update task #39 description to reflect the shipped fix.

### 7.2 Out of scope (design choices, NOT deferrals)

- Adding `--auth-page-background` or any new token.
- Restyling the `Card` primitive itself.
- Adding a light/dark toggle (that's Stage 9.1.5.d).
- Touching `AppLayout` or any in-app surface.
- Adding `dark:` variants anywhere — the existing tokens already handle dark mode correctly.

### 7.3 Audit for similar regressions elsewhere

Performed at spec-write time. Grep for `bg-muted/30` across `ProjectCeres.Client/src/app/layout/**/*.tsx` returns ONE hit: `AuthLayout.tsx:18` (the bug this spec fixes). Other `bg-muted` references in the layout directory (`MobileDrawer.tsx:59`, `TopBar.tsx:160`, `TopBar.tsx:169`, `Sidebar.tsx:80`) are all hover states or small sub-section accents inside already-distinct chrome, not page backgrounds. `min-h-dvh` exists only in `AuthLayout.tsx`. **The bug exists exactly once in the codebase.** No audit deferral needed.

### 7.4 Deferred work

None. The fix is self-contained.

## 8. Verification checklist

- [ ] `AuthLayout.tsx:18` uses `bg-background` (no `muted` family class on the page div).
- [ ] `AuthLayout.test.tsx` exists with the regression test pinning `bg-background` + `not bg-muted/30` + `not bg-muted`.
- [ ] `pnpm --dir ProjectCeres.Client test --run` exits 0.
- [ ] `pnpm --dir ProjectCeres.Client build` exits 0 (no new bundle-budget regressions; no TS errors).
- [ ] `dotnet test` exits 0 (sanity — no backend changes, but stop-hook will run it anyway on commit).
- [ ] Manual browser verification per § 5: light mode card boundary clear, dark mode card lighter than page, mobile 375px clean, both placeholder pages inherit the fix.
- [ ] Roadmap line 1098 (`docs/roadmap-phase-three.md`) flipped to `[x]`; verification text replaced.
- [ ] Task #39 marked completed in the task tracker.

## 9. Open questions

None at spec-write time. All design decisions resolved during brainstorming:

- `bg-background` over `bg-muted` or a new token — chosen because it uses an existing semantic token, requires zero design-system changes, and aligns the auth chrome with the rest of the app shell.
- One vitest structural test + manual UX checklist — chosen over computed-style assertions because jsdom doesn't compute CSS variables. The structural test catches the most likely regression shape; the manual check covers the visual one.
- No `dark:` variant in the fix — the existing token system already handles light/dark; the bug was at the layout-token-choice layer, not the token-definition layer.
