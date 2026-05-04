import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { __resetSettingsForTests } from '../../lib/use-settings';
import { ReportsLayout } from './ReportsLayout';
import { BudgetVsActual } from './BudgetVsActual';

const SETTINGS = { numberFormat: 'period_decimal', dateFormat: 'DD/MM/YYYY', defaultCurrencyCode: 'EUR', defaultCurrencySymbol: '€', periodStartDay: 1 };
const MOCK_ROW = { categoryName: 'Groceries', currencyCode: 'EUR', currencySymbol: '€', limitPerPeriod: 200, totalLimit: 400, actualSpend: 312, variance: 88 };

function mockFetch(reportData: unknown) {
  (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
    if (String(url).includes('/api/settings')) return Promise.resolve({ ok: true, json: async () => SETTINGS });
    if (String(url).includes('budget-vs-actual')) return Promise.resolve({ ok: true, json: async () => reportData });
    return new Promise(() => {});
  });
}

beforeEach(() => {
  __resetSettingsForTests();
  global.fetch = vi.fn();
});
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/budget-vs-actual']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="budget-vs-actual" element={<BudgetVsActual />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('BudgetVsActual report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Budget vs Actual' })).toBeInTheDocument();
  });

  it('renders rows with progress when data loads', async () => {
    mockFetch([MOCK_ROW]);
    renderPage();
    await waitFor(() => expect(screen.getByText('Groceries')).toBeInTheDocument());
    expect(document.querySelector('[data-slot="progress"]')).toBeInTheDocument();
  });

  it('renders empty state when data is []', async () => {
    mockFetch([]);
    renderPage();
    await waitFor(() => expect(screen.getByText(/no active budgets/i)).toBeInTheDocument());
  });

  it('renders KPI tile for Total Budget', async () => {
    mockFetch([
      { categoryName: 'Groceries', currencyCode: 'EUR', currencySymbol: '€', limitPerPeriod: 200, totalLimit: 200, actualSpend: 150, variance: 50 },
      { categoryName: 'Dining',    currencyCode: 'EUR', currencySymbol: '€', limitPerPeriod: 100, totalLimit: 100, actualSpend: 120, variance: -20 },
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('Total Budget')).toBeInTheDocument());
    expect(screen.getAllByText(/300\.00/).length).toBeGreaterThan(0); // 200 + 100 = 300
  });

  it('renders KPI tile for Total Spent', async () => {
    mockFetch([
      { categoryName: 'Groceries', currencyCode: 'EUR', currencySymbol: '€', limitPerPeriod: 200, totalLimit: 200, actualSpend: 150, variance: 50 },
      { categoryName: 'Dining',    currencyCode: 'EUR', currencySymbol: '€', limitPerPeriod: 100, totalLimit: 100, actualSpend: 120, variance: -20 },
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('Total Spent')).toBeInTheDocument());
    expect(screen.getAllByText(/270\.00/).length).toBeGreaterThan(0); // 150 + 120 = 270
  });

  it('renders KPI tile for Overall Used', async () => {
    mockFetch([
      { categoryName: 'Groceries', currencyCode: 'EUR', currencySymbol: '€', limitPerPeriod: 200, totalLimit: 200, actualSpend: 150, variance: 50 },
      { categoryName: 'Dining',    currencyCode: 'EUR', currencySymbol: '€', limitPerPeriod: 100, totalLimit: 100, actualSpend: 120, variance: -20 },
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('Overall Used')).toBeInTheDocument());
    expect(screen.getByText(/90\.0%/)).toBeInTheDocument(); // 270/300 = 90%
  });
});
