import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { BudgetVsActual } from './BudgetVsActual';

beforeEach(() => { global.fetch = vi.fn(); });
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
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ categoryName: 'Groceries', currencyCode: 'EUR', currencySymbol: '€', limitAmount: 400, actualSpend: 312, variance: -88 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Groceries')).toBeInTheDocument());
    expect(document.querySelector('[data-slot="progress"]')).toBeInTheDocument();
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (String(url).includes('budget-vs-actual')) {
        return Promise.resolve({ ok: true, json: async () => [] });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no active budgets/i)).toBeInTheDocument());
  });
});
