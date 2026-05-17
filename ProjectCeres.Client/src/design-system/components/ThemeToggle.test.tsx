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
  // matchMedia stub required because theme-context reads window.matchMedia on mount
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
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
    // Lucide icons render as <svg> with class 'lucide lucide-sun' (or similar — the icon name
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
