import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { MonthlyCashFlow } from './MonthlyCashFlow';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/monthly-cash-flow']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="monthly-cash-flow" element={<MonthlyCashFlow />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('MonthlyCashFlow report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Monthly Cash Flow' })).toBeInTheDocument();
  });

  it('renders month rows when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ year: 2026, month: 4, currencyCode: 'EUR', currencySymbol: '€', totalIncome: 3000, totalExpenses: 2000, net: 1000 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Apr 2026')).toBeInTheDocument());
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no data/i)).toBeInTheDocument());
  });
});
