import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, it, expect } from 'vitest';
import { ReportsTabBar } from './ReportsTabBar';

function renderTabBar(path: string, search = '') {
  render(
    <MemoryRouter initialEntries={[`${path}${search}`]}>
      <Routes>
        <Route path="reports/:slug" element={<ReportsTabBar />} />
        <Route path="reports" element={<ReportsTabBar />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('ReportsTabBar', () => {
  it('renders all 8 report tabs', () => {
    renderTabBar('/reports/net-worth-over-time');
    expect(screen.getByRole('tab', { name: /net worth over time/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /income vs expense/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /transaction history/i })).toBeInTheDocument();
  });

  it('marks active tab with aria-current', () => {
    renderTabBar('/reports/income-expense');
    expect(screen.getByRole('tab', { name: /income vs expense/i })).toHaveAttribute('aria-current', 'page');
  });

  it('carries search params forward in tab links', () => {
    renderTabBar('/reports/net-worth-over-time', '?from=2026-01&to=2026-04&currencyId=1');
    const tab = screen.getByRole('tab', { name: /income vs expense/i });
    expect(tab.getAttribute('href')).toContain('from=2026-01');
    expect(tab.getAttribute('href')).toContain('to=2026-04');
    expect(tab.getAttribute('href')).toContain('currencyId=1');
  });
});
