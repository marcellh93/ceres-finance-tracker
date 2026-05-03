import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TopBar } from './TopBar';
import { ReminderCountProvider } from './ReminderCountProvider';

beforeEach(() => {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: true, // pretend we're on desktop
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => true,
    }),
  });
  // ReminderCountProvider fetches on mount — provide a default empty response
  global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [] });
});

function renderTopBar(route: string) {
  return render(
    <MemoryRouter initialEntries={[route]}>
      <ReminderCountProvider>
        <TopBar onMenuClick={vi.fn()} />
      </ReminderCountProvider>
    </MemoryRouter>,
  );
}

describe('TopBar quick-add suppression', () => {
  it('renders the Quick add button on the dashboard route', () => {
    renderTopBar('/');
    expect(screen.getByRole('button', { name: /quick add/i })).toBeInTheDocument();
  });

  it('hides the Quick add button on /movements', () => {
    renderTopBar('/movements');
    expect(screen.queryByRole('button', { name: /quick add/i })).toBeNull();
  });

  it('hides the Quick add button on /movements/new', () => {
    renderTopBar('/movements/new');
    expect(screen.queryByRole('button', { name: /quick add/i })).toBeNull();
  });

  it('hides the Quick add button on /movements/abc/edit', () => {
    renderTopBar('/movements/abc/edit');
    expect(screen.queryByRole('button', { name: /quick add/i })).toBeNull();
  });
});

describe('TopBar — bell badge', () => {
  function renderWithCount(count: number) {
    const items = Array.from({ length: count }, (_, i) => ({
      id: String(i), name: `R${i}`, estimatedAmount: null, accountId: 'a', accountName: 'A',
      currencySymbol: '€', categoryId: 'c', categoryName: 'C', categoryTypeName: 'Expense',
      frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-03',
      isActive: true, reminderBehaviour: 'SnapToCalendarDay',
    }));
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => items });
    renderTopBar('/');
  }

  it('shows no badge when count=0', async () => {
    renderWithCount(0);
    await waitFor(() =>
      expect(screen.queryByText(/^\d+$|^9\+$/)).not.toBeInTheDocument()
    );
  });

  it('shows badge count when count > 0', async () => {
    renderWithCount(3);
    await waitFor(() => expect(screen.getByText('3')).toBeInTheDocument());
  });

  it('shows 9+ when count > 9', async () => {
    renderWithCount(12);
    await waitFor(() => expect(screen.getByText('9+')).toBeInTheDocument());
  });
});
