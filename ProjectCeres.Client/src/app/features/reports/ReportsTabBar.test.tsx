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
  it('renders all 8 report links', () => {
    renderTabBar('/reports/net-worth-over-time');
    expect(screen.getByRole('link', { name: /net worth over time/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /income vs expense/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /transaction history/i })).toBeInTheDocument();
    expect(screen.getAllByRole('link')).toHaveLength(8);
  });

  it('marks active link with aria-current="page"', () => {
    renderTabBar('/reports/income-expense');
    expect(screen.getByRole('link', { name: /income vs expense/i })).toHaveAttribute('aria-current', 'page');
  });

  it('inactive links do not have aria-current', () => {
    renderTabBar('/reports/income-expense');
    expect(screen.getByRole('link', { name: /net worth over time/i })).not.toHaveAttribute('aria-current');
  });

  it('carries search params forward in tab links', () => {
    renderTabBar('/reports/net-worth-over-time', '?from=2026-01&to=2026-04&currencyId=1');
    const link = screen.getByRole('link', { name: /income vs expense/i });
    expect(link.getAttribute('href')).toContain('from=2026-01');
    expect(link.getAttribute('href')).toContain('to=2026-04');
    expect(link.getAttribute('href')).toContain('currencyId=1');
  });

  it('does not produce trailing ? when search params are empty', () => {
    renderTabBar('/reports/net-worth-over-time');
    const link = screen.getByRole('link', { name: /income vs expense/i });
    expect(link.getAttribute('href')).not.toMatch(/\?$/);
  });
});
