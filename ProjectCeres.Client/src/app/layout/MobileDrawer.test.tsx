import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { MobileDrawer } from './MobileDrawer';

function renderDrawer(open = true, onOpenChange = vi.fn()) {
  return {
    onOpenChange,
    ...render(
      <MemoryRouter>
        <MobileDrawer open={open} onOpenChange={onOpenChange} />
      </MemoryRouter>,
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
});
