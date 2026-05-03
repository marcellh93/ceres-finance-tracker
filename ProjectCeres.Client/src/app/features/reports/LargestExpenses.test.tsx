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

describe('LargestExpenses report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Largest Expenses' })).toBeInTheDocument();
  });

  it('renders rows when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ date: '2026-05-01', description: 'IKEA', categoryName: 'Shopping', accountName: 'Checking', currencySymbol: '€', amount: 350 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('IKEA')).toBeInTheDocument());
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no expenses/i)).toBeInTheDocument());
  });
});
