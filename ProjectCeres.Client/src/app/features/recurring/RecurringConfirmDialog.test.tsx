import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { RecurringConfirmDialog } from './RecurringConfirmDialog';
import type { RecurringTransactionListItemDto } from './reminder-status';

const TODAY = '2026-05-03';

function makeReminder(overrides: Partial<RecurringTransactionListItemDto> = {}): RecurringTransactionListItemDto {
  return {
    id: '1', name: 'Rent', estimatedAmount: 900, accountId: 'a', accountName: 'Checking',
    currencySymbol: '€', categoryId: 'c', categoryName: 'Housing', categoryTypeName: 'Expense',
    frequency: 'Monthly', dayOfPeriod: null, nextDueDate: TODAY,
    isActive: true, reminderBehaviour: 'SnapToCalendarDay', ...overrides,
  };
}

describe('RecurringConfirmDialog', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('populates date with reminder.nextDueDate on open', () => {
    render(<RecurringConfirmDialog open reminder={makeReminder()} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect((screen.getByLabelText(/date \*/i) as HTMLInputElement).value).toBe(TODAY);
  });

  it('populates amount with estimatedAmount on open', () => {
    render(<RecurringConfirmDialog open reminder={makeReminder()} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect((screen.getByLabelText(/amount \*/i) as HTMLInputElement).value).toBe('900');
  });

  it('shows Next due date field for ManualDate reminders', () => {
    render(<RecurringConfirmDialog open reminder={makeReminder({ reminderBehaviour: 'ManualDate' })} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect(screen.getByLabelText(/next due date \*/i)).toBeInTheDocument();
  });

  it('hides Next due date field for Snap reminders', () => {
    render(<RecurringConfirmDialog open reminder={makeReminder()} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect(screen.queryByLabelText(/next due date/i)).not.toBeInTheDocument();
  });

  it('calls POST on confirm and fires onChanged on 201', async () => {
    const onChanged = vi.fn();
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      status: 201, ok: true, json: async () => ({ transactionId: 'tx-1' }),
    });
    render(<RecurringConfirmDialog open reminder={makeReminder()} onChanged={onChanged} onOpenChange={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: /confirm — record/i }));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
  });

  it('disables Confirm button when ManualDate and no next-due-date provided', () => {
    render(<RecurringConfirmDialog open reminder={makeReminder({ reminderBehaviour: 'ManualDate' })} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    const btn = screen.getByRole('button', { name: /confirm — record/i });
    expect(btn).toBeDisabled();
  });

  it('shows inline error on 422 DATE_BEFORE_OPENING_BALANCE', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      status: 422, ok: false,
      json: async () => ({ error: { code: 'DATE_BEFORE_OPENING_BALANCE', message: 'Before opening balance.' } }),
    });
    render(<RecurringConfirmDialog open reminder={makeReminder()} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: /confirm — record/i }));
    await waitFor(() => expect(screen.getByText(/opening balance/i)).toBeInTheDocument());
  });
});
