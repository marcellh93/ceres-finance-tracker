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
    expect(screen.getByText('Net Worth')).toBeInTheDocument();
    expect(screen.getByText('Net Worth Over Time')).toBeInTheDocument();
    expect(screen.getByText('Income vs Expense')).toBeInTheDocument();
    expect(screen.getByText('Monthly Cash Flow')).toBeInTheDocument();
    expect(screen.getByText('Expense Breakdown')).toBeInTheDocument();
    expect(screen.getByText('Budget vs Actual')).toBeInTheDocument();
    expect(screen.getByText('Largest Expenses')).toBeInTheDocument();
    expect(screen.getByText('Transaction History')).toBeInTheDocument();
  });
});
