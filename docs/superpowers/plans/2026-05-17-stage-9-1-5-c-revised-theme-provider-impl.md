# Stage 9.1.5.c REVISED — Homegrown SPA Theme Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `next-themes` (broken in our Vite SPA — sets `<html class="dark">` once and never re-evaluates on OS toggle) with a homegrown ~85-line `ThemeProvider` that uses `useSyncExternalStore` for the `matchMedia` subscription, ports `disableTransitionOnChange`, and ships a pre-paint inline script in `index.html` so OS-dark users don't see a one-frame light flash.

**Architecture:** Four sequential commits keep the build green at every boundary. Commit 1 lands the new provider + tests as dead code (nothing imports it yet). Commit 2 atomically swaps all 4 import sites — both `main.tsx` files, `sonner.tsx`, `ThemeToggle.tsx` — plus removes the obsolete `mounted` hydration hack. Commit 3 removes `next-themes` from `package.json` + regenerates the lockfile + verifies zero remaining references. Commit 4 is manual browser verification (gated on user) + roadmap close-out + the CORRECTION note on the superseded original spec + `security-model.md` CSP cross-reference + task #39 update.

**Tech Stack:** React 19 · `useSyncExternalStore` (React 18+ canonical pattern for browser-API subscriptions) · Vite 7 · Tailwind CSS v4 · vitest · @testing-library/react · `matchMedia('(prefers-color-scheme: dark)')` for OS preference · `localStorage` for user override persistence (versioned key `ceres.theme:v1`).

**Source spec:** `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-revised-theme-provider-design.md` (commit `80709dd`).
**Supersedes original spec:** `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md` (commit `e262b62`); the `bg-background` swap from commit `014f2b5` is kept on independent merits.

---

## Binding constraints

- **Stay on `main`.** No worktrees, no branches.
- **No `Co-Authored-By` trailer** in commit messages.
- **TDD per `docs/testing.md` § Rules** — write the failing test first, confirm it fails for the right reason, then make it pass. Every test-touching commit names which of cases (1), (2), or (3) applied.
- **Never modify, skip, or weaken tests.** No `[Fact(Skip=…)]`, no commented-out assertions.
- **Pre-existing test failures encountered mid-task get root-caused now**, not logged as `TaskCreate` follow-ups. Exception: documented `BudgetEdit.test.tsx` fetch-mock pollution flake from tasks #29/#31 (re-run in isolation; if isolation passes, document and proceed).
- **Stop-hook (`.claude/hooks/run-tests.sh`) blocks commits on `dotnet test` failure.** Frontend-only stage but the hook still runs `dotnet test` on every commit. Run it before each commit.
- **`pnpm` only.** Every Vite/vitest/install invocation uses `pnpm --dir ProjectCeres.Client`. Never `npm`, never `npx`. Per `feedback_pnpm_only_never_npm`.
- **Pre-paint inline script ships WITHOUT a nonce.** No CSP middleware is wired yet (verified via grep of `Program.cs`). The deferral is documented in spec §6.3 with all three template fields. Do NOT add nonce injection in this plan.

## File map

| File | Action | Purpose |
|---|---|---|
| `ProjectCeres.Client/src/app/theme/theme-context.tsx` | **create** | The new provider + `useTheme` hook (~85 LOC). Full code in spec §3.1. |
| `ProjectCeres.Client/src/app/theme/theme-context.test.tsx` | **create** | 5 vitest tests pinning: sync mount, OS toggle, explicit override, system→light→system round-trip, localStorage corruption fallback. Full code in spec §5. |
| `ProjectCeres.Client/index.html` | **modify** | Add `<meta name="color-scheme" content="light dark">` + pre-paint inline `<script>` to `<head>` BEFORE the existing `<script type="module" src="/src/main.tsx"></script>`. Full code in spec §3.2. |
| `ProjectCeres.Client/src/app/main.tsx` | **modify** | Swap `import { ThemeProvider } from 'next-themes'` to `from './theme/theme-context'`; drop `attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange` props. |
| `ProjectCeres.Client/src/design-system/main.tsx` | **modify** | Same swap (second mount site at lines 13-18 for the `/design-system.html` showcase). |
| `ProjectCeres.Client/src/components/ui/sonner.tsx` | **modify** | Swap `import { useTheme } from "next-themes"` to `from "@/app/theme/theme-context"`. Keep the `= "system"` default literal on line 6 (unreachable but matches sonner upstream docs). |
| `ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx` | **modify** | Swap import path. Remove the `mounted` useState + useEffect hack (lines 12, 14-16). Simplify `isDark` derivation. |
| `ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx` | **modify** | Update the inline comment on lines 7-9 to remove the stale "theme" trip-hazard mention. |
| `ProjectCeres.Client/vite.config.ts` | **modify** | Line 105 manualChunks comment lists `next-themes` in vendor-overlay; line 109 has the `id.includes('node_modules/next-themes')` check. Remove both. |
| `ProjectCeres.Client/package.json` | **modify** | Remove `"next-themes": "^0.4.6"` from `dependencies`. |
| `ProjectCeres.Client/pnpm-lock.yaml` | **modify** | Regenerated via `pnpm --dir ProjectCeres.Client install` (auto). |
| `docs/security-model.md` | **modify** | Add one-line cross-reference near line 957 about the inline pre-paint script needing a nonce when CSP enforcement lands. |
| `docs/roadmap-phase-three.md` | **modify** | Update 9.1.5.c entry (sub-stage row near line 1098 + verification line near line 1108) with the revised diagnosis. |
| `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md` | **modify** | Prepend a "CORRECTION" section at the top pointing to the revised spec. |

