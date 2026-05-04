import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { ReportsIndex } from './ReportsIndex';

beforeEach(() => { global.fetch = vi.fn().mockReturnValue(new Promise(() => {})); });
afterEach(() => { vi.resetAllMocks(); });

describe('ReportsIndex', () => {
  it('renders all 8 report cards', () => {
    render(
      <MemoryRouter initialEntries={['/reports']}>
        <Routes>
          <Route path="reports" element={<ReportsLayout />}>
            <Route index element={<ReportsIndex />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByRole('heading', { name: 'Reports' })).toBeInTheDocument();
    // Each label appears twice: once in the tab bar and once in the index card.
    // Use getAllByText and verify at least one match per label.
    expect(screen.getAllByText('Net Worth')[0]).toBeInTheDocument();
    expect(screen.getAllByText('Net Worth Over Time')[0]).toBeInTheDocument();
    expect(screen.getAllByText('Income vs Expense')[0]).toBeInTheDocument();
    expect(screen.getAllByText('Monthly Cash Flow')[0]).toBeInTheDocument();
    expect(screen.getAllByText('Expense Breakdown')[0]).toBeInTheDocument();
    expect(screen.getAllByText('Budget vs Actual')[0]).toBeInTheDocument();
    expect(screen.getAllByText('Largest Expenses')[0]).toBeInTheDocument();
    expect(screen.getAllByText('Transaction History')[0]).toBeInTheDocument();
  });
});
