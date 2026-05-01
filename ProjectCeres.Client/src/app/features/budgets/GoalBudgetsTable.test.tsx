import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { GoalBudgetsTable } from './GoalBudgetsTable';
import type { GoalBudgetListItemDto } from './budgets-api';

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({ data: { numberFormat: 'period_decimal', dateFormat: 'DD/MM/YYYY' }, loading: false }),
}));

const ROWS: GoalBudgetListItemDto[] = [
  {
    id: 'g1', name: 'Trip to Japan', goalType: 'Spending',
    currencyCode: 'EUR', currencySymbol: '€',
    targetAmount: 2000, startDate: '2026-01-01', endDate: '2026-12-31',
    description: null, isActive: true,
    linkedAccountId: null, linkedAccountName: null,
    progress: 850,
  },
  {
    id: 'g2', name: 'Emergency fund', goalType: 'Savings',
    currencyCode: 'EUR', currencySymbol: '€',
    targetAmount: 5000, startDate: '2026-01-01', endDate: null,
    description: null, isActive: true,
    linkedAccountId: 'a1', linkedAccountName: 'Savings',
    progress: 3200,
  },
  {
    id: 'g3', name: 'Old goal', goalType: 'Spending',
    currencyCode: 'EUR', currencySymbol: '€',
    targetAmount: 100, startDate: '2024-01-01', endDate: '2024-12-31',
    description: null, isActive: false,
    linkedAccountId: null, linkedAccountName: null,
    progress: 0,
  },
];

function renderTable() {
  return render(
    <MemoryRouter>
      <GoalBudgetsTable items={ROWS} onChanged={vi.fn()} />
    </MemoryRouter>,
  );
}

describe('GoalBudgetsTable', () => {
  it('renders rows with name, type badge, target', () => {
    renderTable();
    expect(screen.getByText('Trip to Japan')).toBeInTheDocument();
    expect(screen.getByText('Emergency fund')).toBeInTheDocument();
    expect(screen.getAllByText('Spending')).toHaveLength(2);
    expect(screen.getByText('Savings')).toBeInTheDocument();
  });

  it('renders end date or em-dash for open-ended goals', () => {
    renderTable();
    expect(screen.getByText('31/12/2026')).toBeInTheDocument(); // Trip to Japan
    expect(screen.getByText('—')).toBeInTheDocument();           // Emergency fund (no end)
  });

  it('renders Archived badge for inactive rows only', () => {
    renderTable();
    expect(screen.getAllByText(/^archived$/i)).toHaveLength(1);
  });
});
