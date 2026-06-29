import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { SIDEBAR_STORAGE_KEY } from '../lib/sidebar-storage';

function renderSidebar(initialPath: string = '/movements') {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Sidebar />
    </MemoryRouter>,
  );
}

describe('Sidebar', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('renders all nav items in three groups + two bottom-pinned items', () => {
    renderSidebar();
    const expectedLabels = [
      // Activity
      'Movements',
      // Money
      'Accounts', 'Categories', 'Budgets',
      // Tools
      'Recurring Transactions', 'Reports',
      // Bottom-pinned
      'Settings', 'Support',
    ];
    for (const label of expectedLabels) {
      expect(screen.getByRole('link', { name: label })).toBeDefined();
    }
  });

  it('does not render shelved Import or Review nav items', () => {
    renderSidebar();
    expect(screen.queryByRole('link', { name: /import/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /review/i })).not.toBeInTheDocument();
  });

  it('renders the three group headings in order', () => {
    renderSidebar();
    const headings = screen.getAllByRole('heading', { level: 2 });
    expect(headings.map((h) => h.textContent?.trim())).toEqual([
      'Activity',
      'Money',
      'Tools',
    ]);
  });

  it('marks the active route with aria-current="page"', () => {
    renderSidebar('/movements');
    const active = screen.getByRole('link', { name: 'Movements' });
    expect(active.getAttribute('aria-current')).toBe('page');
    const inactive = screen.getByRole('link', { name: 'Accounts' });
    expect(inactive.getAttribute('aria-current')).toBeNull();
  });

  it('collapse toggle has aria-expanded reflecting the current state', async () => {
    const user = userEvent.setup();
    renderSidebar();
    const toggle = screen.getByRole('button', { name: /collapse sidebar/i });
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    await user.click(toggle);
    const expanded = await screen.findByRole('button', { name: /expand sidebar/i });
    expect(expanded.getAttribute('aria-expanded')).toBe('false');
  });

  it('persists the collapse choice to localStorage', async () => {
    const user = userEvent.setup();
    renderSidebar();
    await user.click(screen.getByRole('button', { name: /collapse sidebar/i }));
    expect(localStorage.getItem(SIDEBAR_STORAGE_KEY)).toBe('true');
  });
});
