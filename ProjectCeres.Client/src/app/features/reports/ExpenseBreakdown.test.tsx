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

const breakdown = {
  currencyCode: 'EUR', currencySymbol: '€',
  categories: [
    { categoryName: 'Groceries', lifestyleTag: 'Essential', total: 500 },
    { categoryName: 'Dining', lifestyleTag: null, total: 300 },
    { categoryName: 'Transport', lifestyleTag: 'Essential', total: 200 },
  ],
};

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
    await waitFor(() => expect(screen.getAllByText('Groceries').length).toBeGreaterThan(0));
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

  function mockBreakdown() {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (String(url).includes('expense-breakdown')) {
        return Promise.resolve({ ok: true, json: async () => breakdown });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
  }

  it('renders KPI tile for Largest Category', async () => {
    mockBreakdown();
    renderPage();
    await waitFor(() => expect(screen.getByText('Largest Category')).toBeInTheDocument());
    expect(screen.getAllByText('Groceries').length).toBeGreaterThan(0); // largest by total
  });

  it('renders KPI tile for Total Expenses', async () => {
    mockBreakdown();
    renderPage();
    await waitFor(() => expect(screen.getByText('Total Expenses')).toBeInTheDocument());
    expect(screen.getAllByText(/1000\.00/).length).toBeGreaterThan(0); // 500 + 300 + 200 = 1000
  });

  it('renders KPI tile for Categories count', async () => {
    mockBreakdown();
    renderPage();
    await waitFor(() => expect(screen.getByText('Categories')).toBeInTheDocument());
    expect(screen.getByText('3')).toBeInTheDocument(); // 3 categories in fixture
  });
});
