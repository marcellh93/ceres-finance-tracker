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
// Synchronous on first render; subscribes to change events on a fresh MediaQueryList.
// Replaces the next-themes anti-pattern of useState + useEffect for matchMedia
// (per vercel-react-best-practices and React docs' canonical matchMedia example).
// Note: matchMedia is invoked per-call (not module-cached) so tests can swap
// window.matchMedia in beforeEach without the module holding a stale reference.
function getMql(): MediaQueryList | null {
  return typeof window !== 'undefined'
    ? window.matchMedia('(prefers-color-scheme: dark)')
    : null;
}

function subscribeOsDark(callback: () => void): () => void {
  const mql = getMql();
  if (!mql) return () => {};
  mql.addEventListener('change', callback);
  return () => mql.removeEventListener('change', callback);
}

function getOsDarkSnapshot(): boolean {
  return getMql()?.matches ?? false;
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
