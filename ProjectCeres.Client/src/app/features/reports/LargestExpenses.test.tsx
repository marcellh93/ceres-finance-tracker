import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { LargestExpenses } from './LargestExpenses';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/largest-expenses']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="largest-expenses" element={<LargestExpenses />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

const testRows = [
  { date: '2026-04-01', description: 'Rent', categoryName: 'Housing', accountName: 'Checking', currencySymbol: '€', amount: 900 },
  { date: '2026-04-10', description: 'Groceries', categoryName: 'Food', accountName: 'Checking', currencySymbol: '€', amount: 200 },
  { date: '2026-04-15', description: 'Phone', categoryName: 'Utilities', accountName: 'Checking', currencySymbol: '€', amount: 50 },
];

function mockRows() {
  (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
    if (String(url).includes('largest-expenses')) {
      return Promise.resolve({ ok: true, json: async () => testRows });
    }
    return Promise.resolve({ ok: true, json: async () => [] });
  });
}

describe('LargestExpenses report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Largest Expenses' })).toBeInTheDocument();
  });

  it('renders rows when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (String(url).includes('largest-expenses')) {
        return Promise.resolve({ ok: true, json: async () => [{ date: '2026-05-01', description: 'IKEA', categoryName: 'Shopping', accountName: 'Checking', currencySymbol: '€', amount: 350 }] });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
    renderPage();
    await waitFor(() => expect(screen.getAllByText('IKEA').length).toBeGreaterThan(0));
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (String(url).includes('largest-expenses')) {
        return Promise.resolve({ ok: true, json: async () => [] });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no expenses/i)).toBeInTheDocument());
  });

  it('renders KPI tile for Top Expense', async () => {
    mockRows();
    renderPage();
    await waitFor(() => expect(screen.getByText('Top Expense')).toBeInTheDocument());
    expect(screen.getAllByText('Rent').length).toBeGreaterThan(0); // first row = top expense
  });

  it('renders KPI tile for Total (Top N)', async () => {
    mockRows();
    renderPage();
    await waitFor(() => expect(screen.getByText('Total (Top N)')).toBeInTheDocument());
    expect(screen.getAllByText(/1150\.00/).length).toBeGreaterThan(0); // 900 + 200 + 50 = 1150
  });

  it('renders KPI tile for Avg per Transaction', async () => {
    mockRows();
    renderPage();
    await waitFor(() => expect(screen.getByText('Avg per Transaction')).toBeInTheDocument());
    // 1150 / 3 = 383.33...
    expect(screen.getAllByText(/383\.3/).length).toBeGreaterThan(0);
  });
});
