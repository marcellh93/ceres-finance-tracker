import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '@/app/i18n/i18n';
import { MobileDrawer } from './MobileDrawer';
import { ThemeProvider } from '@/app/theme/theme-context';

beforeEach(() => {
  void i18n.changeLanguage('en');
});

function renderDrawer(open = true, onOpenChange = vi.fn()) {
  return {
    onOpenChange,
    ...render(
      <ThemeProvider>
        <I18nextProvider i18n={i18n}>
          <MemoryRouter>
            <MobileDrawer open={open} onOpenChange={onOpenChange} />
          </MemoryRouter>
        </I18nextProvider>
      </ThemeProvider>,
    ),
  };
}

describe('MobileDrawer', () => {
  it('renders all nav items including bottom-pinned ones', () => {
    renderDrawer(true);
    expect(screen.getByRole('link', { name: 'Movements' })).toBeDefined();
    expect(screen.getByRole('link', { name: 'Settings' })).toBeDefined();
    expect(screen.getByRole('link', { name: 'Support' })).toBeDefined();
  });

  it('clicking a nav item calls onOpenChange(false)', async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDrawer(true);
    await user.click(screen.getByRole('link', { name: 'Movements' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('does not render nav items when closed', () => {
    renderDrawer(false);
    expect(screen.queryByRole('link', { name: 'Movements' })).toBeNull();
  });

  it('nav region scrolls when content exceeds viewport height', () => {
    // Regression guard for Stage 9.1.5.d: adding ThemeToggle to the drawer
    // bottom pushed the total content past the viewport on short mobile
    // screens. Sheet height-bounds its content; the inner <nav> must be
    // flex-1 + overflow-y-auto so the bottom items + ThemeToggle remain
    // reachable via scroll.
    renderDrawer(true);
    const nav = screen.getByRole('navigation', { name: 'Primary' });
    expect(nav.className).toContain('flex-1');
    expect(nav.className).toContain('overflow-y-auto');
  });

  it('renders the theme row as a segmented control with 3 direct-select options', async () => {
    // Regression guard for Stage 9.1.5.d UX fix: dropdown was rejected on
    // mobile because popping above the trigger covered Settings/Support
    // (mobile vertical pixels are scarce). Replaced with a segmented control
    // — 1-tap direct selection, no portal, no positioning bugs. The icon-only
    // dropdown variant of ThemeToggle is still used in TopBar (desktop) and
    // AuthLayout where toolbar real estate forbids 3 inline buttons.
    renderDrawer(true);
    const group = screen.getByRole('radiogroup', { name: /toggle theme/i });
    const radios = within(group).getAllByRole('radio');
    expect(radios).toHaveLength(3);
    expect(radios.map((r) => r.textContent)).toEqual(['System', 'Light', 'Dark']);
    // Default theme is 'system' — that option is checked.
    expect(radios[0].getAttribute('aria-checked')).toBe('true');
    expect(radios[1].getAttribute('aria-checked')).toBe('false');
    expect(radios[2].getAttribute('aria-checked')).toBe('false');
  });

  it('clicking a segmented theme option flips the theme', async () => {
    // Direct-select replacement for the prior dropdown click flow.
    const user = userEvent.setup();
    renderDrawer(true);
    const group = screen.getByRole('radiogroup', { name: /toggle theme/i });
    await user.click(within(group).getByRole('radio', { name: 'Light' }));
    // After click, Light is checked; System is not.
    const radios = within(group).getAllByRole('radio');
    expect(radios.find((r) => r.textContent === 'Light')?.getAttribute('aria-checked')).toBe('true');
    expect(radios.find((r) => r.textContent === 'System')?.getAttribute('aria-checked')).toBe('false');
  });
});
