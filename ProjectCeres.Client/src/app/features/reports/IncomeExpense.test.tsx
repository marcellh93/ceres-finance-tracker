import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { IncomeExpense } from './IncomeExpense';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/income-expense']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="income-expense" element={<IncomeExpense />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('IncomeExpense report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Income vs Expense' })).toBeInTheDocument();
  });

  it('renders KPI values when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (String(url).includes('income-expense')) {
        return Promise.resolve({ ok: true, json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', totalIncome: 3000, totalExpenses: 2000, savingsRate: 0.333 }) });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
    renderPage();
    await waitFor(() => expect(screen.getAllByText(/3000/).length).toBeGreaterThan(0));
    expect(screen.getAllByText(/2000/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/33.3%/).length).toBeGreaterThan(0);
  });

  it('renders CardError on failure', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('fail'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/Couldn't load Income vs Expense/)).toBeInTheDocument());
  });

  it('renders a chart when data is present', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (String(url).includes('income-expense')) {
        return Promise.resolve({ ok: true, json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', totalIncome: 3000, totalExpenses: 2000, savingsRate: 0.33 }) });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Income')).toBeInTheDocument());
    // Chart renders income and expenses as grouped bars — verify both values appear
    expect(screen.getAllByText(/3000\.00/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/2000\.00/).length).toBeGreaterThan(0);
  });
});
