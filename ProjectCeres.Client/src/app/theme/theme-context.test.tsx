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
