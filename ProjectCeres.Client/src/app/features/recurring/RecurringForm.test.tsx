import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { RecurringForm } from './RecurringForm';
import type { AccountListItemDto } from '../accounts/accounts-api';

// Minimal stubs — adjust field names to match actual types if grep above shows differences
const accounts: AccountListItemDto[] = [
  {
    id: 'a1',
    name: 'Checking',
    accountTypeId: 1,
    accountTypeName: 'Asset',
    currencyId: 1,
    currencyCode: 'EUR',
    currencySymbol: '€',
    description: null,
    isActive: true,
    excludeFromSpendable: false,
    excludeFromReports: false,
    liabilityRepaymentType: null,
    interestRate: null,
    balance: 0,
    hasTransactions: false,
  },
];

// CategoryListItemDto stub — shape verified by Step 1 grep
const categories = [{ id: 'c1', name: 'Housing' }] as any[];

const defaults = {
  name: '',
  accountId: 'a1',
  categoryId: 'c1',
  estimatedAmount: '',
  frequency: 'Monthly',
  reminderBehaviour: 'SnapToCalendarDay',
  dayOfPeriod: null as number | null,
  nextDueDate: '2026-05-03',
};

describe('RecurringForm', () => {
  const noop = vi.fn();

  it('renders Name, EstimatedAmount, NextDueDate fields', () => {
    render(
      <RecurringForm
        values={defaults}
        onChange={noop}
        accounts={accounts}
        categories={categories}
      />,
    );
    expect(screen.getByLabelText(/name \*/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/estimated amount/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/next due date \*/i)).toBeInTheDocument();
  });

  it('shows Day of month input for Snap + Monthly', () => {
    render(
      <RecurringForm
        values={{
          ...defaults,
          frequency: 'Monthly',
          reminderBehaviour: 'SnapToCalendarDay',
        }}
        onChange={noop}
        accounts={accounts}
        categories={categories}
      />,
    );
    expect(screen.getByLabelText(/day of month \*/i)).toBeInTheDocument();
  });

  it('shows Day of week picker for Snap + Weekly', () => {
    render(
      <RecurringForm
        values={{
          ...defaults,
          frequency: 'Weekly',
          reminderBehaviour: 'SnapToCalendarDay',
        }}
        onChange={noop}
        accounts={accounts}
        categories={categories}
      />,
    );
    expect(screen.getByLabelText(/day of week \*/i)).toBeInTheDocument();
  });

  it('hides day picker for Snap + Annual', () => {
    render(
      <RecurringForm
        values={{
          ...defaults,
          frequency: 'Annual',
          reminderBehaviour: 'SnapToCalendarDay',
        }}
        onChange={noop}
        accounts={accounts}
        categories={categories}
      />,
    );
    expect(screen.queryByLabelText(/day of (week|month)/i)).not.toBeInTheDocument();
  });

  it('hides day picker for ManualDate regardless of frequency', () => {
    render(
      <RecurringForm
        values={{
          ...defaults,
          frequency: 'Monthly',
          reminderBehaviour: 'ManualDate',
        }}
        onChange={noop}
        accounts={accounts}
        categories={categories}
      />,
    );
    expect(screen.queryByLabelText(/day of (week|month)/i)).not.toBeInTheDocument();
  });

  it('calls onChange with updated name on input', () => {
    const onChange = vi.fn();
    render(
      <RecurringForm
        values={defaults}
        onChange={onChange}
        accounts={accounts}
        categories={categories}
      />,
    );
    fireEvent.change(screen.getByLabelText(/name \*/i), {
      target: { value: 'Rent' },
    });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ name: 'Rent' }));
  });
});
