import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { MonthlyCashFlow } from './MonthlyCashFlow';

beforeEach(() => { global.fetch = vi.fn(); });

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
    // Recharts adds a hidden measurement span with the same text, so use getAllByText
    await waitFor(() => expect(screen.getAllByText('Apr 2026').length).toBeGreaterThan(0));
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no data/i)).toBeInTheDocument());
  });

  const twoRows = [
    { year: 2026, month: 1, currencyCode: 'EUR', currencySymbol: '€', totalIncome: 3000, totalExpenses: 2000, net: 1000 },
    { year: 2026, month: 4, currencyCode: 'EUR', currencySymbol: '€', totalIncome: 3500, totalExpenses: 2200, net: 1300 },
  ];

  it('renders KPI tile for Total Income', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => twoRows });
    renderPage();
    await waitFor(() => expect(screen.getByText('Total Income')).toBeInTheDocument());
    expect(screen.getByText(/6500\.00/)).toBeInTheDocument(); // 3000 + 3500 = 6500
  });

  it('renders KPI tile for Total Expenses', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => twoRows });
    renderPage();
    await waitFor(() => expect(screen.getByText('Total Expenses')).toBeInTheDocument());
    expect(screen.getByText(/4200\.00/)).toBeInTheDocument(); // 2000 + 2200 = 4200
  });

  it('renders KPI tile for Net Flow', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => twoRows });
    renderPage();
    await waitFor(() => expect(screen.getByText('Net Flow')).toBeInTheDocument());
    expect(screen.getByText(/2300\.00/)).toBeInTheDocument(); // 6500 - 4200 = 2300
  });
});
