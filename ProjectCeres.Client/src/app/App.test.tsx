import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { App } from './App';

const routes: Array<{ path: string; expectedHeading: string }> = [
  { path: '/',             expectedHeading: 'Dashboard' },
  { path: '/movements',    expectedHeading: 'Movements' },
  { path: '/transactions', expectedHeading: 'Transactions' },
  { path: '/transfers',    expectedHeading: 'Transfers' },
  { path: '/review',       expectedHeading: 'Review' },
  { path: '/accounts',     expectedHeading: 'Accounts' },
  { path: '/categories',   expectedHeading: 'Categories' },
  { path: '/budgets',      expectedHeading: 'Budgets' },
  { path: '/recurring',    expectedHeading: 'Recurring Transactions' },
  { path: '/import',       expectedHeading: 'Import' },
  { path: '/reports',      expectedHeading: 'Reports' },
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

  it('renders the Movement Create page at /movements/new', () => {
    render(
      <MemoryRouter initialEntries={['/movements/new']}>
        <App />
      </MemoryRouter>,
    );
    // The type-picker is the landing state for /movements/new (no ?type= query)
    expect(screen.getByRole('button', { name: /transaction/i })).toBeDefined();
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
