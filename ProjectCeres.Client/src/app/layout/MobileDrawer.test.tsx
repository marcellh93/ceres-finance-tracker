import { render, screen } from '@testing-library/react';
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

  it('renders the theme row as a full-width DrawerLink-style row, not a floating button', async () => {
    // Regression guard for Stage 9.1.5.d UX fix: the drawer's theme control
    // now matches the Settings/Support row shape (icon-left, label, current
    // preference right-aligned) rather than a floating outline button.
    renderDrawer(true);
    const trigger = screen.getByRole('button', { name: /toggle theme/i });
    expect(trigger.className).toContain('w-full');
    expect(trigger.textContent).toContain('Theme');
    // Current preference is surfaced on the trigger (default = system).
    expect(trigger.textContent).toContain('System');
  });
});
