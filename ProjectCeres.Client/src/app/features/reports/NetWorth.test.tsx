import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { NetWorth } from './NetWorth';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/net-worth']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="net-worth" element={<NetWorth />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('NetWorth report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Net Worth' })).toBeInTheDocument();
  });

  it('renders skeleton after the delay window when loading is slow', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(document.querySelector('[data-slot="skeleton"]')).toBeNull();
    await waitFor(
      () => expect(document.querySelector('[data-slot="skeleton"]')).toBeInTheDocument(),
      { timeout: 500 },
    );
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no accounts/i)).toBeInTheDocument());
  });

  it('renders currency rows when data is present', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ currencyCode: 'EUR', currencySymbol: '€', assets: 1000, liabilities: 200, netWorth: 800 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('EUR')).toBeInTheDocument());
    expect(screen.getByText(/800/)).toBeInTheDocument();
  });

  it('renders CardError on fetch failure', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('fail'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/Couldn't load Net Worth/)).toBeInTheDocument());
  });
});
