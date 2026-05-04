import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { App } from './App';

const routes: Array<{ path: string; expectedHeading: string }> = [
  { path: '/',             expectedHeading: 'Dashboard' },
  { path: '/movements',    expectedHeading: 'Movements' },
  { path: '/review',       expectedHeading: 'Review' },
  { path: '/accounts',     expectedHeading: 'Accounts' },
  { path: '/categories',   expectedHeading: 'Categories' },
  { path: '/budgets',      expectedHeading: 'Budgets' },
  { path: '/import',       expectedHeading: 'Import' },
  { path: '/reports/net-worth-over-time', expectedHeading: 'Net Worth Over Time' },
  { path: '/settings',     expectedHeading: 'Settings' },
  { path: '/support',      expectedHeading: 'Support' },
  { path: '/profile',      expectedHeading: 'Profile' },
  { path: '/security',     expectedHeading: 'Security' },
  { path: '/this-route-does-not-exist', expectedHeading: 'Page not found' },
];

describe('App routes', () => {
  for (const { path, expectedHeading } of routes) {
    it(`renders the heading "${expectedHeading}" at ${path}`, () => {
      render(
        <MemoryRouter initialEntries={[path]}>
          <App />
        </MemoryRouter>,
      );
      const heading = screen.getByRole('heading', { level: 1, name: expectedHeading });
      expect(heading).toBeDefined();
    });
  }

  it('renders recurring list page at /recurring', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [] });
    render(
      <MemoryRouter initialEntries={['/recurring']}>
        <App />
      </MemoryRouter>,
    );
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Recurring transactions' })).toBeInTheDocument()
    );
  });

  it('renders the Movement Create page at /movements/new?type=transaction', () => {
    render(
      <MemoryRouter initialEntries={['/movements/new?type=transaction']}>
        <App />
      </MemoryRouter>,
    );
    // MovementCreate renders the form when ?type= is set; Save is the form's primary action.
    expect(screen.getByRole('button', { name: /save/i })).toBeDefined();
  });

  it('renders the Movement Edit page at /movements/:id/edit', () => {
    render(
      <MemoryRouter initialEntries={['/movements/some-id/edit']}>
        <App />
      </MemoryRouter>,
    );
    // MovementEdit fetches the discriminator on mount; the loading state renders first
    expect(screen.getByText(/loading…/i)).toBeDefined();
  });
});
