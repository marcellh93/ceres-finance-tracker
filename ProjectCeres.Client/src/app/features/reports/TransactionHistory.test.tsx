import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { TransactionHistory } from './TransactionHistory';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/transaction-history']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="transaction-history" element={<TransactionHistory />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('TransactionHistory report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Transaction History' })).toBeInTheDocument();
  });

  it('renders rows when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ id: '1', date: '2026-05-01', accountName: 'Checking', categoryName: 'Groceries', categoryTypeName: 'Expense', description: 'Lidl', amount: 45, currencySymbol: '€' }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Lidl')).toBeInTheDocument());
  });

  it('renders pagination controls when data is full page', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (String(url).includes('transaction-history')) {
        return Promise.resolve({
          ok: true,
          json: async () => Array.from({ length: 50 }, (_, i) => ({
            id: String(i), date: '2026-05-01', accountName: 'Checking', categoryName: 'Groceries',
            categoryTypeName: 'Expense', description: `Tx ${i}`, amount: 10, currencySymbol: '€',
          })),
        });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
    renderPage();
    await waitFor(() => expect(screen.getByRole('button', { name: /next/i })).toBeInTheDocument());
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no transactions/i)).toBeInTheDocument());
  });
});