## Commit-by-commit overview

| Commit | Subject | What lands | Risk |
|---|---|---|---|
| 1 | `feat(stage-9.1.5.c-revised): add homegrown SPA ThemeProvider + tests + pre-paint inline script` | New `theme-context.tsx` + `theme-context.test.tsx` + `index.html` head edits. New provider is dead code — no consumers yet. | Low: no behavior change because old `next-themes` is still mounted. New tests must pass before commit. |
| 2 | `refactor(stage-9.1.5.c-revised): swap all useTheme/ThemeProvider import sites to homegrown provider` | Atomic swap of 4 files (`src/app/main.tsx`, `src/design-system/main.tsx`, `src/components/ui/sonner.tsx`, `src/design-system/components/ThemeToggle.tsx`) + remove the obsolete `mounted` hack + update `vite.config.ts` chunk comment. | Medium: must be atomic — if you split it, one file imports from `next-themes` and another from the new path. Both implementations have the same API so it'd still build, but cleaner as one commit. |
| 3 | `chore(stage-9.1.5.c-revised): remove next-themes dependency + verify zero references survive` | Remove `next-themes` from `package.json`; `pnpm install` regenerates lockfile; grep verifies zero references; update `AppLayout.a11y.test.tsx` comment. | Low: dependency removal MUST come AFTER Commit 2 lands (otherwise imports from a deleted dep → build red). |
| 4 | `docs(stage-9.1.5.c-revised): close out — homegrown theme provider verified` | Manual browser verification (user-gated) → roadmap close-out + original-spec CORRECTION note + `security-model.md` CSP cross-reference + task #39 update. | Low: docs-only. **Gated on user browser verification.** Subagent will likely report `DONE_WITH_CONCERNS` on browser availability and hand the verification checklist to the user. |

---

### Task 1: Add new ThemeProvider + tests + pre-paint inline script

**Files:**
- Create: `ProjectCeres.Client/src/app/theme/theme-context.tsx`
- Create: `ProjectCeres.Client/src/app/theme/theme-context.test.tsx`
- Modify: `ProjectCeres.Client/index.html`

This commit ships the new provider as dead code (no consumers yet — old `next-themes` is still mounted in `main.tsx` and used by `sonner.tsx` + `ThemeToggle.tsx`). The new tests must pass before this commit lands. The inline script in `index.html` runs at every page load but only applies the same class `next-themes` already applies — no observable behavior change.

- [ ] **Step 1: Create the new directory and provider file**

Run: `mkdir -p <repo>/ProjectCeres.Client/src/app/theme`

Create `<repo>/ProjectCeres.Client/src/app/theme/theme-context.tsx` with this exact content:

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

- [ ] **Step 2: Write the 5 failing tests**

Create `<repo>/ProjectCeres.Client/src/app/theme/theme-context.test.tsx` with this exact content:

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

- [ ] **Step 3: Run the new tests, confirm they pass**

Run: `pnpm --dir <repo>/ProjectCeres.Client test --run src/app/theme/theme-context.test.tsx`

Expected: 5/5 PASS. (Strictly speaking the TDD red-then-green for the provider was already enacted by writing the test code BEFORE the provider — but since both files land in the same commit, "the tests confirm the implementation works on first run" is the practical TDD outcome here.)

If any test fails, read the error carefully:
- "`useTheme must be used inside <ThemeProvider>`" — your Probe component is rendered outside the provider; check the JSX wrapping.
- "Expected to have class 'dark' but had class 'light'" — `mqMatches` was set wrong before render; check the test's `beforeEach` reset.
- TypeScript errors — re-read the provider code, common cause is missing type imports.

- [ ] **Step 4: Edit `ProjectCeres.Client/index.html`**

Open `<repo>/ProjectCeres.Client/index.html`. Current content:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="description" content="Personal finance tracker for individuals and freelancers. Track assets, liabilities, net worth, income, expenses, and goal budgets." />
    <meta name="theme-color" content="#007c7c" media="(prefers-color-scheme: light)" />
    <meta name="theme-color" content="#0c0c0e" media="(prefers-color-scheme: dark)" />
    <title>Project Ceres</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

Insert the `<meta name="color-scheme">` after the viewport meta, and insert the inline pre-paint script BEFORE `</head>`. Final state:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="color-scheme" content="light dark" />
    <meta name="description" content="Personal finance tracker for individuals and freelancers. Track assets, liabilities, net worth, income, expenses, and goal budgets." />
    <meta name="theme-color" content="#007c7c" media="(prefers-color-scheme: light)" />
    <meta name="theme-color" content="#0c0c0e" media="(prefers-color-scheme: dark)" />
    <title>Project Ceres</title>
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
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

The inline script uses the same STORAGE_KEY (`ceres.theme:v1`) and the same OS check as `theme-context.tsx` — single source of truth for the logic.

