import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { RecurringRowMenu } from './RecurringRowMenu';
import type { RecurringTransactionListItemDto } from './reminder-status';

function makeReminder(overrides: Partial<RecurringTransactionListItemDto> = {}): RecurringTransactionListItemDto {
  return {
    id: '1', name: 'Rent', estimatedAmount: 900, accountId: 'a', accountName: 'Checking',
    currencySymbol: '€', categoryId: 'c', categoryName: 'Housing', categoryTypeName: 'Expense',
    frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-15',
    isActive: true, reminderBehaviour: 'SnapToCalendarDay', ...overrides,
  };
}

function renderMenu(reminder: RecurringTransactionListItemDto, onChanged = vi.fn()) {
  return render(
    <MemoryRouter><RecurringRowMenu reminder={reminder} onChanged={onChanged} /></MemoryRouter>
  );
}

describe('RecurringRowMenu', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('shows Confirm, Edit, Dismiss, Archive for active row', async () => {
    renderMenu(makeReminder());
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(screen.getByText('Confirm…')).toBeInTheDocument();
    expect(screen.getByText('Edit')).toBeInTheDocument();
    expect(screen.getByText('Dismiss…')).toBeInTheDocument();
    expect(screen.getByText('Archive…')).toBeInTheDocument();
  });

  it('shows only Reactivate for archived row', () => {
    renderMenu(makeReminder({ isActive: false }));
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(screen.getByText('Reactivate')).toBeInTheDocument();
    expect(screen.queryByText('Confirm…')).not.toBeInTheDocument();
  });

  it('opens Confirm dialog when Confirm… clicked', () => {
    renderMenu(makeReminder());
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(screen.getByText('Confirm…'));
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.getByText(/Confirm 'Rent'/)).toBeInTheDocument();
  });

  it('opens Archive dialog when Archive… clicked', () => {
    renderMenu(makeReminder());
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(screen.getByText('Archive…'));
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.getByText(/Archive 'Rent'/)).toBeInTheDocument();
  });

  it('calls Reactivate PATCH and fires onChanged on 204', async () => {
    const onChanged = vi.fn();
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204, ok: true });
    renderMenu(makeReminder({ isActive: false }), onChanged);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(screen.getByText('Reactivate'));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
  });
});
