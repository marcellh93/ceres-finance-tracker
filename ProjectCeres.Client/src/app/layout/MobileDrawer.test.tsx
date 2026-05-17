import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { MobileDrawer } from './MobileDrawer';
import { ThemeProvider } from '@/app/theme/theme-context';

function renderDrawer(open = true, onOpenChange = vi.fn()) {
  return {
    onOpenChange,
    ...render(
      <ThemeProvider>
        <MemoryRouter>
          <MobileDrawer open={open} onOpenChange={onOpenChange} />
        </MemoryRouter>
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
});
