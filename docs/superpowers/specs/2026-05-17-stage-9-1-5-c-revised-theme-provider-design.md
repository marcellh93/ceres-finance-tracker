# Stage 9.1.5.c REVISED — Homegrown SPA theme provider replacing next-themes

**Status:** Spec — pending user review, then writing-plans skill.
**Phase:** Phase 3, Stage 9.1.5 (Phase-1-discovered bugfix batch).
**Supersedes:** `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md` (committed `e262b62`). The original spec misdiagnosed the bug as a token-contrast issue in `AuthLayout`. The shipped commit `014f2b5` (bg-muted/30 → bg-background) is **kept on independent merits** — it's correct per the shadcn page-background convention — but it did NOT fix the user-visible symptom. This revised spec addresses the actual root cause.
**Originating bug:** Roadmap line 1098 (task #39) — "auth-page tokens collapse to identical values in light + dark modes." Runtime DevTools confirmed the actual failure: `<html class="dark">` is stuck regardless of `prefers-color-scheme`; OS toggle has zero effect.
**Date:** 2026-05-17.
**Parent commit:** `014f2b5` (HEAD when revised spec begins).

---

## 1. The actual bug, in plain English

The user reports `/app/login` looks identical regardless of OS dark/light mode. DevTools dump:

| State | `htmlClass` | `mediaQueryDarkMode` |
|---|---|---|
| No OS emulation (OS dark) | `"dark"` | `true` |
| Light emulation forced | **`"dark"`** | `false` |

The `<html>` element gets `class="dark"` on first mount and **never updates** when the OS preference changes. The `:root` vs `.dark` CSS cascade in `index.css` is correct; the tokens have distinct values per mode. The bug is upstream: **the theme class never flips at runtime**.

## 2. Root cause

`next-themes` (mounted in `src/app/main.tsx:15-20` and `src/design-system/main.tsx:13-18` as `ThemeProvider attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange`) is **designed for Next.js**. Its `prefers-color-scheme` `matchMedia` subscription path depends on Next-specific lifecycle. In our Vite SPA mounted inside `<StrictMode>`, the initial class application works but the `MediaQueryList` `change` listener never fires (or fires and isn't honored). Per the next-themes README, the library "works with Gatsby or CRA" but provides no guidance for pure client-side rendering scenarios.

**The library is the wrong tool for our framework.** Patching around it (Option B from the deep-fix-mode round 4 brainstorm) means accumulating workarounds for a library that's documented as Next.js-first. Replacing it with a ~75-line homegrown provider is cheaper than the patching path.

## 3. The fix

Replace `next-themes` with a homegrown theme provider at `src/app/theme/theme-context.tsx` that:
- Uses `useSyncExternalStore` to subscribe to `matchMedia('(prefers-color-scheme: dark)')` (React 18+ canonical pattern for browser API subscriptions; per `vercel-react-best-practices` audit).
- Reads stored preference from `localStorage` (with try/catch + versioned key per `client-localstorage-schema`).
- Applies the resolved theme via `document.documentElement.classList` + `style.colorScheme`.
- Ports next-themes' `disableTransitionOnChange` mechanism (suppresses CSS transitions for one frame during the class swap, preventing a multi-hundred-ms color smear across every element with a color transition).
- Adds a pre-React inline script to `index.html` that applies the correct class BEFORE first paint (kills the FOUC that the React-lifecycle approach can't fix on its own).
- Adds `<meta name="color-scheme" content="light dark">` to `index.html` (MDN-recommended for UA chrome — scrollbars, native form controls — and partially mitigates the flash for the chrome surface even before the inline script runs).

Exposes the same `useTheme()` API surface as `next-themes` (`{ theme, resolvedTheme, setTheme }`) so the two consumers (`src/components/ui/sonner.tsx`, `src/design-system/components/ThemeToggle.tsx`) require only an import-path change.

### 3.1 New file: `src/app/theme/theme-context.tsx` (~85 lines)

```tsx
import { createContext, useCallback, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react';

export type Theme = 'light' | 'dark' | 'system';
export type ResolvedTheme = 'light' | 'dark';

type ThemeContextValue = {
  theme: Theme;
  resolvedTheme: ResolvedTheme;
  setTheme: (theme: Theme) => void;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);
const STORAGE_KEY = 'ceres.theme:v1';

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used inside <ThemeProvider>');
  return ctx;
}

function readStoredTheme(): Theme {
  if (typeof window === 'undefined') return 'system';
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
  } catch {
    // Safari private browsing or storage disabled.
    return 'system';
  }
}

// useSyncExternalStore subscriber: returns true when the OS prefers dark.
// Synchronous on first render; subscribes to change events on the same MediaQueryList.
// Replaces the next-themes anti-pattern of useState + useEffect for matchMedia
// (per vercel-react-best-practices and React docs' canonical matchMedia example).
const mql: MediaQueryList | null = typeof window !== 'undefined'
  ? window.matchMedia('(prefers-color-scheme: dark)')
  : null;

function subscribeOsDark(callback: () => void): () => void {
  if (!mql) return () => {};
  mql.addEventListener('change', callback);
  return () => mql.removeEventListener('change', callback);
}

function getOsDarkSnapshot(): boolean {
  return mql?.matches ?? false;
}

function getServerSnapshot(): boolean {
  return false; // SSR fallback — unused in our Vite SPA but required by the API
}

function applyTheme(resolved: ResolvedTheme): void {
  const root = document.documentElement;

  // disableTransitionOnChange port: inject one-frame transition-blocker so a global
  // theme swap doesn't smear hundreds of elements with color transitions for
  // ~200ms. Removed via requestAnimationFrame after the class is applied.
  // Mirrors next-themes' implementation (see ceres-polish-checklist-frontend.md:133).
  const style = document.createElement('style');
  style.appendChild(document.createTextNode(
    '*,*::before,*::after{transition:none!important;animation:none!important}',
  ));
  document.head.appendChild(style);

  root.classList.remove('light', 'dark');
  root.classList.add(resolved);
  root.style.colorScheme = resolved;

  // Force reflow so the no-transition rule lands before we remove it.
  void window.getComputedStyle(root).opacity;
  requestAnimationFrame(() => { document.head.removeChild(style); });
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(() => readStoredTheme());
  const systemDark = useSyncExternalStore(subscribeOsDark, getOsDarkSnapshot, getServerSnapshot);

  const resolvedTheme: ResolvedTheme = theme === 'system' ? (systemDark ? 'dark' : 'light') : theme;

  useEffect(() => { applyTheme(resolvedTheme); }, [resolvedTheme]);

  const setTheme = useCallback((next: Theme) => {
    setThemeState(next);
    try { window.localStorage.setItem(STORAGE_KEY, next); } catch { /* private mode */ }
  }, []);

  // Memoize the context value so consumers don't re-render on unrelated parent renders.
  const value = useMemo<ThemeContextValue>(
    () => ({ theme, resolvedTheme, setTheme }),
    [theme, resolvedTheme, setTheme],
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}
```

### 3.2 Pre-React inline script + meta tag in `index.html`

Add to `<head>` (BEFORE the existing `<script type="module" src="/src/main.tsx"></script>`):

```html
    <!-- Color-scheme hint for UA chrome (scrollbars, native form controls).
         Partially mitigates first-paint flash before React mounts. -->
    <meta name="color-scheme" content="light dark" />

    <!-- Pre-React theme init: applies .dark/.light class to <html> BEFORE first
         paint so OS-dark users don't see a one-frame light flash. Mirrors what
         next-themes does in Next.js via its <Head> injection. CSP note: when
         Phase 3 CSP enforcement lands (security-model.md §954), this script
         must carry the server-injected nonce. -->
    <script>
      (function () {
        try {
          var stored = localStorage.getItem('ceres.theme:v1');
          var theme = (stored === 'light' || stored === 'dark' || stored === 'system') ? stored : 'system';
          var systemDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
          var resolved = theme === 'system' ? (systemDark ? 'dark' : 'light') : theme;
          var root = document.documentElement;
          root.classList.add(resolved);
          root.style.colorScheme = resolved;
        } catch (e) { /* private mode, etc. — fall through to React's effect-driven application */ }
      })();
    </script>
```

This script runs synchronously during HTML parsing, **before** React loads, so the `.dark` class is on `<html>` by the time CSS variable resolution happens for the first paint. Keys + logic match `theme-context.tsx` exactly — single source of truth for STORAGE_KEY and the OS check.

### 3.3 Other file changes

| File | Change |
|---|---|
| `ProjectCeres.Client/src/app/main.tsx` | Replace `import { ThemeProvider } from 'next-themes'` with `import { ThemeProvider } from './theme/theme-context'`. Drop the `attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange` props (homegrown doesn't take them — behavior is built in). |
| `ProjectCeres.Client/src/design-system/main.tsx` | Same swap — replace next-themes import + props. This is a SECOND mount site I missed in the original brainstorm; the design-system showcase at `/design-system.html` also uses the provider. |
| `ProjectCeres.Client/src/components/ui/sonner.tsx` | Replace `import { useTheme } from "next-themes"` with `import { useTheme } from "@/app/theme/theme-context"`. Keep the `const { theme = "system" } = useTheme()` default (now unreachable since `useTheme` throws if outside the provider, but harmless and matches sonner upstream docs). |
| `ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx` | Replace import path. **Remove** the `const [mounted, setMounted] = useState(false); useEffect(() => setMounted(true), [])` hydration-flash workaround — no longer needed because the homegrown provider returns `resolvedTheme` synchronously on first render. Simplify `isDark` derivation accordingly. Per `feedback_clean_dead_code_immediately`. |
| `ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx` | Update the inline comment that says "JSDOM doesn't choke on portals / canvas / theme" — the theme reference is now stale (the new provider works fine in JSDOM with no mocks). The test itself stubs `@/components/ui/sonner` to `() => null` which sidesteps `useTheme()`; that mock keeps working. |
| `ProjectCeres.Client/vite.config.ts` (line ~105) | Update the `manualChunks` comment that lists `next-themes` as a vendor-overlay member (drop next-themes from the comment). |
| `ProjectCeres.Client/package.json` | Remove `"next-themes": "^0.4.6"` from `dependencies`. |
| `ProjectCeres.Client/pnpm-lock.yaml` | Regenerated via `pnpm --dir ProjectCeres.Client install` (auto). |
| `ProjectCeres.Client/index.html` | Add the `<meta name="color-scheme">` + inline pre-paint script (per §3.2). |
| `docs/security-model.md` (Phase 3 CSP §954 area) | One-line cross-reference: "When CSP enforcement lands, the pre-paint theme-init script at `ProjectCeres.Client/index.html` must receive the server-injected nonce." Tripwire for the future CSP work. |

## 4. Edge cases (audit-validated)

### 4.1 First-paint flash
**Killed** by the inline script in `index.html`. OS-dark users see the dark surface from frame zero. Without the inline script, there would be a one-frame light flash; we explicitly include the script.

### 4.2 Theme-toggle color smear (`disableTransitionOnChange` regression)
**Addressed.** `applyTheme()` injects a one-frame `transition: none !important; animation: none !important` blocker, applies the class, forces reflow, removes the blocker on `requestAnimationFrame`. Mirrors next-themes' implementation. Without this port, every theme toggle would smear every transitioning element for hundreds of milliseconds.

### 4.3 `useSyncExternalStore` race
**Closed.** The previous `useState` + `useEffect` design had a sub-millisecond race window where an OS toggle between `useState`'s initializer and `useEffect`'s `addEventListener` would be missed. `useSyncExternalStore` synchronizes subscription and snapshot read internally; no race.

### 4.4 `<StrictMode>` double-mount
**Safe.** `useSyncExternalStore`'s subscribe + unsubscribe symmetry handles StrictMode's intentional double-mount-and-unmount in dev. Each `<ThemeProvider>` instance attaches and cleans up its own listener.

### 4.5 localStorage corruption / Safari private mode
**Handled.** `readStoredTheme()` wraps `getItem` in try/catch and validates the value against the `Theme` union. `setTheme()` wraps `setItem` in try/catch (Safari private mode throws). Falls back to `'system'` if storage is unavailable. Versioned key (`ceres.theme:v1`) per `client-localstorage-schema` enables future schema migrations.

### 4.6 GDPR / consent banner for localStorage
**Exempt.** Storing a UI preference is "strictly necessary for the provision of a service explicitly requested by the user" per ePrivacy Directive Art. 5(3) — same legal basis as the cookie-dismissal flag. No banner needed. When Phase 3 auth ships, the preference can migrate to a server-side user record for signed-in users.

### 4.7 WCAG 2.3.1 / 2.3.2 (flash thresholds)
**Not violated.** A single one-frame light→dark transition on page load is **one** transition, not a **pair** of opposing luminance changes (the WCAG definition requires both increase AND decrease at >3 Hz over a 341×256px area). The flash mitigation in §3.2 is a UX improvement, not an a11y law requirement.

### 4.8 Throw-on-missing-provider behavior change
The homegrown `useTheme()` throws if called outside `<ThemeProvider>`. next-themes returns silent defaults (`{ theme: undefined, ... }`). This is a deliberate contract change matching the existing `AuthProvider` pattern. Future tests that render `useTheme` consumers must wrap with `<ThemeProvider>`. The existing `AppLayout.a11y.test.tsx` mocks Sonner to `() => null` and never calls `useTheme`, so it continues passing.

### 4.9 shadcn primitives consuming `color-scheme`
**No interaction risk.** Audited every file under `src/components/ui/`: zero `color-scheme` reads, zero `light-dark()` CSS function usage. All primitives read CSS variables (`--popover`, `--background`, etc.) that cascade from the `.dark` class. Setting `style.colorScheme` on `<html>` only affects UA chrome (scrollbars, native `<input type="date">`, autofill) — exactly the intended scope.

### 4.10 prefers-reduced-motion
The existing global `prefers-reduced-motion` rule at `src/index.css:194-203` already collapses all transitions for reduced-motion users. The `disableTransitionOnChange` port adds belt to that suspenders — both layers fire under reduced motion, producing zero animation.

### 4.11 CSP nonce requirement
The Phase 3 CSP per `security-model.md:957` will require nonces on inline scripts. CSP middleware is not yet wired (grep of `Program.cs` returns no `Content-Security-Policy` header config). Inline script ships today without a nonce because there is no CSP to enforce against. Deferral handled per §6.3 (already-scheduled receiving entry: Phase 3 CSP work; tripwire: cross-reference line added to `security-model.md` in this commit).

## 5. Tests

Five vitest tests in `ProjectCeres.Client/src/app/theme/theme-context.test.tsx`:

```tsx
import { act, render } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { ThemeProvider, useTheme } from './theme-context';

let mqListeners: Array<() => void> = [];
let mqMatches = false;

beforeEach(() => {
  mqListeners = [];
  mqMatches = false;
  window.localStorage.clear();
  document.documentElement.className = '';
  document.documentElement.style.colorScheme = '';
  window.matchMedia = vi.fn().mockImplementation(() => ({
    get matches() { return mqMatches; },
    addEventListener: (_: string, cb: () => void) => mqListeners.push(cb),
    removeEventListener: (_: string, cb: () => void) => {
      mqListeners = mqListeners.filter(l => l !== cb);
    },
  })) as unknown as typeof window.matchMedia;
});

function fireOsToggle(toDark: boolean) {
  mqMatches = toDark;
  mqListeners.forEach(cb => cb());
}

function Probe() {
  const { theme, resolvedTheme } = useTheme();
  return <span data-testid="state">{theme}/{resolvedTheme}</span>;
}

describe('ThemeProvider', () => {
  it('applies the correct class synchronously on first mount (no effect-driven flash)', () => {
    // Regression test for the user-visible bug. Previously next-themes set the
    // class only AFTER React mounted; we apply via the inline script + useSyncExternalStore
    // returns the snapshot synchronously, so the first useEffect can paint correctly.
    mqMatches = true; // OS prefers dark
    const { getByTestId } = render(<ThemeProvider><Probe /></ThemeProvider>);
    expect(getByTestId('state').textContent).toBe('system/dark');
    expect(document.documentElement).toHaveClass('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
  });

  it('updates html.classList when the OS prefers-color-scheme media query toggles', () => {
    // The actual bug we're fixing — next-themes failed to honor this.
    mqMatches = false;
    const { getByTestId } = render(<ThemeProvider><Probe /></ThemeProvider>);
    expect(document.documentElement).toHaveClass('light');

    act(() => fireOsToggle(true));
    expect(document.documentElement).toHaveClass('dark');
    expect(getByTestId('state').textContent).toBe('system/dark');

    act(() => fireOsToggle(false));
    expect(document.documentElement).toHaveClass('light');
    expect(getByTestId('state').textContent).toBe('system/light');
  });

  it('honors an explicit setTheme override of the OS preference', () => {
    mqMatches = true; // OS is dark
    function Toggler() {
      const { setTheme, theme, resolvedTheme } = useTheme();
      return (
        <>
          <span data-testid="state">{theme}/{resolvedTheme}</span>
          <button data-testid="force-light" onClick={() => setTheme('light')}>light</button>
        </>
      );
    }
    const { getByTestId } = render(<ThemeProvider><Toggler /></ThemeProvider>);
    expect(getByTestId('state').textContent).toBe('system/dark');

    act(() => getByTestId('force-light').click());
    expect(getByTestId('state').textContent).toBe('light/light');
    expect(document.documentElement).toHaveClass('light');
    expect(window.localStorage.getItem('ceres.theme:v1')).toBe('light');
  });

  it('survives the system → light → system transition (OS-dark re-resolves correctly)', () => {
    mqMatches = true; // OS dark
    function Toggler() {
      const { setTheme, theme, resolvedTheme } = useTheme();
      return (
        <>
          <span data-testid="state">{theme}/{resolvedTheme}</span>
          <button data-testid="to-light" onClick={() => setTheme('light')}>light</button>
          <button data-testid="to-system" onClick={() => setTheme('system')}>system</button>
        </>
      );
    }
    const { getByTestId } = render(<ThemeProvider><Toggler /></ThemeProvider>);
    expect(getByTestId('state').textContent).toBe('system/dark');

    act(() => getByTestId('to-light').click());
    expect(getByTestId('state').textContent).toBe('light/light');

    act(() => getByTestId('to-system').click());
    expect(getByTestId('state').textContent).toBe('system/dark');  // re-resolves to OS dark
    expect(document.documentElement).toHaveClass('dark');
  });

  it('falls back to "system" when localStorage contains a corrupt value', () => {
    window.localStorage.setItem('ceres.theme:v1', 'midnight'); // invalid
    mqMatches = false;
    const { getByTestId } = render(<ThemeProvider><Probe /></ThemeProvider>);
    expect(getByTestId('state').textContent).toBe('system/light');
  });
});
```

Each test pins a distinct failure mode that the audit surfaced:
1. **Test #1** — first-mount sync behavior (audit IMPORTANT #5).
2. **Test #2** — OS-toggle propagates (the actual bug we're fixing — audit IMPORTANT #1).
3. **Test #3** — explicit setTheme override + localStorage persistence.
4. **Test #4** — system ↔ light ↔ system round-trip (audit follow-up #5 in design-system review).
5. **Test #5** — localStorage corruption fallback (audit M2 + #5).

## 6. Scope guard

### 6.1 In scope
1. New `src/app/theme/theme-context.tsx` (~85 LOC, exporting `ThemeProvider`, `useTheme`, `Theme`, `ResolvedTheme`).
2. New `src/app/theme/theme-context.test.tsx` (5 tests above).
3. Pre-paint inline script + `color-scheme` meta tag in `ProjectCeres.Client/index.html`.
4. Swap import in `src/app/main.tsx`; drop next-themes-specific props.
5. Swap import in `src/design-system/main.tsx`; drop next-themes-specific props. **(Was missed in original brainstorm; flagged by audit.)**
6. Swap import in `src/components/ui/sonner.tsx`.
7. Swap import in `src/design-system/components/ThemeToggle.tsx`; remove the unnecessary `mounted` useState + useEffect hack.
8. Update inline comment in `src/app/layout/AppLayout.a11y.test.tsx` (remove stale "theme" trip-hazard mention).
9. Update vendor-chunks comment in `ProjectCeres.Client/vite.config.ts` (drop next-themes from the chunk-membership comment).
10. Remove `next-themes` from `ProjectCeres.Client/package.json`; regenerate `pnpm-lock.yaml` via `pnpm --dir ProjectCeres.Client install`.
11. Verify zero `next-themes` references survive: `grep -rn "next-themes" ProjectCeres.Client/src` returns zero.
12. One-line tripwire cross-reference in `docs/security-model.md` Phase 3 CSP section (inline-script nonce requirement).
13. Update the `roadmap-phase-three.md` 9.1.5.c entry (line 1098 sub-stage row + line 1108 verification line) to reflect the revised diagnosis + new test coverage.
14. Amend the original 9.1.5.c spec (`docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md`) with a "**CORRECTION**" section at the top pointing to this revised spec.
15. Update task #39 description to reflect the revised diagnosis + fix.

### 6.2 Out of scope (design choices, NOT deferrals)
- Wiring CSP middleware in `Program.cs` — that's the larger Phase 3 CSP enforcement work tracked elsewhere; this spec only adds a one-line cross-reference so future CSP work picks up the nonce requirement.
- Migrating the theme preference to a server-side user record once Phase 3 auth ships — orthogonal; future work.
- In-app theme toggle UI on `AuthLayout` — that's Stage 9.1.5.d.
- Touching commit `014f2b5` (the `bg-background` swap from the original 9.1.5.c) — kept on independent merits per shadcn convention; the corresponding `AuthLayout.test.tsx` regression test stays.

### 6.3 Deferred work (with required tripwire fields)
**Phase 3 CSP nonce on the inline pre-paint script** —
- **Cited reason (already-scheduled):** `docs/security-model.md:954` mandates the Phase 3 CSP rollout. CSP middleware in `Program.cs` is not yet wired (verified via grep). The pre-paint inline script ships today without a nonce because there is no CSP to enforce against; when CSP enforcement work lands, the script must carry the server-injected nonce or it gets blocked.
- **Receiving-stage checkbox (same commit):** the cross-reference line added in `docs/security-model.md` Phase 3 CSP section. Acts as the tripwire — whoever picks up CSP work has to address the inline script.
- **Mechanical tripwire:** when CSP enforcement actually ships, any inline `<script>` without a nonce gets blocked at runtime → the page never renders → tests catch it AND any first-paint check catches it.

No other deferrals.

## 7. Verification checklist

- [ ] `src/app/theme/theme-context.tsx` exists with `ThemeProvider` + `useTheme` + `Theme` + `ResolvedTheme` exports; ~75-85 LOC.
- [ ] Uses `useSyncExternalStore` (not `useEffect` + `useState`) for the `matchMedia` subscription.
- [ ] `applyTheme` injects + removes the transition-blocker style around the class swap (`disableTransitionOnChange` port).
- [ ] localStorage reads + writes wrapped in `try/catch`; key is `ceres.theme:v1`.
- [ ] Context value wrapped in `useMemo`.
- [ ] `<meta name="color-scheme" content="light dark">` added to `index.html` `<head>`.
- [ ] Pre-paint inline `<script>` added to `index.html` `<head>` BEFORE `<script type="module" src="/src/main.tsx">`.
- [ ] Inline script uses the SAME storage key (`ceres.theme:v1`) as `theme-context.tsx`.
- [ ] BOTH mount sites updated: `src/app/main.tsx` AND `src/design-system/main.tsx`.
- [ ] `src/components/ui/sonner.tsx` import swapped; `= "system"` default preserved.
- [ ] `src/design-system/components/ThemeToggle.tsx` import swapped; `mounted` hack removed.
- [ ] `src/app/layout/AppLayout.a11y.test.tsx` comment updated.
- [ ] `vite.config.ts` manual-chunks comment updated (remove `next-themes` from the vendor-overlay member list).
- [ ] `package.json` no longer lists `next-themes`; `pnpm install` regenerates lockfile.
- [ ] `grep -rn "next-themes" ProjectCeres.Client/src ProjectCeres.Client/*.config.ts ProjectCeres.Client/package.json` returns ZERO hits.
- [ ] `docs/security-model.md` Phase 3 CSP section has the one-line cross-reference to the inline script's nonce requirement.
- [ ] 5 new vitest tests green (`pnpm --dir ProjectCeres.Client test --run src/app/theme/theme-context.test.tsx`).
- [ ] Full `pnpm --dir ProjectCeres.Client test --run` green.
- [ ] Full `pnpm --dir ProjectCeres.Client build` succeeds (bundle budgets intact; vendor-overlay chunk may shrink slightly without next-themes).
- [ ] `dotnet test` green (stop-hook gate; no backend change but the hook runs it anyway).
- [ ] **Manual browser verification** at `https://localhost:7081/app/login`: run the DevTools snippet from deep-fix-mode round 4 (`document.documentElement.className`, `mediaQueryDarkMode: window.matchMedia('(prefers-color-scheme: dark)').matches`). Toggle DevTools color-scheme emulation between light and dark. `htmlClass` MUST flip between `"light"` and `"dark"` correctly. Page colors MUST change visually. No CSS smear/flash on the toggle.
- [ ] Roadmap line 1098 + 1108 updated; task #39 updated; original 9.1.5.c spec annotated with the CORRECTION section.

## 8. Open questions

None at spec-write time. All design decisions resolved during brainstorming + three-way audit (vercel-react-best-practices, web-design-guidelines, design-system consumer audit) + deep-fix-mode round 4 + CSP verification:

- `useSyncExternalStore` over `useEffect` + `useState` — per React docs canonical matchMedia example + vercel-react-best-practices `rerender-derived-state-no-effect`.
- `disableTransitionOnChange` mechanism ported — per `docs/ceres-polish-checklist-frontend.md:133` documenting it as load-bearing.
- Pre-paint inline script in `index.html` — per audit finding that React lifecycle cannot kill first-paint flash; CSP nonce deferral justified via already-scheduled receiving entry.
- Storage key versioned (`ceres.theme:v1`) — per `client-localstorage-schema`.
- `useMemo` on context value — cheap insurance, React Compiler not enabled.
- `try/catch` on localStorage — Safari private mode safety.
- Throw-on-missing-provider in `useTheme()` — matches `AuthProvider` pattern; deliberate contract change called out in §4.8.
- Both mount sites in scope — audit caught the design-system showcase mount I'd missed.
- Test coverage at 5 tests (sync mount, OS toggle, explicit override, round-trip, localStorage corruption) — pins each distinct failure mode the audit surfaced.
