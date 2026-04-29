import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { AvatarMenu } from './AvatarMenu';

function renderMenu() {
  return render(
    <MemoryRouter>
      <AvatarMenu />
    </MemoryRouter>,
  );
}

describe('AvatarMenu', () => {
  it('shows nothing in the menu before the trigger is clicked', () => {
    renderMenu();
    expect(screen.queryByRole('menuitem', { name: 'Profile' })).toBeNull();
  });

  it('renders Profile, Security, and Logout in that order when opened', async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole('button', { name: /open user menu/i }));
    const items = await screen.findAllByRole('menuitem');
    expect(items.map((i) => i.textContent?.trim())).toEqual([
      'Profile',
      'Security',
      'Logout',
    ]);
  });

  it('clicking Logout opens the placeholder dialog rather than navigating', async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole('button', { name: /open user menu/i }));
    await user.click(await screen.findByRole('menuitem', { name: 'Logout' }));
    expect(
      await screen.findByText(/Logout will end your session/i),
    ).toBeDefined();
  });
});
