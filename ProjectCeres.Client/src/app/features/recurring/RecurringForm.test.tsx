import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { RecurringForm } from './RecurringForm';
import type { AccountOptionDto, CategoryOptionDto } from '../movements/movements-api';

const accounts: AccountOptionDto[] = [
  {
    id: 'a1',
    name: 'Checking',
    accountTypeName: 'Asset',
    currencyCode: 'EUR',
    currencySymbol: '€',
  },
];

const categories: CategoryOptionDto[] = [
  { id: 'c1', name: 'Housing', categoryTypeName: 'Expense' },
];

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
  const noop = () => {};

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
    // NextDueDate uses DatePickerField which renders a Button with id="rt-nextdue"
    expect(screen.getByText(/next due date \*/i)).toBeInTheDocument();
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
    // Day of week uses a combobox (no htmlFor), so check label text
    expect(screen.getByText(/day of week \*/i)).toBeInTheDocument();
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
    expect(screen.queryByText(/day of (week|month) \*/i)).not.toBeInTheDocument();
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
    expect(screen.queryByText(/day of (week|month) \*/i)).not.toBeInTheDocument();
  });

  it('shows Day of week picker for Snap + Biweekly', () => {
    render(<RecurringForm values={{ ...defaults, frequency: 'Biweekly', reminderBehaviour: 'SnapToCalendarDay' }} onChange={noop} accounts={accounts} categories={categories} />);
    expect(screen.getByText(/day of week \*/i)).toBeInTheDocument();
  });

  it('hides day picker for RelativeToLastConfirmation regardless of frequency', () => {
    render(<RecurringForm values={{ ...defaults, frequency: 'Monthly', reminderBehaviour: 'RelativeToLastConfirmation' }} onChange={noop} accounts={accounts} categories={categories} />);
    expect(screen.queryByText(/day of (week|month) \*/i)).not.toBeInTheDocument();
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
