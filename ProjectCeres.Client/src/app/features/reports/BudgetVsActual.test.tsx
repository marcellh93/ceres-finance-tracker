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
});
