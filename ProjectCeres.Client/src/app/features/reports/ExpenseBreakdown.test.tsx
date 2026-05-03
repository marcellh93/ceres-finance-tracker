import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { ExpenseBreakdown } from './ExpenseBreakdown';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/expense-breakdown']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="expense-breakdown" element={<ExpenseBreakdown />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('ExpenseBreakdown report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Expense Breakdown' })).toBeInTheDocument();
  });

  it('renders category rows when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (String(url).includes('expense-breakdown')) {
        return Promise.resolve({ ok: true, json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', categories: [{ categoryName: 'Groceries', lifestyleTag: null, total: 300 }] }) });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Groceries')).toBeInTheDocument());
  });

  it('renders empty state when categories is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (String(url).includes('expense-breakdown')) {
        return Promise.resolve({ ok: true, json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', categories: [] }) });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no expense/i)).toBeInTheDocument());
  });
});
