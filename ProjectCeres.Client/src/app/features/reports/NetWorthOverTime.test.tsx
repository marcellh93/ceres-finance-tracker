import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { NetWorthOverTime } from './NetWorthOverTime';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/net-worth-over-time']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="net-worth-over-time" element={<NetWorthOverTime />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('NetWorthOverTime report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Net Worth Over Time' })).toBeInTheDocument();
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no data/i)).toBeInTheDocument());
  });

  it('renders Export CSV link when data is present', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ year: 2026, month: 4, currencyCode: 'EUR', currencySymbol: '€', assets: 5000, liabilities: 1000, netWorth: 4000 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByRole('link', { name: /export csv/i })).toBeInTheDocument());
  });

  it('renders month rows when data is present', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ year: 2026, month: 4, currencyCode: 'EUR', currencySymbol: '€', assets: 5000, liabilities: 1000, netWorth: 4000 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Apr 2026')).toBeInTheDocument());
  });
});