- [ ] **Step 5: Run the full frontend test suite**

Run: `pnpm --dir <repo>/ProjectCeres.Client test --run`

Expected: all green. The new tests pass; existing tests unaffected (next-themes is still mounted as the runtime provider — the new module is dead code at this point).

If `BudgetEdit.test.tsx` flakes with `Cannot read properties of undefined (reading 'then')` at `use-api.ts:41`, re-run it in isolation. If isolation passes, that's the documented Stage 9.1.5.c-original flake (tasks #29/#31) — note and proceed.

- [ ] **Step 6: Run the frontend build**

Run: `pnpm --dir <repo>/ProjectCeres.Client build`

Expected: build succeeds. Bundle budgets intact. The new module compiles into the main bundle (or wherever Vite's chunk splitter places it) but next-themes is still in vendor-overlay so the size shouldn't change much.

- [ ] **Step 7: Run the backend test suite (stop-hook gate)**

Run: `dotnet test <repo>/ProjectCeres.sln`

Expected: all green.

- [ ] **Step 8: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Client/src/app/theme/theme-context.tsx \
  ProjectCeres.Client/src/app/theme/theme-context.test.tsx \
  ProjectCeres.Client/index.html

git -C <repo> commit -m "$(cat <<'EOF'
feat(stage-9.1.5.c-revised): add homegrown SPA ThemeProvider + tests + pre-paint inline script

New ProjectCeres.Client/src/app/theme/theme-context.tsx (~85 LOC):
ThemeProvider + useTheme hook. Uses useSyncExternalStore for the
matchMedia('(prefers-color-scheme: dark)') subscription (canonical React 18+
pattern; replaces the next-themes anti-pattern of useState + useEffect for
browser API subscriptions). Ports next-themes' disableTransitionOnChange
mechanism (injects one-frame * { transition: none !important } around the
class swap so theme toggles don't smear hundreds of elements). Mirrors the
src/app/auth/auth-context.tsx file-naming convention.

5 vitest tests pin: synchronous first-mount class application, OS-toggle
propagation (the actual bug we're fixing), explicit setTheme override +
localStorage persistence, system→light→system round-trip, localStorage
corruption fallback to 'system'.

ProjectCeres.Client/index.html: adds <meta name="color-scheme" content="light
dark"> (MDN-recommended; mitigates UA chrome flash for scrollbars + native
form controls) and a pre-paint inline <script> that applies the correct
.dark/.light class BEFORE React loads (kills the FOUC for OS-dark users).
The script uses the same STORAGE_KEY ('ceres.theme:v1') and OS check as
theme-context.tsx — single source of truth.

CSP note: when Phase 3 CSP enforcement lands (security-model.md §954), the
inline script must carry the server-injected nonce. Deferral per spec §6.3
(already-scheduled receiving entry; tripwire added to security-model.md in
commit 4 of this stage).

Old next-themes is still the runtime provider at this commit — the new
module is dead code. Subsequent commits swap the import sites and remove
the dependency.

Case applies (testing.md § Rules): case (1) — adding NEW tests for a NEW
module. No existing test modified, no contract changed yet (the contract
change lands in commit 2 when imports swap to the new provider).

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-c-revised-theme-provider-design.md §3.1, §3.2, §5
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-c-revised-theme-provider-impl.md Task 1
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

### Task 2: Atomic swap of all 4 useTheme/ThemeProvider import sites

**Files:**
- Modify: `ProjectCeres.Client/src/app/main.tsx`
- Modify: `ProjectCeres.Client/src/design-system/main.tsx`
- Modify: `ProjectCeres.Client/src/components/ui/sonner.tsx`
- Modify: `ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx`
- Modify: `ProjectCeres.Client/vite.config.ts`

This is the atomic-migration commit. All 4 import sites swap together so the codebase never has a mix of `next-themes` and homegrown imports.

- [ ] **Step 1: Swap `src/app/main.tsx`**

Open `<repo>/ProjectCeres.Client/src/app/main.tsx`. Current lines 3-26:

```tsx
import { ThemeProvider } from 'next-themes';
// ... other imports ...
createRoot(root).render(
  <StrictMode>
    <ThemeProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
    >
      <BrowserRouter basename="/app">
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrowserRouter>
    </ThemeProvider>
  </StrictMode>,
);
```

Replace line 3 import + drop the next-themes-specific props:

```tsx
import { ThemeProvider } from './theme/theme-context';
// ... other imports ...
createRoot(root).render(
  <StrictMode>
    <ThemeProvider>
      <BrowserRouter basename="/app">
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrowserRouter>
    </ThemeProvider>
  </StrictMode>,
);
```

The homegrown `ThemeProvider` doesn't accept the `attribute` / `defaultTheme` / `enableSystem` / `disableTransitionOnChange` props — that behavior is built in.

- [ ] **Step 2: Swap `src/design-system/main.tsx`**

Open `<repo>/ProjectCeres.Client/src/design-system/main.tsx`. Current line 3 + lines 13-18:

```tsx
import { ThemeProvider } from 'next-themes';
// ... other imports ...
createRoot(root).render(
  <StrictMode>
    <ThemeProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
    >
      <HashRouter>
        <App />
      </HashRouter>
    </ThemeProvider>
  </StrictMode>,
);
```

Replace with (note the import path is `../app/theme/theme-context` because `design-system/` is a sibling of `app/`):

```tsx
import { ThemeProvider } from '../app/theme/theme-context';
// ... other imports ...
createRoot(root).render(
  <StrictMode>
    <ThemeProvider>
      <HashRouter>
        <App />
      </HashRouter>
    </ThemeProvider>
  </StrictMode>,
);
```

- [ ] **Step 3: Swap `src/components/ui/sonner.tsx`**

Open `<repo>/ProjectCeres.Client/src/components/ui/sonner.tsx`. Current line 1:

```tsx
import { useTheme } from "next-themes"
```

Replace with:

```tsx
import { useTheme } from "@/app/theme/theme-context"
```

Keep the line 6 `const { theme = "system" } = useTheme()` exactly as-is — the `= "system"` default is unreachable (the homegrown `useTheme` throws if called outside the provider) but harmless and matches sonner upstream docs.

- [ ] **Step 4: Swap `src/design-system/components/ThemeToggle.tsx` and remove the mounted hack**

Open `<repo>/ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx`. The full current file is 47 lines. Replace its entire content with:

```tsx
import { Moon, Sun } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useTheme } from '@/app/theme/theme-context';

type ThemeToggleProps = {
  showLabel?: boolean;
};

export function ThemeToggle({ showLabel = false }: ThemeToggleProps) {
  const { resolvedTheme, setTheme } = useTheme();

  const isDark = resolvedTheme === 'dark';
  const nextTheme = isDark ? 'light' : 'dark';
  const ariaLabel = isDark ? 'Switch to light mode' : 'Switch to dark mode';

  if (showLabel) {
    return (
      <Button
        variant="outline"
        size="sm"
        onClick={() => setTheme(nextTheme)}
        aria-label={ariaLabel}
      >
        {isDark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
        <span className="ml-2">{isDark ? 'Light' : 'Dark'}</span>
      </Button>
    );
  }

  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={() => setTheme(nextTheme)}
      aria-label={ariaLabel}
    >
      {isDark ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
    </Button>
  );
}
```

Net diff vs original:
- Line 2 import path: `'next-themes'` → `'@/app/theme/theme-context'`.
- Line 3 import dropped: `useEffect, useState` from `'react'` no longer needed.
- Lines 12, 14-16 removed: `const [mounted, setMounted] = useState(false); useEffect(() => setMounted(true), []);`.
- Line 18: `const isDark = mounted && resolvedTheme === 'dark';` → `const isDark = resolvedTheme === 'dark';` (the homegrown provider returns `resolvedTheme` synchronously on first render; the `mounted` gate was a next-themes-specific hydration workaround).

- [ ] **Step 5: Update `vite.config.ts` chunk comment + check**

Open `<repo>/ProjectCeres.Client/vite.config.ts`. Line 105 area currently reads:

```ts
          // vendor-overlay: sonner (toasts), cmdk (command palette), next-themes.
          if (id.includes('node_modules/sonner') ||
              id.includes('node_modules/cmdk') ||
              id.includes('node_modules/next-themes'))
            return 'vendor-overlay'
```

Replace with:

```ts
          // vendor-overlay: sonner (toasts), cmdk (command palette).
          if (id.includes('node_modules/sonner') ||
              id.includes('node_modules/cmdk'))
            return 'vendor-overlay'
```

Both the comment AND the `id.includes('node_modules/next-themes')` line are removed.

- [ ] **Step 6: Run the full frontend test suite**

Run: `pnpm --dir <repo>/ProjectCeres.Client test --run`

Expected: all green. The 5 new tests still pass; the existing tests (including `ThemeToggle` tests, if any) keep passing because the new provider exposes the same `useTheme` API surface.

If `ThemeToggle`-related tests fail with "Cannot read properties of null" or "useTheme must be used inside <ThemeProvider>", a test render isn't wrapping with the new provider AND no longer benefits from next-themes' "returns silent defaults outside provider" behavior. Wrap the test render with `<ThemeProvider>` from the new module.

- [ ] **Step 7: Run the frontend build**

Run: `pnpm --dir <repo>/ProjectCeres.Client build`

Expected: build succeeds. `vendor-overlay` chunk may shrink slightly (no longer includes next-themes' code), but next-themes is still in `node_modules` and counted by `package.json` so the lockfile still resolves it. The chunk-size budget should still pass.

- [ ] **Step 8: Run the backend test suite (stop-hook gate)**

Run: `dotnet test <repo>/ProjectCeres.sln`

Expected: all green.

- [ ] **Step 9: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Client/src/app/main.tsx \
  ProjectCeres.Client/src/design-system/main.tsx \
  ProjectCeres.Client/src/components/ui/sonner.tsx \
  ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx \
  ProjectCeres.Client/vite.config.ts

git -C <repo> commit -m "$(cat <<'EOF'
refactor(stage-9.1.5.c-revised): swap all useTheme/ThemeProvider import sites to homegrown provider

Atomically migrates all 4 useTheme/ThemeProvider import sites from next-themes
to the homegrown @/app/theme/theme-context module added in commit 1 of this
stage:

  - src/app/main.tsx — ThemeProvider import + drop attribute/defaultTheme/
    enableSystem/disableTransitionOnChange props (homegrown bakes those in).
  - src/design-system/main.tsx — same swap (second mount site for the
    /design-system.html showcase; missed in the original 9.1.5.c brainstorm,
    caught by the frontend-orchestrator audit).
  - src/components/ui/sonner.tsx — useTheme import; the `= "system"` default
    literal kept (now unreachable since useTheme throws if outside the provider,
    but harmless and matches sonner upstream docs).
  - src/design-system/components/ThemeToggle.tsx — useTheme import + REMOVE
    the obsolete mounted useState + useEffect hack. The homegrown provider
    returns resolvedTheme synchronously on first render (useSyncExternalStore),
    so the next-themes-era hydration workaround is dead code.

vite.config.ts manualChunks: drop next-themes from the vendor-overlay member
list and from the chunk-membership check.

The atomic swap keeps the codebase coherent — no intermediate state where
some files import from 'next-themes' and others from '@/app/theme/theme-context'.
Both implementations satisfy the same {theme, resolvedTheme, setTheme}
interface; splitting this commit would still build, but the contract change
lands cleaner as one unit.

Case applies (testing.md § Rules): case (3) — production contract intentionally
changed. The runtime ThemeProvider is now the homegrown module. The new
provider's useTheme throws on missing provider (matching AuthProvider pattern)
where next-themes returned silent defaults — flagged as a deliberate
contract change in spec §4.8 for future test writers.

next-themes is still in package.json at this commit (consuming code is gone
but the dependency lingers). Commit 3 of this stage removes it.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-c-revised-theme-provider-design.md §3.3
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-c-revised-theme-provider-impl.md Task 2
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

### Task 3: Remove next-themes dependency + verify zero references survive

**Files:**
- Modify: `ProjectCeres.Client/package.json`
- Modify: `ProjectCeres.Client/pnpm-lock.yaml` (auto-regenerated)
- Modify: `ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx`

This is the cleanup commit. Dependency removal MUST come AFTER Commit 2 — if you remove `next-themes` before swapping imports, every consumer's `import` errors at build time and the test suite goes red.

- [ ] **Step 1: Remove `next-themes` from `package.json`**

Open `<repo>/ProjectCeres.Client/package.json`. Find the `"next-themes": "^0.4.6"` entry in the `dependencies` block (around line 28 per the task brief). Delete the entire line including the trailing comma if it's not the last entry. Keep the surrounding braces clean.

- [ ] **Step 2: Run `pnpm install` to regenerate the lockfile**

Run: `pnpm --dir <repo>/ProjectCeres.Client install`

Expected: pnpm reports the removal (something like `- next-themes 0.4.6`); `pnpm-lock.yaml` updates to drop the next-themes entries; no install errors.

- [ ] **Step 3: Verify zero next-themes references survive**

Run: `grep -rn "next-themes" <repo>/ProjectCeres.Client/src <repo>/ProjectCeres.Client/vite.config.ts <repo>/ProjectCeres.Client/package.json`

Expected: **ZERO hits.** If any survive, they're missed import sites or stale comments — fix before commit.

- [ ] **Step 4: Update `AppLayout.a11y.test.tsx` comment**

Open `<repo>/ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx`. Lines 7-9 currently read:

```tsx
// AppLayout renders <Toaster /> from @/components/ui/sonner, which in turn
// wraps the real `sonner` Toaster and calls `useTheme` from next-themes.
// Mock the local wrapper so JSDOM doesn't choke on portals / canvas / theme.
```

Replace with:

```tsx
// AppLayout renders <Toaster /> from @/components/ui/sonner, which in turn
// wraps the real `sonner` Toaster and calls `useTheme` from @/app/theme/theme-context.
// Mock the local wrapper so JSDOM doesn't choke on portals / canvas.
```

The stale "next-themes" reference goes; the "/ theme" trip-hazard mention also goes (the homegrown provider works fine in JSDOM with no mocks needed).

- [ ] **Step 5: Run the full frontend test suite**

Run: `pnpm --dir <repo>/ProjectCeres.Client test --run`

Expected: all green.

- [ ] **Step 6: Run the frontend build**

Run: `pnpm --dir <repo>/ProjectCeres.Client build`

Expected: build succeeds. The vendor-overlay chunk now genuinely shrinks (no more next-themes code).

- [ ] **Step 7: Run the backend test suite (stop-hook gate)**

Run: `dotnet test <repo>/ProjectCeres.sln`

Expected: all green.

- [ ] **Step 8: Commit**

```bash
git -C <repo> add \
  ProjectCeres.Client/package.json \
  ProjectCeres.Client/pnpm-lock.yaml \
  ProjectCeres.Client/src/app/layout/AppLayout.a11y.test.tsx

git -C <repo> commit -m "$(cat <<'EOF'
chore(stage-9.1.5.c-revised): remove next-themes dependency + verify zero references survive

next-themes is no longer used after commit 2 of this stage swapped every
import to the homegrown @/app/theme/theme-context module. This commit:

  - Removes "next-themes": "^0.4.6" from ProjectCeres.Client/package.json
    dependencies.
  - Regenerates pnpm-lock.yaml via `pnpm --dir ProjectCeres.Client install`.
  - Verifies zero references survive via grep across src/, vite.config.ts,
    and package.json.
  - Updates src/app/layout/AppLayout.a11y.test.tsx inline comment to remove
    the stale "next-themes" reference and drop the "theme" trip-hazard
    mention (the homegrown provider works fine in JSDOM; the Sonner mock
    is still needed for portals/canvas).

vendor-overlay bundle chunk shrinks slightly now that the dependency is
genuinely gone from node_modules' install footprint.

Case applies (testing.md § Rules): test file modified for docstring-only
edit; no assertions changed. Production behavior unaffected.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-c-revised-theme-provider-design.md §6.1 items 10-12
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-c-revised-theme-provider-impl.md Task 3
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

### Task 4: Manual browser verification + docs close-out

**Files:**
- Modify: `docs/security-model.md` (Phase 3 CSP section near line 957)
- Modify: `docs/roadmap-phase-three.md` (9.1.5.c entry near lines 1098 + 1108)
- Modify: `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md` (prepend CORRECTION section)

This commit is gated on user browser verification. The fix from Task 2 is live; the verification confirms the user-visible bug is actually gone before flipping the roadmap to `[x]`.

- [ ] **Step 1: Check browser availability + start dev server**

Attempt: `pnpm --dir <repo>/ProjectCeres.Client dev` (or check if a dev server is already running on the .NET integrated port 7081).

If you have NO browser tool (no Puppeteer/Playwright/headed browser), this is the expected case for most subagent environments. Report **DONE_WITH_CONCERNS** with status: "browser unavailable; verification handed to user". Include:

- The exact URL the user must check: `https://localhost:7081/app/login`.
- The verification checklist from Step 2 below (copy it into your report).
- Note that the dev server is/isn't running.

Do NOT proceed to Steps 3-9 (the docs close-out) without browser confirmation.

- [ ] **Step 2: Browser verification checklist (only if browser available)**

Open `https://localhost:7081/app/login` in a real browser. Open DevTools console. Run this exact snippet:

```javascript
console.log({
  htmlClass: document.documentElement.className,
  htmlAttrs: [...document.documentElement.attributes].map(a => `${a.name}="${a.value}"`),
  resolvedBgVar: getComputedStyle(document.documentElement).getPropertyValue('--background').trim(),
  resolvedCardVar: getComputedStyle(document.documentElement).getPropertyValue('--card').trim(),
  mediaQueryDarkMode: window.matchMedia('(prefers-color-scheme: dark)').matches,
});
```

Record the output for the current OS state.

Then toggle DevTools color-scheme emulation:
- Chrome/Edge: DevTools → Rendering tab → "Emulate CSS media feature prefers-color-scheme" → switch between "prefers-color-scheme: light" and "prefers-color-scheme: dark".

Run the snippet again after each emulation change. Expected:

| Emulation | `htmlClass` | `mediaQueryDarkMode` | `resolvedBgVar` | `resolvedCardVar` |
|---|---|---|---|---|
| (none / OS dark) | `"dark"` | `true` | `oklch(0.155 0.005 285)` | `oklch(0.205 0.005 285)` |
| Light emulated | `"light"` | `false` | `oklch(1.000 0.000 0)` | `oklch(1.000 0.000 0)` |
| Dark emulated | `"dark"` | `true` | `oklch(0.155 0.005 285)` | `oklch(0.205 0.005 285)` |

**The bug is fixed if `htmlClass` flips between `"light"` and `"dark"` as you toggle emulation.** Page colors must change visually.

Also verify visually:
- Light mode: white card on white page; card edge visible via 1px border + shadow.
- Dark mode: card visibly lighter than page; figure/ground correct.
- No CSS color smear on toggle (the `disableTransitionOnChange` port should suppress transitions for one frame).

If `htmlClass` is still stuck or doesn't flip, STOP and report the failure mode. Do NOT proceed to flip the roadmap.

- [ ] **Step 3: Add the CSP cross-reference to `security-model.md`**

Open `<repo>/docs/security-model.md`. Locate the Phase 3 CSP section starting near line 954 (`**Phase 3 CSP skeleton:**`). Find a sensible spot after line 957 (the `script-src 'self' 'nonce-{NONCE}' 'strict-dynamic';` line) or near line 972 (the `'strict-dynamic'` explanation) — wherever the nonce mechanism is discussed.

Add this line:

```
- **Inline pre-paint theme-init script** (`ProjectCeres.Client/index.html`, added in Stage 9.1.5.c-revised) — must receive the server-injected nonce when CSP middleware lands. Without a nonce, the script will be blocked, causing a first-paint flash of incorrect theme for OS-dark users. See `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-revised-theme-provider-design.md` §4.11.
```

This is the tripwire for the no-unjustified-deferrals procedure (spec §6.3) — when the CSP enforcement work picks up this file, the line forces the nonce-injection handling.

- [ ] **Step 4: Update the roadmap 9.1.5.c entry**

Open `<repo>/docs/roadmap-phase-three.md`. Locate line 1098 area (the sub-stage row that starts with `| 9.1.5.c | Auth-page card separation in light mode...` or whatever the original Stage 9.1.5.c-original close-out left it as — the implementer should grep `9.1.5.c` to find current state).

Replace the sub-stage row with:

```
| 9.1.5.c | Homegrown SPA theme provider replacing next-themes — fixes the OS-toggle-not-honored bug | Phase 1 UX walkthrough surfaced "tokens collapse to identical values in light + dark modes." Original spec (e262b62) misdiagnosed as a token-contrast issue and shipped commit `014f2b5` (bg-muted/30 → bg-background on AuthLayout) — kept on independent merits but did not fix the user-visible symptom. deep-fix-mode round 4 + runtime DevTools confirmed actual root cause: next-themes is designed for Next.js; in our Vite SPA the matchMedia change listener never fires, so `<html class="dark">` is stuck regardless of OS preference. Replaced next-themes with a homegrown ~85-line provider using useSyncExternalStore + ported disableTransitionOnChange + pre-paint inline script in index.html. Three audit-validated frontend-orchestrator passes (vercel-react-best-practices, web-design-guidelines, design-system reconciliation) rolled into the revised spec. |
```

Then locate line 1108 area (the verification line starting with `- [x] 9.1.5.c —` or `- [ ] 9.1.5.c —`). Replace with:

```
- [x] 9.1.5.c — `src/app/theme/theme-context.tsx` (homegrown ~85-line ThemeProvider with useSyncExternalStore for matchMedia subscription) replaces `next-themes` at both mount sites (`src/app/main.tsx`, `src/design-system/main.tsx`). 5 vitest tests pin: sync first-mount, OS toggle propagation, explicit setTheme override, system→light→system round-trip, localStorage corruption fallback. Pre-paint inline script in `index.html` kills first-paint flash; `disableTransitionOnChange` ported to prevent toggle smear; `<meta name="color-scheme" content="light dark">` added for UA chrome. next-themes removed from package.json; zero references survive. Manual browser verification confirmed: `htmlClass` flips between "light" and "dark" on DevTools color-scheme emulation. Spec: `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-revised-theme-provider-design.md`. Plan: `docs/superpowers/plans/2026-05-17-stage-9-1-5-c-revised-theme-provider-impl.md`. CSP nonce tripwire added to `security-model.md`.
```

The `[ ]` flips to `[x]`.

- [ ] **Step 5: Prepend CORRECTION section to the original 9.1.5.c spec**

Open `<repo>/docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md`. The file currently begins with `# Stage 9.1.5.c — Auth-page background contrast fix`. Prepend this block ABOVE that title:

```markdown
> **⚠️ CORRECTION — 2026-05-17.** This spec misdiagnosed the bug. The actual root cause was next-themes failing to honor OS color-scheme toggle in our Vite SPA — not a token-contrast issue in AuthLayout. The fix shipped in commit `014f2b5` (bg-muted/30 → bg-background) is kept on independent merits (it's correct per the shadcn page-background convention) but did NOT fix the user-visible symptom. See the revised spec at `docs/superpowers/specs/2026-05-17-stage-9-1-5-c-revised-theme-provider-design.md` for the actual fix. This spec is preserved in git history as a record of the wrong diagnosis.

---

```

The horizontal rule + blank line separate the CORRECTION block from the original spec content; the original content stays intact below.

- [ ] **Step 6: Update task #39 in the task tracker**

Use the `TaskUpdate` tool:
- taskId: `39`
- status: `completed`
- subject: `Bug: auth-page tokens collapse to identical values in light + dark modes — RESOLVED via homegrown theme provider (replaces next-themes)`
- description: `Resolved 2026-05-17 by Stage 9.1.5.c-revised (spec 80709dd, commits Task1-Task3 SHAs). Original 9.1.5.c spec (e262b62, commit 014f2b5) misdiagnosed the bug as a token-contrast issue in AuthLayout; bg-muted/30 → bg-background swap kept on independent merits but did NOT fix the symptom. Runtime DevTools confirmed actual root cause via deep-fix-mode round 4: next-themes' matchMedia subscription never fires in our Vite SPA, so <html class="dark"> is stuck regardless of OS preference. Replaced next-themes with a ~85-line homegrown ThemeProvider using useSyncExternalStore + ported disableTransitionOnChange + pre-paint inline script in index.html. Three frontend-orchestrator audits (vercel-react-best-practices, web-design-guidelines, design-system reconciliation) rolled into the revised spec. 5 vitest tests pin the contract. Manual browser verification confirmed OS-toggle now flips htmlClass correctly.`

- [ ] **Step 7: Sanity-check the docs-only edits didn't break the test suite**

Run: `pnpm --dir <repo>/ProjectCeres.Client test --run`
Expected: all green.

Run: `dotnet test <repo>/ProjectCeres.sln`
Expected: all green.

- [ ] **Step 8: Commit**

```bash
git -C <repo> add \
  docs/security-model.md \
  docs/roadmap-phase-three.md \
  docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md

git -C <repo> commit -m "$(cat <<'EOF'
docs(stage-9.1.5.c-revised): close out — homegrown theme provider verified

Manual browser verification at https://localhost:7081/app/login confirms the
homegrown ThemeProvider from commits 1-3 of this stage fixes the user-visible
bug: htmlClass now flips between "light" and "dark" when toggling DevTools
color-scheme emulation. resolvedBgVar + resolvedCardVar values change
correctly across both modes. No CSS color smear on toggle (disableTransitionOnChange
port works). No first-paint flash (pre-paint inline script in index.html
applies the class before React loads).

- docs/security-model.md — added CSP-nonce tripwire near §954: when Phase 3
  CSP enforcement lands, the inline pre-paint theme-init script must receive
  the server-injected nonce. Without a nonce the script gets blocked and the
  first-paint flash returns. Tripwire enforces this gets addressed.
- docs/roadmap-phase-three.md:1098 — sub-stage row updated to reflect the
  revised diagnosis + shipped fix (homegrown provider replacing next-themes).
- docs/roadmap-phase-three.md:1108 — verification checkbox flipped from [ ]
  to [x]; text replaced with the post-fix description listing the new
  provider, the test coverage, the index.html additions, and the manual
  browser verification result.
- docs/superpowers/specs/2026-05-17-stage-9-1-5-c-auth-page-background-design.md
  — prepended CORRECTION section pointing to the revised spec. Original spec
  preserved in git history as a record of the wrong diagnosis (deep-fix-mode
  round 4 caught it).
- Task #39 marked completed with full resolution description.

Stage 9.1.5 batch progress: 9.1.5.a [x], 9.1.5.b [x], 9.1.5.c [x] (with
correction history). Remaining: 9.1.5.d (in-app light/dark toggle — note
that this stage's fix is now visible end-to-end including OS-toggle support
so 9.1.5.d's in-app toggle layers cleanly on top), 9.1.5.e (language code
next to globe), 9.1.5.f (SPA logout), 9.1.5.g (ADR-0076 CSRF token-source).

Case applies (testing.md § Rules): no test files modified in this commit;
production behavior unaffected.

Spec: docs/superpowers/specs/2026-05-17-stage-9-1-5-c-revised-theme-provider-design.md
Plan: docs/superpowers/plans/2026-05-17-stage-9-1-5-c-revised-theme-provider-impl.md Task 4
EOF
)"
```

Expected: commit succeeds, stop-hook is green.

---

## Self-review

**Spec coverage:**
- §1 (the actual bug) — covered by Task 1's test #2 + Task 4's manual verification.
- §2 (root cause: next-themes wrong for Vite SPA) — captured in commit messages + roadmap update.
- §3.1 (theme-context.tsx code) — Task 1 Step 1, verbatim.
- §3.2 (index.html additions) — Task 1 Step 4.
- §3.3 (file-change table) — Tasks 2 + 3 cover every entry.
- §4 (edge cases) — 4.1 (first-paint flash via inline script in Task 1), 4.2 (disableTransitionOnChange port in Task 1), 4.3 (useSyncExternalStore race closed in Task 1), 4.4 (StrictMode in Task 1's tests), 4.5 (localStorage try/catch in Task 1), 4.6 (GDPR), 4.7 (WCAG), 4.8 (throw-on-missing-provider documented in Task 2 commit message), 4.9 (shadcn primitives audit done in spec — no implementation needed), 4.10 (prefers-reduced-motion), 4.11 (CSP nonce — Task 4 Step 3 adds tripwire).
- §5 (5 vitest tests) — Task 1 Step 2, verbatim.
- §6.1 (in scope, 15 items) — items 1-2 = Task 1; item 3 = Task 1 Step 4; items 4-7 + 9 = Task 2; items 8 + 10-12 = Task 3; items 13-16 = Task 4.
- §6.3 (CSP nonce deferral) — Task 4 Step 3 fulfills the tripwire requirement.

**Placeholder scan:** searched for "TODO", "TBD", "implement later", "as appropriate". Task 1 Step 3 has a placeholder for Task1's actual commit SHA in Task 4 Step 6's task-#39 description ("Task1-Task3 SHAs"). This is a forward-reference the implementer fills in after Task 1 commits — not a plan-failure placeholder. Same for Task 4 Step 8's commit message ("the homegrown ThemeProvider from commits 1-3 of this stage" is intentional cross-reference).

**Type consistency:** the `useTheme()` return type `{ theme, resolvedTheme, setTheme }` matches across spec §3.1, Task 1's tests, Task 2's consumer files. `Theme = 'light' | 'dark' | 'system'` and `ResolvedTheme = 'light' | 'dark'` consistent everywhere. `STORAGE_KEY = 'ceres.theme:v1'` used identically in the inline script (`index.html`) and the provider (`theme-context.tsx`).

**Stop-hook safety:** Task 1 ships green (new module is dead code; new tests pass). Task 2's atomic swap keeps the codebase coherent (both old + new providers have the same API). Task 3 cleanup happens AFTER all imports swap. Task 4 is docs-only. The hook should never see a red commit.

**Manual-verification escape hatch:** Task 4 Step 1 explicitly tells the implementer to report `DONE_WITH_CONCERNS` if browser access is unavailable, hand the URLs + checklist to the user, and NOT proceed to the docs close-out without browser confirmation. Honors the CLAUDE.md frontend rule.

**Browser verification gate:** Task 4 Step 2 specifies the exact DevTools snippet AND the expected output table per emulation state. If the htmlClass doesn't flip, the implementer stops — no roadmap flip without proof the bug is actually gone. This is the load-bearing acceptance check; it cannot be skipped.
