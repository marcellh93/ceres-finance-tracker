import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { BudgetCombobox } from './BudgetCombobox';
import type { GoalBudgetListItemDto } from '../features/budgets/budgets-api';

const budgets: GoalBudgetListItemDto[] = [
  { id: 'b1', name: 'Vacation 2026', goalType: 'Spending', currencyCode: 'EUR', currencySymbol: '€', targetAmount: 2000, startDate: '2026-01-01', endDate: '2026-12-31', description: null, isActive: true, linkedAccountId: null, linkedAccountName: null, progress: 0 },
  { id: 'b2', name: 'Old Goal', goalType: 'Spending', currencyCode: 'EUR', currencySymbol: '€', targetAmount: 500, startDate: '2025-01-01', endDate: '2025-12-31', description: null, isActive: false, linkedAccountId: null, linkedAccountName: null, progress: 0 },
];

describe('BudgetCombobox', () => {
  it('shows the placeholder when no budget is selected', () => {
    render(<BudgetCombobox budgets={budgets} value={null} onChange={vi.fn()} />);
    expect(screen.getByText('No budget')).toBeInTheDocument();
  });

  it('shows the selected budget name when value is set', () => {
    render(<BudgetCombobox budgets={budgets} value="b1" onChange={vi.fn()} />);
    expect(screen.getByText('Vacation 2026')).toBeInTheDocument();
  });

  it('appends "(archived)" suffix to inactive budgets in the list', () => {
    render(<BudgetCombobox budgets={budgets} value={null} onChange={vi.fn()} />);
    fireEvent.click(screen.getByRole('combobox'));
    expect(screen.getByText('Old Goal (archived)')).toBeInTheDocument();
  });

  it('calls onChange with the budget id when an option is picked', () => {
    const onChange = vi.fn();
    render(<BudgetCombobox budgets={budgets} value={null} onChange={onChange} />);
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(screen.getByText('Vacation 2026'));
    expect(onChange).toHaveBeenCalledWith('b1');
  });

  it('renders a clear button when onClear is set and a value is selected', () => {
    render(<BudgetCombobox budgets={budgets} value="b1" onChange={vi.fn()} onClear={vi.fn()} />);
    expect(screen.getByRole('button', { name: /clear selection/i })).toBeInTheDocument();
  });

  it('does not render a clear button when no value is selected', () => {
    render(<BudgetCombobox budgets={budgets} value={null} onChange={vi.fn()} onClear={vi.fn()} />);
    expect(screen.queryByRole('button', { name: /clear selection/i })).toBeNull();
  });

  it('clear button calls onClear and does not open the popover', () => {
    const onClear = vi.fn();
    render(<BudgetCombobox budgets={budgets} value="b1" onChange={vi.fn()} onClear={onClear} />);
    fireEvent.click(screen.getByRole('button', { name: /clear selection/i }));
    expect(onClear).toHaveBeenCalledTimes(1);
    expect(screen.queryByPlaceholderText(/search budgets/i)).not.toBeInTheDocument();
  });

  it('hides the clear button when disabled', () => {
    render(<BudgetCombobox budgets={budgets} value="b1" onChange={vi.fn()} onClear={vi.fn()} disabled />);
    expect(screen.queryByRole('button', { name: /clear selection/i })).toBeNull();
  });
});
