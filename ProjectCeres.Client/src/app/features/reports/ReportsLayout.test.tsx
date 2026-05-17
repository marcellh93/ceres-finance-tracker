import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ReportsLayout } from './ReportsLayout';

beforeEach(() => { global.fetch = vi.fn().mockReturnValue(new Promise(() => {})); });

function renderLayout(path = '/reports/net-worth-over-time') {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path=":slug" element={<div>page content</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  );
}

describe('ReportsLayout', () => {
  it('renders the reports nav', () => {
    renderLayout();
    expect(screen.getByRole('navigation', { name: /reports/i })).toBeInTheDocument();
  });

  it('renders all 8 report links', () => {
    renderLayout();
    expect(screen.getAllByRole('link')).toHaveLength(8);
  });

  it('renders outlet content', () => {
    renderLayout();
    expect(screen.getByText('page content')).toBeInTheDocument();
  });
});
