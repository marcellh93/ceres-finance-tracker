import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RecurringLayout } from './RecurringLayout';

function renderLayout(path = '/recurring') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="recurring" element={<RecurringLayout />}>
          <Route path="new" element={<div>Create form</div>} />
          <Route path=":id/edit" element={<div>Edit form</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  );
}

describe('RecurringLayout', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('shows skeleton while loading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation(() => new Promise(() => {}));
    renderLayout();
    expect(screen.getByTestId('recurring-skeleton')).toBeInTheDocument();
  });

  it('renders h1 "Recurring transactions"', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderLayout();
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Recurring transactions' })).toBeInTheDocument()
    );
  });

  it('renders first-run empty state when no reminders exist', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderLayout();
    await waitFor(() => expect(screen.getByText(/No recurring reminders yet/i)).toBeInTheDocument());
  });

  it('renders child route when on /recurring/new (hides list chrome)', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderLayout('/recurring/new');
    await waitFor(() => expect(screen.getByText('Create form')).toBeInTheDocument());
    expect(screen.queryByRole('heading', { name: 'Recurring transactions' })).not.toBeInTheDocument();
  });

  it('filters rows by search term', async () => {
    // First call (list), second call (allList) — both return a set of reminders
    const items = [
      { id: 'r1', name: 'Rent', estimatedAmount: null, accountId: 'a', accountName: 'A',
        currencySymbol: '€', categoryId: 'c', categoryName: 'C', categoryTypeName: 'Expense',
        frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-15',
        isActive: true, reminderBehaviour: 'SnapToCalendarDay' },
      { id: 'r2', name: 'Salary', estimatedAmount: null, accountId: 'a', accountName: 'A',
        currencySymbol: '€', categoryId: 'c', categoryName: 'C', categoryTypeName: 'Income',
        frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-01',
        isActive: true, reminderBehaviour: 'SnapToCalendarDay' },
    ];
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => items });
    renderLayout('/recurring?q=rent');
    await waitFor(() => expect(screen.getByText('Rent')).toBeInTheDocument());
    expect(screen.queryByText('Salary')).not.toBeInTheDocument();
  });

  it('shows no-match state with clear button when search has no results', async () => {
    const items = [
      { id: 'r1', name: 'Rent', estimatedAmount: null, accountId: 'a', accountName: 'A',
        currencySymbol: '€', categoryId: 'c', categoryName: 'C', categoryTypeName: 'Expense',
        frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-15',
        isActive: true, reminderBehaviour: 'SnapToCalendarDay' },
    ];
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => items });
    renderLayout('/recurring?q=xyz');
    await waitFor(() => expect(screen.getByText(/No reminders match/i)).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /clear search/i })).toBeInTheDocument();
  });

  it('shows "No active reminders." when list is empty but allList has items', async () => {
    // list returns empty (active only), allList returns the archived item
    const archived = [
      { id: 'r1', name: 'Old Bill', estimatedAmount: null, accountId: 'a', accountName: 'A',
        currencySymbol: '€', categoryId: 'c', categoryName: 'C', categoryTypeName: 'Expense',
        frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-04-01',
        isActive: false, reminderBehaviour: 'SnapToCalendarDay' },
    ];
    let callCount = 0;
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation(() => {
      callCount++;
      // First call = active-only list (empty), second call = all list (has archived item)
      const data = callCount === 1 ? [] : archived;
      return Promise.resolve({ ok: true, json: async () => data });
    });
    renderLayout();
    await waitFor(() => expect(screen.getByText('No active reminders.')).toBeInTheDocument());
  });

  it('shows error state when fetch fails', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: false, status: 500, json: async () => ({}) });
    renderLayout();
    await waitFor(() => expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument());
  });
});
