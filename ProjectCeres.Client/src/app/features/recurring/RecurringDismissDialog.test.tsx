import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { RecurringDismissDialog } from './RecurringDismissDialog';
import type { RecurringTransactionListItemDto } from './reminder-status';

const TODAY = '2026-05-03';

function makeReminder(overrides: Partial<RecurringTransactionListItemDto> = {}): RecurringTransactionListItemDto {
  return {
    id: '1', name: 'Gym', estimatedAmount: 40, accountId: 'a', accountName: 'Checking',
    currencySymbol: '€', categoryId: 'c', categoryName: 'Wellness', categoryTypeName: 'Expense',
    frequency: 'Monthly', dayOfPeriod: null, nextDueDate: TODAY,
    isActive: true, reminderBehaviour: 'SnapToCalendarDay', ...overrides,
  };
}

describe('RecurringDismissDialog', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('shows simple confirm for Snap reminders (no next due date input)', () => {
    render(<RecurringDismissDialog open reminder={makeReminder()} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect(screen.queryByLabelText(/next due date/i)).not.toBeInTheDocument();
  });

  it('shows next due date input for ManualDate reminders', () => {
    render(<RecurringDismissDialog open reminder={makeReminder({ reminderBehaviour: 'ManualDate' })} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect(screen.getByLabelText(/next due date \*/i)).toBeInTheDocument();
  });

  it('calls POST on dismiss and fires onChanged on 204', async () => {
    const onChanged = vi.fn();
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204, ok: true });
    render(<RecurringDismissDialog open reminder={makeReminder()} onChanged={onChanged} onOpenChange={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: /^dismiss$/i }));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
  });
});
