import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { CategoryBudgetsTable } from './CategoryBudgetsTable';
import type { CategoryBudgetListItemDto } from './budgets-api';

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({ data: { numberFormat: 'period_decimal', dateFormat: 'DD/MM/YYYY' }, loading: false }),
}));

const ROWS: CategoryBudgetListItemDto[] = [
  {
    id: 'b1', categoryId: 'c1', categoryName: 'Groceries',
    currencyCode: 'EUR', currencySymbol: '€',
    limitAmount: 600, isActive: true,
    currentPeriodSpend: 250, currentPeriodEnd: '2026-05-31',
  },
  {
    id: 'b2', categoryId: 'c2', categoryName: 'Old Hobby',
    currencyCode: 'EUR', currencySymbol: '€',
    limitAmount: 100, isActive: false,
    currentPeriodSpend: 0, currentPeriodEnd: '2026-05-31',
  },
];

function renderTable() {
  return render(
    <MemoryRouter>
      <CategoryBudgetsTable items={ROWS} onChanged={vi.fn()} />
    </MemoryRouter>,
  );
}

describe('CategoryBudgetsTable', () => {
  it('renders rows with category, currency, spent/limit', () => {
    renderTable();
    expect(screen.getByText('Groceries')).toBeInTheDocument();
    expect(screen.getByText('Old Hobby')).toBeInTheDocument();
    expect(screen.getAllByText(/€ EUR/i)).toHaveLength(2);
    // Spent/limit rendered as plain text — combined "€ 250.00 / € 600.00"
    expect(screen.getByText(/€ 250\.00/)).toBeInTheDocument();
    expect(screen.getByText(/€ 600\.00/)).toBeInTheDocument();
  });

  it('renders the Archived badge for inactive rows only', () => {
    renderTable();
    const archived = screen.getAllByText(/^archived$/i);
    expect(archived).toHaveLength(1);
  });

  it('shows a row-actions menu button per row', () => {
    renderTable();
    expect(screen.getAllByRole('button', { name: /row actions/i })).toHaveLength(2);
  });
});
