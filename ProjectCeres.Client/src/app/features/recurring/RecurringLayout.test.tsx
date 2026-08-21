import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RecurringLayout } from './RecurringLayout';
import { ReminderCountProvider } from '../../layout/ReminderCountProvider';
import { primeCsrfToken } from '../../../test/csrf-fetch-mock';

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

function renderLayoutWithBell(path = '/recurring') {
  return render(
    <ReminderCountProvider>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="recurring" element={<RecurringLayout />}>
            <Route path="new" element={<div>Create form</div>} />
            <Route path=":id/edit" element={<div>Edit form</div>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </ReminderCountProvider>
  );
}

describe('RecurringLayout', () => {
  beforeEach(async () => {
    await primeCsrfToken(); global.fetch = vi.fn(); });

  it('shows skeleton after the delay window when loading is slow', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation(() => new Promise(() => {}));
    renderLayout();
    expect(screen.queryByTestId('recurring-skeleton')).toBeNull();
    await waitFor(
      () => expect(screen.getByTestId('recurring-skeleton')).toBeInTheDocument(),
      { timeout: 500 },
    );
  });

  it('renders h1 "Recurring transactions"', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => [] });
    renderLayout();
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Recurring transactions' })).toBeInTheDocument()
    );
  });

  it('renders first-run empty state when no reminders exist', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => [] });
    renderLayout();
    await waitFor(() => expect(screen.getByText(/No recurring reminders yet/i)).toBeInTheDocument());
  });

  it('renders child route when on /recurring/new (hides list chrome)', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => [] });
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
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => items });
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
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => items });
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
      return Promise.resolve({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => data });
    });
    renderLayout();
    await waitFor(() => expect(screen.getByText('No active reminders.')).toBeInTheDocument());
  });

  it('shows error state when fetch fails', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: false, status: 500, json: async () => ({}) });
    renderLayout();
    await waitFor(() => expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument());
  });

  it('refetches the bell reminder count after a row Confirm completes', async () => {
    const item = {
      id: 'r1', name: 'Rent', estimatedAmount: 900, accountId: 'a', accountName: 'Checking',
      currencySymbol: '€', categoryId: 'c', categoryName: 'Housing', categoryTypeName: 'Expense',
      frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-15',
      isActive: true, reminderBehaviour: 'SnapToCalendarDay',
    };
    const fetchMock = vi.fn().mockImplementation((url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/confirm') && init?.method === 'POST') {
        return Promise.resolve({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  status: 201, ok: true, json: async () => ({ transactionId: 'tx-1' }) });
      }
      return Promise.resolve({ headers: { get: (n: string) => (n === 'Content-Type' ? 'application/json' : null) },  ok: true, json: async () => [item] });
    });
    global.fetch = fetchMock;

    renderLayoutWithBell();
    await waitFor(() => expect(screen.getByText('Rent')).toBeInTheDocument());

    const bellUrl = '/api/recurring-transactions/upcoming?days=0';
    const bellCallsBefore = fetchMock.mock.calls.filter(([u]) => u === bellUrl).length;
    expect(bellCallsBefore).toBeGreaterThanOrEqual(1);

    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(screen.getByText('Confirm'));
    fireEvent.click(screen.getByRole('button', { name: /^confirm$/i }));

    await waitFor(() => {
      const bellCallsAfter = fetchMock.mock.calls.filter(([u]) => u === bellUrl).length;
      expect(bellCallsAfter).toBeGreaterThan(bellCallsBefore);
    });
  });
});
